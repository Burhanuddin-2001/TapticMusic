using System.Security.Cryptography;
using System.Text;
using Windows.Storage;

namespace MusicPlayer.Services;

/// <summary>
/// The on-disk album art cache. Step 12.
///
/// One image per ALBUM, not per track: a twelve-track record writes one file, and tracks two
/// through twelve find it already there and never open a picture frame of their own.
///
/// Static, and shaped like Data/Db.cs — a lazy path plus plain System.IO. It holds no state beyond
/// that path, and LibraryScanner calls it from inside Task.Run, where a static call is far cheaper
/// than threading an eighth service through App's wiring (A3).
///
/// This file imports ZERO UI namespaces and must stay that way. The scanner calls into it, and A11
/// says the scanner touches no UI object, so a BitmapImage here would pull one straight into its
/// reference graph. The decode lives in PlayerBarViewModel instead.
/// </summary>
internal static class AlbumArtCache
{
    /// <summary>
    /// Beside library.db and settings.json, where every other per-user file already lives. This
    /// name is also half of UriFor, so renaming it moves the SMTC thumbnail path with it.
    /// </summary>
    private const string FolderName = "artcache";

    /// <summary>
    /// Embedded art is JPEG or PNG in practice, and both decode straight from their stored bytes,
    /// which is what keeps Windows.Graphics.Imaging out of the app entirely. Find probes these two
    /// and nothing else, so Write must never produce a third.
    /// </summary>
    private static readonly string[] Extensions = [".jpg", ".png"];

    private static string? _root;

    private static string Root => _root ??= EnsureFolder();

    /// <summary>
    /// The album identity. Callers must pass the same fallback-applied strings they write to the
    /// tracks table, or every "Unknown album" track gets a key of its own instead of sharing one.
    /// </summary>
    internal static string KeyFor(string artist, string album)
    {
        byte[] hash = SHA256.HashData(
            Encoding.UTF8.GetBytes($"{artist.ToLowerInvariant()}\n{album.ToLowerInvariant()}"));

        // 8 bytes is 16 hex characters: far more than a local library needs to stay unique, and
        // short enough that the folder is still readable when something needs debugging by hand.
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }

    /// <summary>The cached file name for this album, or null if it has never been written.</summary>
    internal static string? Find(string key)
    {
        foreach (string extension in Extensions)
        {
            if (File.Exists(Path.Combine(Root, key + extension)))
            {
                return key + extension;
            }
        }

        return null;
    }

    /// <summary>
    /// Writes picture bytes verbatim — no decode, no resize. Downscaling happens once at display
    /// time through BitmapImage.DecodePixelWidth, so this stays a dumb byte store.
    ///
    /// mimeOrExtension takes either an APIC mime type (image/png) or a file extension (.png):
    /// both are matched on the substring png, and everything else is stored as .jpg. That single
    /// rule is what keeps Find's two-extension probe honest, including for a cover.jpeg.
    ///
    /// Returns the file name, or an empty string if the write failed. Empty is the same "looked,
    /// found nothing" sentinel art_key already uses, so a failure degrades to no art instead of
    /// ending a scan.
    /// </summary>
    internal static string Save(string key, byte[] bytes, string mimeOrExtension)
        => Write(
            key + (mimeOrExtension.Contains("png", StringComparison.OrdinalIgnoreCase) ? ".png" : ".jpg"),
            bytes);

    /// <summary>An image file found sitting beside the audio file, rather than inside its tags.</summary>
    internal static string SaveCopy(string key, string sourcePath)
    {
        try
        {
            return Save(key, File.ReadAllBytes(sourcePath), Path.GetExtension(sourcePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A cover we cannot write is a cover the app draws a placeholder for. Same trade as the
            // twin below and as SettingsService.Save: degrade, never end a scan.
            return string.Empty;
        }
    }

    /// <summary>
    /// How every reader addresses a cached cover. ms-appdata:///local/ resolves to exactly the
    /// folder Root is built from, and both consumers need that form rather than a file path:
    /// BitmapImage does not accept a bare Windows path, and SMTC cannot use
    /// RandomAccessStreamReference.CreateFromFile because that wants a StorageFile, and therefore
    /// an await, while AudioService.OpenSource is synchronous.
    /// </summary>
    internal static string UriFor(string fileName) => $"ms-appdata:///local/{FolderName}/{fileName}";

    private static string EnsureFolder()
    {
        string path = Path.Combine(ApplicationData.Current.LocalFolder.Path, FolderName);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string Write(string fileName, byte[] bytes)
    {
        try
        {
            File.WriteAllBytes(Path.Combine(Root, fileName), bytes);
            return fileName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing one cover is not worth ending a scan over. Same trade as SettingsService.Save.
            return string.Empty;
        }
    }
}
