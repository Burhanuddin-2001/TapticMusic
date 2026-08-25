using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MusicPlayer.Controls;

/// <summary>
/// Centred fallback for an empty library, playlist, or queue. design-plan section 4.
/// Eight instances across MainWindow's queue rail, Dashboard, Library, Search and Playlists.
/// </summary>
public sealed partial class EmptyStateView : UserControl
{
    /// <summary>Backing store for <see cref="Glyph"/>. Defaults to empty so an unset control draws no icon rather than a box.</summary>
    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(EmptyStateView),
            new PropertyMetadata(string.Empty));

    /// <summary>Backing store for <see cref="Title"/>.</summary>
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(EmptyStateView),
            new PropertyMetadata(string.Empty));

    /// <summary>Backing store for <see cref="Message"/>.</summary>
    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(nameof(Message), typeof(string), typeof(EmptyStateView),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// Builds the control. All three values arrive from the call site's XAML, so there is nothing to
    /// resolve here.
    /// </summary>
    public EmptyStateView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// The Segoe Fluent Icons character shown above the text. A string and not an enum because every
    /// call site writes the code point inline, the same way FontIcon does.
    /// </summary>
    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>The bold line - what is empty, for example "No music yet".</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>The quiet line under the title - what the user can do about it.</summary>
    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }
}
