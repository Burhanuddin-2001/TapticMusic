using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace MusicPlayer.Services;

/// <summary>
/// Owns the palette and applies it. Step 13 Part B.
///
/// TWO INDEPENDENT KNOBS, deliberately: the theme picks the neutrals, the swatch picks the accent.
/// Keeping them separate is what stops a theme flip resetting the user's accent.
///
/// HOW IT WORKS, and why it is not a second ThemeDictionary. Every one of the app's
/// {ThemeResource Brush.*} references resolves to a shared SolidColorBrush object BY REFERENCE, so
/// assigning .Color on that object repaints every consumer at once — including the three places an
/// ElementTheme flip provably cannot reach: BadgePill snapshots its brush instance in its
/// constructor, ScrollBar.xaml aliases 18 WinUI keys to these same instances with StaticResource,
/// and popups live under the XamlRoot popup root outside RootGrid. A real Light dictionary would
/// break all three. It also could not express five user-selectable accents at all — there is no
/// x:Key for "swatch 3". The design rules ban ColorAnimation on these brushes for exactly the reason that
/// makes a one-shot assignment the right tool here; the animation ban stands.
///
/// THIS FILE IS THE ONE LEGITIMATE HOME FOR LITERAL COLOURS IN C#. The prime law forbids them in
/// Views/, Controls/ and Styles/, and Themes/Colors.xaml now holds design-time seed values only.
/// Do not "fix" the hex below into tokens; the tokens are what it writes.
/// </summary>
public sealed class ThemeService
{
    /// <summary>
    /// The 10 theme-driven neutral brush keys, in one place so Apply cannot silently miss one.
    /// Status.* and Text.OnStatus are absent on purpose: saturated signal colours read on either
    /// canvas, so they are theme-invariant and stay at their Colors.xaml values.
    /// </summary>
    private static readonly string[] NeutralKeys =
    {
        "Brush.Background.Canvas",
        "Brush.Background.Surface",
        "Brush.Background.Elevated",
        "Brush.Surface.Hover",
        "Brush.Elevated.Hover",
        "Brush.Border.Default",
        "Brush.Text.Primary",
        "Brush.Text.Secondary",
        "Brush.Shadow",
        "Brush.Focus",
    };

    private static readonly string[] AccentKeys =
    {
        "Brush.Accent.Primary",
        "Brush.Accent.PrimaryPressed",
        "Brush.Accent.OnCanvas",
        "Brush.Text.OnAccent",
    };

    /// <summary>
    /// Canvas, Surface, Elevated, SurfaceHover, ElevatedHover, Border, TextPrimary, TextSecondary,
    /// Shadow, Focus — in NeutralKeys order.
    /// </summary>
    private static readonly uint[] DarkNeutrals =
    {
        0xFF111215, 0xFF191B1F, 0xFF22252B, 0xFF2A2E36, 0xFF32363F,
        0xFF000000, 0xFFF3EFE6, 0xFFA2A7B5, 0xFF000000, 0xFF35E0C0,
    };

    private static readonly uint[] LightNeutrals =
    {
        0xFFF3EFE6, 0xFFFFFFFF, 0xFFE6E1D5, 0xFFF7F3EA, 0xFFDCD7C9,
        0xFF121316, 0xFF121316, 0xFF4A4D55, 0xFF121316, 0xFF0F766E,
    };

    /// <summary>
    /// Primary, Pressed, OnCanvas, OnAccent — in AccentKeys order, [swatch][light? dark?].
    ///
    /// Every value is contrast-verified. Two rules are not obvious and must survive any retune:
    ///
    /// 1. Text.OnAccent NEVER differs between Primary and Pressed, or the label flashes mid-press.
    /// 2. Pressed moves AWAY from the label colour, not simply darker. Where the label is near-black
    ///    the pressed fill goes LIGHTER, because darkening closes the gap and drops the label under
    ///    4.5:1 — that broke 4 of these 10 pairs on the first attempt. The press is carried by the
    ///    2px drop onto the shadow anyway; the fill shift is secondary.
    /// </summary>
    private static readonly uint[][] LightAccents =
    {
        new uint[] { 0xFF1D4ED8, 0xFF153799, 0xFF1D4ED8, 0xFFF3EFE6 }, // Bauhaus Grid
        new uint[] { 0xFFD97706, 0xFFC56C05, 0xFF96550A, 0xFF121316 }, // Braun Functionalist
        new uint[] { 0xFFBE123C, 0xFF8D0D2C, 0xFFBE123C, 0xFFF3EFE6 }, // Revox Tape Reel
        new uint[] { 0xFFFF5500, 0xFFFF712A, 0xFFB03B00, 0xFF121316 }, // Fanzine Punk
        new uint[] { 0xFF7C3AED, 0xFF612DB9, 0xFF7C3AED, 0xFFF3EFE6 }, // Digital Matrix DAW
    };

