using Microsoft.UI.Xaml.Media;
using MusicPlayer.Data;

namespace MusicPlayer.ViewModels;

/// <summary>
/// One playlist as the index tile draws it: the record it came from, plus up to four decoded covers.
/// Step 14.
///
/// The covers cannot live on Data.Playlist. That is a record in Data/, and an ImageSource is a UI
/// type - putting one there would pull Microsoft.UI into the persistence layer.
///
/// No change notification, deliberately. The whole List is replaced and re-Raised in one go,
/// exactly as the List of Playlist it replaced was.
/// </summary>
public sealed class PlaylistTile
{
    /// <summary>
    /// The row this tile was built from. Kept whole rather than copied field by field, because the
    /// flyout handlers and PlaylistsViewModel.OpenAsync all want the Id, and PlaylistsPage.PlaylistOf
    /// hands this straight back out.
    /// </summary>
    public required Playlist Playlist { get; init; }

    /// <summary>
    /// First of up to four album covers, picked by PlaylistsViewModel.PickCovers. ArtMosaic works out
    /// the layout from how many are non-null, so leaving all four unset asks for the placeholder.
    /// </summary>
    public ImageSource? Cover0 { get; init; }

    /// <summary>Second cover slot. See Cover0.</summary>
    public ImageSource? Cover1 { get; init; }

    /// <summary>Third cover slot. See Cover0 - note ArtMosaic draws three covers as two.</summary>
    public ImageSource? Cover2 { get; init; }

    /// <summary>Fourth cover slot. See Cover0.</summary>
    public ImageSource? Cover3 { get; init; }

    /// <summary>The playlist name, for the label under the tile. Pass-through so the DataTemplate
    /// can x:Bind one level deep instead of reaching through Playlist.</summary>
    public string Name => Playlist.Name;

    /// <summary>The "12 tracks" line, already singularised by the record. Pass-through, as Name.</summary>
    public string TrackCountLabel => Playlist.TrackCountLabel;

    /// <summary>Total runtime in milliseconds, for the tile's clock text. Pass-through, as Name.</summary>
    public long TotalMs => Playlist.TotalMs;
}
