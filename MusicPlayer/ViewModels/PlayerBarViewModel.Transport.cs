using MusicPlayer.Models;
using MusicPlayer.Services;

namespace MusicPlayer.ViewModels;

/// <summary>
/// PlayerBarViewModel, transport half: what is playing, where it is up to, how loud, and every button
/// that changes one of those. Split out in Step 15 - see PlayerBarViewModel.cs for why this is a
/// partial file and not a new type.
/// </summary>
public sealed partial class PlayerBarViewModel
{
    // Segoe Fluent Icons code points, built from the number for the same reason RepeatGlyph's are.
    private static readonly string VolumeGlyph = char.ConvertFromUtf32(0xE767);
    private static readonly string MutedGlyph = char.ConvertFromUtf32(0xE74F);

    /// <summary>
    /// How far one keyboard press moves. Step 12. Behaviour constants, so they live here and not in
    /// Themes/ — nothing about them is a design token, and the shell must not carry the numbers.
    /// </summary>
    private static readonly TimeSpan SeekStep = TimeSpan.FromSeconds(5);

    /// <summary>On the same 0-100 scale as the slider and settings.json.</summary>
    private const double VolumeStep = 5;

    private bool _isScrubbing;

    /// <summary>Where the current track is up to, in milliseconds. Re-read on every position tick.</summary>
    public double PositionMs => _audio.Position.TotalMilliseconds;

    /// <summary>The current track's total length in milliseconds. Zero before anything is loaded.</summary>
    public double DurationMs => _audio.Duration.TotalMilliseconds;

    /// <summary>SeekBarSliderStyle runs on a 0-100 scale. design-plan section 4.1.</summary>
    public double PositionPercent => DurationMs <= 0 ? 0 : PositionMs / DurationMs * 100;

    /// <summary>Whether audio is actually coming out. Drives the play/pause glyph through a converter.</summary>
    public bool IsPlaying => _audio.IsPlaying;

    /// <summary>
    /// The play/pause button's accessible name. It names the ACTION available, not the current state,
    /// so a screen reader announces the same thing the glyph shows.
    /// </summary>
    public string PlayPauseLabel => _audio.IsPlaying ? "Pause" : "Play";

    /// <summary>Volume on the 0-100 scale. Read straight from the service, never cached here.</summary>
    public double Volume => _audio.Volume;

    /// <summary>
    /// Replaces the queue with a list and starts playing one item of it. This is the single entry
    /// point for every "play this" gesture in the app - a Library row, Play all, a playlist, a
    /// dashboard card - which is why the error bar is cleared here rather than at each call site.
    /// An out-of-range index is ignored instead of throwing, because a stale list index is a
    /// plausible thing for a page to hand over.
    /// </summary>
    public void PlayFrom(IReadOnlyList<Track> tracks, int index)
    {
        if (index < 0 || index >= tracks.Count)
        {
            return;
        }

        ErrorMessage = null;
        _queue.Load(tracks, index);
        _audio.Play(_queue.Current!);
        RememberPosition();
    }

    /// <summary>
    /// Jumps to a row the user clicked in the queue rail. The queue keeps its contents and its order;
    /// only the cursor moves. An index the queue rejects does nothing at all, which is the right
    /// answer when the rail is a frame behind.
    /// </summary>
    public void JumpTo(int index)
    {
        Track? track = _queue.JumpTo(index);

        if (track is not null)
        {
            ErrorMessage = null;
            _audio.Play(track);
            RememberPosition();
        }
    }

    /// <summary>
    /// Play or pause, from the transport button, the Space key or a media key. It saves the position
    /// afterwards, so closing the app while paused resumes at the right place rather than at the last
    /// track change.
    /// </summary>
    public void TogglePlayPause()
    {
        _audio.TogglePlayPause();
        RememberPosition();
    }

    /// <summary>
    /// Goes back one track. Does nothing at the start of the queue with repeat off - it deliberately
    /// does not restart the current track, because "Previous" that replays the same song reads as
    /// broken.
    /// </summary>
    public void Previous()
    {
        Track? track = _queue.MovePrevious();

        if (track is not null)
        {
            _audio.Play(track);
            RememberPosition();
        }
    }

