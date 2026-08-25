using Microsoft.Data.Sqlite;
using Windows.Storage;

namespace MusicPlayer.Data;

/// <summary>
/// The library database file and its schema. Raw SQLite, hand-written SQL, no ORM.
/// Architecture notes, section 2.
///
/// Connections are opened per call and closed. Microsoft.Data.Sqlite pools the underlying
/// handles, so this is cheap, and it removes every cross-thread sharing bug that one long-lived
/// connection would invite once the scanner runs on a background task.
/// </summary>
public static class Db
{
    /// <summary>
    /// Version 1. Two tables. Artists and albums are derived with GROUP BY rather than stored,
    /// because every question the five pages ask is answerable from one flat tracks table and a
    /// local library on one machine is never big enough for the joins to pay for themselves.
    ///
    /// file_size and mtime_utc exist so a rescan can skip an unchanged file without opening it.
    /// unreadable exists because settled fact D-10 requires the UI to mark a track it cannot read.
    /// </summary>
    private const string V1Sql = """
        CREATE TABLE IF NOT EXISTS folders (
            id        INTEGER PRIMARY KEY,
            path      TEXT NOT NULL UNIQUE,
            added_utc TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS tracks (
            id              INTEGER PRIMARY KEY,
            path            TEXT NOT NULL UNIQUE,
            folder_id       INTEGER NOT NULL REFERENCES folders(id) ON DELETE CASCADE,
            title           TEXT NOT NULL,
            artist          TEXT NOT NULL,
            album           TEXT NOT NULL,
            duration_ms     INTEGER NOT NULL DEFAULT 0,
            file_size       INTEGER NOT NULL,
            mtime_utc       TEXT NOT NULL,
            added_utc       TEXT NOT NULL,
            last_played_utc TEXT,
            play_count      INTEGER NOT NULL DEFAULT 0,
            unreadable      INTEGER NOT NULL DEFAULT 0
        );

        CREATE INDEX IF NOT EXISTS ix_tracks_artist ON tracks(artist);
        CREATE INDEX IF NOT EXISTS ix_tracks_album  ON tracks(album);
        CREATE INDEX IF NOT EXISTS ix_tracks_added  ON tracks(added_utc);
        CREATE INDEX IF NOT EXISTS ix_tracks_played ON tracks(last_played_utc);
        """;

    /// <summary>
    /// Version 2, Step 11. Playlists.
    ///
    /// The key is (playlist_id, position) and deliberately NOT (playlist_id, track_id): the same
    /// track appearing twice in one playlist is a feature, not a bug to design out. So a row is
    /// addressed by its position, never by its track id, and a remove takes a position.
    ///
    /// Both cascades are real, because Db.Open sets PRAGMA foreign_keys = ON per connection.
    /// Deleting a folder therefore reaches its tracks and then their playlist rows.
    /// </summary>
    private const string V2Sql = """
        CREATE TABLE IF NOT EXISTS playlists (
            id          INTEGER PRIMARY KEY,
            name        TEXT NOT NULL,
            created_utc TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS playlist_tracks (
            playlist_id INTEGER NOT NULL REFERENCES playlists(id) ON DELETE CASCADE,
            track_id    INTEGER NOT NULL REFERENCES tracks(id)    ON DELETE CASCADE,
            position    INTEGER NOT NULL,
            PRIMARY KEY (playlist_id, position)
        );

        CREATE INDEX IF NOT EXISTS ix_playlist_tracks_track ON playlist_tracks(track_id);
        """;

    /// <summary>
    /// Version 3, Step 12. Album art.
    ///
    /// art_key is a three-state sentinel, and the scanner's skip predicate reads all three:
    ///   NULL           never looked for art on this file
    ///   empty string   looked, found none — do not look again
    ///   abc123.jpg     the file name inside the artcache folder
    ///
    /// NULL is what makes the upgrade self-healing. LibraryScanner refuses to skip a file whose
    /// art_key is still NULL, so every already-indexed track is re-read exactly once and never
    /// again, because a track with no art is stamped empty rather than left NULL.
    ///
    /// No index: nothing queries BY art_key, it is only ever selected alongside a track row.
    ///
    /// Unlike V1 and V2 this script is NOT re-runnable — SQLite has no ADD COLUMN IF NOT EXISTS.
    /// That is safe only because Migrate commits the DDL and the user_version stamp in one
    /// transaction, so the stamp can never run ahead of the column.
    /// </summary>
    private const string V3Sql = """
        ALTER TABLE tracks ADD COLUMN art_key TEXT;
        """;

