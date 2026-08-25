using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace MusicPlayer.Converters;

/// <summary>
/// Bool to Visibility, with "Invert" as the parameter for the opposite sense.
///
/// Needed because a ControlTemplate inside a ResourceDictionary cannot use x:Bind, so the track row
/// has no way to call a function or read a computed property — only classic Binding with a
/// converter. Six bindings in Styles/TrackRow.xaml use it, all reading Content.Unreadable: three
/// take the plain sense to show the broken-file chrome, and three take Invert to hide the normal
/// cells behind it.
/// </summary>
public sealed partial class BoolToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// Turns a bool into Visibility so a track row can show or hide a cell. Passing "Invert" as the
    /// ConverterParameter flips the answer, which is what lets one converter drive both halves of
    /// the broken-file row instead of needing a second inverted property on the model. Anything that
    /// is not literally true is treated as false, so an unset binding collapses rather than showing.
    /// </summary>
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool flag = value is true;

        if (parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase))
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Never runs: all six bindings are one-way. It throws rather than returning null so that a
    /// two-way binding added later fails loudly instead of quietly writing nothing back.
    /// </summary>
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException("BoolToVisibilityConverter is one-way.");
}
