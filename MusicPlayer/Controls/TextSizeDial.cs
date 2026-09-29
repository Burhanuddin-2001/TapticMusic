using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.System;

namespace MusicPlayer.Controls;

/// <summary>
/// v1.1. A radio tuning dial: a tape of tick marks slides under a fixed needle, and the value is
/// whatever tick the needle sits on. Built for the app-wide text size, but it is a plain RangeBase and
/// knows nothing about text.
///
/// A RangeBase and not a UserControl, so Value, SmallChange and LargeChange, two-way binding and the
/// screen-reader range pattern all come from WinUI. Not a retemplated Slider: a Slider moves its thumb
/// to where you click, and a dial must move its tape by how far you drag, from wherever you grab it.
///
/// Smoothness: during a drag Value follows the pointer continuously, unrounded, so the tape tracks the
/// pointer exactly. It snaps to a whole step only on release. The tape rides a TranslateTransform,
/// which stays on the compositor, and never touches layout.
///
/// Chrome is in Styles/TextSizeDial.xaml. The ticks are built here because their count depends on
/// Minimum and Maximum.
/// </summary>
public sealed partial class TextSizeDial : RangeBase
{
    private FrameworkElement? _viewport;
    private Canvas? _tape;
    private TranslateTransform? _tapeTransform;

    /// <summary>DIPs between two whole steps. Read from Metrics.xaml when the ticks are built.</summary>
    private double _spacing = 1;

    private uint? _dragPointer;
    private double _dragStartX;
    private double _dragStartValue;

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (_viewport is not null)
        {
            _viewport.SizeChanged -= OnViewportSizeChanged;
        }

        _viewport = GetTemplateChild("PART_Viewport") as FrameworkElement;
        _tape = GetTemplateChild("PART_Tape") as Canvas;
        _tapeTransform = GetTemplateChild("PART_TapeTransform") as TranslateTransform;

        if (_viewport is not null)
        {
            _viewport.SizeChanged += OnViewportSizeChanged;
        }

