using System.Runtime.InteropServices;
using AimOdometer.App.Localization;
using AimOdometer.App.Services;
using AimOdometer.Cloud;
using AimOdometer.Core;
using AimOdometer.Core.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AimOdometer.App.ViewModels;

public enum FeedbackKind
{
    Bug,
    Idea,
    Other,
}

/// <summary>"Complaints and suggestions": a message to the author, stored in the AimOdometer cloud.</summary>
public sealed partial class FeedbackViewModel(CloudService cloud, Action close) : ObservableObject
{
    public const int MaxLength = 4000;

    [ObservableProperty]
    public partial FeedbackKind Kind { get; set; } = FeedbackKind.Idea;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial string Message { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Contact { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IncludeDetails { get; set; } = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial bool IsSending { get; set; }

    [ObservableProperty]
    public partial bool IsSent { get; set; }

    [ObservableProperty]
    public partial string? Problem { get; set; }

    /// <summary>What "include details" adds, shown next to the checkbox.</summary>
    public string Details { get; } = $"AimOdometer {AppIdentity.Version} · {RuntimeInformation.OSDescription}";

    public bool SignedIn => cloud.Client.IsSignedIn;

    private bool CanSend() => !IsSending && Message.Trim().Length >= 3;

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        IsSending = true;
        Problem = null;
        try
        {
            var kind = Kind switch { FeedbackKind.Bug => "bug", FeedbackKind.Idea => "idea", _ => "other" };
            await cloud.Client.SendFeedbackAsync(
                kind,
                Message.Trim(),
                Contact.Trim(),
                IncludeDetails ? AppIdentity.Version : null,
                IncludeDetails ? RuntimeInformation.OSDescription : null,
                Loc.Instance.Code,
                CancellationToken.None);
            IsSent = true;
        }
        catch (CloudException ex)
        {
            Log.Warning($"Feedback not sent: {ex.Error} {ex.Message}");
            Problem = ex.Error == CloudError.Rejected ? Loc.Instance["Feedback.TooShort"] : CloudService.Describe(ex.Error);
        }
        finally
        {
            IsSending = false;
        }
    }

    [RelayCommand]
    private void Close() => close();
}
