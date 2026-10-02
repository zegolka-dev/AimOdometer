using AimOdometer.App.Localization;
using AimOdometer.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AimOdometer.App.ViewModels;

/// <summary>A page in the left navigation.</summary>
public abstract partial class PageViewModel : ObservableObject
{
    protected PageViewModel(AppData data)
    {
        Data = data;
        Loc.Instance.LanguageChanged += (_, _) => OnPropertyChanged(nameof(Title));
    }

    protected AppData Data { get; }

    /// <summary>Navigation label in the current language.</summary>
    public string Title => Loc.Instance[TitleKey];

    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    /// <summary>Localization key of the navigation label.</summary>
    public abstract string TitleKey { get; }

    /// <summary>Segoe Fluent Icons glyph.</summary>
    public abstract string Icon { get; }

    /// <summary>Reloads everything from the database (on open, after changes, after a language switch).</summary>
    public abstract void Refresh();

    /// <summary>Called once a second while the page is visible, with the tracker's live status.</summary>
    public virtual void OnLiveUpdate(TrackerConnection tracker)
    {
    }
}
