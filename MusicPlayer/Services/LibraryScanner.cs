using MusicPlayer.Data;

// A11: THIS FILE MUST TOUCH ZERO UI OBJECTS. It runs inside Task.Run, and a DependencyObject built
// on a pool thread throws RPC_E_WRONG_THREAD.
//
// The proof used to be visible in this using block - five System.* imports and nothing else. Step 15
// removed those five because <ImplicitUsings> already supplies them, so a short list no longer
// proves anything. VERIFY A11 INSTEAD by checking that the using block above names no UI namespace
// at all - the framework ones, or anything ending in Xaml or Imaging. Those names are spelled out in
// the architecture notes under A11 rather than here, so that grepping this file for them stays clean.
//
// The one deliberate near-miss is AlbumArtCache, called from here, which itself imports zero UI
// namespaces for exactly this reason.

namespace MusicPlayer.Services;

/// <summary>
/// What one pass of the scanner found. Unreadable is carried separately because ATL degrades
/// instead of throwing, so a scan that failed on every single file would otherwise report a healthy
/// track count and nothing else - which is exactly how T-11 stayed invisible for a whole release.
/// </summary>
public sealed record ScanResult(int Tracks, int Unreadable);

/// <summary>
/// Walks the watched folders, reads tags with ATL, and hands the result to the repository.
/// Local files only. Architecture notes, section 2.
///
/// Runs entirely on a background thread and touches no UI object.
/// </summary>
public sealed class LibraryScanner
{
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".m4a", ".aac", ".wav", ".ogg", ".opus", ".wma"
    };

    /// <summary>
    /// Cover file names probed beside an audio file, in this order, when its tags carry no picture.
    /// Both spellings of each extension, because AlbumArtCache.Save normalises them anyway.
    /// </summary>
    private static readonly string[] CoverNames =
    [
        "cover.jpg", "cover.jpeg", "cover.png",
        "folder.jpg", "folder.jpeg", "folder.png",
        "front.jpg", "front.jpeg", "front.png",
    ];

    /// <summary>
    /// IgnoreInaccessible keeps a denied subfolder from killing the whole walk, and skipping
    /// reparse points keeps a junction or symlink from sending it round in a loop. Without these
    /// the first awkward directory throws and everything below it is lost.
    /// </summary>
    private static readonly EnumerationOptions WalkOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
    };

    private readonly LibraryRepository _library;

    /// <summary>
    /// Holds the repository the scan writes through. There is nothing else to set up: every folder,
    /// path and timestamp is read fresh on each ScanAllAsync, so one scanner is built once at
    /// startup and reused for every rescan.
    /// </summary>
    public LibraryScanner(LibraryRepository library)
    {
        _library = library;
    }

    /// <summary>
    /// Returns the number of audio files now indexed, plus how many of the files actually READ this
    /// pass no parser recognised. Safe to call on a folder that has not changed: an unchanged file
    /// is skipped without being opened at all.
    /// </summary>
    public Task<ScanResult> ScanAllAsync() => Task.Run(() =>
    {
        int total = 0;
        int unreadable = 0;

        foreach ((long folderId, string root) in _library.GetFolders())
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            Dictionary<string, (long Size, string MtimeUtc, bool ArtChecked, bool Unreadable)> index =
                _library.GetPathIndex(folderId);
            List<ScannedTrack> found = [];
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

            foreach (string file in Directory.EnumerateFiles(root, "*", WalkOptions))
            {
                if (!AudioExtensions.Contains(Path.GetExtension(file)))
                {
                    continue;
                }

                seen.Add(file);

                FileInfo info = new(file);
                string mtime = info.LastWriteTimeUtc.ToString("o");

                // Size and modified time both unchanged means the tags cannot have changed
                // either, so there is nothing to read. This is what makes a rescan cheap.
                //
                // ArtChecked is the third leg, added in Step 12: a row written before art_key
                // existed reads NULL, and skipping it would leave it coverless forever. It costs
                // one re-read per track on the first scan after the upgrade and nothing after
                // that, because a track with no art is stamped empty rather than left NULL.
                //
                // Unreadable is the FOURTH leg, added in Step 17, and it is what makes Rescan a
                // repair. A row marked unreadable is always re-read, so a bad verdict - from a
                // reader bug, a trimmed build, or the art failure T-11 describes - heals itself
                // instead of being skipped forever and needing library.db deleted by hand.
                if (index.TryGetValue(file, out (long Size, string MtimeUtc, bool ArtChecked, bool Unreadable) known)
                    && known.Size == info.Length
                    && known.MtimeUtc == mtime
                    && known.ArtChecked
                    && !known.Unreadable)
                {
                    continue;
                }

                found.Add(ReadTags(file, info.Length, mtime));
            }

            List<string> removed = index.Keys.Where(path => !seen.Contains(path)).ToList();

            _library.WriteScan(folderId, found, removed);
            total += seen.Count;
            unreadable += found.Count(track => track.Unreadable);
        }

        return new ScanResult(total, unreadable);
    });

    /// <summary>
    /// A file no parser recognises is still part of the library, so it is recorded with
    /// unreadable = 1 and filename-derived text. Settled fact D-10 is what consumes that flag:
    /// the UI marks the track rather than hiding it.
    ///
    /// ATL does not throw on a corrupt or unsupported file - it substitutes a dummy reader and
    /// reports Format.UNKNOWN_FORMAT - so the verdict is one explicit test rather than a broad
    /// catch. The catch that remains is only for a file that cannot be OPENED. Trap: see T-11.
    /// </summary>
    private static ScannedTrack ReadTags(string path, long size, string mtime)
    {
        try
        {
            ATL.Track file = new(path);

            // Artist and album come out into locals first because the art key is built from them,
            // and it has to be built from the SAME fallback-applied strings that reach the row.
            // Keying off the raw tags would give every "Unknown album" track a key of its own
            // instead of one shared between them.
            string artist = Fallback(file.Artist, Fallback(file.AlbumArtist, "Unknown artist"));
            string album = Fallback(file.Album, "Unknown album");

            // ID 0 is Format.UNKNOWN_FORMAT: no parser recognised this file. Together with the
            // catch below this is the ONLY thing allowed to set the flag, which is the whole point -
            // a cover that failed to extract can no longer condemn a track that plays perfectly.
            bool unreadable = file.AudioFormat.ID == ATL.Format.UNKNOWN_FORMAT.ID;

            return new ScannedTrack(
                path,
                Fallback(file.Title, Path.GetFileNameWithoutExtension(path)),
                artist,
                album,
                (long)file.DurationMs,
                size,
                mtime,
                unreadable,

                // Empty and never null for an unrecognised file: its artist and album both fell back
                // to "Unknown", so every such file shares one art key, and letting the first one
                // cache a folder cover under it would hand that picture to all of them. Empty rather
                // than null because null means "never looked" to the skip test - see V3Sql in Db.cs.
                unreadable ? string.Empty : TryReadArt(file, path, artist, album));
        }
        catch (Exception)
        {
            // ATL reports a file it cannot PARSE as UNKNOWN_FORMAT rather than throwing, so getting
            // here means the file could not be OPENED at all - locked, or gone between the directory
            // walk and this read. One bad file must never end a scan, so the filter stays broad.
            // Two Step 17 changes are what make that safe where T-11 was not: album art is read
            // inside its own guard and can never land here, and an unreadable row is re-read by
            // every later scan instead of being skipped forever.
            return new ScannedTrack(
                path,
                Path.GetFileNameWithoutExtension(path),
                "Unknown artist",
                "Unknown album",
                DurationMs: 0,
                size,
                mtime,
                Unreadable: true,
                ArtKey: string.Empty);
        }
    }

    /// <summary>
    /// Album art is a bonus, never a verdict on the file. A cover that fails to extract, or an art
    /// cache that cannot be written, must not decide whether a song is playable - that exact
    /// conflation is what marked readable files unplayable in T-11. Empty is the same answer
    /// ReadArt gives for a track with no picture at all, so nothing downstream sees a difference.
    /// </summary>
    private static string TryReadArt(ATL.Track file, string path, string artist, string album)
    {
        try
        {
            return ReadArt(file, path, artist, album);
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// One cover per ALBUM. The first track of a record pays for the extraction and every later
    /// track finds the file already cached, which is why this looks in the cache before the tags.
    ///
    /// Returns the cache file name, or an empty string meaning "looked, found none". Never null.
    /// </summary>
    private static string ReadArt(ATL.Track file, string path, string artist, string album)
    {
        string key = AlbumArtCache.KeyFor(artist, album);

        if (AlbumArtCache.Find(key) is string cached)
        {
            return cached;
        }

        ATL.PictureInfo? embedded = file.EmbeddedPictures.FirstOrDefault();

        if (embedded is not null && embedded.PictureData.Length > 0)
        {
            return AlbumArtCache.Save(key, embedded.PictureData, embedded.MimeType);
        }

        return FolderCover(path) is string cover ? AlbumArtCache.SaveCopy(key, cover) : string.Empty;
    }

    /// <summary>
    /// An image sitting beside the audio file, for a rip whose tags carry no picture. A fixed list
    /// of probes on purpose: nine File.Exists calls beat listing the directory once per track, and
    /// this only runs for an album that has no embedded art, once.
    /// </summary>
    private static string? FolderCover(string audioFilePath)
    {
        string? directory = Path.GetDirectoryName(audioFilePath);

        if (directory is null)
        {
            return null;
        }

        foreach (string name in CoverNames)
        {
            string candidate = Path.Combine(directory, name);

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>An empty tag field is as useless as a missing one, so both take the fallback.</summary>
    private static string Fallback(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value;
}
