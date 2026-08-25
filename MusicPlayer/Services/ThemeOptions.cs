namespace MusicPlayer.Services;

// The two user-facing theme choices. Lifted out of ThemeService.cs in Step 15: SettingsService is
// pure JSON persistence with no UI in it, and it was taking a dependency on a file that names
// App.Window and Microsoft.UI.Xaml purely to reach these two enums. Same namespace, so nothing
// changed at any call site.

/// <summary>Step 13. System follows Windows. Persisted as an int, so the values are pinned.</summary>
public enum AppTheme
{
    /// <summary>Follow Windows. Possible only because App.xaml never sets RequestedTheme.</summary>
    System = 0,

    /// <summary>The paper canvas. Written from ThemeService.LightNeutrals.</summary>
    Light = 1,

    /// <summary>The dark canvas, and what Themes/Colors.xaml holds as design-time values.</summary>
    Dark = 2,
}

/// <summary>
/// Step 13. The five shipped accents. Persisted as an int, so the values are pinned — reordering
/// these members would silently change every existing user's accent.
/// </summary>
public enum AccentSwatch
{
    /// <summary>Ultramarine. The default, and what a fresh settings.json gets.</summary>
    BauhausGrid = 0,

    /// <summary>Amber. Needs Accent.OnCanvas on the light canvas - the fill alone scores 2.78:1.</summary>
    BraunFunctionalist = 1,

    /// <summary>Vermillion.</summary>
    RevoxTapeReel = 2,

    /// <summary>Tangerine. Same light-canvas caveat as Braun, at 2.79:1.</summary>
    FanzinePunk = 3,

    /// <summary>Violet.</summary>
    DigitalMatrix = 4,
}
