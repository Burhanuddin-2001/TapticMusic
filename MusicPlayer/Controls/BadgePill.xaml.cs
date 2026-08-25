using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MusicPlayer.Controls;

/// <summary>
/// A small pill tag, used for the six count badges. One of only three places Radius.Pill is allowed,
/// and it is legal here only because Step 12 paired the radius with MinHeight = Size.ControlHeight
/// and MinWidth = Size.Badge.MinWidth. design-plan section 4. Trap: see TRAPS.md T-04.
/// </summary>
public sealed partial class BadgePill : UserControl
{
    /// <summary>Backing store for <see cref="Text"/>. The only one of the three every call site sets.</summary>
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(BadgePill),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// Backing store for <see cref="Fill"/>. The default is null rather than a brush, because
    /// PropertyMetadata cannot resolve a ThemeResource - the constructor fills it in instead.
    /// </summary>
    public static readonly DependencyProperty FillProperty =
        DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(BadgePill),
            new PropertyMetadata(null));

    /// <summary>Backing store for <see cref="LabelForeground"/>. Null default, as <see cref="FillProperty"/>.</summary>
    public static readonly DependencyProperty LabelForegroundProperty =
        DependencyProperty.Register(nameof(LabelForeground), typeof(Brush), typeof(BadgePill),
            new PropertyMetadata(null));

    /// <summary>
    /// Builds the badge and resolves the two brushes it cannot get from PropertyMetadata. This is
    /// the constructor snapshot that the architecture notes name as one of three reasons ThemeService mutates
    /// shared brush objects rather than swapping a theme dictionary. Trap: see TRAPS.md T-04.
    /// </summary>
    public BadgePill()
    {
        InitializeComponent();

        // Defaults come from the theme dictionary, which is only reachable once the control
        // exists. A PropertyMetadata default cannot resolve a ThemeResource.
        // Step 11: was Accent.Secondary. Turquoise is reserved for focus, so a count badge wearing
        // it read as a third accent colour nobody could click. App chrome instead.
        Fill ??= (Brush)Application.Current.Resources["Brush.Background.Elevated"];
        LabelForeground ??= (Brush)Application.Current.Resources["Brush.Text.Primary"];
    }

    /// <summary>The label, normally a count like "128 tracks". Kept short: a long one makes the pill wide enough to stop reading as a badge.</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>
    /// The pill's background. Settable but never set - all six call sites take the constructor's
    /// Background.Elevated default. Kept because removing it would change how the badge gets its
    /// brush, which is load-bearing for theming. Trap: see TRAPS.md T-04.
    /// </summary>
    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>The label's ink. Settable but never set, exactly as <see cref="Fill"/>.</summary>
    public Brush LabelForeground
    {
        get => (Brush)GetValue(LabelForegroundProperty);
        set => SetValue(LabelForegroundProperty, value);
    }
}
