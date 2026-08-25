namespace MusicPlayer.Data;

// The shapes LibraryRepository hands back, lifted out of it in Step 15. That file was 522 lines,
// the largest in the app, and four of the six types declared in it were small records and an enum
// that six other files reach for. Same namespace, so nothing changed at any call site.

/// <summary>
/// One audio file as the scanner found it on disk. Carries the write-only columns that Track
/// does not, so LibraryScanner can hand over plain data and keep every SQL statement in Data/.
/// </summary>
public sealed record ScannedTrack(
    string Path,
    string Title,
    string Artist,
    string Album,
    long DurationMs,
    long FileSize,
    string MtimeUtc,
    bool Unreadable,
    string ArtKey);

/// <summary>
/// One album, as the Dashboard deal offers it. Step 14. Derived by GROUP BY at query time, the same
/// as every other album fact in this app - there is no albums table and there should not be.
/// </summary>
public sealed record AlbumPick(string Artist, string Album, string? ArtKey, int TrackCount, long TotalMs);

/// <summary>
/// One artist, as the Dashboard deal offers it. Step 14. ArtKey is one of the artist's album covers,
/// picked by the query rather than chosen - an artist has no cover of their own.
/// </summary>
public sealed record ArtistPick(string Artist, string? ArtKey, int TrackCount, long TotalMs);

/// <summary>
/// Which sortable column header the track table is ordered by. The five values match the five
/// columns in design-plan section 5.1. Number is the library's natural order, which is what the
/// table showed before headers became clickable.
/// </summary>
public enum TrackSort
{
    /// <summary>The library's natural order - the order files were first indexed.</summary>
    Number,

    /// <summary>ORDER BY title, case-insensitive.</summary>
    Title,

    /// <summary>ORDER BY artist then title, so an artist's tracks stay grouped and readable.</summary>
    Artist,

    /// <summary>ORDER BY album then title, for the same reason as Artist.</summary>
    Album,

    /// <summary>ORDER BY duration_ms - a numeric sort, not a text one.</summary>
    Duration,
}
