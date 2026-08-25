using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using MusicPlayer.ViewModels;
using Windows.System;

namespace MusicPlayer.Controls;

/// <summary>
/// The bottom transport dock. design-plan section 4 and section 5.
///
/// This control renders state; it does not own it. Everything it shows comes from
/// PlayerBarViewModel, which reads through to IAudioService. The six dependency properties this
/// file used to hold were scaffolding for exactly that view model, and went when it arrived.
/// </summary>
public sealed partial class PlayerBarView : UserControl
{
    /// <summary>
    /// Builds the transport bar and brackets the seek slider's own input handling. Those five
    /// AddHandler calls are not optional: a Slider's template marks pointer and key input handled on
    /// the way past, so a plain XAML event attribute never sees a drag start at all. Both pointer and
    /// keyboard get a begin and an end, and PointerCaptureLost shares the release handler so a drag
    /// that leaves the window still ends the scrub instead of leaving the poll suppressed forever.
    /// </summary>
    public PlayerBarView()
    {
        InitializeComponent();

        // A Slider's template handles pointer and key input on the way past, so a plain XAML
        // event attribute never sees it. handledEventsToo is the only way to know a drag started.
        SeekSlider.AddHandler(PointerPressedEvent, new PointerEventHandler(Seek_PointerPressed), true);
        SeekSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler(Seek_PointerReleased), true);
        SeekSlider.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(Seek_PointerReleased), true);
        SeekSlider.AddHandler(KeyDownEvent, new KeyEventHandler(Seek_KeyDown), true);
        SeekSlider.AddHandler(KeyUpEvent, new KeyEventHandler(Seek_KeyUp), true);
    }

    /// <summary>
    /// The bar outlives every page, so its view model is the one the app owns. Set before this
    /// control is ever constructed, in App.OnLaunched.
    /// </summary>
    public PlayerBarViewModel ViewModel => App.PlayerBar;

    private void Previous_Click(object sender, RoutedEventArgs e) => ViewModel.Previous();

    private void PlayPause_Click(object sender, RoutedEventArgs e) => ViewModel.TogglePlayPause();

    private void Next_Click(object sender, RoutedEventArgs e) => ViewModel.Next();

    private void Mute_Click(object sender, RoutedEventArgs e) => ViewModel.ToggleMute();

    private void Repeat_Click(object sender, RoutedEventArgs e) => ViewModel.CycleRepeat();

    private void Shuffle_Click(object sender, RoutedEventArgs e) => ViewModel.ToggleShuffle();

    private void Volume_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        => ViewModel.SetVolume(e.NewValue);

    private void Seek_PointerPressed(object sender, PointerRoutedEventArgs e) => ViewModel.BeginScrub();

    private void Seek_PointerReleased(object sender, PointerRoutedEventArgs e)
        => ViewModel.EndScrub(SeekSlider.Value);

    /// <summary>
    /// Step 16. Only the keys that actually move a Slider may open a scrub. Every key used to,
    /// which was harmless while the shell handed all input to a focused Slider - but Space is now a
    /// global shortcut, so a press on the focused seek bar fired a BeginScrub/EndScrub pair and
    /// seeked to the position it was already at.
    /// </summary>
    private static bool MovesSlider(VirtualKey key)
        => key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down
               or VirtualKey.Home or VirtualKey.End or VirtualKey.PageUp or VirtualKey.PageDown;

    private void Seek_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (MovesSlider(e.Key))
        {
            ViewModel.BeginScrub();
        }
    }

    private void Seek_KeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (MovesSlider(e.Key))
        {
            ViewModel.EndScrub(SeekSlider.Value);
        }
    }
}
