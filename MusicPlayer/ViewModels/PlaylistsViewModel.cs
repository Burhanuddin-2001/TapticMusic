using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using MusicPlayer.Data;
using MusicPlayer.Models;

namespace MusicPlayer.ViewModels;

/// <summary>
/// The Playlists page, Step 11. One page holding two views: an index of playlists, and one open
/// playlist. Selected being null or not is the whole of that switch — the XAML x:Loads on it.
///
/// Not a second Page type, because the sidebar rail owns navigation and points at exactly one
/// PlaylistsPage. Leaving the page and coming back lands on the index, since page ViewModels are
/// new-ed per visit and deliberately not cached (A4).
/// </summary>
public sealed class PlaylistsViewModel : ObservableObject
{
    /// <summary>
    /// Which covers each tile shows is seeded from this, so it is fixed for one run of the app
    /// and different on the next. A static readonly field runs its initialiser ONCE per process,
    /// which is exactly "per startup" - and per startup is the requirement, not per visit.
    /// LoadAsync runs on every navigation to this page, because page ViewModels are new-ed per
    /// visit and never cached (A4), so an unseeded shuffle would redeal every tile each time the
    /// user tabbed back and the index would never sit still.
    /// </summary>
    private static readonly int SessionSeed = Random.Shared.Next();

    private readonly PlaylistRepository _playlists;

    private List<PlaylistTile> _all = [];
    private Playlist? _selected;

    /// <summary>
    /// Stores the repository. Nothing is loaded here - LoadAsync does that from OnNavigatedTo,
    /// because a constructor cannot await and the index needs two queries.
    /// </summary>
    public PlaylistsViewModel(PlaylistRepository playlists) => _playlists = playlists;

    /// <summary>
    /// The index. A plain List replaced wholesale, the same as LibraryViewModel.Tracks and for the
    /// same reason: it only ever changes all at once.
    /// </summary>
    public List<PlaylistTile> Playlists => _all;

    /// <summary>
    /// The open playlist's tracks. ObservableCollection and not a plain List, which is the one
    /// place this page departs from the house style: WinUI's drag-to-reorder mutates the bound
    /// IList in place, and a List plus a single Raise cannot express a move. It is in the BCL, so
    /// it is not a sixth package, and A6 governs ObservableObject rather than collection types.
    /// </summary>
    public ObservableCollection<Track> Tracks { get; } = [];

    /// <summary>
    /// The open playlist, or null on the index. Null-versus-not-null IS the view switch: there is no
    /// second Page type and no separate mode flag, and the six x:Load flags below all derive from
    /// this one value. The setter re-announces all of them, which is what makes the swap atomic.
    /// </summary>
    public Playlist? Selected
    {
        get => _selected;
        private set
        {
            Set(ref _selected, value);
            RaiseViews();
        }
    }

    // x:Load flags. ShowIndexView and ShowDetail pick the view; the rest pick what is inside it.
    /// <summary>Show the index half of the page at all. The outer switch; the three flags after it choose what goes inside.</summary>
    public bool ShowIndexView => _selected is null;

    /// <summary>Show the tile grid - on the index, with at least one playlist.</summary>
    public bool ShowIndex => _selected is null && _all.Count > 0;

    /// <summary>Show the "no playlists yet" state. The counterpart to ShowIndex, spelled out because x:Load cannot invert a bool.</summary>
    public bool ShowNoPlaylists => _selected is null && _all.Count == 0;

    /// <summary>Show the open-playlist half of the page. The inverse of ShowIndexView, for the same x:Load reason.</summary>
    public bool ShowDetail => _selected is not null;

    /// <summary>Show the open playlist's track table. Requires a selection, so it cannot flash on during the swap.</summary>
    public bool DetailHasTracks => _selected is not null && Tracks.Count > 0;

    /// <summary>Show the "this playlist is empty" state. A different message from having no playlists at all.</summary>
    public bool DetailIsEmpty => _selected is not null && Tracks.Count == 0;

    /// <summary>The index header's badge text. Pre-formatted here so no fourth converter is needed.</summary>
    public string PlaylistCount => _all.Count.ToString();

    /// <summary>
    /// Loads every playlist and builds its cover tile, on every visit to the page. Both queries go in
    /// one Task.Run, and the art keys come back as a single SELECT DISTINCT for ALL playlists rather
    /// than one query each. The covers are decoded after the await, never inside Task.Run, because a
    /// BitmapImage is a DependencyObject. Which covers a tile shows is a pure function of a
    /// per-process seed and the playlist id, so it is stable for a whole run without being cached.
    /// </summary>
    public async Task LoadAsync()
    {
        (List<Playlist> playlists, Dictionary<long, List<string>> artKeys) = await Task.Run(
            () => (_playlists.GetPlaylists(), _playlists.GetArtKeys()));

        // Decoded AFTER the await and never inside Task.Run. A BitmapImage is a DependencyObject,
        // so building one on a pool thread throws RPC_E_WRONG_THREAD. SQL off-thread, art on.
        //
        // Read once rather than per cover: this is four decodes per tile.
        int coverPixels = (int)(double)Application.Current.Resources["Size.Tile"];

        _all = playlists.Select(p => BuildTile(p, artKeys, coverPixels)).ToList();

        Raise(nameof(Playlists));
        Raise(nameof(PlaylistCount));
        RaiseViews();
    }

    /// <summary>
    /// Opens a playlist into the detail view. The tracks are fetched BEFORE Selected is assigned, so
    /// the view switches to a list that is already full rather than flashing its empty state.
    /// </summary>
    public async Task OpenAsync(Playlist playlist)
    {
        await FillTracksAsync(playlist.Id);
        Selected = playlist;
    }