        BuildTicks();
        PlaceTape();
    }

    protected override void OnMinimumChanged(double oldMinimum, double newMinimum)
    {
        base.OnMinimumChanged(oldMinimum, newMinimum);
        BuildTicks();
        PlaceTape();
    }

    protected override void OnMaximumChanged(double oldMaximum, double newMaximum)
    {
        base.OnMaximumChanged(oldMaximum, newMaximum);
        BuildTicks();
        PlaceTape();
    }

    protected override void OnValueChanged(double oldValue, double newValue)
    {
        base.OnValueChanged(oldValue, newValue);
        PlaceTape();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new TextSizeDialAutomationPeer(this);

    /// <summary>
    /// Grab anywhere. Capture keeps the drag alive when the pointer leaves the dial, and it has to,
    /// because the page reflows under the pointer as the text grows.
    /// </summary>
    protected override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        base.OnPointerPressed(e);

        var point = e.GetCurrentPoint(this);
        bool primary = e.Pointer.PointerDeviceType != PointerDeviceType.Mouse || point.Properties.IsLeftButtonPressed;

        if (!primary || !CapturePointer(e.Pointer))
        {
            return;
        }

        _dragPointer = e.Pointer.PointerId;
        _dragStartX = point.Position.X;
        _dragStartValue = Value;
        Focus(FocusState.Pointer);
        e.Handled = true;
    }

    /// <summary>
    /// Drag left and the tape goes left, bringing higher numbers under the needle - a physical dial.
    /// Measured from the press, not from the last move, so no rounding can accumulate.
    /// </summary>
    protected override void OnPointerMoved(PointerRoutedEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_dragPointer != e.Pointer.PointerId)
        {
            return;
        }

        Value = _dragStartValue - (e.GetCurrentPoint(this).Position.X - _dragStartX) / _spacing;
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerRoutedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_dragPointer == e.Pointer.PointerId)
        {
            // Raises PointerCaptureLost, which ends the drag.
            ReleasePointerCapture(e.Pointer);
            e.Handled = true;
        }
    }

    /// <summary>A release, an Alt+Tab mid-drag and a lost window all land here, so every drag ends snapped.</summary>
    protected override void OnPointerCaptureLost(PointerRoutedEventArgs e)
    {
        base.OnPointerCaptureLost(e);

        if (_dragPointer == e.Pointer.PointerId)
        {
            _dragPointer = null;
            Value = Math.Round(Value);
        }
    }

    /// <summary>One step per wheel notch. Handled, so the page does not scroll away from under it.</summary>
    protected override void OnPointerWheelChanged(PointerRoutedEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        int delta = e.GetCurrentPoint(this).Properties.MouseWheelDelta;

        if (delta == 0 || _dragPointer is not null)
        {
            return;
        }

        Value = Math.Round(Value) + Math.Sign(delta) * SmallChange;
        e.Handled = true;
    }

    /// <summary>
    /// The Slider key map. MainWindow hands the arrows to any focused RangeBase, not just a Slider,
    /// or Left and Right would seek the track instead.
    /// </summary>
    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        double step = Math.Round(Value);

        double? target = e.Key switch
        {
            VirtualKey.Left or VirtualKey.Down => step - SmallChange,
            VirtualKey.Right or VirtualKey.Up => step + SmallChange,
            VirtualKey.PageDown => step - LargeChange,
            VirtualKey.PageUp => step + LargeChange,
            VirtualKey.Home => Minimum,
            VirtualKey.End => Maximum,
            _ => null,
        };

        if (target is double value)
        {
            Value = value;
            e.Handled = true;
        }
        else
        {
            base.OnKeyDown(e);
        }
    }

    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Canvas never clips, so without this the tape would draw across the whole panel.
        ((UIElement)sender).Clip = new RectangleGeometry { Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
        PlaceTape();
    }

    /// <summary>
    /// One tick per whole step, a taller one every 5 and a labelled one every 10. The labels take
    /// Text.Micro, so they grow with the text size too - the dial previews itself as it turns.
    ///
    /// The brushes are the shared Brush.* objects, so a theme change repaints the ticks with no rebuild.
    /// </summary>
    private void BuildTicks()
    {
        if (_tape is null)
        {
            return;
        }

        _tape.Children.Clear();

        ResourceDictionary resources = Application.Current.Resources;
        _spacing = (double)resources["Size.Dial.Tick.Spacing"];
        double width = (double)resources["Size.Dial.Tick.Width"];
        double minor = (double)resources["Size.Dial.Tick.Minor"];
        double middle = (double)resources["Size.Dial.Tick.Middle"];
        double major = (double)resources["Size.Dial.Tick.Major"];
        double labelWidth = (double)resources["Size.Dial.Label.Width"];
        double labelTop = (double)resources["Size.Dial.Needle.Height"];
        var faint = (Brush)resources["Brush.Text.Secondary"];
        var strong = (Brush)resources["Brush.Text.Primary"];
        var labelStyle = (Style)resources["Text.Micro"];

        for (int step = (int)Math.Ceiling(Minimum); step <= Maximum; step++)
        {
            double x = (step - Minimum) * _spacing;
            bool labelled = step % 10 == 0;

            var tick = new Rectangle
            {
                Width = width,
                Height = labelled ? major : step % 5 == 0 ? middle : minor,
                Fill = labelled ? strong : faint,
            };
            Canvas.SetLeft(tick, x - width / 2);
            _tape.Children.Add(tick);

            if (labelled)
            {
                var label = new TextBlock
                {
                    Style = labelStyle,
                    Text = step.ToString(),
                    Width = labelWidth,
                    TextAlignment = TextAlignment.Center,
                };
                Canvas.SetLeft(label, x - labelWidth / 2);
                Canvas.SetTop(label, labelTop);
                _tape.Children.Add(label);
            }
        }
    }

    /// <summary>Puts the tick for Value under the needle, which is the viewport's centre line.</summary>
    private void PlaceTape()
    {
        if (_tapeTransform is null || _viewport is null)
        {
            return;
        }

        _tapeTransform.X = _viewport.ActualWidth / 2 - (Value - Minimum) * _spacing;
    }

    /// <summary>
    /// RangeBaseAutomationPeer supplies the range pattern. This only makes the dial announce itself as
    /// a slider, which is the control type a screen reader already knows how to drive.
    /// </summary>
    private sealed partial class TextSizeDialAutomationPeer(TextSizeDial owner) : RangeBaseAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;

        protected override string GetClassNameCore() => nameof(TextSizeDial);
    }
}
