using System.ComponentModel;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using MusicPlayer.Models;
using MusicPlayer.Services;
using MusicPlayer.ViewModels;
using MusicPlayer.Views;
using Windows.System;
using Windows.UI.Core;

namespace MusicPlayer;

/// <summary>
/// The application window. It hosts the Frame that displays pages, and owns the
/// mapping from a sidebar rail tag to the page that tag leads to.
/// </summary>
public sealed partial class MainWindow : Window
{
    /// <summary>
    /// The app-lifetime transport view model. The shell needs it because MainWindow.xaml cannot bind
    /// (A7b), so every value it shows is pushed here by hand from OnPlayerPropertyChanged.
    /// </summary>
    public PlayerBarViewModel Player => App.PlayerBar;

    /// <summary>
    /// Builds the shell: the custom title bar, the app icon, the first theme apply, the landing page,
    /// and the one global keyboard handler. Order matters here more than anywhere else in the app -
    /// the theme is applied before the first Navigate and before Activate, so nothing is ever shown
    /// unthemed. This is also the earliest point Application.Resources is provably live, which is why
    /// the first ThemeService.Apply is here and not in App. Trap: see TRAPS.md T-07.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();

        this.SystemBackdrop = null;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        // v1.1. Hides the system caption buttons so the title bar's own neo-brutalist set can stand
        // in their place. The drag region and the resize borders are untouched: those come from
        // SetTitleBar and the window frame, not from this option. It also zeroes RightInset, which
        // is what lets the TitleBar control give the space back to RightHeader.
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;

        // A drag to the screen edge, Win+Up and a title bar double-click all maximize without
        // going through our button, so the glyph follows the window, not the click.
        AppWindow.Changed += (_, args) =>
        {
            if (args.DidPresenterChange || args.DidSizeChange)
            {
                SyncMaximizeButton();
            }
        };

        AppWindow.SetIcon("Assets/AppIcon.ico");

        // Step 13. THE FIRST Apply, and the reason it lives here rather than in App. Application
        // .Resources is not reachable from the App constructor — get_Resources throws COMException
        // 0x8000FFFF that early, even after InitializeComponent. Here it is provably live, because
        // this window's own InitializeComponent has just resolved dozens of brushes out of it.
        // Before the first Navigate and before Window.Activate(), so nothing is shown unthemed.
        App.Theme.Apply(App.Settings.Theme, App.Settings.Accent);

        // Only fires for System, where RootGrid.RequestedTheme is Default and therefore follows
        // Windows. It is how a theme change made in Windows Settings reaches a running app.
        RootGrid.ActualThemeChanged += (_, _) =>
        {
            if (App.Settings.Theme == AppTheme.System)
            {
                App.Theme.Apply(AppTheme.System, App.Settings.Accent);
            }
        };

