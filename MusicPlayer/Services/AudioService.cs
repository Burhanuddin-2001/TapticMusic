using Microsoft.UI.Dispatching;
using MusicPlayer.Data;
using MusicPlayer.Models;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace MusicPlayer.Services;

/// <summary>
/// The only file in the application permitted to name MediaPlayer. Invariant A1.
///
/// Must be constructed on the UI thread. MediaPlayer raises every one of its own events on a
/// background thread, and touching a DependencyObject from one throws RPC_E_WRONG_THREAD, so the
/// dispatcher captured here is what every callback is marshalled back through.
/// </summary>
public sealed class AudioService : IAudioService, IDisposable
{
    private readonly MediaPlayer _player = new();
    private readonly LibraryRepository _library;
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _positionTimer;

    private MediaSource? _source;
    private TimeSpan _duration;
    private TimeSpan? _pendingSeek;

    /// <summary>
    /// Builds the one MediaPlayer the app owns and wires all five of its off-thread callbacks.
    /// MUST be constructed on the UI thread: the dispatcher captured here is what every callback is
    /// marshalled back through, and getting it from a pool thread would capture the wrong queue.
    /// It also turns on System Media Transport Controls, which is the whole of the app's media-key
    /// support, and starts the 250ms position timer that stands in for PlaybackSession.PositionChanged
    /// (A10). App.OnLaunched is the only caller.
    /// </summary>
    public AudioService(LibraryRepository library)
    {
        _library = library;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        _player.AutoPlay = false;

        // Step 12. A packaged app auto-registers System Media Transport Controls, and this used to
        // be false because the overlay would have appeared on the volume keys showing blank
        // metadata. OpenSource now applies real display properties, so it has something to show.
        //
        // This line is also the whole of "media keys": Windows routes the hardware Play, Pause,
        // Next and Previous keys to whichever app last played audio through SMTC, so there is no
        // VirtualKey handling to write for them anywhere in the app.
        _player.CommandManager.IsEnabled = true;

        // The player is handed one MediaPlaybackItem at a time and never a MediaPlaybackList, so
        // the command manager sees no track on either side of the current one and would grey both
        // buttons out. The real queue is QueueService's, which this file cannot see — the two
        // events below are how a press reaches it.
        _player.CommandManager.NextBehavior.EnablingRule = MediaCommandEnablingRule.Always;
        _player.CommandManager.PreviousBehavior.EnablingRule = MediaCommandEnablingRule.Always;

        _player.CommandManager.NextReceived += OnNextReceived;
        _player.CommandManager.PreviousReceived += OnPreviousReceived;

        _player.MediaEnded += OnMediaEnded;
        _player.MediaFailed += OnMediaFailed;
        _player.MediaOpened += OnMediaOpened;

        // READ, never write. Step 18 made Volume the system master level, so pushing a stored
        // number here would move the user's speakers every time the app launched.
        RefreshVolume();

        // The engine's own PositionChanged fires far more often than the UI needs, and every one
        // would have to cross a thread boundary. A timer on the UI thread gives four honest
        // updates a second instead. Invariant A10.
        _positionTimer = _dispatcher.CreateTimer();
        _positionTimer.Interval = TimeSpan.FromMilliseconds(250);
        _positionTimer.Tick += (_, _) => PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Fires when playback starts, pauses, stops or fails, so the transport can re-read everything.
    /// Always raised on the UI thread, which for the MediaPlayer-driven cases is what the TryEnqueue
    /// wrappers below are for. PlayerBarViewModel.OnStateChanged is the only subscriber.
    /// </summary>
    public event EventHandler? StateChanged;

    /// <summary>
    /// Fires four times a second while a track is playing, and never while it is paused, because the
    /// timer that drives it is stopped. It carries no value - the subscriber re-reads Position.
    /// </summary>
    public event EventHandler? PositionChanged;

    /// <summary>
    /// Fires when a track reaches its natural end, but NOT when Stop is called. The subscriber owns
    /// what happens next, because repeat and shuffle live in QueueService and this file cannot see it.
    /// </summary>
    public event EventHandler? TrackEnded;

    /// <summary>
    /// Fires when the engine cannot play a file, carrying a message meant for a human to read.
    /// It ends up in PlayerBarViewModel.ErrorMessage and from there in MainWindow's ErrorBar, so the
    /// text is written to be shown, not logged.
    /// </summary>
    public event EventHandler<string>? Failed;

    /// <summary>
    /// Fires when something OUTSIDE the app asks for the next track - the Windows media overlay or a
    /// hardware media key. It is a request and not an action, because the queue belongs to
    /// QueueService and A1 keeps that out of this file. PlayerBarViewModel routes it into its own
    /// Next(), so an overlay press behaves exactly like the transport button. Trap: see TRAPS.md T-05.
    /// </summary>
    public event EventHandler? NextRequested;

    /// <summary>The previous-track twin of <see cref="NextRequested"/>. Same reasoning, same routing.</summary>
    public event EventHandler? PreviousRequested;

    /// <summary>
    /// The track currently loaded, or null before anything has been opened. Set by OpenSource and
    /// never cleared, so it survives a pause and is what TogglePlayPause guards on.
    /// </summary>
    public Track? Current { get; private set; }

    /// <summary>
    /// Internal bookkeeping only, and deliberately PRIVATE rather than on IAudioService: it had no
    /// reader outside this file, and every consumer uses the derived IsPlaying instead. Narrowed in
    /// Step 15 after confirming zero external readers.
    /// </summary>
    private PlaybackState State { get; set; }

    /// <summary>
    /// True only while audio is actually coming out. This is the single playback fact the rest of the
    /// app is allowed to read, which is what keeps PlaybackState from leaking onto the interface.
    /// Note that Loading is NOT playing yet, so the transport shows Play for the moment before
    /// MediaOpened lands.
    /// </summary>
    public bool IsPlaying => State == PlaybackState.Playing;

    /// <summary>
    /// Where the current track is up to, read live from the engine rather than cached. Callers poll
    /// this from the PositionChanged event instead of holding their own copy, so the two can never
    /// drift apart.
    /// </summary>
    public TimeSpan Position => _player.PlaybackSession.Position;

    /// <summary>
    /// The current track's length. Taken from the tag at scan time so the seek bar can draw its full
    /// width immediately, then corrected in OnMediaOpened for a file whose tag reported zero.
    /// </summary>
    public TimeSpan Duration => _duration;

    /// <summary>
    /// Volume on a 0-100 scale, which is the scale the whole app speaks. Since Step 18 this is the
    /// system master level, so it is the same number the Windows slider shows. The 0.0-1.0 scale
    /// underneath exists only inside SetVolume and RefreshVolume.
    /// </summary>
    public double Volume { get; private set; }

    /// <summary>
    /// Whether the system is muted. A real mute bit, separate from a volume of zero, so the volume
    /// bar keeps showing the level the user will come back to when they unmute.
    /// </summary>
    public bool IsMuted { get; private set; }

    /// <summary>
    /// Opens a track and starts it from the beginning. It clears any pending seek first, so calling
    /// this after Load cancels the resume position rather than jumping to it. The play count is
    /// incremented HERE, at the start: a track you chose to start is a track you played. State goes
    /// to Loading rather than Playing, because the engine has not actually opened the file yet -
    /// OnMediaOpened promotes it.
    /// </summary>
    public void Play(Track track)
    {
        OpenSource(track);
        _pendingSeek = null;

        State = PlaybackState.Loading;
        _player.Play();

        _positionTimer.Start();

        // A track you started is a track you played, so this counts at the start, not the end.
        _library.MarkPlayed(track.Id);

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Opens a track PAUSED at a given position, which is what "resume where I left off" needs.
    /// Called once, by PlayerBarViewModel.RestoreLastPlayed at startup. It deliberately does not
    /// increment the play count, because the user has not chosen to play anything yet. The state is
    /// set to Paused and NOT Loading, or OnMediaOpened would promote it to Playing and the app would
    /// start making noise on launch.
    /// </summary>
    public void Load(Track track, TimeSpan position)
    {
        OpenSource(track);

        // Seeking before the engine has opened the file does nothing, so the position is parked
        // here and applied in OnMediaOpened. AutoPlay is off, so opening it does not start it.
        _pendingSeek = position;

        // Not Loading: AutoPlay is off, so OnMediaOpened must not promote this to Playing.
        State = PlaybackState.Paused;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Flips between playing and paused, and starts or stops the position timer with it. Does nothing
    /// at all when no track is loaded, which is what makes the Space shortcut safe to press on a
    /// freshly launched app. Note it does not re-open anything, so it is the cheap path and the one
    /// the transport button and the media keys both end up in.
    /// </summary>
    public void TogglePlayPause()
    {
        if (Current is null)
        {
            return;
        }

        if (IsPlaying)
        {
            _player.Pause();
            _positionTimer.Stop();
            State = PlaybackState.Paused;
        }
        else
        {
            _player.Play();
            _positionTimer.Start();
            State = PlaybackState.Playing;
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Pauses and rewinds to zero, leaving Current set so the transport still shows the track.
    /// Reached when the queue runs out with repeat off, and it raises StateChanged but NOT TrackEnded,
    /// so it cannot start the next track by accident.
    /// </summary>
    public void Stop()
    {
        _player.Pause();
        _player.PlaybackSession.Position = TimeSpan.Zero;
        _positionTimer.Stop();

        State = PlaybackState.Idle;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Jumps to a position, if the engine says this file can seek at all. Called once per scrub, on
    /// release rather than per drag tick - PlayerBarViewModel._isScrubbing is what holds the poll off
    /// in between. A file that cannot seek is silently left alone rather than throwing.
    /// </summary>
    public void Seek(TimeSpan position)
    {
        if (_player.PlaybackSession.CanSeek)
        {
            _player.PlaybackSession.Position = position;
        }
    }

    /// <summary>
    /// Sets the volume, clamping to 0-100 so a bad value cannot get through. Since Step 18 this is
    /// the WINDOWS MASTER volume, not this app's own attenuation, which is what makes Ctrl+Up able to
    /// climb past a system level of 20%. Still the one place the app's 0-100 scale and the 0.0-1.0
    /// scale below it meet, which is why no caller ever divides by 100.
    /// </summary>
    public void SetVolume(double volume0To100)
    {
        Volume = Math.Clamp(volume0To100, 0, 100);

        // The system endpoint when there is one, this app's own stream when there is not, so a
        // machine with no playback device still has a working volume bar. Pinned to 1.0 on the
        // system path because the two multiply: leaving an old attenuation behind would make the
        // bar read 100 while the output stayed quiet.
        bool viaSystem = SystemVolume.SetLevel(Volume / 100d);

        _player.Volume = viaSystem ? 1d : Volume / 100d;
    }

    /// <summary>
    /// Mutes or unmutes the system, which is the mute Windows draws as a crossed-out speaker. A real
    /// mute rather than a volume of zero, so Windows remembers the level behind it and this app does
    /// not have to. Does nothing when there is no usable endpoint.
    /// </summary>
    public void SetMuted(bool muted)
    {
        if (SystemVolume.SetMuted(muted))
        {
            IsMuted = muted;
        }
    }

    /// <summary>
    /// Re-reads volume and mute from the system, for when something outside this app moved them -
    /// the hardware volume keys, or the Windows slider. Both readings are left untouched when there
    /// is no usable endpoint, so an unplugged output does not reset the bar to zero.
    /// </summary>
    public void RefreshVolume()
    {
        if (SystemVolume.GetLevel() is double scalar)
        {
            // Rounded, because the whole promise of Step 18 is that this reads as the same number
            // Windows shows, and Windows shows whole percent. Unrounded, a read of 0.34999 would
            // make Ctrl+Up land on 39.999 and the bar would disagree with the system slider.
            Volume = Math.Round(scalar * 100d);
        }

        if (SystemVolume.GetMuted() is bool muted)
        {
            IsMuted = muted;
        }
    }

    /// <summary>
    /// Stops the timer, unsubscribes all five callbacks, and disposes the player and the open source.
    /// Called once, from App's Window.Closed handler. The unsubscribes matter as much as the disposes:
    /// a callback that survives the player can fire against a dead dispatcher during shutdown.
    /// </summary>
    public void Dispose()
    {
        _positionTimer.Stop();

        _player.CommandManager.NextReceived -= OnNextReceived;
        _player.CommandManager.PreviousReceived -= OnPreviousReceived;

        _player.MediaEnded -= OnMediaEnded;
        _player.MediaFailed -= OnMediaFailed;
        _player.MediaOpened -= OnMediaOpened;

        _player.Dispose();
        _source?.Dispose();

        SystemVolume.Release();
    }

    /// <summary>
    /// Wrappers four and five under A2 — the command manager raises these off the UI thread too.
    ///
    /// Handled is set synchronously, BEFORE this returns and outside the enqueue. Set it inside and
    /// the handler has already returned unhandled by then, so the command manager applies its own
    /// default and moves the playback position instead of changing track.
    /// </summary>
    private void OnNextReceived(MediaPlaybackCommandManager sender, MediaPlaybackCommandManagerNextReceivedEventArgs args)
    {
        args.Handled = true;
        _dispatcher.TryEnqueue(() => NextRequested?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>
    /// The previous-track twin of <see cref="OnNextReceived"/>. Same synchronous Handled, same reason.
    /// </summary>
    private void OnPreviousReceived(MediaPlaybackCommandManager sender, MediaPlaybackCommandManagerPreviousReceivedEventArgs args)
    {
        args.Handled = true;
        _dispatcher.TryEnqueue(() => PreviousRequested?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>
    /// Wrapper one under A2: the engine reports a finished track on a background thread. It parks the
    /// state at Paused and stops the timer, then hands the decision to the subscriber - repeat and
    /// shuffle belong to QueueService, so this file must not choose the next track itself.
    /// </summary>
    private void OnMediaEnded(MediaPlayer sender, object args) => _dispatcher.TryEnqueue(() =>
    {
        State = PlaybackState.Paused;
        _positionTimer.Stop();
        TrackEnded?.Invoke(this, EventArgs.Empty);
    });

    /// <summary>
    /// Wrapper two under A2. Turns an engine failure into a sentence a person can read and raises both
    /// StateChanged and Failed, so the transport settles and the error bar appears from one event.
    /// A missing or corrupt file lands here rather than throwing out of Play.
    /// </summary>
    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args) => _dispatcher.TryEnqueue(() =>
    {
        State = PlaybackState.Failed;
        _positionTimer.Stop();
        StateChanged?.Invoke(this, EventArgs.Empty);
        Failed?.Invoke(this, $"Could not play this file ({args.Error})");
    });

    /// <summary>
    /// The first moment a seek can land, which is what a resumed track waits for. Also the
    /// fallback for a file whose stored duration is 0, meaning the tag reader could not read its
    /// properties: the engine knows better once the file is actually open.
    /// </summary>
    private void OnMediaOpened(MediaPlayer sender, object args)
    {
        TimeSpan natural = sender.PlaybackSession.NaturalDuration;

        _dispatcher.TryEnqueue(() =>
        {
            if (_pendingSeek is TimeSpan resume)
            {
                _pendingSeek = null;
                Seek(resume);
            }

            if (_duration <= TimeSpan.Zero && natural > TimeSpan.Zero)
            {
                _duration = natural;
            }

            if (State == PlaybackState.Loading)
            {
                State = PlaybackState.Playing;
            }

            StateChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    /// <summary>
    /// Points the engine at a file and gives Windows the metadata for its media overlay. Shared by
    /// Play and Load, which is why it neither starts playback nor touches the state. It disposes the
    /// previous MediaSource first, because assigning Source does not, and one leaked handle per track
    /// adds up over an evening. Trap: see TRAPS.md T-06 for why the metadata goes on a
    /// MediaPlaybackItem rather than through SystemMediaTransportControls.DisplayUpdater.
    /// </summary>
    private void OpenSource(Track track)
    {
        // Replacing Source does not dispose the old MediaSource, and one leaked handle per track
        // played adds up over an evening.
        _source?.Dispose();
        _source = MediaSource.CreateFromUri(new Uri(track.Path));

        Current = track;

        // The scanner already measured this at scan time, so the bar can draw the full length
        // immediately instead of waiting for the engine to open the file.
        _duration = TimeSpan.FromMilliseconds(track.DurationMs);

        // Step 12. A MediaPlaybackItem rather than the bare MediaSource, because display properties
        // hang off the item and the command manager pushes them out to SMTC on its own. Driving
        // SystemMediaTransportControls.DisplayUpdater by hand instead would fight that.
        MediaPlaybackItem item = new(_source);
        MediaItemDisplayProperties display = item.GetDisplayProperties();

        display.Type = MediaPlaybackType.Music;
        display.MusicProperties.Title = track.Title;
        display.MusicProperties.Artist = track.Artist;
        display.MusicProperties.AlbumTitle = track.Album;

        if (!string.IsNullOrEmpty(track.ArtKey))
        {
            display.Thumbnail =
                RandomAccessStreamReference.CreateFromUri(new Uri(AlbumArtCache.UriFor(track.ArtKey)));
        }

        item.ApplyDisplayProperties(display);

        // The item is not held in a field: _player.Source owns it, and what actually needs
        // disposing is the MediaSource underneath, which _source already tracks.
        _player.Source = item;
    }
}
