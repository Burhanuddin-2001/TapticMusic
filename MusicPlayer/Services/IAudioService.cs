using MusicPlayer.Models;

namespace MusicPlayer.Services;

/// <summary>
/// The playback lifecycle. UI-thread, same as every other member on this interface.
///
/// There is no Seeking member on purpose. A scrub is a UI gesture, not a playback state: the
/// engine keeps playing or stays paused throughout, and the guard that stops the 250ms poll
/// fighting the thumb is PlayerBarViewModel._isScrubbing. Adding Seeking here would be a state
/// nothing ever assigns.
/// </summary>
public enum PlaybackState
{
    /// <summary>Nothing loaded, or stopped and rewound. What the app starts in.</summary>
    Idle,

    /// <summary>Play was called but the engine has not opened the file yet, so no sound yet.</summary>
    Loading,

    /// <summary>Audio is actually playing. The only state that makes IsPlaying true.</summary>
    Playing,

    /// <summary>Loaded and holding position. Also where a finished track lands.</summary>
    Paused,

    /// <summary>The engine could not open or decode the file. The message went out on Failed.</summary>
    Failed,
}

/// <summary>
/// The whole audio surface the rest of the app is allowed to see. Views and view models talk to
/// this and never to a playback engine. Invariant A1.
///
/// Every event on this interface is raised on the UI thread.
/// </summary>
public interface IAudioService
{
    /// <summary>Play, pause, or a change of track. Anything that alters what the bar shows.</summary>
    event EventHandler? StateChanged;

    /// <summary>Roughly four times a second while playing, and not at all while paused.</summary>
    event EventHandler? PositionChanged;

    /// <summary>
    /// A track reached its natural end. Not raised by Stop, so a subscriber can always treat this as
    /// "decide what plays next" without checking why playback stopped.
    /// </summary>
    event EventHandler? TrackEnded;

    /// <summary>A file that could not be opened or decoded. The string is fit to show a person.</summary>
    event EventHandler<string>? Failed;

    /// <summary>
    /// The Windows media overlay, or a hardware media key, asked for the next track. Step 12.
    ///
    /// It is a request and not an action because the queue lives in QueueService, which the audio
    /// implementation deliberately cannot see. PlayerBarViewModel already bridges the two, so it
    /// subscribes to this and calls its own Next(), and the SMTC button ends up doing exactly what
    /// the Next button in the transport bar does.
    /// </summary>
    event EventHandler? NextRequested;

    /// <summary>The same, for the previous track.</summary>
    event EventHandler? PreviousRequested;

    /// <summary>The loaded track, or null before anything has been opened. Survives a pause.</summary>
    Track? Current { get; }

    /// <summary>
    /// True only while audio is actually coming out. This is the ONLY playback-state fact on the
    /// interface: the full PlaybackState enum stays inside the implementation, because no consumer
    /// outside it ever needed the difference between Idle, Paused and Failed.
    /// </summary>
    bool IsPlaying { get; }

    /// <summary>How far into the track playback has reached. Read it on PositionChanged rather than caching it.</summary>
    TimeSpan Position { get; }

    /// <summary>
    /// The track's total length. Comes from the tag first so the seek bar can draw full width at
    /// once, and is corrected once the engine has the file open if the tag said zero.
    /// </summary>
    TimeSpan Duration { get; }

    /// <summary>
    /// 0-100, the scale the player bar slider already uses. Since Step 18 this is the WINDOWS MASTER
    /// volume, not this app's own attenuation, so it is the same number the Windows slider shows.
    /// </summary>
    double Volume { get; }

    /// <summary>Whether the system is muted. A real mute bit, not a volume of zero.</summary>
    bool IsMuted { get; }

    /// <summary>
    /// Open a track and start it from the beginning. This is the call that counts as "played" and
    /// increments the play count, so it is for a deliberate user action - use Load for a restore.
    /// </summary>
    void Play(Track track);

    /// <summary>
    /// Open a track paused at a position, without counting it as played. This is what "resume
    /// where I left off" needs: the bar comes back filled in, and nothing starts making noise
    /// on its own.
    /// </summary>
    void Load(Track track, TimeSpan position);

    /// <summary>
    /// Flip between playing and paused without re-opening the file. Does nothing when no track is
    /// loaded, which is what makes it safe to wire straight to the Space key.
    /// </summary>
    void TogglePlayPause();

    /// <summary>
    /// Pause and rewind to zero, leaving the track loaded so the bar still shows it. Does NOT raise
    /// TrackEnded, so it cannot trigger the next track.
    /// </summary>
    void Stop();

    /// <summary>
    /// Jump to a position. Call it ONCE on scrub release, never per drag tick: seeking on every tick
    /// makes the engine stutter, and the guard that keeps the thumb steady in between is
    /// PlayerBarViewModel._isScrubbing. A file the engine cannot seek is left alone silently.
    /// </summary>
    void Seek(TimeSpan position);

    /// <summary>
    /// Set the system volume on the 0-100 scale. Values outside that range are clamped rather than
    /// rejected, so a bad caller cannot make the machine silent or deafening.
    /// </summary>
    void SetVolume(double volume0To100);

    /// <summary>
    /// Mute or unmute the system. Windows remembers the level behind a mute, which is why this is a
    /// separate bit and not a volume of zero.
    /// </summary>
    void SetMuted(bool muted);

    /// <summary>
    /// Re-read volume and mute from the system, for when something outside this app changed them.
    /// Raises nothing: the caller decides what to notify, because the transport owns the bindings.
    /// </summary>
    void RefreshVolume();
}
