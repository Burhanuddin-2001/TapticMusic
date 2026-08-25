using Microsoft.UI.Xaml.Controls;

namespace MusicPlayer.Views;

/// <summary>
/// A page that names one list for the shell's bare Up and Down keys. MainWindow owns those keys
/// because the transport outlives every page (A4), but it cannot see inside a page, so each page
/// says which control should take keyboard focus when the user starts arrowing. A page that has no
/// focusable list simply does not implement this - SettingsPage is the one that does not. Trap: see
/// TRAPS.md T-10.
/// </summary>
internal interface IArrowHost
{
    /// <summary>
    /// The control the shell focuses on a bare Up or Down. Null is legal and expected: every one of
    /// these lists sits behind an x:Load gate, so its field stays null until the view model flips
    /// the bool, and the shell then leaves the key to bubble. A page with two views decides here
    /// which one is showing rather than making the shell guess.
    /// </summary>
    Control? ArrowTarget { get; }
}
