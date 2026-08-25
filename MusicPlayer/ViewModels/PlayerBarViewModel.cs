using MusicPlayer.Data;
using MusicPlayer.Models;
using MusicPlayer.Services;

namespace MusicPlayer.ViewModels;

/// <summary>
/// What the transport dock shows and what its buttons do. Reads through to IAudioService rather
/// than holding a copy of the playback state, so the two can never disagree.
///
/// SPLIT ACROSS FOUR PARTIAL FILES in Step 15 - this one plus .Transport, .Queue and .Art. It is
/// still ONE class and ONE instance, which is what A4 requires; the split is for reading, and it
/// changes nothing at runtime. Do not turn a partial into a separate type: A4 says this is the only
/// app-lifetime view model in the app.
///
/// This file holds the wiring: the services, the constructor, the error surface, resume, and the four
/// event handlers everything else reports through.
/// </summary>
public sealed partial class PlayerBarViewModel : ObservableObject
{
    private readonly IAudioService _audio;
    private readonly SettingsService _settings;
    private readonly LibraryRepository _library;
    private readonly QueueService _queue;

    private string? _errorMessage;

    /// <summary>
    /// Builds the transport and subscribes to everything that can change it. The four event
    /// subscriptions are the whole reason this class exists: it is the only object that holds both
    /// the audio service and the queue, so it is the only place their two halves can be joined.
    /// NextRequested and PreviousRequested are wired here too, which is how a press on the Windows
    /// media overlay or a hardware media key ends up doing exactly what the transport buttons do.
    /// Built once, in App.OnLaunched, and never rebuilt.
    /// </summary>
    public PlayerBarViewModel(IAudioService audio, SettingsService settings, LibraryRepository library, QueueService queue)
    {
        _audio = audio;
        _settings = settings;
        _library = library;
        _queue = queue;

        _audio.StateChanged += OnStateChanged;
        _audio.PositionChanged += OnPositionChanged;
        _audio.TrackEnded += (_, _) => Next();
        _audio.Failed += OnFailed;

        // Step 12. The media overlay and the hardware media keys land here, so an SMTC press does
        // exactly what the transport bar's own buttons do, queue and repeat rules included.
        _audio.NextRequested += (_, _) => Next();
        _audio.PreviousRequested += (_, _) => Previous();

        _queue.Changed += OnQueueChanged;
    }

    /// <summary>
    /// The big line in the transport bar. Falls back to "Nothing playing" rather than an empty
    /// string, so the bar reads as deliberate on a freshly launched app instead of looking broken.
    /// </summary>
    public string TrackTitle => _audio.Current?.Title ?? "Nothing playing";

    /// <summary>
    /// Doubles as the error line. A failed file has no artist worth showing, and the bar is the
    /// one place already looking at the track the user just picked.
    /// </summary>
    public string TrackArtist => ErrorMessage ?? _audio.Current?.Artist ?? "Pick a track to start";

    /// <summary>
    /// The current error, or null when there is none. The setter re-announces HasError with it,
    /// because the InfoBar's visibility and its text always change together.
    /// </summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            Set(ref _errorMessage, value);
            Raise(nameof(HasError));
        }
    }

    /// <summary>
    /// Whether the error bar is showing. MainWindow.xaml.cs watches this by name, because
    /// MainWindow.xaml cannot bind at all (A7b).
    /// </summary>
    public bool HasError => ErrorMessage is not null;

    /// <summary>
    /// Clears the error bar. Called from its own close button - the error is dismissed by the user
    /// rather than on a timer, because a failure the user did not read is a failure they will hit
    /// again.
    /// </summary>
    public void DismissError() => ErrorMessage = null;

    /// <summary>The InfoBar error channel is not only for playback failures — App uses this for a
    /// startup-scan failure too, since it is the only error surface the app has.</summary>
    public void ReportError(string message) => ErrorMessage = message;

    /// <summary>
    /// Writes "where I left off". Called when a track starts, when playback pauses, and when the
    /// window closes — deliberately not from the general state-changed handler, because a restore
    /// raises that before the seek has landed and would save a position of zero over the real one.
    /// </summary>
    public void RememberPosition()
        => _settings.SetLastPlayed(_audio.Current?.Id, _audio.Position.TotalMilliseconds);

    /// <summary>
    /// Puts the last track back in the bar, paused, at the position it was left at. A file that
    /// has since been deleted clears the setting instead of failing at startup.
    /// </summary>
    public void RestoreLastPlayed()
    {
        if (!_settings.ResumeOnLaunch || _settings.LastTrackId is not long id)
        {
            return;
        }

        Track? track = _library.GetTrackById(id);

        if (track is null || !File.Exists(track.Path))
        {
            _settings.SetLastPlayed(null, 0);
            return;
        }

        _audio.Load(track, TimeSpan.FromMilliseconds(_settings.LastPositionMs));
    }

    /// <summary>
    /// Re-reads everything a play, pause, track change or failure can affect. It refreshes the art
    /// first, which is cheap on a repeat because RefreshArt compares the album key before decoding
    /// anything. It deliberately does NOT call RememberPosition: a resume raises this before the
    /// pending seek has landed, so saving here would overwrite the stored position with zero.
    /// </summary>
    private void OnStateChanged(object? sender, EventArgs e)
    {
        RefreshArt();
        Raise(nameof(TrackTitle));
        Raise(nameof(TrackArtist));
        Raise(nameof(DurationMs));
        Raise(nameof(IsPlaying));
        Raise(nameof(PlayPauseLabel));
        OnPositionChanged(sender, e);
    }

    /// <summary>
    /// Moves the seek bar. Runs four times a second while playing, and is also called by hand from
    /// EndScrub and SeekBy, because the 250ms timer is stopped while paused and the bar would
    /// otherwise not move on a seek. It returns immediately during a drag, which is what stops the
    /// poll fighting the thumb the user is holding.
    /// </summary>
    private void OnPositionChanged(object? sender, EventArgs e)
    {
        if (_isScrubbing)
        {
            return;
        }

        Raise(nameof(PositionMs));
        Raise(nameof(PositionPercent));
    }

    /// <summary>
    /// Puts a playback failure on screen. It also re-announces TrackArtist, because that property
    /// doubles as the error line and would otherwise still be showing the old artist.
    /// </summary>
    private void OnFailed(object? sender, string message)
    {
        ErrorMessage = message;
        Raise(nameof(TrackArtist));
    }

    /// <summary>
    /// Re-announces the four queue-rail properties whenever the queue changes. Kept to exactly these
    /// four: MainWindow.xaml.cs filters PropertyChanged by name, and this event is the only thing
    /// that should ever rebuild the rail - not the 250ms position poll.
    /// </summary>
    private void OnQueueChanged(object? sender, EventArgs e)
    {
        Raise(nameof(QueueItems));
        Raise(nameof(QueueIndex));
        Raise(nameof(QueueListVisibility));
        Raise(nameof(EmptyQueueVisibility));
    }
}
