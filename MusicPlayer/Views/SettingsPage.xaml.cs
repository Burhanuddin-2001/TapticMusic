using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using MusicPlayer.ViewModels;
using Microsoft.Windows.Storage.Pickers;

namespace MusicPlayer.Views;

/// <summary>
/// Preferences, and the watched folders that feed the library.
/// </summary>
public sealed partial class SettingsPage : Page
{
    /// <summary>
    /// Builds the page. Nothing else happens here: the view model is built by its own initialiser
    /// and the data is loaded in OnNavigatedTo, because a constructor cannot await.
    /// </summary>
    public SettingsPage()
    {
        InitializeComponent();
    }

    /// <summary>
    /// This visit's view model, reached from XAML by x:Bind. New per visit, which is exactly why
    /// SettingsViewModel reaches App.Theme statically instead of taking it here: a page rebuilt on
    /// every visit cannot own live theme state.
    /// </summary>
    public SettingsViewModel ViewModel { get; } = new(App.Library, App.Scanner, App.Settings);

    /// <summary>
    /// Reloads on EVERY visit rather than caching, because NavigationCacheMode is deliberately unset
    /// and the view model is new for each navigation (A4). That is what makes a rescan or an edit on
    /// another page show up here without any cross-page notification.
    /// </summary>
    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.LoadAsync();
    }

    /// <summary>
    /// The Windows App SDK picker, which takes a WindowId directly. The older WinRT picker needs
    /// InitializeWithWindow interop in a desktop app and throws without it.
    /// </summary>
    private async void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        FolderPicker picker = new(App.Window.AppWindow.Id)
        {
            SuggestedStartLocation = PickerLocationId.MusicLibrary,
        };

        PickFolderResult? folder = await picker.PickSingleFolderAsync();

        if (folder is not null)
        {
            await ViewModel.AddFolderAsync(folder.Path);
        }
    }

    /// <summary>
    /// Rescans every watched folder. Safe to press repeatedly: an unchanged file is skipped without
    /// being opened, so a second scan over a large library is fast.
    /// </summary>
    private async void Rescan_Click(object sender, RoutedEventArgs e) => await ViewModel.RescanAsync();
}
