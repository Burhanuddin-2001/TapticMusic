using Microsoft.UI.Xaml.Media;

namespace MusicPlayer.ViewModels;

/// <summary>
/// One card in the dashboard's dealt hand: an album, an artist or a playlist. Step 14.
///
/// ONE type for all three kinds and not three types, because all three draw the same way - covers,
/// a title, one line under it - and a ListView takes a single ItemTemplate. The kind is folded into
/// Subtitle rather than given its own line above the title, and Payload is deliberately object.
/// Trap: see TRAPS.md T-01 for why both of those are the way they are.
/// </summary>
public sealed class DealCard
{
    /// <summary>The album name, artist name or playlist name, drawn big on the card.</summary>
    public required string Title { get; init; }

    /// <summary>
    /// The line under the title, which also carries the KIND - "Album · Radiohead". Stating the kind
    /// here is not decoration: an album called "Radiohead" and the artist Radiohead would otherwise
    /// be the same square with the same name on it.
    /// </summary>
    public required string Subtitle { get; init; }

    /// <summary>
    /// What a click turns back into tracks. Typed object on purpose - AlbumPick, ArtistPick and
    /// Playlist share nothing worth an interface, and DashboardViewModel.TracksForAsync switches on
    /// the runtime type anyway.
    /// </summary>
    public required object Payload { get; init; }

    /// <summary>
    /// First of up to four album covers. ArtMosaic reads all four and works out the layout from how
    /// many are non-null, so leaving these unset is how a card with no art asks for the placeholder.
    /// </summary>
    public ImageSource? Cover0 { get; init; }

    /// <summary>Second cover slot. See Cover0.</summary>
    public ImageSource? Cover1 { get; init; }

    /// <summary>Third cover slot. See Cover0 - note ArtMosaic draws three covers as two.</summary>
    public ImageSource? Cover2 { get; init; }

    /// <summary>Fourth cover slot. See Cover0.</summary>
    public ImageSource? Cover3 { get; init; }
}
