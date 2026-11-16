using Microsoft.Data.Sqlite;
using MusicPlayer.Models;

namespace MusicPlayer.Data;

/// <summary>
/// One playlist as the index page shows it. TrackCount and TotalMs are computed by the query
/// rather than stored, for the same reason artists and albums are: one less thing that can drift
/// out of step with the rows it describes.
/// </summary>
public sealed record Playlist(long Id, string Name, int TrackCount, long TotalMs, bool IsFavorites)
{
    /// <summary>
    /// "1 track" or "12 tracks". Lives here rather than in a converter because the singular is a
    /// property of the number, and a converter cannot be handed one without a second parameter.
    /// </summary>
    public string TrackCountLabel => TrackCount == 1 ? "1 track" : $"{TrackCount} tracks";
}

/// <summary>
/// Playlists and their contents. Step 11, schema version 2.
///
/// Same shape as LibraryRepository: hand-written SQL, a connection per call inside a using, and
/// every value a parameter (A8, A9).
/// </summary>
public sealed class PlaylistRepository
{
    /// <summary>
    /// Step 19. The Favorites playlist's display name, and the one name users may not give their own
    /// playlists. Display only: the code finds Favorites by kind = 1, never by this text.
    /// </summary>
    public const string FavoritesName = "Favorites";

