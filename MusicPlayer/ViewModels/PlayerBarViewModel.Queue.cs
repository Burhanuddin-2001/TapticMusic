using Microsoft.UI.Xaml;
using MusicPlayer.Models;
using MusicPlayer.Services;

namespace MusicPlayer.ViewModels;

/// <summary>
/// PlayerBarViewModel, queue half: the rail's contents, and the repeat and shuffle modes. Split out
/// in Step 15 - see PlayerBarViewModel.cs for why this is a partial file and not a new type.
///
/// Everything here writes through to BOTH QueueService and SettingsService, which is what makes the
/// modes survive a restart.
/// </summary>
public sealed partial class PlayerBarViewModel
{
    // Segoe Fluent Icons code points. Built from the number, same reason as BoolToPlayPauseGlyphConverter.
    private static readonly string RepeatAllGlyph = char.ConvertFromUtf32(0xE1CD);
    private static readonly string RepeatOneGlyph = char.ConvertFromUtf32(0xE8ED);

    /// <summary>
    /// The queue in play order, for the rail. MainWindow.xaml.cs watches this by name and rebuilds the
    /// rail from it, which is why OnQueueChanged is the only thing that announces it.
    /// </summary>
    public IReadOnlyList<Track> QueueItems => _queue.Items;

    /// <summary>Which row in QueueItems is playing, or -1 when the queue is empty. Drives the rail's highlight.</summary>
    public int QueueIndex => _queue.CurrentIndex;

    /// <summary>
    /// Whether the rail shows its list. A Visibility and not a bool because MainWindow.xaml cannot
    /// bind or use a converter at all (A7b), so the shell needs the final value pushed to it.
    /// </summary>
    public Visibility QueueListVisibility => QueueItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The inverse of QueueListVisibility, for the rail's empty state. Same reason it is a Visibility.</summary>
    public Visibility EmptyQueueVisibility => QueueItems.Count > 0 ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>
    /// The repeat mode. The setter writes to the queue AND to settings, so the choice survives a
    /// restart, then announces the two derived display properties with it. Private set: the only way
    /// to change it is CycleRepeat, so the three modes can only ever be walked in order.
    /// </summary>
    public RepeatMode Repeat
    {
        get => _queue.Repeat;
        private set
        {
            _queue.Repeat = value;
            _settings.Repeat = value;

            // Repeat itself has no binding today - the button reads RepeatGlyph and RepeatOpacity.
            // Raised anyway: dropping a change notification for a public property is exactly how a
            // future binding ends up silently never updating, which is the failure A7b documents.
            Raise(nameof(Repeat));

            Raise(nameof(RepeatGlyph));
            Raise(nameof(RepeatOpacity));
        }
    }

    /// <summary>
    /// Shuffle as a mode, not a one-shot. Turning it on reshuffles immediately but keeps the playing
    /// track, so toggling mid-song never restarts it. Writes to both the queue and settings, as
    /// Repeat does. Public set because Ctrl+S and the button both assign it directly.
    /// </summary>
    public bool Shuffle
    {
        get => _queue.Shuffle;
        set
        {
            _queue.Shuffle = value;
            _settings.Shuffle = value;

            // As with Repeat: no binding today, raised so a future one cannot fail silently.
            Raise(nameof(Shuffle));

            Raise(nameof(ShuffleOpacity));
        }
    }

    /// <summary>
    /// The repeat button's glyph. Only repeat-ONE gets its own icon; Off and All share the loop glyph
    /// and are told apart by RepeatOpacity, because three distinct icons in one 32px button read as
    /// noise.
    /// </summary>
    public string RepeatGlyph => Repeat == RepeatMode.One ? RepeatOneGlyph : RepeatAllGlyph;

    /// <summary>Dims the repeat button when repeat is off. Opacity, so it animates without touching a shared brush.</summary>
    public double RepeatOpacity => Repeat == RepeatMode.Off ? 0.4 : 1.0;

    /// <summary>Dims the shuffle button when shuffle is off. The same on/off language as RepeatOpacity.</summary>
    public double ShuffleOpacity => Shuffle ? 1.0 : 0.4;

    /// <summary>
    /// Walks repeat Off to All to One and back to Off, from the button or Ctrl+R. One direction only:
    /// three states on one button need a predictable cycle, and a modifier to go backwards would be a
    /// shortcut nobody discovers.
    /// </summary>
    public void CycleRepeat()
    {
        Repeat = Repeat switch
        {
            RepeatMode.Off => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            _ => RepeatMode.Off,
        };
    }

    /// <summary>Flips shuffle, from the button or Ctrl+S. The queue pins the playing track, so nothing restarts.</summary>
    public void ToggleShuffle() => Shuffle = !Shuffle;

    /// <summary>
    /// The Library's Shuffle button. Turns the mode on and starts on a random track.
    ///
    /// Distinct from PlayFrom with shuffle already on: there the user picked a row and QueueService
    /// deliberately keeps that track first. Here nobody picked anything, so pinning index 0 made
    /// every shuffled run open with the same song.
    /// </summary>
    public void ShuffleAll(IReadOnlyList<Track> tracks)
    {
        if (tracks.Count == 0)
        {
            return;
        }

        Shuffle = true;
        PlayFrom(tracks, Random.Shared.Next(tracks.Count));
    }
}
