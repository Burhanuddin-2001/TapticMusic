using Microsoft.UI.Xaml;
using MusicPlayer.Data;
using MusicPlayer.Services;
using MusicPlayer.ViewModels;

namespace MusicPlayer;

/// <summary>
/// The composition root. It builds every service the app has, in an order that matters, and hands
/// them out through the static properties below.
///
/// There is no DI container (A3). Eight services created once do not need one, and a container would
/// be a sixth NuGet package against a ceiling of five. Pages take what they need through their
/// constructors and reach these statics only to fill them in.
///
/// Construction is deliberately SPLIT across two methods. Settings and Theme are built in the
/// CONSTRUCTOR because Application.RequestedTheme is write-once and settable only before a window
/// exists; everything else is built in OnLaunched. Trap: see TRAPS.md T-07.
/// </summary>
public partial class App : Application
{
    /// <summary>Needed by the folder picker, which opens relative to a window.</summary>
    public static Window Window { get; private set; } = null!;

    /// <summary>
    /// settings.json, with a 500ms debounce on writes. Built FIRST, in the constructor, because
    /// Application.RequestedTheme has to be set from it before any window exists.
    /// </summary>
    public static SettingsService Settings { get; private set; } = null!;

    /// <summary>All folders and tracks SQL. Every page and the scanner read through this one instance.</summary>
    public static LibraryRepository Library { get; private set; } = null!;

    /// <summary>All playlists SQL. Shares LibraryRepository.ReadTracks so there is one Track reader, not two.</summary>
    public static PlaylistRepository Playlists { get; private set; } = null!;

    /// <summary>
    /// The tag scanner. Stateless between runs, so one instance serves the startup scan and every
    /// later Rescan press.
    /// </summary>
    public static LibraryScanner Scanner { get; private set; } = null!;

    /// <summary>
    /// Playback, behind the interface rather than the concrete type, so nothing outside
    /// AudioService.cs can reach a MediaPlayer (A1). The only service App disposes on close, and
    /// since Step 18 it reads the system volume for itself rather than being handed a stored one.
    /// </summary>
    public static IAudioService Audio { get; private set; } = null!;

    /// <summary>
    /// Play order, cursor, repeat and shuffle. Seeded from Settings.Repeat and Settings.Shuffle at
    /// construction, so the user's modes survive a restart.
    /// </summary>
    public static QueueService Queue { get; private set; } = null!;

    /// <summary>
    /// Step 13. Owns the palette. CONSTRUCTED in the App constructor, but the first Apply is in
    /// MainWindow's constructor - Application.Resources is not reachable this early. Splitting the two
    /// is load-bearing, not tidiness. Trap: see TRAPS.md T-07.
    /// </summary>
    public static ThemeService Theme { get; private set; } = null!;

    /// <summary>
    /// One instance for the life of the app, because the player bar outlives every page. Pages
    /// reach it here rather than through a navigation parameter, which would mean threading it
    /// through all five of them for the sake of one.
    /// </summary>
    public static PlayerBarViewModel PlayerBar { get; private set; } = null!;

    /// <summary>
    /// Builds the two services that must exist before any window does. The order inside is forced and
    /// must not be rearranged: InitializeComponent, then SettingsService, then
    /// Application.RequestedTheme from it, then ThemeService. RequestedTheme is write-once and only
    /// settable here, which is what makes the "System" theme option possible at all and what gets the
    /// very first frame drawn in the right theme. It deliberately does NOT call Theme.Apply - see
    /// TRAPS.md T-07.
    /// </summary>
    public App()
    {
        InitializeComponent();

        // Step 13. Settings is read HERE and not in OnLaunched for exactly one reason:
        // Application.RequestedTheme is write-once and only settable before any window exists, so
        // the constructor is the only legal place to assign it, and it needs the saved value.
        // App.xaml no longer sets RequestedTheme at all — leaving it unset is what makes "System"
        // mean "follow Windows", per WinUI's own documented default.
        //
        // SettingsService needs a DispatcherQueue for its debounce timer. One exists here: the
        // generated Main calls DispatcherQueue.GetForCurrentThread() immediately before new App().
        Settings = new SettingsService();

        if (Settings.Theme != AppTheme.System)
        {
            RequestedTheme = Settings.Theme == AppTheme.Light
                ? ApplicationTheme.Light
                : ApplicationTheme.Dark;
        }

        // Constructed but NOT applied. Do not call Apply here: Application.Resources is not
        // reachable yet this early — the native Application object is still mid-initialisation and
        // get_Resources throws COMException 0x8000FFFF (E_UNEXPECTED). InitializeComponent having
        // run is not enough. The first Apply happens at the top of OnLaunched.
        Theme = new ThemeService();
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        // Only the schema check and the settings read run before the first frame. Both are
        // single-digit milliseconds. No scan and no track query happens here: cold start is
        // a priority in the architecture notes, and the pages load their own data once they are up.
        Db.EnsureCreated();

        // NOTE Step 13: there is deliberately no Theme.Apply call here. The first one lives in
        // MainWindow's constructor, which is the earliest point Application.Resources is PROVABLY
        // reachable — the window's own InitializeComponent has just resolved dozens of brushes out
        // of it. Calling it in the App ctor threw COMException 0x8000FFFF, and nothing establishes
        // that this spot is any safer. It costs nothing to wait: ThemeService mutates the shared
        // brush objects, so every element already built picks the change up, and MainWindow applies
        // before Window.Activate() below, so nothing is ever shown in the wrong palette.
        Library = new LibraryRepository();
        Playlists = new PlaylistRepository();
        Scanner = new LibraryScanner(Library);
        Audio = new AudioService(Library);
        Queue = new QueueService
        {
            Repeat = Settings.Repeat,
            Shuffle = Settings.Shuffle,
        };
        PlayerBar = new PlayerBarViewModel(Audio, Settings, Library, Queue);

        Window = new MainWindow();

        Window.Closed += (_, _) =>
        {
            // Closing mid-track is the one case no other save covers, because nothing paused.
            PlayerBar.RememberPosition();

            // A change in the last half second is still sitting in the debounce timer.
            Settings.Flush();

            // IAudioService deliberately says nothing about lifetime; only the app that built the
            // implementation gets to end it.
            (Audio as IDisposable)?.Dispose();
        };

        Window.Activate();

        // Both of these run after the window is up, never before it. Restoring reads one row and
        // opens a file paused; a startup scan can take seconds and must not hold the first frame.
        PlayerBar.RestoreLastPlayed();

        if (Settings.ScanOnStartup)
        {
            _ = ScanAsync();
        }

        // A discarded Task's exception vanishes unobserved, so this awaits inside its own
        // try/catch and routes any failure into the InfoBar instead.
        async Task ScanAsync()
        {
            try
            {
                await Scanner.ScanAllAsync();
            }
            catch (Exception ex)
            {
                PlayerBar.ReportError($"Library scan failed: {ex.Message}");
            }
        }
    }
}