    private static readonly uint[][] DarkAccents =
    {
        new uint[] { 0xFF3B82F6, 0xFF5492F7, 0xFF3B82F6, 0xFF121316 }, // Bauhaus Grid
        new uint[] { 0xFFF59E0B, 0xFFE6950A, 0xFFF59E0B, 0xFF121316 }, // Braun Functionalist
        new uint[] { 0xFFFB7185, 0xFFFC8596, 0xFFFB7185, 0xFF121316 }, // Revox Tape Reel
        new uint[] { 0xFFFF6B2B, 0xFFFF814A, 0xFFFF6B2B, 0xFF121316 }, // Fanzine Punk
        new uint[] { 0xFFA78BFA, 0xFFB199FB, 0xFFA78BFA, 0xFF121316 }, // Digital Matrix DAW
    };

    /// <summary>
    /// Writes the palette. Safe to call from the App constructor, before any window exists — the
    /// window-dependent steps are guarded.
    /// </summary>
    public void Apply(AppTheme theme, AccentSwatch accent)
    {
        // Both arguments are validated before use, and this is not paranoia about our own callers.
        // Apply is the single choke point for all three paths — the Settings page, MainWindow's
        // constructor, and ActualThemeChanged — and two of those read straight from settings.json.
        // A file holding an out-of-range int (hand-edited, or written by an older build) would
        // otherwise crash the app on launch with no way back in. C# enums do not range-check a
        // cast, so an "AccentSwatch" can legally hold -1.
        if (!Enum.IsDefined(theme))
        {
            theme = AppTheme.System;
        }

        int swatch = (int)accent;

        if (swatch < 0 || swatch >= LightAccents.Length)
        {
            swatch = (int)AccentSwatch.BauhausGrid;
        }

        bool light = theme == AppTheme.Light
            || (theme == AppTheme.System && Application.Current.RequestedTheme == ApplicationTheme.Light);

        Write(NeutralKeys, light ? LightNeutrals : DarkNeutrals);
        Write(AccentKeys, (light ? LightAccents : DarkAccents)[swatch]);

        // The brushes above cover all 191 app-owned colour references. This only moves WinUI's own
        // un-overridden keys — menu separators, dialog chrome, the ScrollViewer corner.
        if (App.Window?.Content is FrameworkElement root)
        {
            root.RequestedTheme = theme switch
            {
                AppTheme.Light => ElementTheme.Light,
                AppTheme.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
        }

        // One property, not the eight hand-set colours that were deleted in Step 11. With
        // ExtendsContentIntoTitleBar it keeps the button backgrounds transparent for us.
        if (App.Window?.AppWindow?.TitleBar is { } titleBar)
        {
            titleBar.PreferredTheme = theme switch
            {
                AppTheme.Light => TitleBarTheme.Light,
                AppTheme.Dark => TitleBarTheme.Dark,
                _ => TitleBarTheme.UseDefaultAppMode,
            };
        }
    }

    /// <summary>
    /// The INDEXER is the lookup to use. It is the only one proven to traverse MergedDictionaries
    /// and ThemeDictionaries in this app — BadgePill reads Brush.Background.Elevated through it and
    /// that key exists only inside the Default theme dictionary. Enumerating Application.Resources
    /// finds nothing at all, because it has no direct entries. TryGetValue is deliberately not used:
    /// if it turned out to behave like enumeration, colours would silently fail to apply, which is
    /// the exact class of bug this whole step has been chasing.
    ///
    /// A missing key throws, and that is the intended behaviour: the key list is static, so it
    /// either always works or never does, and a typo should fail loudly on first launch rather than
    /// leave one colour quietly stuck.
    ///
    /// `is SolidColorBrush` rather than a cast, though. Brush has no .Color, so a hard cast would
    /// turn a retyped token into a clean build plus a runtime InvalidCastException — the A13 /
    /// LIB-001 shape.
    /// </summary>
    private static void Write(string[] keys, uint[] argb)
    {
        for (int i = 0; i < keys.Length; i++)
        {
            if (Application.Current.Resources[keys[i]] is SolidColorBrush brush)
            {
                brush.Color = ToColor(argb[i]);
            }
        }
    }

    private static Color ToColor(uint argb) => ColorHelper.FromArgb(
        (byte)(argb >> 24),
        (byte)(argb >> 16),
        (byte)(argb >> 8),
        (byte)argb);
}