    /// <summary>
    /// Advances the queue, and is also what a finished track runs into. Three outcomes: a next track
    /// plays, repeat-one restarts the current one, or the queue is over and playback stops. The
    /// repeat-one branch rewinds and toggles instead of calling Play, because Play increments the play
    /// count and a looping track would inflate it every lap. Trap: see TRAPS.md T-08.
    /// </summary>
    public void Next()
    {
        Track? track = _queue.MoveNext();

        if (track is not null)
        {
            _audio.Play(track);
            RememberPosition();
        }
        else if (Repeat == RepeatMode.One)
        {
            // Must not re-enter Play(): that re-fires MarkPlayed on every loop and inflates
            // play_count / Recently Played. CONSTRAINT M, design-plan Step 10b.
            _audio.Seek(TimeSpan.Zero);
            _audio.TogglePlayPause();
            RememberPosition();
        }
        else
        {
            _audio.Stop();
        }
    }

    /// <summary>
    /// Held-thumb guard. While the user drags, the service must not push its own position in, or
    /// the two fight and the thumb jumps. A ValueChanged handler alone cannot tell a drag from
    /// our own four-times-a-second update, so the drag says so explicitly.
    /// </summary>
    public void BeginScrub() => _isScrubbing = true;

    /// <summary>
    /// Ends a drag and seeks ONCE, on release rather than per tick. Safe to call more than once: a
    /// slider ends a drag through either a pointer release or a lost capture, and both are wired so a
    /// drag that leaves the window still finishes instead of leaving the poll suppressed forever.
    /// </summary>
    public void EndScrub(double percent)
    {
        if (!_isScrubbing)
        {
            return;
        }

        _isScrubbing = false;

        if (DurationMs > 0)
        {
            _audio.Seek(TimeSpan.FromMilliseconds(DurationMs * percent / 100));
        }

        OnPositionChanged(this, EventArgs.Empty);
    }

    /// <summary>Keyboard transport, Step 12. The step sizes stay in this file, not in the shell.</summary>
    public void SeekForward() => SeekBy(SeekStep);

    /// <summary>Steps back 5 seconds, from the Left arrow key. The mirror of SeekForward.</summary>
    public void SeekBack() => SeekBy(SeekStep.Negate());

    /// <summary>Raises the volume 5, from Ctrl+Up. Clamped by SetVolume, so it stops at 100.</summary>
    public void VolumeUp() => SetVolume(Volume + VolumeStep);

    /// <summary>Lowers the volume 5, from Ctrl+Down. Reaching 0 mutes without touching the mute memory.</summary>
    public void VolumeDown() => SetVolume(Volume - VolumeStep);

    /// <summary>
    /// Clamped at both ends: seeking past the duration parks the engine at the end of the file with
    /// a thumb the user then cannot drag back.
    ///
    /// The explicit OnPositionChanged is load-bearing. The 250ms poll is STOPPED while paused, so
    /// without it a seek on a paused track would move the audio and leave the bar where it was.
    /// </summary>
    private void SeekBy(TimeSpan delta)
    {
        if (_audio.Current is null)
        {
            return;
        }

        _audio.Seek(TimeSpan.FromTicks(
            Math.Clamp((_audio.Position + delta).Ticks, 0, _audio.Duration.Ticks)));

        OnPositionChanged(this, EventArgs.Empty);
    }

    /// <summary>
    /// Sets the system volume. Nothing is persisted since Step 18: the level belongs to Windows now,
    /// and writing it to settings.json would only invite a later launch to push it back onto the
    /// user's speakers.
    /// </summary>
    public void SetVolume(double volume0To100)
    {
        _audio.SetVolume(volume0To100);
        Raise(nameof(Volume));
    }

    /// <summary>
    /// The mute button's glyph. A crossed-out speaker while muted, because a real mute leaves the
    /// volume bar showing the level it will return to, so the bar alone cannot say whether there is
    /// any sound. Same shape as RepeatGlyph, which is why this needs no converter.
    /// </summary>
    public string MuteGlyph => _audio.IsMuted ? MutedGlyph : VolumeGlyph;

    /// <summary>
    /// Mutes or unmutes the system, from the button or Ctrl+M. Step 18 made this a real mute bit
    /// instead of "set the volume to zero and remember the old one" - Windows already remembers the
    /// level behind a mute, so the app no longer keeps a copy that could drift from it.
    /// </summary>
    public void ToggleMute()
    {
        _audio.SetMuted(!_audio.IsMuted);
        Raise(nameof(MuteGlyph));
    }

    /// <summary>
    /// Re-reads volume and mute from the system, for when the hardware keys or the Windows slider
    /// moved them behind the app's back. Called from MainWindow when the window is activated, which
    /// is the cheapest moment that covers the realistic case.
    /// </summary>
    public void RefreshVolume()
    {
        _audio.RefreshVolume();
        Raise(nameof(Volume));
        Raise(nameof(MuteGlyph));
    }
}
