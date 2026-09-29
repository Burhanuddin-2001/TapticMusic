using MusicPlayer.Data;
using MusicPlayer.Services;

namespace MusicPlayer.ViewModels;

/// <summary>
/// The watched folders and the scan. This is the only way anything gets into the library, which is
/// why it exists in a step that otherwise adds no UI: without it nothing can ever be scanned.
/// The rest of the Settings panel is rebuilt from design-plan section 5.1 in Step 10.
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly LibraryRepository _library;
    private readonly LibraryScanner _scanner;
    private readonly SettingsService _settings;

    private List<string> _folders = [];
    private string _status = string.Empty;

    /// <summary>
    /// Stores the three services this page drives. Note what is NOT here: ThemeService. This page is
    /// new-ed on every visit, so it cannot own live theme state, and the theme properties below reach
    /// App.Theme statically instead. That is the one documented exception to A3's constructor rule.
    /// </summary>
    public SettingsViewModel(LibraryRepository library, LibraryScanner scanner, SettingsService settings)
    {
        _library = library;
        _scanner = scanner;
        _settings = settings;
    }

    /// <summary>
    /// Nullable because ToggleButton.IsChecked and CheckBox.IsChecked are, and a two-way x:Bind
    /// has to match the target type exactly. Indeterminate is never set, so null reads as off.
    /// </summary>
    public bool? ResumeOnLaunch
    {
        get => _settings.ResumeOnLaunch;
        set
        {
            _settings.ResumeOnLaunch = value == true;
            Raise();
        }
    }

    /// <summary>
    /// Step 13. int, not the enum: this is bound to RadioButtons.SelectedIndex, and a two-way
    /// x:Bind has to match the target type exactly — the same reason the toggles above are bool?.
    /// The order here is the order of the radio buttons in SettingsPage.xaml and of AppTheme's
    /// pinned values; all three must stay in step.
    ///
    /// Applied immediately rather than on navigation away, because a theme picker that does not
    /// preview itself is useless. ThemeService is on App, not injected: SettingsPage is new-ed on
    /// every visit, so this view model cannot own live theme state.
    /// </summary>
    public int ThemeIndex
    {
        get => (int)_settings.Theme;
        set
        {
            // RadioButtons.SelectedIndex is -1 when nothing is selected, and it publishes that -1
            // as an INTERMEDIATE while it clears the old selection before setting the new one. Do
            // not delete this as a redundant bounds check: without it the cast below stores -1 as
            // the theme, and SettingsService starts its save timer before anything validates, so
            // the invalid value reaches settings.json and the app opens wrong forever after.
            if (value < 0)
            {
                return;
            }

            _settings.Theme = (AppTheme)value;
            App.Theme.Apply(_settings.Theme, _settings.Accent);
            Raise();
        }
    }

    /// <summary>Step 13. Same int-for-SelectedIndex reasoning as ThemeIndex.</summary>
    public int AccentIndex
    {
        get => (int)_settings.Accent;
        set
        {
            // Same -1 guard as ThemeIndex, and here it is not cosmetic: the accent IS used as an
            // array index in ThemeService, so -1 threw IndexOutOfRangeException and killed the app.
            if (value < 0)
            {
                return;
            }

            _settings.Accent = (AccentSwatch)value;
            App.Theme.Apply(_settings.Theme, _settings.Accent);
            Raise();
        }
    }

    /// <summary>
    /// v1.1. The text-size dial, in percent. Two-way bound to RangeBase.Value, so it is a double and
    /// arrives unrounded mid-drag; TextScale takes the raw value, which is what keeps the text gliding,
    /// and SettingsService rounds what it stores. The dial snaps to a whole percent on release.
    /// </summary>
    public double TextScalePercent
    {
        get => _settings.TextScalePercent;
        set
        {
            _settings.TextScalePercent = value;
            TextScale.Factor = value / 100;

            // Deliberately NOT Raise() for this property. The getter is rounded, and handing that
            // back to the dial mid-drag would pull the tape onto whole percents and make it tick.
            Raise(nameof(TextScaleLabel));
        }
    }

    /// <summary>The readout beside the dial. Rounded, so it never shows "112.37%" mid-drag.</summary>
    public string TextScaleLabel => $"{Math.Round(TextScale.Factor * 100)}%";

    /// <summary>
    /// Whether a library rescan runs at launch. Nullable because a WinUI CheckBox is three-state and
    /// binds bool?; the setter collapses null to false, so an indeterminate box can never be
    /// persisted. Writing it starts SettingsService's 500ms save timer.
    /// </summary>
    public bool? ScanOnStartup
    {
        get => _settings.ScanOnStartup;
        set
        {
            _settings.ScanOnStartup = value == true;
            Raise();
        }
    }

    /// <summary>
    /// The watched folder paths, as the Settings list shows them. The setter re-announces
    /// HasNoFolders with it, because the empty state and the list always change together.
    /// </summary>
    public List<string> Folders
    {
        get => _folders;
        private set
        {
            Set(ref _folders, value);
            Raise(nameof(HasNoFolders));
        }
    }

    /// <summary>
    /// Drives x:Load on the "add a folder" empty state. This is the most important empty state in the
    /// app: with no folders there is no way for anything to enter the library at all.
    /// </summary>
    public bool HasNoFolders => _folders.Count == 0;

    /// <summary>
    /// The one line under the folder list - a track count, or "Scanning…" while a scan runs. Set
    /// twice by RescanAsync on purpose, so the user sees the scan start rather than a frozen number.
    /// </summary>
    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    /// <summary>
    /// Reads the folder list and counts the tracks, on every visit to the page. The count runs inside
    /// Task.Run because it is a full table COUNT, which is slow enough to notice on a large library.
    /// </summary>
    public async Task LoadAsync()
    {
        ReadFolders();
        Status = FormatCount(await Task.Run(_library.CountTracks));
    }

    /// <summary>
    /// Adds a folder and immediately scans it, so the user sees tracks appear rather than having to
    /// press Rescan themselves. The folder row is written first and the list re-read before the scan
    /// starts, which is what makes the new path show up straight away.
    /// </summary>
    public async Task AddFolderAsync(string path)
    {
        _library.AddFolder(path);
        ReadFolders();
        await RescanAsync();
    }

    /// <summary>
    /// Rescans every watched folder and reports the resulting track count. Safe to call repeatedly:
    /// the scanner skips a file whose size, mtime and art_key are unchanged AND whose last verdict
    /// was readable, so a second pass over a large library is fast. The scan runs on a background
    /// thread inside the scanner.
    ///
    /// The unreadable count is appended only when it is non-zero. ATL degrades rather than throwing,
    /// so this line is the one place a scan that failed on everything can say so. Trap: see T-11.
    /// </summary>
    public async Task RescanAsync()
    {
        Status = "Scanning…";

        ScanResult result = await _scanner.ScanAllAsync();

        Status = result.Unreadable == 0
            ? FormatCount(result.Tracks)
            : $"{FormatCount(result.Tracks)} · {result.Unreadable} unreadable";
    }

    /// <summary>
    /// Re-reads the folder paths from the database. Called after any change rather than mutating the
    /// list in place, which keeps the database as the single source of truth for what is watched.
    /// </summary>
    private void ReadFolders() => Folders = _library.GetFolders().Select(folder => folder.Path).ToList();

    /// <summary>
    /// Singularises the track count for the status line. This is the THIRD copy of that rule in the
    /// app - Playlist.TrackCountLabel and DashboardViewModel.TrackWord are the others - and it is
    /// left duplicated on purpose: a shared helper for one short line would add a cross-layer
    /// dependency between Data, ViewModels and back, to save nothing. See Plan-15.md.
    /// </summary>
    private static string FormatCount(int tracks) => tracks == 1 ? "1 track" : $"{tracks} tracks";
}
