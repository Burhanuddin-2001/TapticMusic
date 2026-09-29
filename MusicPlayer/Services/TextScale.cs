using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MusicPlayer.Services;

/// <summary>
/// v1.1. The app-wide text size. Every text style declares its size as BaseSize instead of FontSize,
/// and this class writes FontSize = BaseSize × Factor onto each element that carries one.
///
/// WHY AN ATTACHED PROPERTY, and not the three obvious routes, all of which were tried on paper first:
///
/// 1. {Binding} in a Style Setter. WinUI does not evaluate it — no error, no effect. Documented on
///    the Setter class.
/// 2. Swapping the Text.* styles or the FontSize.* keys at run time. Every consumer took them with
///    StaticResource, which resolves once. Nothing already on screen would move until it was rebuilt,
///    so a live dial would do nothing until the next navigation.
/// 3. A ScaleTransform or Viewbox on the root. That scales icons, borders and spacing too, and layout
///    never reflows, so a larger size clips instead of wrapping.
///
/// A Setter CAN carry a custom attached property, and its changed-callback runs on every element the
/// style reaches, including ones created later inside DataTemplates and ControlTemplates. That gives
/// this class a live list of exactly the elements to update, with no tree walk.
///
/// SMOOTHNESS. A drag can raise Factor several times per frame. Only the last value per frame is
/// written, from CompositionTarget.Rendering, so the app lays out once per frame however fast the dial
/// moves. The sizes are not rounded: DirectWrite renders fractional sizes cleanly, and rounding is what
/// would make the text step instead of glide.
/// </summary>
public static class TextScale
{
    /// <summary>The dial's range, as percentages. Persisted as a percentage too.</summary>
    public const double MinPercent = 80;
    public const double MaxPercent = 250;

    /// <summary>
    /// The size an element would have at 100%. Set from styles, not from views: a literal FontSize on an
    /// element would lose to this and never scale, so there are none.
    /// </summary>
    public static readonly DependencyProperty BaseSizeProperty =
        DependencyProperty.RegisterAttached("BaseSize", typeof(double), typeof(TextScale),
            new PropertyMetadata(0.0, OnBaseSizeChanged));

    /// <summary>
    /// Strong references, but only while an element is loaded, so nothing leaks. Not WeakReference: a
    /// C#/WinRT wrapper can be collected while its native element is still on screen, and the element
    /// would silently stop scaling.
    /// </summary>
    private static readonly HashSet<FrameworkElement> Live = [];

    private static double _factor = 1.0;
    private static bool _pending;

    public static double GetBaseSize(DependencyObject element) => (double)element.GetValue(BaseSizeProperty);

    public static void SetBaseSize(DependencyObject element, double value) => element.SetValue(BaseSizeProperty, value);

    /// <summary>
    /// 1.0 is 100%. Safe to set before any window exists: with nothing registered yet it only stores
    /// the value, which is how App applies the saved size before the first frame.
    /// </summary>
    public static double Factor
    {
        get => _factor;
        set
        {
            value = Math.Clamp(value, MinPercent / 100, MaxPercent / 100);

            if (value == _factor)
            {
                return;
            }

            _factor = value;

            if (!_pending && Live.Count > 0)
            {
                _pending = true;
                CompositionTarget.Rendering += ApplyOnce;
            }
        }
    }

    private static void OnBaseSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // 0 is the unset default, so 0 -> something is the first time this element is seen.
        if ((double)e.OldValue == 0 && d is FrameworkElement element)
        {
            element.Loaded += OnLoaded;
            element.Unloaded += OnUnloaded;

            if (element.IsLoaded)
            {
                Live.Add(element);
            }
        }

        Write(d);
    }

    /// <summary>Also catches up an element that was off-screen while the size moved.</summary>
    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        var element = (FrameworkElement)sender;
        Live.Add(element);
        Write(element);
    }

    /// <summary>
    /// WinUI can raise Unloaded after the Loaded of a re-parent, so an element that reports itself
    /// loaded again is kept.
    /// </summary>
    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        var element = (FrameworkElement)sender;

        if (!element.IsLoaded)
        {
            Live.Remove(element);
        }
    }

    private static void ApplyOnce(object? sender, object e)
    {
        CompositionTarget.Rendering -= ApplyOnce;
        _pending = false;

        foreach (FrameworkElement element in Live)
        {
            Write(element);
        }
    }

    /// <summary>The three kinds of element a style in this app sets BaseSize on.</summary>
    private static void Write(DependencyObject d)
    {
        double size = GetBaseSize(d) * _factor;

        if (size <= 0)
        {
            return;
        }

        switch (d)
        {
            case TextBlock text: text.FontSize = size; break;
            case Control control: control.FontSize = size; break;
            case ContentPresenter presenter: presenter.FontSize = size; break;
        }
    }
}
