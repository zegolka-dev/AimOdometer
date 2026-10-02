using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace AimOdometer.App.Views;

public partial class GamesView : UserControl
{
    public GamesView() => InitializeComponent();

    /// <summary>The "…" button opens its context menu on a normal left click too.</summary>
    private void OnActionsClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.DataContext = button.DataContext;
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }
}
