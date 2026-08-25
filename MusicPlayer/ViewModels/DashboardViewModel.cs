using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using MusicPlayer.Data;
using MusicPlayer.Models;

namespace MusicPlayer.ViewModels;

/// <summary>
/// The Dashboard: one hand of three random picks, and the library's four numbers under it. Step 14.
///
/// This replaced "recently added" and "recently played". Both told the user something they already
/// knew, and neither earned the landing page. The hand answers the only question the page is really
/// asked - what do I play now - and the accent button beside it answers the impatient version.
/// </summary>
public sealed class DashboardViewModel : ObservableObject
{
    /// <summary>
    /// The hand, dealt ONCE per process. Leaving the page and coming back keeps it; only Reroll and
    /// a restart change it.
    ///
    /// Static for the same reason PlaylistsViewModel seeds its covers statically: page ViewModels
    /// are new-ed per visit and never cached (A4), so an instance field would redeal on every
    /// navigation and Reroll would stop meaning anything - the hand would already be changing on
    /// its own.
    ///
    /// It holds the PICKED DATA and not the decoded covers, so no DependencyObject is parked in a
    /// static for the life of the process. Three bitmaps get rebuilt per visit, which is cheap.
    /// </summary>
    private static Hand? _hand;

    private readonly LibraryRepository _library;
    private readonly PlaylistRepository _playlists;

    private (int Tracks, int Albums, int Artists, long TotalMs) _stats;
    private List<DealCard> _deal = [];

    /// <summary>
    /// Stores the two repositories the page reads. It deals nothing here: the hand is dealt inside
    /// LoadAsync, on a background thread, because dealing means running SQL.
    /// </summary>
    public DashboardViewModel(LibraryRepository library, PlaylistRepository playlists)
    {
        _library = library;
        _playlists = playlists;
    }

    /// <summary>N0 so a five-figure library reads as 12,480 rather than 12480.</summary>
    public string TrackCount => _stats.Tracks.ToString("N0");

    /// <summary>
    /// Distinct album count, thousands-separated. Counted as DISTINCT album, so two artists both
    /// having a "Greatest Hits" collapse into one - unlike the deal queries, which group by the pair.
    /// </summary>
    public string AlbumCount => _stats.Albums.ToString("N0");

    /// <summary>Distinct artist count, thousands-separated. See AlbumCount.</summary>
    public string ArtistCount => _stats.Artists.ToString("N0");

    /// <summary>
    /// Whole hours, with HOURS as the label beside it. This used to be TotalMs fed through
    /// DurationToStringConverter, whose hours component is unbounded, so a large library rendered
    /// as "900:14:07" - a duration format doing a quantity's job.
    /// </summary>
    public string TotalHours => TimeSpan.FromMilliseconds(_stats.TotalMs).TotalHours.ToString("N0");

    /// <summary>
    /// The three dealt cards - an album, an artist and a playlist - bound to the tile ListView. It is
    /// two cards, not three, when there are no playlists at all; the wrap panel closes the gap.
    /// </summary>
    public List<DealCard> Deal => _deal;

    /// <summary>
    /// Drives x:Load on the "No music yet" state. An empty library shows that and nothing else - no
    /// hand, no stat strip - because dealing from nothing would show three blank squares.
    /// </summary>
    public bool IsEmpty => _stats.Tracks == 0;

    /// <summary>
    /// The inverse of IsEmpty, which exists separately because x:Load takes a plain bool and cannot
    /// invert one.
    /// </summary>
    public bool HasTracks => _stats.Tracks > 0;

    /// <summary>
    /// Loads the stat strip and the hand, and runs on EVERY visit to the page. It reuses the existing
    /// static hand if there is one, so navigating away and back shows the same three cards - dealing
    /// here unconditionally is what would make the Reroll button meaningless. All the SQL happens
    /// inside one Task.Run, and the covers are decoded AFTER the await, because a BitmapImage built
    /// on a pool thread throws RPC_E_WRONG_THREAD.
    /// </summary>
    public async Task LoadAsync()
    {
        (_stats, Hand hand, Dictionary<long, List<string>> artKeys) = await Task.Run(()
            => (_library.GetStats(), _hand ?? DealHand(), _playlists.GetArtKeys()));

        _hand = hand;

        // Built AFTER the await and never inside Task.Run: a BitmapImage is a DependencyObject, so
        // creating one on a pool thread throws RPC_E_WRONG_THREAD. SQL off-thread, art on.
        _deal = BuildCards(hand, artKeys);

        Raise(nameof(TrackCount));
        Raise(nameof(AlbumCount));
        Raise(nameof(ArtistCount));
        Raise(nameof(TotalHours));
        Raise(nameof(Deal));
        Raise(nameof(IsEmpty));
        Raise(nameof(HasTracks));
    }