        // Step 17. Global shortcuts, on the TUNNELLING event. PreviewKeyDown routes from the root
        // DOWN towards the focused element, so RootGrid sees a key before any control has acted on
        // it, and marking it handled here means the control never sees it at all. That is the whole
        // reason Space can be unconditionally play/pause: a focused track row cannot replay itself,
        // because its own KeyDown never runs. Trap: see TRAPS.md T-10.
        //
        // UIElement.PreviewKeyDownEvent must be qualified here. PlayerBarView can write the bare
        // name because a UserControl IS a UIElement; MainWindow is a Window and is not one, so the
        // unqualified name does not resolve — and the XAML compiler reports that as a WMC9999
        // internal error on top of the real CS0103.
        RootGrid.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(RootGrid_PreviewKeyDown), true);

        // x:Bind's data root must be a FrameworkElement, and Window is not one — binding the
        // queue rail and the InfoBar straight to Player here crashed MeasureOverride at runtime
        // (COMException 0x80004005) instead of failing at compile time. Pushed by hand instead.
        Player.PropertyChanged += OnPlayerPropertyChanged;
        SyncPlayerBar();

        // Step 18. Volume is the system's now, so the hardware volume keys and the Windows slider can
        // both move it behind the app's back. There is nothing to poll on - the 250ms position timer
        // stops whenever playback does - so the bar is re-read whenever the window comes forward,
        // which is the cheapest moment that covers a change made while the app was in the background.
        Activated += (_, args) =>
        {
            if (args.WindowActivationState != WindowActivationState.Deactivated)
            {
                Player.RefreshVolume();
            }
        };

        // One subscription covers both Navigate call sites: the landing page below, and every
        // later rail click. Step 10c.
        RootFrame.Navigated += OnRootFrameNavigated;

        // The rail sets SelectedIndex 0 while it builds itself, which is before this window
        // has subscribed. So the first page is navigated to here, not by the event.
        RootFrame.Navigate(typeof(DashboardPage));
    }

    /// <summary>
    /// Step 10c. SyncPlayerBar reassigns QueueList.ItemsSource, so running it on every notification
    /// rebuilt the whole rail about 8 times a second during playback: the 250 ms position poll
    /// raises PositionMs and PositionPercent on every tick, and this handler ignored the name.
    /// Only the queue and the error channel change anything this window shows.
    /// </summary>
    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PlayerBarViewModel.QueueItems):
            case nameof(PlayerBarViewModel.QueueIndex):
            case nameof(PlayerBarViewModel.QueueListVisibility):
            case nameof(PlayerBarViewModel.EmptyQueueVisibility):
            case nameof(PlayerBarViewModel.HasError):
            case nameof(PlayerBarViewModel.ErrorMessage):
                SyncPlayerBar();
                break;
        }
    }

    private void SyncPlayerBar()
    {
        SyncQueue();
        QueueList.Visibility = Player.QueueListVisibility;
        QueueEmptyState.Visibility = Player.EmptyQueueVisibility;
        ErrorBar.IsOpen = Player.HasError;
        ErrorBar.Message = Player.ErrorMessage ?? string.Empty;
    }

    /// <summary>
    /// v1.3. This used to assign ItemsSource on every notification, and QueueItems is a new list on
    /// every read, so each song change rebuilt the rail from nothing - four times, once per name
    /// OnQueueChanged raises - and threw the scroll position away. A queue scrolled half way down was
    /// back at the top whenever a song ended.
    ///
    /// Now the list is replaced only when its tracks or their order really changed; a song change
    /// moves the highlight and nothing else. Track does not override Equals, so SequenceEqual compares
    /// references, which is exactly "the same tracks in the same order".
    ///
    /// The rail then follows the playing row, but only while the row that WAS playing is on screen.
    /// Scroll away on purpose and it stays where you put it.
    /// </summary>
    private void SyncQueue()
    {
        IReadOnlyList<Track> items = Player.QueueItems;
        bool replaced = QueueList.ItemsSource is not IReadOnlyList<Track> shown || !shown.SequenceEqual(items);
        bool following = replaced || IsQueueRowInView(QueueList.SelectedIndex);

        if (replaced)
        {
            QueueList.ItemsSource = items;
        }

        QueueList.SelectedIndex = Player.QueueIndex;

        if (following)
        {
            BringQueueRowIntoView(Player.QueueIndex, replaced);
        }
    }

    private bool IsQueueRowInView(int index)
        => index >= 0
           && QueueList.ItemsPanelRoot is ItemsStackPanel panel
           && index >= panel.FirstVisibleIndex
           && index <= panel.LastVisibleIndex;

    /// <summary>
    /// A row already fully on screen stays put, and one cut off at an edge is nudged in, so a click on
    /// the bottom row never throws it to the top. A row off screen lands second from the top, with
    /// the song before it still in sight - and so does every row of a queue that was just replaced,
    /// because the panel's visible range still describes the old list until the next layout pass.
    /// </summary>
    private void BringQueueRowIntoView(int index, bool replaced)
    {
        if (index < 0 || index >= QueueList.Items.Count)
        {
            return;
        }

        if (!replaced && QueueList.ItemsPanelRoot is ItemsStackPanel panel
            && index >= panel.FirstVisibleIndex && index <= panel.LastVisibleIndex)
        {
            if (index == panel.FirstVisibleIndex || index == panel.LastVisibleIndex)
            {
                QueueList.ScrollIntoView(QueueList.Items[index]);
            }

            return;
        }

        QueueList.ScrollIntoView(QueueList.Items[Math.Max(0, index - 1)], ScrollIntoViewAlignment.Leading);
    }

    /// <summary>
    /// Step 10c. Pages enter with Opacity and a short rise — the only two things that stay on the
    /// compositor, per the 60fps directive. Never Width, Height or Margin.
    ///
    /// The storyboard itself is declared in MainWindow.xaml, NOT built here. Motion.Duration.*
    /// resolves correctly through {StaticResource} in XAML, but the same key read from C# comes
    /// back as a System.Double and throws InvalidCastException on a cast to Duration. Building it
    /// in XAML keeps the tokens working and keeps C# out of guessing a resource's runtime type.
    ///
    /// Enter only. Frame.Navigate destroys the outgoing page immediately, so animating an exit
    /// would mean deferring navigation itself, which buys very little here.
    ///
    /// Pages are new-ed per visit and never cached (A4), so the transform is always fresh and no
    /// page can arrive still carrying the last one.
    ///
    /// The transport bar lives outside RootFrame and must never move with this (A4).
    /// </summary>
    private void OnRootFrameNavigated(object sender, NavigationEventArgs e)
    {
        if (e.Content is not UIElement page)
        {
            return;
        }

        var rise = new TranslateTransform();
        page.RenderTransform = rise;

        // One storyboard instance, retargeted per navigation. Stop() first so it releases the
        // outgoing page, which the Frame has already thrown away by this point.
        var enter = (Storyboard)RootGrid.Resources["PageEnter"];
        enter.Stop();

        Storyboard.SetTarget(enter.Children[0], page);
        Storyboard.SetTarget(enter.Children[1], rise);

        enter.Begin();
    }

    /// <summary>
    /// Closes open item O-3. PreferredMinimum* take physical pixels, so a flat 900x600 shrank to
    /// a 600x400 logical floor at 150% scaling. XamlRoot.RasterizationScale is the scale factor,
    /// and it needs no P/Invoke and no package — but XamlRoot is null until the tree is loaded,
    /// which is why this cannot go in the constructor.
    /// </summary>
    private void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        XamlRoot root = RootGrid.XamlRoot;

        // Also fires when the window is dragged to a monitor at a different scale.
        root.Changed += (changed, _) => ApplyMinimumSize(changed.RasterizationScale);

        ApplyMinimumSize(root.RasterizationScale);
    }

    private void ApplyMinimumSize(double scale)
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter)
        {
            return;
        }

        // Minimum window bounds from Metrics.xaml. design-plan section 5.
        presenter.PreferredMinimumWidth =
            (int)Math.Ceiling((double)Application.Current.Resources["Size.Window.MinWidth"] * scale);
        presenter.PreferredMinimumHeight =
            (int)Math.Ceiling((double)Application.Current.Resources["Size.Window.MinHeight"] * scale);
    }

    private void SidebarRail_SelectionRequested(object sender, string tag)
    {
        Type page = tag switch
        {
            "Library" => typeof(LibraryPage),
            "Search" => typeof(SearchPage),
            "Playlists" => typeof(PlaylistsPage),
            "Settings" => typeof(SettingsPage),
            _ => typeof(DashboardPage),
        };

        if (RootFrame.CurrentSourcePageType != page)
        {
            RootFrame.Navigate(page);
        }
    }

    /// <summary>
    /// Step 12, reworked in Step 16 and again in Step 17. The shell owns this rather than a page,
    /// because the transport outlives every page (A4) and pages are never cached. It runs on the
    /// tunnelling pass now, so every key in the table below is the shell's outright, and exactly two
    /// controls are ever handed one back: a TextBox being typed into and a Slider being arrowed.
    /// Handled is set true up front and cleared by the default arm, so anything outside the table
    /// still reaches the focused control.
    /// </summary>
    private void RootGrid_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        Control? focused = FocusManager.GetFocusedElement(RootGrid.XamlRoot) as Control;

        bool control = InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(CoreVirtualKeyStates.Down);

        // Typing owns every key but the two a single-line box cannot use. Up and Down leave the box
        // for the page list, Ctrl+Up/Down is volume, and Space stays a space. No TextBox in the app
        // sets AcceptsReturn, so nothing is stolen from a caret here.
        if (focused is TextBox && e.Key is not (VirtualKey.Up or VirtualKey.Down))
        {
            return;
        }

        // A focused Slider keeps the bare arrows. The seek bar is already bracketed by
        // BeginScrub/EndScrub and the volume bar is doing the same job these keys would; Space and
        // every Ctrl pair still reach the shell, which is why this no longer returns on any key.
        // RangeBase rather than Slider since v1.1, so the text-size dial keeps them too.
        if (focused is RangeBase && !control
            && e.Key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down)
        {
            return;
        }

        // Up and Down are the shell's only until focus is already inside a list; from then on they
        // belong to that list, or its highlight could never walk - EnterPageContent would just
        // re-focus the same control on every press. FocusState is the test, and it reads Keyboard
        // both after a Tab and after EnterPageContent took focus, which is exactly the point.
        // SPACE IS DELIBERATELY NOT HERE: it is the shell's unconditionally, and that is what stops
        // a focused row replaying itself. Trap: see TRAPS.md T-10.
        if (!control && e.Key is VirtualKey.Up or VirtualKey.Down
            && focused?.FocusState == FocusState.Keyboard)
        {
            return;
        }

        e.Handled = true;

        // Switched on the pair so each row reads as one shortcut. A guarded duplicate case label
        // would say the same thing less plainly.
        switch (e.Key, control)
        {
            case (VirtualKey.Space, false): Player.TogglePlayPause(); break;
            case (VirtualKey.Up, false):
            case (VirtualKey.Down, false): e.Handled = EnterPageContent(); break;
            case (VirtualKey.Left, false): Player.SeekBack(); break;
            case (VirtualKey.Right, false): Player.SeekForward(); break;
            case (VirtualKey.Left, true): Player.Previous(); break;
            case (VirtualKey.Right, true): Player.Next(); break;
            case (VirtualKey.Up, true): Player.VolumeUp(); break;
            case (VirtualKey.Down, true): Player.VolumeDown(); break;
            case (VirtualKey.M, true): Player.ToggleMute(); break;
            case (VirtualKey.S, true): Player.ToggleShuffle(); break;
            case (VirtualKey.R, true): Player.CycleRepeat(); break;
            default: e.Handled = false; break;
        }
    }

    /// <summary>
    /// Step 16. Moves keyboard focus into the current page's own list, so a bare Up or Down starts
    /// walking the middle column instead of doing nothing. Focus is taken with FocusState.Keyboard
    /// deliberately: that is what makes the NEXT arrow hand the key straight back to the list. False
    /// means the page named no target, and the key is then left to bubble untouched.
    /// </summary>
    private bool EnterPageContent()
        => RootFrame.Content is IArrowHost host
           && host.ArrowTarget is Control target
           && target.Focus(FocusState.Keyboard);

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        => (AppWindow.Presenter as OverlappedPresenter)?.Minimize();

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter)
        {
            return;
        }

        if (presenter.State == OverlappedPresenterState.Maximized)
        {
            presenter.Restore();
        }
        else
        {
            presenter.Maximize();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// v1.1. ChromeMaximizeContrast or ChromeRestoreContrast, with the tooltip and the automation
    /// name kept in step, so a screen reader never announces the action the button no longer does.
    /// </summary>
    private void SyncMaximizeButton()
    {
        bool maximized = AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };
        string action = maximized ? "Restore" : "Maximize";

        MaximizeGlyph.Glyph = maximized ? "" : "";
        ToolTipService.SetToolTip(MaximizeButton, action);
        AutomationProperties.SetName(MaximizeButton, action);
    }

    private void QueueList_ItemClick(object sender, ItemClickEventArgs e)
        => Player.JumpTo(((ListView)sender).Items.IndexOf(e.ClickedItem));

    private void ErrorBar_CloseButtonClick(InfoBar sender, object args) => Player.DismissError();
}
