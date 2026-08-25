using MusicPlayer.Models;

namespace MusicPlayer.Services;

/// <summary>
/// Owns the play order, the cursor, and the repeat/shuffle modes. A plain service, not a view
/// model — PlayerBarViewModel is the only thing with INotifyPropertyChanged. Invariants A3, A4.
/// </summary>
public sealed class QueueService
{
    private List<Track> _source = [];

    /// <summary>Indices into _source, in play order. This is what Items projects.</summary>
    private List<int> _order = [];

    /// <summary>Position within _order. -1 when the queue is empty.</summary>
    private int _cursor = -1;

    private bool _shuffle;

    /// <summary>
    /// The queue in PLAY order, which is what the queue rail draws. Projected fresh on every read
    /// rather than cached, so it can never disagree with _order; that is affordable because the rail
    /// only re-reads it when Changed fires, not on the 250ms position poll.
    /// </summary>
    public IReadOnlyList<Track> Items => _order.Select(i => _source[i]).ToList();

    /// <summary>
    /// The track the cursor is on, or null when the queue is empty. The double bounds check is
    /// deliberate belt-and-braces: every mutator here keeps the cursor valid, and returning null is
    /// still better than throwing into a UI binding if one ever does not.
    /// </summary>
    public Track? Current => _cursor >= 0 && _cursor < _order.Count ? _source[_order[_cursor]] : null;

    /// <summary>
    /// Where the cursor sits within Items, or -1 when the queue is empty. The queue rail uses it to
    /// highlight the playing row, which is why it is an index into Items and not into _source.
    /// </summary>
    public int CurrentIndex => _cursor;

    /// <summary>
    /// What happens at the end of a track. A plain property with no side effects: MoveNext reads it
    /// when the moment comes, so changing it mid-track never interrupts anything. Persisted by
    /// SettingsService, which is why RepeatMode's member order is pinned.
    /// </summary>
    public RepeatMode Repeat { get; set; }

    /// <summary>
    /// Shuffle as a MODE, not a one-shot. Turning it on reshuffles immediately but pins the playing
    /// track first, so toggling mid-song does not restart it; turning it off restores the original
    /// order and keeps the same track current. Persisted by SettingsService.
    ///
    /// Two things the setter relies on that are not obvious. Reading _order[_cursor] is safe because
    /// _cursor is only ever -1 when _order is empty, which the Count guard already excludes. And
    /// assigning _cursor a SOURCE index when un-shuffling is correct only because the un-shuffled
    /// _order is the identity permutation, so position and source index are the same number.
    /// </summary>
    public bool Shuffle
    {
        get => _shuffle;
        set
        {
            if (_shuffle == value)
            {
                return;
            }

            _shuffle = value;

            if (_order.Count > 0)
            {
                int currentSourceIndex = _order[_cursor];

                if (value)
                {
                    ShuffleKeeping(currentSourceIndex);
                }
                else
                {
                    _order = Enumerable.Range(0, _source.Count).ToList();
                    _cursor = currentSourceIndex;
                }
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Fires whenever the contents, the order or the cursor change. PlayerBarViewModel re-reads
    /// everything from it, which is why none of the mutators here bother reporting what changed.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Replaces the whole queue and points the cursor at one track. Every "play this list" path ends
    /// up here - a Library double-click, Play all, Shuffle all, a playlist, a dashboard deal card.
    /// startIndex is clamped rather than validated, so a caller cannot crash the queue with a stale
    /// index. If shuffle is already on, the new list is shuffled at once with the chosen track pinned
    /// first, which is what stops Shuffle all from opening on a different track than it started.
    /// </summary>
    public void Load(IReadOnlyList<Track> tracks, int startIndex)
    {
        _source = new List<Track>(tracks);
        _order = Enumerable.Range(0, _source.Count).ToList();

        if (_source.Count == 0)
        {
            _cursor = -1;
        }
        else
        {
            int clamped = Math.Clamp(startIndex, 0, _source.Count - 1);

            if (_shuffle)
            {
                ShuffleKeeping(clamped);
            }
            else
            {
                _cursor = clamped;
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Advances the cursor and hands back the track to play, or null when there is nothing to do.
    /// Null means three different things on purpose - empty queue, repeat-one, or the end with repeat
    /// off - because the caller's response to all three is the same: do not change track here.
    /// Repeat-one is checked BEFORE the advance, or it would only take effect once the cursor
    /// happened to reach the end of the queue.
    /// </summary>
    public Track? MoveNext()
    {
        if (_order.Count == 0)
        {
            return null;
        }

        if (Repeat == RepeatMode.One)
        {
            // Handled by the caller, which restarts the current track without re-entering
            // AudioService.Play (CONSTRAINT M). Checked before the normal advance below, or One
            // would only take effect once the cursor happened to reach the end of the queue.
            return null;
        }

        if (_cursor + 1 < _order.Count)
        {
            _cursor++;
        }
        else if (Repeat == RepeatMode.All)
        {
            _cursor = 0;
        }
        else
        {
            // Off stops at the end.
            return null;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return Current;
    }

    /// <summary>
    /// Steps the cursor back and hands back the track to play, wrapping to the end only when repeat
    /// is All. Returns null at the start of the queue with repeat off, which the transport treats as
    /// "stay where you are". Note it does NOT honour repeat-one: pressing Previous is a deliberate
    /// request to move, so re-serving the same track would look broken.
    /// </summary>
    public Track? MovePrevious()
    {
        if (_order.Count == 0)
        {
            return null;
        }

        if (_cursor > 0)
        {
            _cursor--;
        }
        else if (Repeat == RepeatMode.All)
        {
            _cursor = _order.Count - 1;
        }
        else
        {
            return null;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return Current;
    }

    /// <summary>
    /// Moves the cursor straight to a row the user clicked in the queue rail. The index is into
    /// Items, in play order, not into the unshuffled source. An out-of-range index returns null
    /// rather than throwing, because the rail can be a frame behind the queue it draws.
    /// </summary>
    public Track? JumpTo(int indexInItems)
    {
        if (indexInItems < 0 || indexInItems >= _order.Count)
        {
            return null;
        }

        _cursor = indexInItems;
        Changed?.Invoke(this, EventArgs.Empty);
        return Current;
    }

    /// <summary>Fisher-Yates, then the playing track is moved to the front so it keeps playing.</summary>
    private void ShuffleKeeping(int sourceIndexToKeepFirst)
    {
        for (int i = _order.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (_order[i], _order[j]) = (_order[j], _order[i]);
        }

        int pos = _order.IndexOf(sourceIndexToKeepFirst);
        (_order[0], _order[pos]) = (_order[pos], _order[0]);
        _cursor = 0;
    }
}
