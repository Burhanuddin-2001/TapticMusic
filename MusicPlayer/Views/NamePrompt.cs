using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using MusicPlayer.Services;

namespace MusicPlayer.Views;

/// <summary>
/// "Give it a name." Step 11.
///
/// Extracted rather than duplicated because it has two real callers: PlaylistsPage uses it to
/// create and to rename, and LibraryPage uses it for "New playlist..." inside the Add to playlist
/// menu. The first caller would not have earned this file; the second one does.
/// </summary>
internal static class NamePrompt
{
    /// <summary>
    /// Puts up the name dialog and waits for the user. Returns the trimmed name, or null if they
    /// cancelled or left the box blank - so a caller only ever has to check for null. The primary
    /// button disables itself while the box is empty, which is why a blank name cannot normally get
    /// this far. The XamlRoot has to be passed in: a ContentDialog throws in WinUI 3 without one, and
    /// it has to come from a Page, because App.Window is a Window and has no XamlRoot at all.
    /// </summary>
    public static async Task<string?> ShowAsync(XamlRoot root, string title, string primaryText, string initial)
    {
        TextBox input = new()
        {
            Style = (Style)Application.Current.Resources["PillTextBoxStyle"],
            PlaceholderText = "Name it something you will recognise",
            Text = initial,
        };

        // An attached property cannot go in an object initializer - that is CS0103 plus CS0747, and
        // it takes the XAML compiler down with it. Step 11.
        AutomationProperties.SetName(input, title);

        // FontSize.Body (13) is right for a row of metadata and too small for the one field the
        // user is actively typing into. Step 13. BaseSize and not FontSize since v1.1: the style's own
        // BaseSize would overwrite a plain FontSize the moment the box entered the tree.
        TextScale.SetBaseSize(input, (double)Application.Current.Resources["FontSize.Input"]);

        ContentDialog dialog = new()
        {
            XamlRoot = root,
            Title = title,
            Content = input,
            PrimaryButtonText = primaryText,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = initial.Trim().Length > 0,

            // WinUI's ContentDialog template hands its default button WinUI's own AccentButtonStyle,
            // which is driven by SystemAccentColor — so the primary button wore the Windows accent
            // instead of this app's. The template binds PrimaryButtonStyle, so setting it here is
            // the only place that wins; an app-level AccentButtonStyle override does not, because
            // generic.xaml resolves that key inside its own scope. Step 13.
            PrimaryButtonStyle = (Style)Application.Current.Resources["PillAccentButtonStyle"],
        };

        input.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = input.Text.Trim().Length > 0;

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        string name = input.Text.Trim();

        return name.Length > 0 ? name : null;
    }
}
