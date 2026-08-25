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
/// The track table, built to design-plan 5.1. Rows come from TrackRowItemStyle.
/// </summary>
public sealed partial class LibraryPage : Page, IArrowHost
{
    /// <summary>Which row the last right-click landed on. Null when it missed. Step 11.</summary>
    private Track? _rightClicked;

    /// <summary>
    /// Builds the page. Nothing else happens here: the view model is built by its own initialiser
    /// and the data is loaded in OnNavigatedTo, because a constructor cannot await.
    /// </summary>
    public LibraryPage()
    {
        InitializeComponent();
    }

    /// <summary>
    /// This visit's view model, reached from XAML by x:Bind. New per visit, so the sort column and
    /// the filter text both reset when the page is left - deliberate, because a hidden filter is a
    /// list that looks broken when you come back to it.
    /// </summary>
    public LibraryViewModel ViewModel { get; } = new(App.Library);

    /// <summary>
    /// Step 16. The list the shell's bare Up and Down keys walk. Null until ViewModel.HasTracks
    /// realises the x:Load, which is the empty-library case, and the shell then leaves the key alone.
    /// </summary>
    public Control? ArrowTarget => TrackList;

    /// <summary>
    /// Reloaded on every visit rather than cached, so a rescan always shows up. The query runs on
    /// a background thread and a few thousand rows cost a few milliseconds.
    /// </summary>
    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.LoadAsync();
    }

    private void Filter_TextChanged(object sender, TextChangedEventArgs e)
        => ViewModel.SetFilter(((TextBox)sender).Text);

    /// <summary>
    /// The column comes from the button's Tag so all five headers share one handler. Tag holds the
    /// TrackSort name, and Enum.Parse failing here would mean the XAML and the enum disagree, which
    /// is a build-time mistake worth surfacing rather than swallowing.
    /// </summary>
    private async void Header_Click(object sender, RoutedEventArgs e)
        => await ViewModel.SortByAsync(Enum.Parse<TrackSort>((string)((Button)sender).Tag));

    private void PlayAll_Click(object sender, RoutedEventArgs e)
        => App.PlayerBar.PlayFrom(ViewModel.Tracks, 0);

    /// <summary>
    /// Shuffle means "start a shuffled playthrough", so it begins somewhere random. PlayFrom pins
    /// whatever index it is given to the front of the shuffled order — correct when the user clicked
    /// a row, wrong for this button, which is why passing 0 here always replayed the first row.
    /// </summary>
    private void Shuffle_Click(object sender, RoutedEventArgs e)
        => App.PlayerBar.ShuffleAll(ViewModel.Tracks);

    /// <summary>
    /// Step 17 removed the !track.Unreadable guard. That flag means "no parser read the tags", not
    /// "this file cannot be played", and the nine other play paths in the app never checked it - so
    /// a row would refuse a click while Play all played the very same file. Whether audio opens is
    /// the engine's call: a real failure comes back through IAudioService.Failed and the ErrorBar,
    /// which tells the user something, unlike a silently dropped click.
    /// </summary>
    private void Tracks_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Track track)
        {
            App.PlayerBar.PlayFrom(ViewModel.Tracks, ViewModel.Tracks.IndexOf(track));
        }
    }

    // ---- Add to playlist, Step 11 ----------------------------------------------------------

    private void Tracks_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        DependencyObject? node = e.OriginalSource as DependencyObject;

        while (node is not null and not ListViewItem)
        {
            node = VisualTreeHelper.GetParent(node);
        }

        _rightClicked = (node as ListViewItem)?.Content as Track;
    }

    /// <summary>
    /// Read synchronously, not through Task.Run like the rest of the app. A menu cannot wait for an
    /// await: the flyout is already on screen by the time one would resume, so the submenu would
    /// visibly pop in. This is a handful of rows off a local file, and it runs once per right-click.
    /// </summary>
    private void TrackMenu_Opening(object sender, object e)
    {
        AddToPlaylistMenu.Items.Clear();

        foreach (Playlist playlist in App.Playlists.GetPlaylists())
        {
            MenuFlyoutItem item = new() { Text = playlist.Name, Tag = playlist.Id };
            item.Click += AddToPlaylist_Click;
            AddToPlaylistMenu.Items.Add(item);
        }

        if (AddToPlaylistMenu.Items.Count > 0)
        {
            AddToPlaylistMenu.Items.Add(new MenuFlyoutSeparator());
        }

        MenuFlyoutItem create = new() { Text = "New playlist..." };
        create.Click += NewPlaylistWithTrack_Click;
        AddToPlaylistMenu.Items.Add(create);
    }

    private async void AddToPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (_rightClicked is not Track track)
        {
            return;
        }

        long playlistId = (long)((MenuFlyoutItem)sender).Tag;
        await Task.Run(() => App.Playlists.AddTrack(playlistId, track.Id));
    }

    private async void NewPlaylistWithTrack_Click(object sender, RoutedEventArgs e)
    {
        if (_rightClicked is not Track track)
        {
            return;
        }

        string? name = await NamePrompt.ShowAsync(XamlRoot, "New playlist", "Create", string.Empty);

        if (name is null)
        {
            return;
        }

        await Task.Run(() =>
        {
            long playlistId = App.Playlists.Create(name);
            App.Playlists.AddTrack(playlistId, track.Id);
        });
    }
}
