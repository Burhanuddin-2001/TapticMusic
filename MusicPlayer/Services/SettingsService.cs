using System.Text.Json;
using Microsoft.UI.Dispatching;
using Windows.Storage;

namespace MusicPlayer.Services;

/// <summary>
/// One flat settings.json holding small preferences only. Architecture notes, section 2.
///
/// Writes are debounced: dragging the volume slider would otherwise rewrite the file dozens of
/// times a second.
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly DispatcherQueueTimer _saveTimer;
    private readonly Data _data;

    /// <summary>
    /// Must be constructed on the UI thread: the debounce timer comes from that thread's queue.
    /// </summary>
    public SettingsService()
    {
        _path = Path.Combine(ApplicationData.Current.LocalFolder.Path, "settings.json");
        _data = Load(_path);

        _saveTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(500);
        _saveTimer.IsRepeating = false;
        _saveTimer.Tick += (_, _) => Save();
    }

    /// <summary>
    /// Whether the app re-opens the last track, paused at where it stopped, on the next launch.
    /// Defaults to true. Read once by PlayerBarViewModel.RestoreLastPlayed and toggled from Settings.
    /// </summary>
    public bool ResumeOnLaunch
    {
        get => _data.ResumeOnLaunch;
        set
        {
            if (_data.ResumeOnLaunch == value)
            {
                return;
            }

            _data.ResumeOnLaunch = value;
            _saveTimer.Start();
        }
    }

    /// <summary>
    /// Whether App.OnLaunched kicks off a library rescan in the background. Defaults to false,
    /// because a scan on every launch costs disk on a large library for a folder that rarely changes.
    /// </summary>
    public bool ScanOnStartup
    {
        get => _data.ScanOnStartup;
        set
        {
            if (_data.ScanOnStartup == value)
            {
                return;
            }

            _data.ScanOnStartup = value;
            _saveTimer.Start();
        }
    }

    /// <summary>Step 13. System means follow Windows. Applied by ThemeService.</summary>
    public AppTheme Theme
    {
        get => _data.Theme;
        set
        {
            if (_data.Theme == value)
            {
                return;
            }

            _data.Theme = value;
            _saveTimer.Start();
        }
    }

    /// <summary>Step 13. Which of the five accent swatches. Applied by ThemeService.</summary>
    public AccentSwatch Accent
    {
        get => _data.Accent;
        set
        {
            if (_data.Accent == value)
            {
                return;
            }

            _data.Accent = value;
            _saveTimer.Start();
        }
    }

    /// <summary>
    /// The saved repeat mode, seeded into QueueService at startup and written back when the user
    /// cycles it. The equality guard is what stops a no-op assignment restarting the 500ms save
    /// timer. Stored as the enum's int, so RepeatMode's member order can never be changed.
    /// </summary>
    public RepeatMode Repeat
    {
        get => _data.Repeat;
        set
        {
            if (_data.Repeat == value)
            {
                return;
            }

            _data.Repeat = value;
            _saveTimer.Start();
        }
    }

    /// <summary>
    /// The saved shuffle mode, seeded into QueueService at startup. Shuffle is a mode and not a
    /// one-shot, which is the whole reason it is worth persisting at all.
    /// </summary>
    public bool Shuffle
    {
        get => _data.Shuffle;
        set
        {
            if (_data.Shuffle == value)
            {
                return;
            }

            _data.Shuffle = value;
            _saveTimer.Start();
        }
    }

    /// <summary>
    /// The track playing when the app last closed, or null if there was none. Read-only here on
    /// purpose: it moves together with the position, so both are written through SetLastPlayed.
    /// </summary>
    public long? LastTrackId => _data.LastTrackId;

    /// <summary>
    /// How far into that track the user had got. Read-only for the same reason as LastTrackId - a
    /// position without its track would resume the wrong song.
    /// </summary>
    public double LastPositionMs => _data.LastPositionMs;

    /// <summary>
    /// Both halves of "where I left off" move together, so they are written together rather than
    /// as two properties that could each start their own save.
    /// </summary>
    public void SetLastPlayed(long? trackId, double positionMs)
    {
        if (_data.LastTrackId == trackId && _data.LastPositionMs == positionMs)
        {
            return;
        }

        _data.LastTrackId = trackId;
        _data.LastPositionMs = positionMs;
        _saveTimer.Start();
    }

    /// <summary>
    /// Write now instead of in 500ms. Called when the window closes, so a change made in the last
    /// half second before exit is not lost.
    /// </summary>
    public void Flush()
    {
        if (_saveTimer.IsRunning)
        {
            _saveTimer.Stop();
            Save();
        }
    }

    /// <summary>
    /// A missing, empty, or truncated file is not an error. Defaults win and the next save
    /// overwrites it. Refusing to start because a preferences file is malformed would be worse
    /// than losing the preferences. This runs from the App constructor, before any window exists,
    /// so anything it lets escape is a launch failure with no UI to report it in.
    /// </summary>
    private static Data Load(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<Data>(File.ReadAllText(path), JsonOptions) ?? new Data();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException
                                        or NotSupportedException or InvalidOperationException)
        {
            return new Data();
        }
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(_data, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                        or NotSupportedException or InvalidOperationException)
        {
            // Losing a preference write is not worth crashing over.
            //
            // The two serializer faults are here because this runs on a timer tick with nothing
            // above it on the stack, so anything that escapes kills the process outright rather
            // than surfacing anywhere. That is not hypothetical: it is what a trimmed build did.
        }
    }

    private sealed class Data
    {
        // No Volume. Step 18 made the volume the system's, so storing one here would be a number
        // nothing applies - and applying it would move the user's speakers on every launch. An old
        // settings.json keeps its "volume" key; unknown properties are ignored on read.
        public bool ResumeOnLaunch { get; set; } = true;

        public bool ScanOnStartup { get; set; }

        public RepeatMode Repeat { get; set; }

        public bool Shuffle { get; set; }

        public long? LastTrackId { get; set; }

        public double LastPositionMs { get; set; }

        public AppTheme Theme { get; set; }

        public AccentSwatch Accent { get; set; }
    }
}
