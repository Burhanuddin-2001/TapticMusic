using Microsoft.UI.Xaml.Data;

namespace MusicPlayer.Converters;

/// <summary>
/// IsPlaying to a Segoe Fluent Icons glyph. True shows Pause, because the button offers the
/// action the user can take next, not the state they are already in.
/// </summary>
public sealed partial class BoolToPlayPauseGlyphConverter : IValueConverter
{
    // Segoe Fluent Icons code points. Built from the number rather than pasted as a character,
    // so the glyph cannot be mangled by a file-encoding change.
    private static readonly string PlayGlyph = char.ConvertFromUtf32(0xE768);
    private static readonly string PauseGlyph = char.ConvertFromUtf32(0xE769);

    /// <summary>
    /// Picks the glyph for the transport bar's big round button: playing shows Pause, anything else
    /// shows Play. It is the only consumer, bound once in Controls/PlayerBarView.xaml, and it re-runs
    /// each time PlayerBarViewModel.IsPlaying changes. Anything that is not literally true falls to
    /// Play, so the button reads correctly before the first track is ever opened.
    /// </summary>
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? PauseGlyph : PlayGlyph;

    /// <summary>
    /// Never runs: the glyph binding is one-way. It throws rather than returning null so that a
    /// two-way binding added later fails loudly instead of quietly writing nothing back.
    /// </summary>
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException("BoolToPlayPauseGlyphConverter is one-way.");
}
