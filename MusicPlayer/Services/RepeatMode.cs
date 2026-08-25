namespace MusicPlayer.Services;

/// <summary>
/// What happens when a track finishes. Off stops at the end of the queue, All wraps back to the
/// first track, and One re-loads the same track. Persisted as an int by SettingsService, so the
/// member order is pinned - reordering these would silently change every existing user's setting.
/// Lifted out of QueueService.cs in Step 15, for the same reason as AppTheme: SettingsService needed
/// the whole queue engine in scope to reach one enum.
/// </summary>
public enum RepeatMode
{
    /// <summary>Stop when the queue runs out. MoveNext returns null at the end.</summary>
    Off,

    /// <summary>Wrap from the last track back to the first, forever.</summary>
    All,

    /// <summary>Re-load the same track. MoveNext returns null so the caller restarts it in place.</summary>
    One,
}
