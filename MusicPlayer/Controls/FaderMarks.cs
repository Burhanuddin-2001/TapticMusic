using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using MusicPlayer.Models;
using Windows.Foundation.Collections;

namespace MusicPlayer.Controls;

/// <summary>
/// v1.3. The two readouts the Channel Fader lays over every vertical scroll bar.
///
/// THE NOW NOTCH marks the playing song on the fader, and a click brings that row into view. It is
/// placed in the cap's own travel, not in content space: it sits where the cap would be with the
/// playing row in the middle of the view, so whenever that row is on screen the notch is on the cap.
///
/// THE LABEL STRIP hangs left of the cap while the cap is pointed at or held, and names the row at
/// the top of the list: its number, the same one the Library's # column shows, and the song. A page
/// has no rows, so there it says how far down the page you are.
///
/// It lives in the ScrollBar template (Styles/ScrollBar.xaml) and finds everything else by walking
/// up the visual tree: the bar, the bar's ScrollViewer, and the ListView around that when there is
/// one. No page or list has to know it exists.
/// </summary>
public sealed partial class FaderMarks : Control
{
    private ScrollBar? _bar;
    private ScrollViewer? _viewer;
    private ListViewBase? _list;
    private Thumb? _cap;
    private long _maximumToken;

    private ButtonBase? _now;
    private FrameworkElement? _strip;
    private TextBlock? _row;
    private TextBlock? _count;
    private TextBlock? _title;
    private TextBlock? _artist;

    /// <summary>Index of the playing song in the list, or -1 when it is not in this list.</summary>
    private int _nowIndex = -1;
    private bool _pointing;
    private bool _holding;

    public FaderMarks()
    {
        Loaded += OnLoaded;
        Unloaded += (_, _) => Detach();
        SizeChanged += (_, _) => Refresh();
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (_now is not null)
        {
            _now.Click -= OnNowClick;
        }

        if (_strip is not null)
        {
            _strip.SizeChanged -= OnStripSizeChanged;
        }

        _now = GetTemplateChild("PART_Now") as ButtonBase;
        _strip = GetTemplateChild("PART_Strip") as FrameworkElement;
        _row = GetTemplateChild("PART_Row") as TextBlock;
        _count = GetTemplateChild("PART_Count") as TextBlock;
        _title = GetTemplateChild("PART_Title") as TextBlock;
        _artist = GetTemplateChild("PART_Artist") as TextBlock;

        if (_now is not null)
        {
            _now.Click += OnNowClick;
        }

        // The strip is measured only once it is shown, so the first placement has to wait for its size.
        if (_strip is not null)
        {
            _strip.SizeChanged += OnStripSizeChanged;
        }
    }

    /// <summary>
    /// Loaded can fire again after Unloaded, when a page brings a list back with x:Load, so this starts
    /// from nothing every time.
    ///
    /// Vertical bars only. The template draws both orientations and WinUI collapses the one it is not
    /// using, but a collapsed element still loads, and this must not drive a horizontal bar's states.
    /// </summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Detach();

        _bar = FindAncestor<ScrollBar>(this);
        _viewer = _bar is { Orientation: Orientation.Vertical } ? FindAncestor<ScrollViewer>(_bar) : null;

        if (_bar is null || _viewer is null)
        {
            return;
        }

        // Two steps up and no further: a ListView's template is one Border around its ScrollViewer.
        // A page's own ScrollViewer must never pick up some list further out.
        _list = FindAncestor<ListViewBase>(_viewer, 2);
        _cap = VisualTreeHelper.GetParent(this) is Panel track ? track.Children.OfType<Thumb>().FirstOrDefault() : null;

        _viewer.ViewChanged += OnViewChanged;
        _maximumToken = _bar.RegisterPropertyChangedCallback(RangeBase.MaximumProperty, OnMaximumChanged);

        if (_cap is not null)
        {
            _cap.PointerEntered += OnCapEntered;
            _cap.PointerExited += OnCapExited;
            _cap.DragStarted += OnCapDragStarted;
            _cap.DragCompleted += OnCapDragCompleted;
            _cap.SizeChanged += OnCapSizeChanged;
        }

        if (_list is not null)
        {
            _list.Items.VectorChanged += OnItemsChanged;
            _list.SelectionChanged += OnSelectionChanged;
            App.Audio.StateChanged += OnPlaybackChanged;
        }

