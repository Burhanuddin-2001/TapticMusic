using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using MusicPlayer.Data;
using MusicPlayer.Models;
using MusicPlayer.ViewModels;

namespace MusicPlayer.Views;

/// <summary>
/// Playlists. An index, and one open playlist. Step 11.
///
/// Every handler here is one line into the ViewModel or straight into App.PlayerBar, which is the
/// house pattern: XAML event attribute, one-line code-behind, ViewModel method. No ICommand (A5).
/// Playback does not route through this page's ViewModel, same as the Library.
/// </summary>
public sealed partial class PlaylistsPage : Page, IArrowHost
{
    /// <summary>
    /// Which row the last right-click landed on. The track list is templated by
    /// TrackRowItemStyle rather than a DataTemplate, so its flyout sits on the ListView and there
    /// is no per-item DataContext to read — unlike the playlist rows below, which have one.
    /// </summary>
    private int _rightClickedTrack = -1;

    /// <summary>
    /// Builds the page. Nothing else happens here: the view model is built by its own initialiser
    /// and the data is loaded in OnNavigatedTo, because a constructor cannot await.
    /// </summary>
    public PlaylistsPage()
    {
        InitializeComponent();
    }

    /// <summary>
    /// This visit's view model, reached from XAML by x:Bind. New per visit, which is what closes an
    /// open playlist when you navigate away: Selected starts null again, and null-versus-not-null IS
    /// the switch between the index and the detail view.
    /// </summary>
    public PlaylistsViewModel ViewModel { get; } = new(App.Playlists);

    /// <summary>
    /// Step 16. The list the shell's bare Up and Down keys walk, chosen by which half of the page is
    /// showing. Keyed off ShowDetail rather than a null test on the two fields, because x:Load
    /// unloads as well as loads and the order of those two events is not ours to rely on.
    /// </summary>
    public Control? ArrowTarget => ViewModel.ShowDetail ? TrackList : PlaylistList;

    /// <summary>
    /// Reloads the index on every visit, which is what picks up a playlist created from the Library's
    /// right-click menu. The tile covers are re-picked here too, but from a per-process seed, so they
    /// come back identical within one run and differ after a restart.
    /// </summary>
    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.LoadAsync();
    }

    // ---- index ----------------------------------------------------------------------------

    private async void PlaylistList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PlaylistTile tile)
        {
            await ViewModel.OpenAsync(tile.Playlist);
        }
    }

    private async void NewPlaylist_Click(object sender, RoutedEventArgs e)
    {
        string? name = await NamePrompt.ShowAsync(XamlRoot, "New playlist", "Create", string.Empty);

        if (name is not null)
        {
            await ViewModel.CreateAsync(name);
        }
    }

    private async void PlaylistPlay_Click(object sender, RoutedEventArgs e)
    {
        if (PlaylistOf(sender) is Playlist playlist)
        {
            await ViewModel.OpenAsync(playlist);
            App.PlayerBar.PlayFrom(ViewModel.Tracks, 0);
        }
    }

    private async void PlaylistRename_Click(object sender, RoutedEventArgs e)
    {
        if (PlaylistOf(sender) is not Playlist playlist)
        {
            return;
        }

        string? name = await NamePrompt.ShowAsync(XamlRoot, "Rename playlist", "Rename", playlist.Name);

        if (name is not null)
        {
            await ViewModel.RenameAsync(playlist.Id, name);
        }
    }

    private async void PlaylistDelete_Click(object sender, RoutedEventArgs e)
    {
        if (PlaylistOf(sender) is not Playlist playlist)
        {
            return;
        }

        ContentDialog confirm = new()
        {
            XamlRoot = XamlRoot,
            Title = $"Delete \"{playlist.Name}\"?",
            Content = $"The playlist and its order go away. Your {playlist.TrackCountLabel} and "
                    + "their files are not touched — only this grouping of them.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Keep it",
            DefaultButton = ContentDialogButton.Close,

            // Same reason as NamePrompt: the template's default button would otherwise wear
            // SystemAccentColor rather than this app's accent. Step 13.
            PrimaryButtonStyle = (Style)Application.Current.Resources["PillAccentButtonStyle"],
        };

        if (await confirm.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.DeleteAsync(playlist.Id);
        }
    }

    // ---- one open playlist ------------------------------------------------------------------

    private void Back_Click(object sender, RoutedEventArgs e) => ViewModel.Close();

    private void PlayAll_Click(object sender, RoutedEventArgs e)
        => App.PlayerBar.PlayFrom(ViewModel.Tracks, 0);

    private void Shuffle_Click(object sender, RoutedEventArgs e)
        => App.PlayerBar.ShuffleAll(ViewModel.Tracks);

    private void Tracks_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Track track)
        {
            App.PlayerBar.PlayFrom(ViewModel.Tracks, ViewModel.Tracks.IndexOf(track));
        }
    }

    /// <summary>
    /// Fires once per drag, not per row. The ListView has already moved the item inside the bound
    /// ObservableCollection, so its order is the new order and the ViewModel just writes it down.
    /// </summary>
    private async void TrackList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        => await ViewModel.SaveOrderAsync();

    private void TrackList_RightTapped(object sender, RightTappedRoutedEventArgs e)
        => _rightClickedTrack = IndexFromSource(TrackList, e.OriginalSource);

    private async void RemoveTrack_Click(object sender, RoutedEventArgs e)
        => await ViewModel.RemoveAtAsync(_rightClickedTrack);

    // ---- shared -----------------------------------------------------------------------------

    /// <summary>
    /// A MenuFlyoutItem inherits DataContext from the element its flyout is attached to, which for
    /// the index tiles is the DataTemplate root, which is the PlaylistTile. The record inside it is
    /// what every caller wants, so it is unwrapped here rather than at four call sites.
    /// </summary>
    private static Playlist? PlaylistOf(object sender)
        => ((sender as FrameworkElement)?.DataContext as PlaylistTile)?.Playlist;

    /// <summary>
    /// Walks up from whatever was actually clicked to the row that contains it. Returns -1 when the
    /// click landed on the list background rather than a row.
    /// </summary>
    private static int IndexFromSource(ListView list, object originalSource)
    {
        DependencyObject? node = originalSource as DependencyObject;

        while (node is not null and not ListViewItem)
        {
            node = VisualTreeHelper.GetParent(node);
        }

        return node is ListViewItem item ? list.IndexFromContainer(item) : -1;
    }
}
