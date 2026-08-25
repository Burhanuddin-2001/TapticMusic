using Microsoft.UI.Dispatching;
using MusicPlayer.Data;
using MusicPlayer.Models;

namespace MusicPlayer.ViewModels;

/// <summary>
/// Results as you type, grouped Artists then Albums then Tracks. design-plan section 5.1.
/// </summary>
public sealed class SearchViewModel : ObservableObject
{
    private const int NameLimit = 20;
    private const int TrackLimit = 100;

    private readonly LibraryRepository _library;
    private readonly DispatcherQueueTimer _debounce;

    private string _term = string.Empty;
    private List<string> _artists = [];
    private List<string> _albums = [];
    private List<Track> _tracks = [];
    private bool _hasSearched;

    /// <summary>
    /// Must be constructed on the UI thread: the debounce timer comes from that thread's queue.
    /// </summary>
    public SearchViewModel(LibraryRepository library)
    {
        _library = library;

        // Without this, every keystroke fires three LIKE queries.
        _debounce = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _debounce.Interval = TimeSpan.FromMilliseconds(200);
        _debounce.IsRepeating = false;
        _debounce.Tick += async (_, _) => await RunAsync();
    }

    /// <summary>Matching artist names, capped at 20. Bound straight to the Artists group's ListView.</summary>
    public List<string> Artists => _artists;

    /// <summary>Matching album names, capped at 20.</summary>
    public List<string> Albums => _albums;

    /// <summary>
    /// Matching tracks, capped at 100. That cap is why the Search page can get away with not
    /// virtualizing, unlike the Library.
    /// </summary>
    public List<Track> Tracks => _tracks;

    /// <summary>
    /// The artist group's badge text. Pre-formatted as a string here rather than converted in XAML,
    /// which is part of why the app still needs only three converters.
    /// </summary>
    public string ArtistCount => _artists.Count.ToString();

    /// <summary>The album group's badge text. See ArtistCount.</summary>
    public string AlbumCount => _albums.Count.ToString();

    /// <summary>The track group's badge text. Reads 100 when the cap is hit, not the true total.</summary>
    public string TrackCount => _tracks.Count.ToString();

    /// <summary>
    /// Whether to show the Artists group at all. Each group hides itself independently, so a term
    /// matching only albums shows one section rather than two empty headings.
    /// </summary>
    public bool HasArtists => _artists.Count > 0;

    /// <summary>Whether to show the Albums group. See HasArtists.</summary>
    public bool HasAlbums => _albums.Count > 0;

    /// <summary>Whether to show the Tracks group. See HasArtists.</summary>
    public bool HasTracks => _tracks.Count > 0;

    /// <summary>An empty box is a different state from a search that found nothing. design-plan 5.1.</summary>
    public bool HasNotSearched => _term.Length == 0;

    /// <summary>
    /// The "nothing matched" empty state. It requires _hasSearched, so it cannot appear in the gap
    /// between a keystroke and its results - only after a query has actually come back empty.
    /// </summary>
    public bool HasNoResults => _hasSearched && !HasArtists && !HasAlbums && !HasTracks;

    /// <summary>
    /// Takes what the user has typed and schedules a search 200ms later. Called on every keystroke by
    /// SearchPage, and it restarts the timer each time rather than letting a half-elapsed interval
    /// fire, so a fast typist causes exactly one query. Clearing the box is handled immediately and
    /// separately: the results are emptied on the spot and the timer is stopped, which is what
    /// returns the page to "not searched yet" rather than "found nothing".
    /// </summary>
    public void SetTerm(string term)
    {
        _term = term.Trim();

        if (_term.Length == 0)
        {
            _debounce.Stop();
            _artists = [];
            _albums = [];
            _tracks = [];
            _hasSearched = false;
            RaiseResults();
            return;
        }

        // Restart rather than let a half-elapsed interval fire early.
        _debounce.Stop();
        _debounce.Start();
    }

    /// <summary>
    /// Runs all three queries on a background thread and publishes the results. It snapshots the term
    /// first and compares it again afterwards, so a slow query whose keystroke has been superseded
    /// throws its own results away - without that, an earlier search could overwrite a later one.
    /// </summary>
    private async Task RunAsync()
    {
        string term = _term;

        (List<string> artists, List<string> albums, List<Track> tracks) = await Task.Run(() => (
            _library.SearchArtists(term, NameLimit),
            _library.SearchAlbums(term, NameLimit),
            _library.SearchTracks(term, TrackLimit)));

        // A newer keystroke started while this was running, so these results are already stale.
        if (term != _term)
        {
            return;
        }

        _artists = artists;
        _albums = albums;
        _tracks = tracks;
        _hasSearched = true;
        RaiseResults();
    }

    /// <summary>
    /// Announces all eleven derived properties in one go. They all change together on every result,
    /// so raising them individually at each assignment would just repaint the page repeatedly.
    /// </summary>
    private void RaiseResults()
    {
        Raise(nameof(Artists));
        Raise(nameof(Albums));
        Raise(nameof(Tracks));
        Raise(nameof(ArtistCount));
        Raise(nameof(AlbumCount));
        Raise(nameof(TrackCount));
        Raise(nameof(HasArtists));
        Raise(nameof(HasAlbums));
        Raise(nameof(HasTracks));
        Raise(nameof(HasNotSearched));
        Raise(nameof(HasNoResults));
    }
}
