using Microsoft.UI.Xaml.Controls;
using MusicPlayer.Models;
using MusicPlayer.ViewModels;

namespace MusicPlayer.Views;

/// <summary>
/// Live search across the library, grouped Artists then Albums then Tracks.
/// </summary>
public sealed partial class SearchPage : Page, IArrowHost
{
    /// <summary>
    /// Builds the page. Nothing else happens here - this page has no OnNavigatedTo, because its
    /// content is driven entirely by what the user types rather than by anything loaded up front.
    /// </summary>
    public SearchPage()
    {
        InitializeComponent();
    }

    /// <summary>
    /// This visit's view model, reached from XAML by x:Bind. New per visit and never cached, so the
    /// search box starts empty every time the page is opened. It takes the library repository from
    /// App's static locator, which is how every page gets its dependencies under A3.
    /// </summary>
    public SearchViewModel ViewModel { get; } = new(App.Library);

    /// <summary>
    /// Step 16. The list the shell's bare Up and Down keys walk. Only the Tracks section can take
    /// them: the Artists and Albums blocks are ItemsControls, which generate no focusable rows.
    /// </summary>
    public Control? ArrowTarget => TrackList;

    private void Term_TextChanged(object sender, TextChangedEventArgs e)
        => ViewModel.SetTerm(((TextBox)sender).Text);

    private void Tracks_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Track track)
        {
            App.PlayerBar.PlayFrom(ViewModel.Tracks, ViewModel.Tracks.IndexOf(track));
        }
    }
}
