using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace MusicPlayer.ViewModels;

/// <summary>
/// PlayerBarViewModel, album-art half: the 64px cover in the transport bar. Split out in Step 15 -
/// see PlayerBarViewModel.cs for why this is a partial file and not a new type.
/// </summary>
public sealed partial class PlayerBarViewModel
{
    /// <summary>
    /// The key ArtSource was last built from. OnStateChanged fires on every play and every pause,
    /// and decoding the same cover again on each of those would be pure waste, so the bitmap is
    /// rebuilt only when the album actually changes.
    /// </summary>
    private string? _artKey;

    private ImageSource? _artSource;

    /// <summary>
    /// This album's cover, or null when it has none. An ImageSource and not a path, so the view
    /// binds it straight and the app needs no fourth converter.
    /// </summary>
    public ImageSource? ArtSource => _artSource;

    /// <summary>Whether there is a cover to draw. Drives x:Load on the Image in the transport bar.</summary>
    public bool HasArt => _artSource is not null;

    /// <summary>
    /// The inverse of HasArt, because x:Load takes a plain bool and cannot invert one. Same two
    /// property pattern as IsEmpty / HasTracks elsewhere, rather than a Visibility converter.
    /// </summary>
    public bool HasNoArt => _artSource is null;

    /// <summary>
    /// Rebuilds the cover when the album changes, and does nothing when it has not. Cheap on a repeat
    /// call: the key is compared first, so a play or a pause on the same track costs one string
    /// comparison and nothing else - which matters because OnStateChanged runs it every time.
    /// </summary>
    private void RefreshArt()
    {
        string? key = _audio.Current?.ArtKey;

        if (key == _artKey)
        {
            return;
        }

        _artKey = key;

        // 64 doubled: the thumb is 64 logical pixels, and a 200% display asks for 128 real ones.
        // Reading an x:Double token from C# is safe, it comes back as a System.Double — that is
        // NOT true of Motion.Duration.*, which also returns Double and cannot be cast to Duration.
        _artSource = string.IsNullOrEmpty(key)
            ? null
            : AlbumArt.Decode(key, (int)((double)Application.Current.Resources["Size.PlayerBar.ArtThumb"] * 2));

        Raise(nameof(ArtSource));
        Raise(nameof(HasArt));
        Raise(nameof(HasNoArt));
    }
}
