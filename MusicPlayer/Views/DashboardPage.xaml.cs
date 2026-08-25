using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using MusicPlayer.ViewModels;

namespace MusicPlayer.Views;

/// <summary>
/// The landing page. Replaced the old root-level landing page when navigation arrived in Step 06,
/// and became The Deal in Step 14.
/// </summary>
public sealed partial class DashboardPage : Page, IArrowHost
{
    /// <summary>
    /// Builds the page. Nothing else happens here: the view model is built by its own initialiser
    /// and the data is loaded in OnNavigatedTo, because a constructor cannot await.
    /// </summary>
    public DashboardPage()
    {
        InitializeComponent();
    }

    /// <summary>
    /// This visit's view model, reached from XAML by x:Bind. New per visit - which is why the dealt
    /// hand lives in a private STATIC on DashboardViewModel: an instance field would redeal on every
    /// navigation and make the Reroll button meaningless.
    /// </summary>
    public DashboardViewModel ViewModel { get; } = new(App.Library, App.Playlists);

    /// <summary>
    /// Step 16. The list the shell's bare Up and Down keys walk. The three cards sit in ONE
    /// ItemsWrapGrid row, so those keys enter the row and then have nowhere left to go. Left and
    /// Right are the grid's own axis but they stay with the transport as seek, by decision.
    /// </summary>
    public Control? ArrowTarget => DealList;

    /// <summary>
    /// Refreshes the stat strip on every visit. It does NOT redeal the hand - LoadAsync reuses the
    /// static one if it exists, so navigating away and back shows the same three cards.
    /// </summary>
    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.LoadAsync();
    }

    /// <summary>
    /// The SQL is awaited first and ShuffleAll is called after, because it reaches straight into the
    /// audio service and the queue, both of which are UI-thread only. ShuffleAll no-ops on an empty
    /// list, so there is nothing to guard.
    /// </summary>
    private async void PlaySomething_Click(object sender, RoutedEventArgs e)
        => App.PlayerBar.ShuffleAll(await ViewModel.AllTracksAsync());

    /// <summary>
    /// Deals a fresh hand of three cards. It only touches the deal, so it never interrupts whatever
    /// is currently playing.
    /// </summary>
    private async void Reroll_Click(object sender, RoutedEventArgs e)
        => await ViewModel.RerollAsync();

    /// <summary>
    /// A dealt card becomes the queue, so previous and next walk that album, artist or playlist -
    /// the same rule as clicking a row in the Library.
    /// </summary>
    private async void Deal_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is DealCard card)
        {
            App.PlayerBar.PlayFrom(await ViewModel.TracksForAsync(card), 0);
        }
    }
}
