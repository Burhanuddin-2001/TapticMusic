using Microsoft.UI.Xaml.Controls;

namespace MusicPlayer.Controls;

/// <summary>
/// The icon navigation rail: 70px wide holding 60x60 items, the extra 10 being what lets an item
/// cast its shadow. It reports which item was picked and nothing more; the hosting window owns the
/// mapping from tag to page.
/// </summary>
public sealed partial class CustomSidebarRail : UserControl
{
    /// <summary>
    /// Fires with the picked item's Tag string - "Dashboard", "Library", "Search", "Playlists" or
    /// "Settings". A string and not a Page type on purpose: this control must never know what a page
    /// is, so MainWindow.SidebarRail_SelectionRequested owns the whole tag-to-page switch.
    /// </summary>
    public event EventHandler<string>? SelectionRequested;

    /// <summary>
    /// Builds the rail. There is nothing to wire up: the five items are declared in the XAML and the
    /// only event is forwarded straight back out.
    /// </summary>
    public CustomSidebarRail()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Turns a click on a rail item into the SelectionRequested event. The pattern match is a guard,
    /// not a formality - SelectionChanged also fires with a null SelectedItem while the list is being
    /// rebuilt, and an item with no Tag would otherwise raise an empty navigation.
    /// </summary>
    private void RailItems_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RailItems.SelectedItem is ListViewItem { Tag: string tag })
        {
            SelectionRequested?.Invoke(this, tag);
        }
    }
}