    /// <summary>
    /// Clearing the hand is the whole of it - LoadAsync deals a fresh one whenever there is not one
    /// already, so there is no second code path to keep in step with the first.
    /// </summary>
    public async Task RerollAsync()
    {
        _hand = null;
        await LoadAsync();
    }

    /// <summary>
    /// The whole library, for the shuffle button. TrackSort.Number is the library's natural order;
    /// ShuffleAll reorders it anyway, so the sort only decides what "unshuffled" means afterwards.
    /// </summary>
    public async Task<List<Track>> AllTracksAsync()
        => await Task.Run(() => _library.GetTracks(TrackSort.Number, descending: false, filter: null));

    /// <summary>
    /// What a clicked card plays. An album and an artist come back in path order, which is the
    /// closest this schema gets to running order; a playlist comes back in the user's own order.
    /// </summary>
    public async Task<List<Track>> TracksForAsync(DealCard card)
        => await Task.Run(() => card.Payload switch
        {
            AlbumPick album => _library.GetAlbumTracks(album.Artist, album.Album),
            ArtistPick artist => _library.GetArtistTracks(artist.Artist),
            Playlist playlist => _playlists.GetTracks(playlist.Id),
            _ => [],
        });

    private Hand DealHand()
        => new(_library.PickRandomAlbum(), _library.PickRandomArtist(), _playlists.PickRandom());

    /// <summary>
    /// Only the picks that exist become cards. A library with no playlists deals two, and the wrap
    /// grid closes up around the gap - which is why there is no placeholder card here. The nudge to
    /// make a first playlist already lives in the Playlists page's own empty state.
    /// </summary>
    private static List<DealCard> BuildCards(Hand hand, Dictionary<long, List<string>> artKeys)
    {
        int coverPixels = (int)(double)Application.Current.Resources["Size.Tile"];
        List<DealCard> cards = [];

        if (hand.Album is AlbumPick album)
        {
            cards.Add(new DealCard
            {
                Title = album.Album,
                Subtitle = $"Album · {album.Artist}",
                Payload = album,
                Cover0 = DecodeCover(album.ArtKey, coverPixels),
            });
        }

        if (hand.Artist is ArtistPick artist)
        {
            cards.Add(new DealCard
            {
                Title = artist.Artist,
                Subtitle = $"Artist · {TrackWord(artist.TrackCount)}",
                Payload = artist,
                Cover0 = DecodeCover(artist.ArtKey, coverPixels),
            });
        }

        if (hand.Playlist is Playlist playlist)
        {
            List<string> covers = artKeys.TryGetValue(playlist.Id, out List<string>? available)
                ? available
                : [];

            // Handed over raw, up to four. ArtMosaic owns the count-to-layout rule, including
            // drawing three as two, so there is no second copy of it here.
            cards.Add(new DealCard
            {
                Title = playlist.Name,
                Subtitle = $"Playlist · {playlist.TrackCountLabel}",
                Payload = playlist,
                Cover0 = DecodeCover(At(covers, 0), coverPixels),
                Cover1 = DecodeCover(At(covers, 1), coverPixels),
                Cover2 = DecodeCover(At(covers, 2), coverPixels),
                Cover3 = DecodeCover(At(covers, 3), coverPixels),
            });
        }

        return cards;
    }

    private static string? At(List<string> covers, int index)
        => index < covers.Count ? covers[index] : null;

    private static ImageSource? DecodeCover(string? artKey, int pixelWidth)
        => string.IsNullOrEmpty(artKey) ? null : AlbumArt.Decode(artKey, pixelWidth);

    /// <summary>
    /// The singular rule Playlist.TrackCountLabel already applies. It cannot be reused here: it is a
    /// member of that record, and AlbumPick and ArtistPick are records of their own.
    /// </summary>
    private static string TrackWord(int count) => count == 1 ? "1 track" : $"{count} tracks";

    /// <summary>
    /// One dealt hand. A record so a reroll is one assignment, and nullable members because a fresh
    /// library has no playlists and an empty one has no albums either.
    /// </summary>
    private sealed record Hand(AlbumPick? Album, ArtistPick? Artist, Playlist? Playlist);
}