        FindNow();
        Refresh();
    }

    private void Detach()
    {
        if (_viewer is not null)
        {
            _viewer.ViewChanged -= OnViewChanged;
        }

        if (_bar is not null && _maximumToken != 0)
        {
            _bar.UnregisterPropertyChangedCallback(RangeBase.MaximumProperty, _maximumToken);
        }

        if (_cap is not null)
        {
            _cap.PointerEntered -= OnCapEntered;
            _cap.PointerExited -= OnCapExited;
            _cap.DragStarted -= OnCapDragStarted;
            _cap.DragCompleted -= OnCapDragCompleted;
            _cap.SizeChanged -= OnCapSizeChanged;
        }

        if (_list is not null)
        {
            _list.Items.VectorChanged -= OnItemsChanged;
            _list.SelectionChanged -= OnSelectionChanged;
            App.Audio.StateChanged -= OnPlaybackChanged;
        }

        _bar = null;
        _viewer = null;
        _list = null;
        _cap = null;
        _maximumToken = 0;
        _pointing = false;
        _holding = false;
    }

    private void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs e) => Refresh();

    private void OnMaximumChanged(DependencyObject sender, DependencyProperty property) => Refresh();

    private void OnCapSizeChanged(object sender, SizeChangedEventArgs e) => Refresh();

    private void OnStripSizeChanged(object sender, SizeChangedEventArgs e) => Refresh();

    private void OnCapEntered(object sender, PointerRoutedEventArgs e)
    {
        _pointing = true;
        Refresh();
    }

    private void OnCapExited(object sender, PointerRoutedEventArgs e)
    {
        _pointing = false;
        Refresh();
    }

    private void OnCapDragStarted(object sender, DragStartedEventArgs e)
    {
        _holding = true;
        Refresh();
    }

    private void OnCapDragCompleted(object sender, DragCompletedEventArgs e)
    {
        _holding = false;
        Refresh();
    }

    private void OnItemsChanged(IObservableVector<object> sender, IVectorChangedEventArgs e)
    {
        FindNow();
        Refresh();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        FindNow();
        Refresh();
    }

    private void OnPlaybackChanged(object? sender, EventArgs e)
    {
        FindNow();
        Refresh();
    }

    /// <summary>
    /// The same landing as the queue rail's follow in MainWindow: the row before the playing one at the
    /// top, so the song that led into it is still in sight.
    /// </summary>
    private void OnNowClick(object sender, RoutedEventArgs e)
    {
        if (_list is not null && _nowIndex >= 0 && _nowIndex < _list.Items.Count)
        {
            _list.ScrollIntoView(_list.Items[Math.Max(0, _nowIndex - 1)], ScrollIntoViewAlignment.Leading);
        }
    }

    /// <summary>
    /// The list's own selection wins when it IS the playing track, because the queue can hold one song
    /// twice and only the selection knows which of the two is playing. Otherwise the match is by
    /// reference, then by Id: a track restored at launch is a new object, not the one in the Library.
    /// </summary>
    private void FindNow()
    {
        _nowIndex = -1;

        if (_list is null || App.Audio.Current is not Track playing)
        {
            return;
        }

        if (_list.SelectedItem is Track picked && ReferenceEquals(picked, playing))
        {
            _nowIndex = _list.SelectedIndex;
            return;
        }

        for (int i = 0; i < _list.Items.Count; i++)
        {
            if (_list.Items[i] is Track track && (ReferenceEquals(track, playing) || (playing.Id != 0 && track.Id == playing.Id)))
            {
                _nowIndex = i;
                return;
            }
        }
    }

    private void Refresh()
    {
        if (_bar is null || _viewer is null)
        {
            return;
        }

        double scrollable = _viewer.ScrollableHeight;
        VisualStateManager.GoToState(_bar, scrollable > 0 ? "Travel" : "NoTravel", false);

        double cap = _cap?.ActualHeight ?? 0;
        double travel = Math.Max(0, ActualHeight - cap);

        PlaceNow(scrollable, cap, travel);
        PlaceStrip(scrollable, cap, travel);
    }

    /// <summary>
    /// Every row in these lists is one height, so the extent divides evenly by the item count. That
    /// gives the playing row's middle, then the offset that would centre it, then where the cap would be.
    /// </summary>
    private void PlaceNow(double scrollable, double cap, double travel)
    {
        if (_now is null)
        {
            return;
        }

        int count = _list?.Items.Count ?? 0;

        if (scrollable <= 0 || _nowIndex < 0 || _nowIndex >= count)
        {
            _now.Visibility = Visibility.Collapsed;
            return;
        }

        double rowMiddle = (_nowIndex + 0.5) * _viewer!.ExtentHeight / count;
        double t = Math.Clamp((rowMiddle - _viewer.ViewportHeight / 2) / scrollable, 0, 1);

        Canvas.SetTop(_now, t * travel + (cap - _now.Height) / 2);
        _now.Visibility = Visibility.Visible;
    }

    private void PlaceStrip(double scrollable, double cap, double travel)
    {
        if (_strip is null)
        {
            return;
        }

        bool show = scrollable > 0 && (_pointing || _holding);
        _strip.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

        if (!show)
        {
            return;
        }

        FillStrip(scrollable);

        double capTop = _viewer!.VerticalOffset / scrollable * travel;
        double gap = (double)Application.Current.Resources["Space.S"];

        Canvas.SetLeft(_strip, -_strip.ActualWidth - gap);
        Canvas.SetTop(_strip, Math.Clamp(capTop + (cap - _strip.ActualHeight) / 2, 0, Math.Max(0, ActualHeight - _strip.ActualHeight)));
    }

    private void FillStrip(double scrollable)
    {
        if (_row is null || _count is null || _title is null || _artist is null)
        {
            return;
        }

        int first = _list?.ItemsPanelRoot switch
        {
            ItemsStackPanel stack => stack.FirstVisibleIndex,
            ItemsWrapGrid grid => grid.FirstVisibleIndex,
            _ => -1,
        };

        Track? track = null;

        if (_list is not null && first >= 0 && first < _list.Items.Count)
        {
            _row.Text = (first + 1).ToString();
            _count.Text = $"of {_list.Items.Count}";
            track = _list.Items[first] as Track;
        }
        else
        {
            _row.Text = $"{Math.Round(100 * _viewer!.VerticalOffset / scrollable)}%";
            _count.Text = "down the page";
        }

        _title.Text = track?.Title ?? string.Empty;
        _artist.Text = track?.Artist ?? string.Empty;
        _title.Visibility = track is null ? Visibility.Collapsed : Visibility.Visible;
        _artist.Visibility = _title.Visibility;
    }

    private static T? FindAncestor<T>(DependencyObject from, int maxSteps = int.MaxValue) where T : DependencyObject
    {
        for (DependencyObject? node = VisualTreeHelper.GetParent(from); node is not null && maxSteps-- > 0; node = VisualTreeHelper.GetParent(node))
        {
            if (node is T found)
            {
                return found;
            }
        }

        return null;
    }
}