    /// <summary>
    /// Version 4, Step 14. Drops two indexes that nothing reads any more.
    ///
    /// GetRecentlyAdded and GetRecentlyPlayed were the only queries that ordered by added_utc or
    /// last_played_utc, and both went with the dashboard lists they fed. An index nobody reads is
    /// not free: every WriteScan row and every MarkPlayed call still pays to keep it sorted, and
    /// MarkPlayed is on the hot path - it runs on each track that starts.
    ///
    /// The COLUMNS stay. They are still written, and any future "recently added" or listening
    /// history feature wants them; re-creating an index is a one-line V5, re-deriving lost
    /// timestamps is impossible.
    ///
    /// V1Sql still CREATEs both, and that is deliberate, not an oversight. Never edit a shipped
    /// VnSql - every database already past it would skip the edit forever. So a brand-new file
    /// creates the two indexes in V1 and drops them again here. That costs microseconds once.
    ///
    /// Re-runnable, unlike V3: DROP INDEX IF EXISTS is a no-op the second time.
    /// </summary>
    private const string V4Sql = """
        DROP INDEX IF EXISTS ix_tracks_added;
        DROP INDEX IF EXISTS ix_tracks_played;
        """;

    private static string? _connectionString;

    /// <summary>
    /// LocalState inside the package container, beside settings.json.
    /// </summary>
    private static string ConnectionString => _connectionString ??= new SqliteConnectionStringBuilder
    {
        DataSource = Path.Combine(ApplicationData.Current.LocalFolder.Path, "library.db")
    }.ToString();

    /// <summary>
    /// Idempotent. Called once from App.OnLaunched; single-digit milliseconds, so it is the only
    /// database work allowed before the first frame. Cold start is a priority in the architecture notes.
    ///
    /// Step 11 turned this into a real migration runner. Before, one script ran on every launch and
    /// wrote user_version = 1 without ever reading it back, so there was no way to add a table
    /// without either re-running everything or guessing what was already there.
    ///
    /// Every path ends at the newest version. A new database reads 0 and runs V1 through V4. A
    /// database from Step 12 reads 3 and runs V4 only. One from Step 11 reads 2 and runs V3 then V4,
    /// keeping its scanned tracks, play counts and last-played times. Older still reads 1 and runs
    /// V2 onward.
    /// </summary>
    public static void EnsureCreated()
    {
        using SqliteConnection conn = Open();

        // Outside the version gate and outside any transaction: journal_mode is stored in the file
        // and cannot be set from inside one. Re-running it on an already-WAL database is free.
        Execute(conn, null, "PRAGMA journal_mode = WAL;");

        long version = CurrentVersion(conn);

        if (version < 1)
        {
            Migrate(conn, V1Sql, 1);
        }

        if (version < 2)
        {
            Migrate(conn, V2Sql, 2);
        }

        if (version < 3)
        {
            Migrate(conn, V3Sql, 3);
        }

        if (version < 4)
        {
            Migrate(conn, V4Sql, 4);
        }
    }

    /// <summary>
    /// The timestamp text every dated column in this database is written with. Round-trip UTC ("o"),
    /// which sorts correctly as plain text so ORDER BY needs no conversion. It lives here, on the one
    /// file both repositories already depend on, because it used to be a private copy in each: two
    /// copies of a format string is one edit away from a database holding two formats, and then
    /// ORDER BY silently stops working. Consolidated in Step 15.
    /// </summary>
    internal static string UtcNowText() => DateTime.UtcNow.ToString("o");

    internal static SqliteConnection Open()
    {
        SqliteConnection conn = new(ConnectionString);
        conn.Open();

        // journal_mode is stored in the file and survives; these two are per connection and
        // SQLite resets them on every handle, so they have to be set here rather than in the
        // schema script.
        Execute(conn, null, "PRAGMA foreign_keys = ON; PRAGMA synchronous = NORMAL;");

        return conn;
    }

    private static long CurrentVersion(SqliteConnection conn)
    {
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    /// <summary>
    /// One migration and its version stamp, in a single transaction. If the script throws, the
    /// stamp rolls back with it, so the recorded version can never run ahead of the real schema
    /// and leave the next launch skipping a table it never created.
    /// </summary>
    private static void Migrate(SqliteConnection conn, string sql, int version)
    {
        using SqliteTransaction tx = conn.BeginTransaction();

        Execute(conn, tx, sql);

        // SQLite cannot parameterise a PRAGMA value — @v is taken as a literal string and the
        // statement silently does nothing. Interpolating is safe and stays inside A9: A9 forbids
        // building an identifier out of USER TEXT, and this is an int from a call site two lines
        // up. Do not "fix" this into a parameter.
        Execute(conn, tx, $"PRAGMA user_version = {version};");

        tx.Commit();
    }

    private static void Execute(SqliteConnection conn, SqliteTransaction? tx, string sql)
    {
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
