using Microsoft.UI.Xaml.Data;

namespace MusicPlayer.Converters;

/// <summary>
/// Milliseconds to a clock string. Under an hour gives m:ss, an hour or more gives h:mm:ss.
/// </summary>
public sealed partial class DurationToStringConverter : IValueConverter
{
    /// <summary>
    /// Turns a millisecond count into the clock text shown on the seek bar, on every track row and
    /// under the playlist tiles. It accepts a double, int, long or TimeSpan because the call sites
    /// bind different shapes at it, and anything else reads as zero rather than throwing. A negative
    /// or NaN value means no track is loaded yet, so that case is answered with 0:00.
    /// </summary>
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        double ms = value switch
        {
            double d => d,
            int i => i,
            long l => l,
            TimeSpan ts => ts.TotalMilliseconds,
            _ => 0
        };

        // Negative or NaN comes from an unloaded track, not from a real position.
        if (double.IsNaN(ms) || ms < 0)
        {
            return "0:00";
        }

        TimeSpan span = TimeSpan.FromMilliseconds(ms);

        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes}:{span.Seconds:00}";
    }

    /// <summary>
    /// Never runs: every binding that uses this converter is one-way. It throws rather than
    /// returning null so that adding a two-way binding fails loudly instead of quietly writing
    /// nothing back.
    /// </summary>
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException("DurationToStringConverter is one-way.");
}
