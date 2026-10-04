using System.IO.Pipes;
using System.Security.AccessControl;
using AimOdometer.Core;
using AimOdometer.Tracker;

namespace AimOdometer.Tracker.Tests;

public class PipeSecurityTests
{
    [Fact]
    public async Task PipeWithTheTrackerSecurityAcceptsTheSameUser()
    {
        var security = PipeServer.CreateSecurity();
        var sddl = security.GetSecurityDescriptorSddlForm(AccessControlSections.Access);
        Assert.StartsWith("D:", sddl, StringComparison.Ordinal);
        Assert.Contains(System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value, sddl, StringComparison.Ordinal);

        var name = $"AimOdometer.Test.{Guid.NewGuid():N}";
        using var server = PipeServer.CreatePipe(name, security, elevated: false);
        var accept = server.WaitForConnectionAsync(TestContext.Current.CancellationToken);
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut);
        await client.ConnectAsync(2000, TestContext.Current.CancellationToken);
        await accept;
        client.WriteByte(42);
        Assert.Equal(42, server.ReadByte());
    }

    [Fact]
    public async Task LoweringTheLabelKeepsThePipeUsable()
    {
        // In tests the process is not elevated: setting the medium label is allowed and must not break the pipe.
        var name = $"AimOdometer.Test.{Guid.NewGuid():N}";
        using var server = PipeServer.CreatePipe(name, PipeServer.CreateSecurity(), elevated: true);
        Assert.True(AimOdometer.Win32.IntegrityLabel.SetMedium(server.SafePipeHandle));
        var accept = server.WaitForConnectionAsync(TestContext.Current.CancellationToken);
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut);
        await client.ConnectAsync(2000, TestContext.Current.CancellationToken);
        await accept;
        server.WriteByte(7);
        Assert.Equal(7, client.ReadByte());
    }

    [Fact]
    public void TaskDefinitionRunsTheTrackerElevatedAtThisUsersLogon()
    {
        var xml = ElevatedTask.TaskXml(@"C:\Users\a&b\AppData\Local\AimOdometerApp\current\AimOdometer.Tracker.exe", @"PC\user");
        var doc = System.Xml.Linq.XDocument.Parse(xml);
        System.Xml.Linq.XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        Assert.Equal("HighestAvailable", doc.Descendants(ns + "RunLevel").Single().Value);
        Assert.Equal(@"PC\user", doc.Descendants(ns + "LogonTrigger").Single().Element(ns + "UserId")!.Value);
        Assert.Equal(@"C:\Users\a&b\AppData\Local\AimOdometerApp\current\AimOdometer.Tracker.exe", doc.Descendants(ns + "Command").Single().Value);
        Assert.Equal("--autostart", doc.Descendants(ns + "Arguments").Single().Value);
        Assert.Equal("PT0S", doc.Descendants(ns + "ExecutionTimeLimit").Single().Value);
    }
}
