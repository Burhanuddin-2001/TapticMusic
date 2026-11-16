using MusicPlayer.Models;

namespace MusicPlayer.Views;

/// <summary>
/// "Mark as favorite" / "Remove from favorites". Step 19.
///
/// Extracted for the same reason NamePrompt was: three track menus share it - Library, an open
/// playlist, and Search - and three copies of the label rule would drift.
/// </summary>
internal static class FavoriteMenu
{
    /// <summary>
    /// The item's label for this track. Read synchronously in the flyout's Opening, the same as the
    /// Library's Add to playlist submenu: a menu cannot wait for an await, and this is one indexed
    /// lookup off a local file.
    /// </summary>
    public static string Label(Track? track)
        => track is not null && App.Playlists.IsFavorite(track.Id) ? "Remove from favorites" : "Mark as favorite";

    /// <summary>
    /// Flips the track in or out of Favorites. Re-reads the state rather than trusting the label, so
    /// a menu left open while another page changed it still does the right thing.
    /// </summary>
    public static Task ToggleAsync(Track? track)
    {
        if (track is null)
        {
            return Task.CompletedTask;
        }

        long id = track.Id;
        return Task.Run(() => App.Playlists.SetFavorite(id, !App.Playlists.IsFavorite(id)));
    }
}