    /// <summary>Trimmed, case-insensitive. " favorites " and "FAVORITES" are both reserved.</summary>
    public static bool IsReservedName(string name)
        => string.Equals(name.Trim(), FavoritesName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A LEFT JOIN so a playlist with nothing in it still comes back, with a count of zero.
    /// </summary>
    public List<Playlist> GetPlaylists()
    {
        List<Playlist> playlists = [];

        using SqliteConnection conn = Db.Open();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT p.id, p.name, COUNT(pt.track_id), COALESCE(SUM(t.duration_ms), 0), p.kind
            FROM playlists p
            LEFT JOIN playlist_tracks pt ON pt.playlist_id = p.id
            LEFT JOIN tracks t ON t.id = pt.track_id
            GROUP BY p.id, p.name, p.kind
            ORDER BY p.kind DESC, p.name COLLATE NOCASE;
            """;

        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            playlists.Add(new Playlist(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetInt64(3),
                reader.GetInt64(4) == 1));
        }

        return playlists;
    }

    /// <summary>
    /// One playlist at random, for the Dashboard deal. Step 14.
    ///
    /// Plain JOINs and not the LEFT JOINs above, and that IS the empty-playlist filter: a playlist
    /// with no rows drops out of an inner join on its own, so no HAVING is needed. A card the user
    /// cannot play would be a dead card.
    ///
    /// ORDER BY RANDOM() is a literal in this file's own SQL, not an identifier built from user
    /// text, so A9 is untouched - the same standing as the interpolated PRAGMA in Db.cs.
    /// </summary>
    public Playlist? PickRandom()
    {
        using SqliteConnection conn = Db.Open();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT p.id, p.name, COUNT(pt.track_id), COALESCE(SUM(t.duration_ms), 0), p.kind
            FROM playlists p
            JOIN playlist_tracks pt ON pt.playlist_id = p.id
            JOIN tracks t ON t.id = pt.track_id
            GROUP BY p.id, p.name, p.kind
            ORDER BY RANDOM() LIMIT 1;
            """;

        using SqliteDataReader reader = cmd.ExecuteReader();

        return reader.Read()
            ? new Playlist(reader.GetInt64(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt64(3), reader.GetInt64(4) == 1)
            : null;
    }

    /// <summary>
    /// Every playlist's distinct album covers, in ONE query rather than one per playlist. Step 14.
    ///
    /// DISTINCT is load-bearing. Art is cached one file per ALBUM, so a twelve-track album hands
    /// back the same key twelve times, and a single-album playlist would mosaic as four copies of
    /// one cover. The ORDER BY fixes each list's order, which is what lets the caller take a seeded
    /// subset that stays the same for the whole run.
    ///
    /// Empty art_key is filtered alongside NULL. The difference between the two belongs to the
    /// scanner and means nothing to a reader - see V3Sql in Db.cs.
    /// </summary>
    public Dictionary<long, List<string>> GetArtKeys()
    {
        Dictionary<long, List<string>> keys = [];

        using SqliteConnection conn = Db.Open();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT DISTINCT pt.playlist_id, t.art_key
            FROM playlist_tracks pt
            JOIN tracks t ON t.id = pt.track_id
            WHERE t.art_key IS NOT NULL AND t.art_key <> ''
            ORDER BY pt.playlist_id, t.art_key;
            """;

        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            long playlistId = reader.GetInt64(0);

            if (!keys.TryGetValue(playlistId, out List<string>? covers))
            {
                covers = [];
                keys[playlistId] = covers;
            }

            covers.Add(reader.GetString(1));
        }

        return keys;
    }

    /// <summary>
    /// Names are not unique. Two playlists called "new" are the user's business, and a UNIQUE
    /// constraint here would mean an exception to catch for something nobody asked to prevent.
    /// </summary>
    public long Create(string name)
    {
        using SqliteConnection conn = Db.Open();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO playlists (name, created_utc) VALUES (@name, @now);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@now", Db.UtcNowText());
        return (long)cmd.ExecuteScalar()!;
    }

    /// <summary>
    /// Changes a playlist's name and nothing else. No uniqueness check, deliberately: two playlists
    /// may share a name, exactly as Create allows, because the id is what identifies one. It silently
    /// does nothing if the id has gone, which is the right answer for a rename racing a delete.
    /// </summary>
    public void Rename(long id, string name)
    {
        using SqliteConnection conn = Db.Open();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE playlists SET name = @name WHERE id = @id;";
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// The playlist_tracks rows go with it through ON DELETE CASCADE, which is live because
    /// Db.Open sets PRAGMA foreign_keys = ON. The tracks themselves are untouched.
    /// </summary>
    public void Delete(long id)
    {
        using SqliteConnection conn = Db.Open();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM playlists WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// In playlist order, which is the user's manual order, not any sort.
    ///
    /// The eight columns and their order are fixed by LibraryRepository.ReadTracks, which reads by
    /// position. They match TrackColumns there; they just have to be qualified here because of the
    /// join. Change one, change both.
    /// </summary>
    public List<Track> GetTracks(long playlistId)
    {
        using SqliteConnection conn = Db.Open();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT t.id, t.path, t.title, t.artist, t.album, t.duration_ms, t.unreadable, t.art_key
            FROM playlist_tracks pt
            JOIN tracks t ON t.id = pt.track_id
            WHERE pt.playlist_id = @playlist
            ORDER BY pt.position;
            """;
        cmd.Parameters.AddWithValue("@playlist", playlistId);
        return LibraryRepository.ReadTracks(cmd);
    }

    /// <summary>
    /// Appends. One transaction, because reading the last position and writing the next one are
    /// only correct together.
    /// </summary>
    public void AddTrack(long playlistId, long trackId)
    {
        using SqliteConnection conn = Db.Open();
        using SqliteTransaction tx = conn.BeginTransaction();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO playlist_tracks (playlist_id, track_id, position)
            SELECT @playlist, @track, COALESCE(MAX(position), -1) + 1
            FROM playlist_tracks WHERE playlist_id = @playlist;
            """;
        cmd.Parameters.AddWithValue("@playlist", playlistId);
        cmd.Parameters.AddWithValue("@track", trackId);
        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    /// <summary>
    /// Step 19. A track is a favorite when it has a row in the Favorites playlist. No flag on tracks,
    /// so there is one source of truth and "Remove from playlist" inside Favorites unfavorites it.
    /// </summary>
    public bool IsFavorite(long trackId)
    {
        using SqliteConnection conn = Db.Open();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT EXISTS (
                SELECT 1 FROM playlist_tracks pt
                JOIN playlists p ON p.id = pt.playlist_id
                WHERE p.kind = 1 AND pt.track_id = @track);
            """;
        cmd.Parameters.AddWithValue("@track", trackId);
        return Convert.ToInt64(cmd.ExecuteScalar()) == 1;
    }

    /// <summary>
    /// Step 19. On appends like AddTrack, in one transaction, and only if the track is not already
    /// there: Favorites never holds a duplicate. Off deletes every row of that track; the gap it
    /// leaves in position is harmless, because reads ORDER BY position, appends use MAX + 1 and a
    /// reorder rewrites them all.
    /// </summary>
    public void SetFavorite(long trackId, bool on)
    {
        using SqliteConnection conn = Db.Open();
        using SqliteTransaction tx = conn.BeginTransaction();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = on
            ? """
              INSERT INTO playlist_tracks (playlist_id, track_id, position)
              SELECT fav.id, @track,
                     (SELECT COALESCE(MAX(position), -1) + 1 FROM playlist_tracks WHERE playlist_id = fav.id)
              FROM playlists fav
              WHERE fav.kind = 1
                AND NOT EXISTS (SELECT 1 FROM playlist_tracks
                                WHERE playlist_id = fav.id AND track_id = @track);
              """
            : """
              DELETE FROM playlist_tracks
              WHERE track_id = @track
                AND playlist_id = (SELECT id FROM playlists WHERE kind = 1);
              """;
        cmd.Parameters.AddWithValue("@track", trackId);
        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    /// <summary>
    /// The whole order, rewritten. This is both the reorder path and the remove path.
    ///
    /// Deliberately a delete-and-reinsert rather than shifting positions with UPDATE. A playlist is
    /// a few hundred rows at worst, so the rewrite costs nothing measurable, and it is the only
    /// version that is obviously correct when the same track appears twice — which the schema
    /// allows on purpose. Position-shifting would have to get its comparisons right in both
    /// directions against duplicate track ids.
    /// </summary>
    public void ReplaceTracks(long playlistId, IReadOnlyList<long> trackIds)
    {
        using SqliteConnection conn = Db.Open();
        using SqliteTransaction tx = conn.BeginTransaction();

        using (SqliteCommand clear = conn.CreateCommand())
        {
            clear.Transaction = tx;
            clear.CommandText = "DELETE FROM playlist_tracks WHERE playlist_id = @playlist;";
            clear.Parameters.AddWithValue("@playlist", playlistId);
            clear.ExecuteNonQuery();
        }

        using (SqliteCommand insert = conn.CreateCommand())
        {
            insert.Transaction = tx;
            insert.CommandText = """
                INSERT INTO playlist_tracks (playlist_id, track_id, position)
                VALUES (@playlist, @track, @position);
                """;

            // Prepared once and reused, the same way WriteScan does it in LibraryRepository.
            SqliteParameter playlist = insert.Parameters.Add("@playlist", SqliteType.Integer);
            SqliteParameter track = insert.Parameters.Add("@track", SqliteType.Integer);
            SqliteParameter position = insert.Parameters.Add("@position", SqliteType.Integer);
            playlist.Value = playlistId;

            for (int i = 0; i < trackIds.Count; i++)
            {
                track.Value = trackIds[i];
                position.Value = i;
                insert.ExecuteNonQuery();
            }
        }

        tx.Commit();
    }
}
