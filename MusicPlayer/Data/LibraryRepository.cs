using Microsoft.Data.Sqlite;
using MusicPlayer.Models;

namespace MusicPlayer.Data;

/// <summary>
/// Every SQL statement in the application lives here. Two rules, both non-negotiable:
/// every value is a parameter, and no identifier is ever built from user text.
/// </summary>
public sealed class LibraryRepository
{
    /// <summary>
    /// Every folder the user has added to the library, ordered by path so the Settings list is
    /// stable between visits. The scanner walks these; Settings lists them.
    /// </summary>
    public List<(long Id, string Path)> GetFolders()
    {
        List<(long, string)> folders = [];

        using SqliteConnection conn = Db.Open();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, path FROM folders ORDER BY path;";

        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            folders.Add((reader.GetInt64(0), reader.GetString(1)));
        }

        return folders;
    }

    /// <summary>
    /// Returns the id of the folder, whether it was just inserted or already watched. Adding the
    /// same folder twice is a normal thing for a user to do, not an error.
    /// </summary>
    public long AddFolder(string path)
    {
        using SqliteConnection conn = Db.Open();

        using SqliteCommand insert = conn.CreateCommand();
        insert.CommandText = """
            INSERT INTO folders (path, added_utc) VALUES (@path, @now)
            ON CONFLICT(path) DO NOTHING;
            """;
        insert.Parameters.AddWithValue("@path", path);
        insert.Parameters.AddWithValue("@now", Db.UtcNowText());
        insert.ExecuteNonQuery();

        using SqliteCommand select = conn.CreateCommand();
        select.CommandText = "SELECT id FROM folders WHERE path = @path;";
        select.Parameters.AddWithValue("@path", path);

        return (long)select.ExecuteScalar()!;
    }

    /// <summary>
    /// path to (size, mtime) for one folder. The scanner compares against this and skips any file
    /// whose size and modified time both still match, which is what makes a rescan of a few
    /// thousand files fast: no tag read at all for an unchanged file.
    ///
    /// OrdinalIgnoreCase because Windows paths are case-insensitive, so a casing difference must
    /// count as the same file. Keys keep the casing the database holds, so they are safe to
    /// delete by.
    /// </summary>
    public Dictionary<string, (long Size, string MtimeUtc, bool ArtChecked, bool Unreadable)> GetPathIndex(long folderId)
    {
        Dictionary<string, (long, string, bool, bool)> index = new(StringComparer.OrdinalIgnoreCase);

        using SqliteConnection conn = Db.Open();
        using SqliteCommand cmd = conn.CreateCommand();

        // ArtChecked, added in Step 12, is the third leg of the scanner's skip test. Without it a
        // file that was already indexed before art existed would be skipped forever and never get
        // a cover. NULL means never looked; empty means looked and found none, which counts as
        // checked, so the re-read happens exactly once per track and not on every scan.
        //
        // Unreadable is the fourth leg, added in Step 17, and it is what turns Rescan into a
        // repair: a row carrying a bad verdict is re-read instead of skipped forever.
        cmd.CommandText =
            "SELECT path, file_size, mtime_utc, art_key IS NOT NULL, unreadable FROM tracks WHERE folder_id = @folder;";
        cmd.Parameters.AddWithValue("@folder", folderId);

        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            index[reader.GetString(0)] =
                (reader.GetInt64(1), reader.GetString(2), reader.GetInt64(3) != 0, reader.GetInt64(4) != 0);
        }

        return index;
    }

    /// <summary>
    /// The whole scan result, in one transaction. Without the transaction SQLite fsyncs per row
    /// and a few thousand files take minutes instead of seconds. Both commands are prepared once
    /// outside their loops and only their parameter values change.
    /// </summary>
    public void WriteScan(long folderId, IReadOnlyList<ScannedTrack> found, IReadOnlyCollection<string> removedPaths)
    {
        using SqliteConnection conn = Db.Open();
        using SqliteTransaction tx = conn.BeginTransaction();

        if (removedPaths.Count > 0)
        {
            using SqliteCommand delete = conn.CreateCommand();
            delete.CommandText = "DELETE FROM tracks WHERE path = @path;";
            SqliteParameter path = delete.Parameters.Add("@path", SqliteType.Text);

            foreach (string gone in removedPaths)
            {
                path.Value = gone;
                delete.ExecuteNonQuery();
            }
        }

        if (found.Count > 0)
        {
            using SqliteCommand upsert = conn.CreateCommand();

            // added_utc is deliberately absent from the DO UPDATE list: re-scanning a file must
            // not make it "recently added" again.
            upsert.CommandText = """
                INSERT INTO tracks (path, folder_id, title, artist, album, duration_ms,
                                    file_size, mtime_utc, added_utc, unreadable, art_key)
                VALUES (@path, @folder, @title, @artist, @album, @dur,
                        @size, @mtime, @now, @bad, @art)
                ON CONFLICT(path) DO UPDATE SET
                    title       = excluded.title,
                    artist      = excluded.artist,
                    album       = excluded.album,
                    duration_ms = excluded.duration_ms,
                    file_size   = excluded.file_size,
                    mtime_utc   = excluded.mtime_utc,
                    unreadable  = excluded.unreadable,
                    art_key     = excluded.art_key;
                """;

            SqliteParameter path = upsert.Parameters.Add("@path", SqliteType.Text);
            SqliteParameter title = upsert.Parameters.Add("@title", SqliteType.Text);
            SqliteParameter artist = upsert.Parameters.Add("@artist", SqliteType.Text);
            SqliteParameter album = upsert.Parameters.Add("@album", SqliteType.Text);
            SqliteParameter duration = upsert.Parameters.Add("@dur", SqliteType.Integer);
            SqliteParameter size = upsert.Parameters.Add("@size", SqliteType.Integer);
            SqliteParameter mtime = upsert.Parameters.Add("@mtime", SqliteType.Text);
            SqliteParameter unreadable = upsert.Parameters.Add("@bad", SqliteType.Integer);
            SqliteParameter art = upsert.Parameters.Add("@art", SqliteType.Text);
            upsert.Parameters.AddWithValue("@folder", folderId);
            upsert.Parameters.AddWithValue("@now", Db.UtcNowText());

            foreach (ScannedTrack track in found)
            {
                path.Value = track.Path;
                title.Value = track.Title;
                artist.Value = track.Artist;
                album.Value = track.Album;
                duration.Value = track.DurationMs;
                size.Value = track.FileSize;
                mtime.Value = track.MtimeUtc;
                unreadable.Value = track.Unreadable ? 1 : 0;

                // Never null. The scanner has always looked by the time it builds a ScannedTrack,
                // so writing NULL here would tell the next scan to look again — see V3Sql.
                art.Value = track.ArtKey;
                upsert.ExecuteNonQuery();
            }
        }

        tx.Commit();
    }

    /// <summary>
    /// The whole track table, as the sortable headers and the filter box ask for it. A null or
    /// blank filter emits no WHERE clause at all, rather than a LIKE '%%' the planner has to chew.
    /// Ordering is built by OrderBy below, which is where the identifier whitelist lives.
    /// </summary>
    public List<Track> GetTracks(TrackSort sort, bool descending, string? filter)
    {
        using SqliteConnection conn = Db.Open();
        using SqliteCommand cmd = conn.CreateCommand();

        string where = string.Empty;

        if (!string.IsNullOrWhiteSpace(filter))
        {
            where = "WHERE title LIKE @q OR artist LIKE @q OR album LIKE @q";
            cmd.Parameters.AddWithValue("@q", Contains(filter));
        }

        cmd.CommandText = $"{TrackColumns} {where} ORDER BY {OrderBy(sort, descending)};";

        return ReadTracks(cmd);
    }

    /// <summary>
    /// Null when the row is gone, which is what a resumed track hits after its file was removed
    /// and a rescan cleaned up after it.
    /// </summary>
    public Track? GetTrackById(long id)
    {
        using SqliteConnection conn = Db.Open();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = $"{TrackColumns} WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);

        List<Track> found = ReadTracks(cmd);

        return found.Count > 0 ? found[0] : null;
    }

    /// <summary>
    /// The four numbers on the dashboard strip. Albums and artists are counted with DISTINCT
    /// rather than read from tables of their own, which is why those tables do not exist.
    /// </summary>
    public (int Tracks, int Albums, int Artists, long TotalMs) GetStats()
    {
        using SqliteConnection conn = Db.Open();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*), COUNT(DISTINCT album), COUNT(DISTINCT artist),
                   COALESCE(SUM(duration_ms), 0)
            FROM tracks;
            """;

        using SqliteDataReader reader = cmd.ExecuteReader();
        reader.Read();

        return (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt64(3));
    }

    /// <summary>
    /// One album at random, for the Dashboard deal. Step 14.
    ///
    /// GROUP BY is the PAIR, never album alone. GetStats above counts DISTINCT album, which collapses
    /// two different artists' "Greatest Hits" into one; a deal card doing the same would offer an
    /// album that is really two, and then play both.
    ///
    /// MAX(art_key) and never MIN. art_key is a three-state sentinel - NULL never looked, empty
    /// looked and found nothing, otherwise a file name - and both aggregates skip NULL, but MIN of
    /// an empty string beside a real file name returns the empty one and the cover silently
    /// vanishes. All of an album's rows normally carry one key; they diverge only when some were
    /// scanned before the art existed.
    ///
    /// ORDER BY RANDOM() is a literal in this file's own SQL, not an identifier built from user
    /// text, so A9 is untouched - the same standing as the interpolated PRAGMA in Db.cs.
    /// </summary>
    public AlbumPick? PickRandomAlbum()
    {
        using SqliteConnection conn = Db.Open();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT artist, album, MAX(art_key), COUNT(*), COALESCE(SUM(duration_ms), 0)
            FROM tracks
            GROUP BY artist, album
            ORDER BY RANDOM() LIMIT 1;
            """;

        using SqliteDataReader reader = cmd.ExecuteReader();

        return reader.Read()
            ? new AlbumPick(
                reader.GetString(0),
                reader.GetString(1),
                ArtKeyOrNull(reader, 2),
                reader.GetInt32(3),
                reader.GetInt64(4))
            : null;
    }

    /// <summary>
    /// One artist at random, for the Dashboard deal. Step 14. Same MAX(art_key) rule as above.
    /// </summary>
    public ArtistPick? PickRandomArtist()
    {
        using SqliteConnection conn = Db.Open();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT artist, MAX(art_key), COUNT(*), COALESCE(SUM(duration_ms), 0)
            FROM tracks
            GROUP BY artist
            ORDER BY RANDOM() LIMIT 1;
            """;

        using SqliteDataReader reader = cmd.ExecuteReader();

        return reader.Read()
            ? new ArtistPick(
                reader.GetString(0),
                ArtKeyOrNull(reader, 1),
                reader.GetInt32(2),
                reader.GetInt64(3))
            : null;
    }

    /// <summary>
    /// One album in playing order, as close as this schema gets to it. Step 14.
    ///
    /// ORDER BY path, not title. There is no track_no column, and files are named "01 - ..." far
    /// more often than not, so path order approximates the running order where title order does not.
    ///
    /// No unreadable filter, since Step 17: a tag read that failed is not a verdict on playability,
    /// so hiding the track here would contradict the click that now plays it.
    /// </summary>
    public List<Track> GetAlbumTracks(string artist, string album)
    {
        using SqliteConnection conn = Db.Open();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            {TrackColumns}
            WHERE artist = @artist AND album = @album
            ORDER BY path COLLATE NOCASE;
            """;
        cmd.Parameters.AddWithValue("@artist", artist);
        cmd.Parameters.AddWithValue("@album", album);

        return ReadTracks(cmd);
    }

    /// <summary>
    /// Everything by one artist, album by album, each album in the same path order as above.
    /// </summary>
    public List<Track> GetArtistTracks(string artist)
    {
        using SqliteConnection conn = Db.Open();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            {TrackColumns}
            WHERE artist = @artist
            ORDER BY album COLLATE NOCASE, path COLLATE NOCASE;
            """;
        cmd.Parameters.AddWithValue("@artist", artist);

        return ReadTracks(cmd);
    }

    /// <summary>
    /// Distinct artist names matching the search box, capped at limit. Delegates to SearchNames,
    /// which is the only place a column name is chosen - and it comes from this call site, never
    /// from user text, which is what keeps A9 intact.
    /// </summary>
    public List<string> SearchArtists(string term, int limit)
        => SearchNames("artist", term, limit);

    /// <summary>Distinct album names matching the search box, capped at limit. The album twin of SearchArtists.</summary>
    public List<string> SearchAlbums(string term, int limit)
        => SearchNames("album", term, limit);

    /// <summary>
    /// Tracks whose title, artist OR album matches the search box, capped at limit and ordered by
    /// title case-insensitively. One parameter is reused across all three columns, so the term binds
    /// once. That cap is why the Search page can get away with not virtualizing.
    /// </summary>
    public List<Track> SearchTracks(string term, int limit)
    {
        using SqliteConnection conn = Db.Open();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            {TrackColumns}
            WHERE title LIKE @q OR artist LIKE @q OR album LIKE @q
            ORDER BY title COLLATE NOCASE LIMIT @n;
            """;
        cmd.Parameters.AddWithValue("@q", Contains(term));
        cmd.Parameters.AddWithValue("@n", limit);

        return ReadTracks(cmd);
    }

    /// <summary>
    /// How many tracks are indexed, unreadable ones included. Passed as a METHOD GROUP from
    /// SettingsViewModel - Task.Run(_library.CountTracks) - which is why a plain-name grep makes it
    /// look like it has no callers.
    /// </summary>
    public int CountTracks()
    {
        using SqliteConnection conn = Db.Open();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM tracks;";

        return (int)(long)cmd.ExecuteScalar()!;
    }

    /// <summary>
    /// Called when a track starts, not when it finishes. A track you began is one you played.
    /// </summary>
    public void MarkPlayed(long trackId)
    {
        using SqliteConnection conn = Db.Open();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE tracks SET play_count = play_count + 1, last_played_utc = @now
            WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("@now", Db.UtcNowText());
        cmd.Parameters.AddWithValue("@id", trackId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// MAX(art_key) comes back NULL when not one row of the group has ever been looked at for art.
    /// Reading that as a string would throw, so both Pick queries funnel through here.
    /// </summary>
    private static string? ArtKeyOrNull(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    /// <summary>
    /// The column list every Track query selects, in the order ReadTracks expects. Keeping the
    /// two together is what stops a new query from reading the columns off by one.
    /// </summary>
    private const string TrackColumns =
        "SELECT id, path, title, artist, album, duration_ms, unreadable, art_key FROM tracks";

    /// <summary>
    /// The ORDER BY clause for a sortable column header. The column names are interpolated because
    /// SQLite cannot parameterise an identifier, so this switch is the whitelist — a TrackSort value
    /// can only ever be one of five, and no user text reaches it.
    ///
    /// Every sort falls back to album then title, because without a tiebreak a repeated value
    /// scatters an album's tracks into an arbitrary order that changes between queries.
    /// </summary>
    private static string OrderBy(TrackSort sort, bool descending)
    {
        string direction = descending ? "DESC" : "ASC";

        string primary = sort switch
        {
            TrackSort.Title => $"title COLLATE NOCASE {direction}",
            TrackSort.Artist => $"artist COLLATE NOCASE {direction}",
            TrackSort.Album => $"album COLLATE NOCASE {direction}",
            TrackSort.Duration => $"duration_ms {direction}",
            _ => $"artist COLLATE NOCASE {direction}",
        };

        return $"{primary}, album COLLATE NOCASE, title COLLATE NOCASE";
    }

    /// <summary>
    /// DISTINCT names for a search group. The column name is interpolated because SQLite cannot
    /// parameterise an identifier — which is safe here and only here, because the only two callers
    /// pass a literal. No caller may ever pass user text into this argument.
    /// </summary>
    private static List<string> SearchNames(string column, string term, int limit)
    {
        List<string> names = [];

        using SqliteConnection conn = Db.Open();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT DISTINCT {column} FROM tracks WHERE {column} LIKE @q
            ORDER BY {column} COLLATE NOCASE LIMIT @n;
            """;
        cmd.Parameters.AddWithValue("@q", Contains(term));
        cmd.Parameters.AddWithValue("@n", limit);

        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    /// <summary>
    /// internal, not private, since Step 11: PlaylistRepository reads Track rows out of a JOIN and
    /// there is no reason for a second copy of this loop. Its caller must SELECT the same eight
    /// columns in the same order as TrackColumns above — the reader goes by position.
    /// </summary>
    internal static List<Track> ReadTracks(SqliteCommand cmd)
    {
        List<Track> tracks = [];

        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            tracks.Add(new Track
            {
                Id = reader.GetInt64(0),
                Path = reader.GetString(1),
                Title = reader.GetString(2),
                Artist = reader.GetString(3),
                Album = reader.GetString(4),
                DurationMs = reader.GetInt64(5),
                Unreadable = reader.GetInt64(6) != 0,
                ArtKey = reader.IsDBNull(7) ? null : reader.GetString(7),
            });
        }

        return tracks;
    }

    /// <summary>
    /// SQLite's LIKE is case-insensitive for ASCII only, so an accented or non-Latin title still
    /// matches case-sensitively. Accepted for now: the fix is a collation or FTS, and neither is
    /// worth a schema change before Step 10.
    /// </summary>
    private static string Contains(string term) => $"%{term}%";
}
