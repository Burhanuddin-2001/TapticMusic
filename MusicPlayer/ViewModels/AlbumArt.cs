using Microsoft.UI.Xaml.Media.Imaging;
using MusicPlayer.Services;

namespace MusicPlayer.ViewModels;

/// <summary>
/// Turns an art_key into a decoded bitmap. Step 14.
///
/// Lifted out of PlayerBarViewModel once the playlist tiles and the dashboard deal became a second
/// and a third caller, each wanting its own decode size.
///
/// It lives here and deliberately NOT beside Services/AlbumArtCache.cs. That file imports zero UI
/// namespaces, and A11's proof that LibraryScanner touches no UI object runs straight through it —
/// a BitmapImage there would pull one into the scanner's reference graph.
/// </summary>
internal static class AlbumArt
{
    /// <summary>
    /// Loads one cached cover and hands back a bitmap sized for the box that will show it. Three
    /// callers: the player bar at twice its thumb size, and the playlist tiles and dashboard deal at
    /// tile size. pixelWidth is REAL pixels, so a caller passes its logical size times the scale it
    /// expects. Callers test string.IsNullOrEmpty first, because art_key's null-versus-empty split
    /// belongs to the scanner and means nothing here. Must run on the UI thread - see TRAPS.md T-03.
    /// </summary>
    internal static BitmapImage Decode(string artKey, int pixelWidth)
    {
        // DecodePixelWidth is set BEFORE UriSource on purpose. Assign the source first and the
        // image decodes at full resolution, then the hint only applies on a later pass.
        BitmapImage image = new()
        {
            DecodePixelWidth = pixelWidth,
        };

        // ms-appdata:///local/ and not a bare Windows path: that scheme resolves against exactly
        // the folder the cache writes into, and BitmapImage does not accept a plain file path.
        image.UriSource = new Uri(AlbumArtCache.UriFor(artKey));

        return image;
    }
}
