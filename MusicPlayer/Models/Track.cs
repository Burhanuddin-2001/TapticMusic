namespace MusicPlayer.Models;

/// <summary>
/// One local audio file, as the app reads it back out of the tracks table.
///
/// file_size, mtime_utc and play_count are still deliberately absent: nothing displays them. They
/// live on ScannedTrack in Data/LibraryRepository.cs, which is the only thing that writes them.
///
/// DO NOT RENAME A PROPERTY HERE without grepping the two ResourceDictionary templates first.
/// Trap: see TRAPS.md T-02.
/// </summary>
public sealed class Track
{
    /// <summary>The tracks table rowid. Zero means a Track that never came from the database.</summary>
    public long Id { get; init; }

    /// <summary>Full path on disk, and the only thing AudioService needs to open the file.</summary>
    public required string Path { get; init; }

    /// <summary>Display title. Never null: the scanner falls back to the file name.</summary>
    public required string Title { get; init; }

    /// <summary>Display artist. Never null: the scanner falls back to "Unknown artist".</summary>
    public required string Artist { get; init; }

    /// <summary>Display album, and half of the key the art cache hashes. Never null.</summary>
    public required string Album { get; init; }

    /// <summary>
    /// Length in milliseconds, straight from the tag. Zero for a file whose tag could not be read,
    /// which is why the row shows 0:00 rather than blank.
    /// </summary>
    public long DurationMs { get; init; }

    /// <summary>
    /// The tag reader could not open this file. Step 10a is the first reader: the track row marks
    /// it per settled fact D-10 rather than hiding it.
    /// </summary>
    public bool Unreadable { get; init; }

    /// <summary>
    /// File name of this album's cover inside the artcache folder, or null / empty when there is
    /// none. Step 12. Deliberately the key and not an image: the decode belongs to whoever draws
    /// it, so the model stays free of a UI type and the scanner stays free of one too (A11).
    ///
    /// null and empty mean different things to the SCANNER — see V3Sql in Data/Db.cs. To a reader
    /// they are the same thing, so read it through string.IsNullOrEmpty and not a null check.
    /// </summary>
    public string? ArtKey { get; init; }

    /// <summary>
    /// 1-based position in the list currently on screen, which is what the table's # column shows.
    /// Settable, and not read from the database, because it belongs to the list and not to the
    /// file: the same track is #3 filtered and #41 unfiltered.
    /// </summary>
    public int Ordinal { get; set; }
}
