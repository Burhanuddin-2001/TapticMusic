using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MusicPlayer.Controls;

/// <summary>
/// Up to four album covers arranged in one square. Step 14.
///
/// Callers fill Cover0 upward and stop; nulls are compacted, so a gap cannot punch a hole in the
/// grid. Four DependencyProperties rather than one collection: x:Bind in a DataTemplate binds a
/// property per cover cleanly, and a bound IList would need change notification nothing else here
/// has.
/// </summary>
public sealed partial class ArtMosaic : UserControl
{
    /// <summary>Backing store for <see cref="Cover0"/>. Registered through <see cref="RegisterCover"/> so all four share one changed-handler.</summary>
    public static readonly DependencyProperty Cover0Property = RegisterCover(nameof(Cover0));

    /// <summary>Backing store for <see cref="Cover1"/>.</summary>
    public static readonly DependencyProperty Cover1Property = RegisterCover(nameof(Cover1));

    /// <summary>Backing store for <see cref="Cover2"/>.</summary>
    public static readonly DependencyProperty Cover2Property = RegisterCover(nameof(Cover2));

    /// <summary>Backing store for <see cref="Cover3"/>.</summary>
    public static readonly DependencyProperty Cover3Property = RegisterCover(nameof(Cover3));

    /// <summary>
    /// Builds the control and lays it out once immediately. That first call is not redundant: a
    /// template can realise this control with no covers set at all, and running the layout here is
    /// what puts the placeholder up in that case rather than showing four empty cells.
    /// </summary>
    public ArtMosaic()
    {
        InitializeComponent();

        // The four covers are usually set before this control is realised, but a template can also
        // realise it empty. Running once here is what puts the placeholder up in that case.
        ApplyLayout();
    }

    /// <summary>
    /// The first cover. Callers fill these in order and stop, because nulls are compacted rather
    /// than left as holes - setting only Cover0 and Cover2 draws the same as setting Cover0 and
    /// Cover1.
    /// </summary>
    public ImageSource? Cover0
    {
        get => (ImageSource?)GetValue(Cover0Property);
        set => SetValue(Cover0Property, value);
    }

    /// <summary>The second cover. See <see cref="Cover0"/>.</summary>
    public ImageSource? Cover1
    {
        get => (ImageSource?)GetValue(Cover1Property);
        set => SetValue(Cover1Property, value);
    }

    /// <summary>The third cover. See <see cref="Cover0"/>, and note three covers draw as two.</summary>
    public ImageSource? Cover2
    {
        get => (ImageSource?)GetValue(Cover2Property);
        set => SetValue(Cover2Property, value);
    }

    /// <summary>The fourth cover. See <see cref="Cover0"/>.</summary>
    public ImageSource? Cover3
    {
        get => (ImageSource?)GetValue(Cover3Property);
        set => SetValue(Cover3Property, value);
    }

    /// <summary>
    /// Registers one cover property. Exists so the four declarations cannot drift apart - all four
    /// must share the same type, the same null default and the same changed-handler, and a copied
    /// block is where that kind of thing goes wrong.
    /// </summary>
    private static DependencyProperty RegisterCover(string name)
        => DependencyProperty.Register(name, typeof(ImageSource), typeof(ArtMosaic),
            new PropertyMetadata(null, OnCoverChanged));

    /// <summary>
    /// Re-runs the layout whenever any cover changes. Static because a DependencyProperty callback
    /// has to be, so it casts the sender back to the instance it belongs to.
    /// </summary>
    private static void OnCoverChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        => ((ArtMosaic)sender).ApplyLayout();

    /// <summary>
    /// The whole count-to-layout rule, in one place: it counts the non-null covers, then sets each
    /// cell's visibility, source and Grid spans so one cover fills the square, two split it into
    /// columns and four take a cell each.
    ///
    /// THREE covers is deliberately drawn as two. A 2x2 with one empty cell reads as a bug, and the
    /// third cover is not lost — the caller reshuffles which two it picks on the next launch.
    ///
    /// Not called Arrange(): UIElement already has an Arrange(Rect), and a same-named zero-argument
    /// method beside it is an overload that reads like an override.
    /// </summary>
    private void ApplyLayout()
    {
        Image[] cells = [Cell0, Cell1, Cell2, Cell3];
        List<ImageSource> covers = [];

        foreach (ImageSource? cover in new[] { Cover0, Cover1, Cover2, Cover3 })
        {
            if (cover is not null)
            {
                covers.Add(cover);
            }
        }

        int shown = covers.Count >= 4 ? 4 : covers.Count >= 2 ? 2 : covers.Count;

        Placeholder.Visibility = shown == 0 ? Visibility.Visible : Visibility.Collapsed;

        for (int i = 0; i < cells.Length; i++)
        {
            Image cell = cells[i];
            bool used = i < shown;

            cell.Visibility = used ? Visibility.Visible : Visibility.Collapsed;

            // Cleared, not just hidden, so a tile that loses a cover releases that bitmap too.
            cell.Source = used ? covers[i] : null;

            // One cover fills the square, two split it into columns, four take a cell each.
            Grid.SetRowSpan(cell, shown <= 2 ? 2 : 1);
            Grid.SetColumnSpan(cell, shown == 1 ? 2 : 1);
            Grid.SetRow(cell, shown <= 2 ? 0 : i / 2);
            Grid.SetColumn(cell, shown == 2 ? i : i % 2);
        }
    }
}
