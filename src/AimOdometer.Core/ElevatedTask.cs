using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace AimOdometer.Core;

/// <summary>
/// "Count games that run as administrator". Windows does not deliver raw mouse input to a normal program while a window
/// running as administrator is in front (User Interface Privilege Isolation), and some games always run that way
/// (Genshin Impact, Honkai: Star Rail, Zenless Zone Zero: their anti-cheat needs it). The fix is to run the tracker with
/// administrator rights too: a Task Scheduler task starts it elevated at logon without a UAC prompt each time. Creating
/// and deleting the task asks for UAC once; starting it does not.
/// </summary>
public static class ElevatedTask
{
    public const string TaskName = @"AimOdometer\Tracker";

    /// <summary>True when this process runs with administrator rights.</summary>
    public static bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    /// <summary>Whether the task exists (readable by its own user without elevation).</summary>
    public static bool Exists() => Run("schtasks.exe", $"/Query /TN \"{TaskName}\"", elevated: false) == 0;

    /// <summary>Starts the elevated tracker through the task (no UAC prompt).</summary>
    public static bool Start() => Run("schtasks.exe", $"/Run /TN \"{TaskName}\"", elevated: false) == 0;

    /// <summary>Registers the task (one UAC prompt). False when the user declined or it failed.</summary>
    public static bool Create(string trackerExePath)
    {
        var xml = Path.Combine(Path.GetTempPath(), $"aimodometer-task-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(xml, TaskXml(trackerExePath, CurrentUser()), Encoding.Unicode);
            return Run("schtasks.exe", $"/Create /TN \"{TaskName}\" /XML \"{xml}\" /F", elevated: true) == 0;
        }
        finally
        {
            File.Delete(xml);
        }
    }

    /// <summary>Removes the task (one UAC prompt). False when the user declined or it failed.</summary>
    public static bool Delete() => Run("schtasks.exe", $"/Delete /TN \"{TaskName}\" /F", elevated: true) == 0;

    /// <summary>Task definition: at this user's logon, run the tracker with the highest rights, no time limit.</summary>
    public static string TaskXml(string trackerExePath, string user)
    {
        var exe = SecurityElement.Escape(trackerExePath);
        var who = SecurityElement.Escape(user);
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>AimOdometer mouse distance tracker, started with administrator rights so that games running as administrator are counted too.</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{who}</UserId>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{who}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{exe}</Command>
                  <Arguments>--autostart</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    private static string CurrentUser()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.Name;
    }

    private static int Run(string file, string arguments, bool elevated)
    {
        var start = new ProcessStartInfo(file, arguments)
        {
            UseShellExecute = elevated,
            Verb = elevated ? "runas" : string.Empty,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        try
        {
            using var process = Process.Start(start);
            if (process is null)
            {
                return -1;
            }

            return process.WaitForExit(30_000) ? process.ExitCode : -1;
        }
        catch (Win32Exception)
        {
            return -1; // UAC declined (1223) or schtasks missing
        }
    }
}