    /// <summary>
    /// Goes back to the index. Clearing Tracks first and then Selected matters: Selected is what
    /// flips the view, so clearing it last means the detail view is never seen empty.
    /// </summary>
    public void Close()
    {
        Tracks.Clear();
        Selected = null;
    }

    /// <summary>
    /// Creates a playlist and reloads the index so its tile appears. No uniqueness check - two
    /// playlists may share a name, because the id is what identifies one.
    /// </summary>
    public async Task CreateAsync(string name)
    {
        await Task.Run(() => _playlists.Create(name));
        await LoadAsync();
    }

    /// <summary>
    /// Renames a playlist and reloads the index. The last line re-finds the selection afterwards,
    /// because LoadAsync rebuilds every tile from scratch and the old Playlist record - which the
    /// detail header is bound to - would otherwise still be showing the previous name.
    /// </summary>
    public async Task RenameAsync(long id, string name)
    {
        await Task.Run(() => _playlists.Rename(id, name));
        await LoadAsync();
        Selected = _selected is null ? null : Find(id);
    }

    /// <summary>
    /// Deletes a playlist and its rows, and closes the detail view first if the open one is the one
    /// going. The playlist_tracks rows go with it through ON DELETE CASCADE, so there is nothing to
    /// clean up here.
    /// </summary>
    public async Task DeleteAsync(long id)
    {
        await Task.Run(() => _playlists.Delete(id));

        if (_selected?.Id == id)
        {
            Close();
        }

        await LoadAsync();
    }

    /// <summary>
    /// Called once when a drag finishes, never per row. The collection has already been reordered
    /// in place by the ListView, so its current order IS the new order.
    ///
    /// The reload afterwards is not redundant: Ordinal is a plain settable field on Track with no
    /// change notification, so the number cells would keep their pre-drag values without it.
    /// </summary>
    public async Task SaveOrderAsync()
    {
        if (_selected is not Playlist playlist)
        {
            return;
        }

        List<long> order = Tracks.Select(t => t.Id).ToList();
        await Task.Run(() => _playlists.ReplaceTracks(playlist.Id, order));
        await FillTracksAsync(playlist.Id);
    }

    /// <summary>
    /// Remove is a position, not a track id, because the same track is allowed in a playlist twice.
    /// </summary>
    public async Task RemoveAtAsync(int index)
    {
        if (_selected is null || index < 0 || index >= Tracks.Count)
        {
            return;
        }

        Tracks.RemoveAt(index);
        await SaveOrderAsync();
        await LoadAsync();
        Selected = Find(_selected.Id);
    }

    private async Task FillTracksAsync(long playlistId)
    {
        List<Track> loaded = await Task.Run(() => _playlists.GetTracks(playlistId));

        // The # column is the row's place in the list on screen, the same as the Library.
        for (int i = 0; i < loaded.Count; i++)
        {
            loaded[i].Ordinal = i + 1;
        }

        Tracks.Clear();

        foreach (Track track in loaded)
        {
            Tracks.Add(track);
        }

        RaiseViews();
    }

    private static PlaylistTile BuildTile(
        Playlist playlist,
        Dictionary<long, List<string>> artKeys,
        int coverPixels)
    {
        List<string> covers = artKeys.TryGetValue(playlist.Id, out List<string>? available)
            ? PickCovers(playlist.Id, available)
            : [];

        return new PlaylistTile
        {
            Playlist = playlist,
            Cover0 = DecodeCover(covers, 0, coverPixels),
            Cover1 = DecodeCover(covers, 1, coverPixels),
            Cover2 = DecodeCover(covers, 2, coverPixels),
            Cover3 = DecodeCover(covers, 3, coverPixels),
        };
    }

    /// <summary>
    /// Four covers if the playlist has four different albums in it, otherwise two, otherwise
    /// however few there are.
    ///
    /// THREE is deliberately drawn as two. A 2x2 with one empty cell reads as a bug, and the
    /// third cover is not lost - the seed changes next launch and a different two come up.
    ///
    /// Seeded from the run AND the playlist id, so this is a pure function for the whole session:
    /// every revisit recomputes the same answer without anything being cached anywhere.
    /// </summary>
    private static List<string> PickCovers(long playlistId, List<string> available)
    {
        if (available.Count < 2)
        {
            return available;
        }

        List<string> shuffled = [.. available];
        Random random = new(HashCode.Combine(SessionSeed, playlistId));

        for (int i = shuffled.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }

        return shuffled.GetRange(0, available.Count >= 4 ? 4 : 2);
    }

    /// <summary>
    /// Decoded at the tile's own edge in real pixels. A four-up cell is half that, so those get
    /// 2x and look sharp; a one-up gets 1x. That is the trade that keeps a twenty-playlist index
    /// off tens of megabytes of bitmap.
    /// </summary>
    private static ImageSource? DecodeCover(List<string> covers, int index, int pixelWidth)
        => index < covers.Count ? AlbumArt.Decode(covers[index], pixelWidth) : null;

    private Playlist? Find(long id)
        => _all.FirstOrDefault(t => t.Playlist.Id == id)?.Playlist;

    private void RaiseViews()
    {
        Raise(nameof(ShowIndexView));
        Raise(nameof(ShowIndex));
        Raise(nameof(ShowNoPlaylists));
        Raise(nameof(ShowDetail));
        Raise(nameof(DetailHasTracks));
        Raise(nameof(DetailIsEmpty));
    }
}
