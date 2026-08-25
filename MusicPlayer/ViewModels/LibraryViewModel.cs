using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using MusicPlayer.Data;
using MusicPlayer.Models;

namespace MusicPlayer.ViewModels;

/// <summary>
/// The full track table for LibraryPage: its sort, its filter, and the two buttons above it.
///
/// A plain List, not an ObservableCollection: the list is only ever replaced wholesale, and a
/// Clear plus one Add per track would fire thousands of notifications on a real library.
/// One notification for the whole list is both correct and far lighter.
/// </summary>
public sealed class LibraryViewModel : ObservableObject
{
    private readonly LibraryRepository _library;
    private readonly DispatcherQueueTimer _filterDebounce;

    private List<Track> _tracks = [];
    private string _filter = string.Empty;
    private int _folderCount;

    /// <summary>
    /// Must be constructed on the UI thread: the debounce timer comes from that thread's queue.
    /// </summary>
    public LibraryViewModel(LibraryRepository library)
    {
        _library = library;

        // Without this, every keystroke in the filter box runs a full table query.
        // Same 200ms as SearchViewModel, for the same reason.
        _filterDebounce = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _filterDebounce.Interval = TimeSpan.FromMilliseconds(200);
        _filterDebounce.IsRepeating = false;
        _filterDebounce.Tick += async (_, _) => await LoadAsync();
    }

    /// <summary>
    /// The rows on screen, already sorted, filtered and numbered. The setter re-announces the three
    /// derived empty-state flags with it, because all four change together and the page would
    /// otherwise show a stale empty state over a full list.
    /// </summary>
    public List<Track> Tracks
    {
        get => _tracks;
        private set
        {
            Set(ref _tracks, value);
            Raise(nameof(IsEmpty));
            Raise(nameof(HasTracks));
            Raise(nameof(FilterFoundNothing));
        }
    }

    /// <summary>
    /// Which column the table is ordered by. It must be raised on change even though nothing binds it
    /// directly: SortMarker takes it as an x:Bind function argument, and an x:Bind function only
    /// re-evaluates when one of its arguments does.
    /// </summary>
    public TrackSort Sort { get; private set; } = TrackSort.Number;

    /// <summary>
    /// Whether the current sort is reversed. Read by SortGlyph and by LoadAsync; the header chevron
    /// binds to SortGlyph rather than to this.
    /// </summary>
    public bool Descending { get; private set; }

    /// <summary>The watched-folder count beside the page title. design-plan 5.1.</summary>
    public string FolderBadge => _folderCount == 1 ? "1 FOLDER" : $"{_folderCount} FOLDERS";

    /// <summary>Segoe MDL2 chevrons. Up for ascending, down for descending.</summary>
    public string SortGlyph => Descending ? "\uE70D" : "\uE70E";

    /// <summary>
    /// Shows the caret and the 2px underline on the sorted column only. Gate 0 chose that pair.
    ///
    /// Takes the current sort as an argument rather than reading the property, because an x:Bind
    /// function only re-evaluates when one of its arguments is an observable path — called with a
    /// bare string literal it would evaluate once at load and never move again.
    /// </summary>
    public Visibility SortMarker(TrackSort sort, string column)
        => sort.ToString() == column ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Drives x:Load on the empty state, which avoids needing a bool-to-Visibility converter.</summary>
    public bool IsEmpty => _tracks.Count == 0 && _filter.Length == 0;

    /// <summary>
    /// The inverse of IsEmpty, and it exists separately because x:Load takes a plain bool and cannot
    /// invert one. Same shape as HasArt / HasNoArt on the transport.
    /// </summary>
    public bool HasTracks => _tracks.Count > 0;

    /// <summary>
    /// A different state from an empty library, and it needs different words. SearchPage already
    /// makes the same distinction.
    /// </summary>
    public bool FilterFoundNothing => _tracks.Count == 0 && _filter.Length > 0;

    /// <summary>
    /// Runs the table query and republishes the whole list. Called on every page visit, after a sort,
    /// and 200ms after the last filter keystroke. The SQL runs inside Task.Run because a real library
    /// is thousands of rows, and the sort and filter are snapshotted first so a query cannot be
    /// affected by a change made while it was running. The # column is numbered HERE and not read
    /// from the database, because it is the row's place on screen rather than a fact about the file.
    /// </summary>
    public async Task LoadAsync()
    {
        string filter = _filter;
        TrackSort sort = Sort;
        bool descending = Descending;

        (List<Track> loaded, int folders) = await Task.Run(() => (
            _library.GetTracks(sort, descending, filter),
            _library.GetFolders().Count));

        _folderCount = folders;
        Raise(nameof(FolderBadge));

        // The # column is the row's place in the list on screen, so it is numbered here rather
        // than read from the database: the same track is #3 filtered and #41 unfiltered.
        for (int i = 0; i < loaded.Count; i++)
        {
            loaded[i].Ordinal = i + 1;
        }

        Tracks = loaded;
    }

    /// <summary>
    /// Clicking the header of the column already sorted reverses it. Clicking any other column
    /// sorts by it ascending.
    /// </summary>
    public async Task SortByAsync(TrackSort sort)
    {
        Descending = Sort == sort && !Descending;
        Sort = sort;

        Raise(nameof(Sort));

        // Descending has no binding of its own today - the chevron reads SortGlyph. It is raised
        // anyway: dropping a change notification for a public property is how a future binding ends
        // up silently never updating, which is the same failure A7b documents for the shell.
        Raise(nameof(Descending));

        Raise(nameof(SortGlyph));

        await LoadAsync();
    }

    /// <summary>
    /// Takes what the user typed in the filter box and schedules a reload 200ms later. Restarting the
    /// timer on each keystroke is what stops a fast typist running one full table query per letter.
    /// The filter is trimmed, so trailing spaces cannot make a match fail for no visible reason.
    /// </summary>
    public void SetFilter(string filter)
    {
        _filter = filter.Trim();
        _filterDebounce.Stop();
        _filterDebounce.Start();
    }
}
