using System.Windows.Media;
using ClaudeTimer.Models;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Microsoft.Win32;

namespace ClaudeTimer.Themes;

/// <summary>
/// Skifter mellem lys og mørk palet ved at erstatte de navngivne pensler i
/// App.Resources. Al XAML refererer dem med DynamicResource, så skiftet slår
/// igennem med det samme – også når Windows skifter tema mens appen kører.
/// </summary>
public static class ThemeManager
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static readonly Dictionary<string, string> Light = new()
    {
        ["CanvasBrush"] = "#F5F5F7", ["SurfaceBrush"] = "#FCFCFD", ["TextBrush"] = "#19191B",
        ["MutedTextBrush"] = "#66656B", ["StrokeBrush"] = "#18000000", ["AccentBrush"] = "#6D52C7",
        ["AccentHoverBrush"] = "#5D43B8", ["AccentSoftBrush"] = "#EEEAFB", ["AccentForegroundBrush"] = "#FFFFFF",
        ["ErrorBrush"] = "#B42318", ["HoverFillBrush"] = "#0D000000", ["PressedFillBrush"] = "#18000000",
        ["SubtleFillBrush"] = "#0A000000", ["SubtleHoverFillBrush"] = "#12000000", ["TrackFillBrush"] = "#0C000000",
        ["InputBackgroundBrush"] = "#FFFFFF", ["InputBorderBrush"] = "#30000000", ["ScrollThumbBrush"] = "#33000000",
        ["ScrollThumbHoverBrush"] = "#55000000", ["ScrollThumbPressedBrush"] = "#77000000",
        ["OverlayBrush"] = "#66000000", ["GoodBrush"] = "#1A7F37", ["WarningBrush"] = "#B76E00",
        ["CriticalBrush"] = "#C4320A",
    };

    private static readonly Dictionary<string, string> Dark = new()
    {
        ["CanvasBrush"] = "#1C1C1F", ["SurfaceBrush"] = "#2A2A2E", ["TextBrush"] = "#F2F2F4",
        ["MutedTextBrush"] = "#A3A2AA", ["StrokeBrush"] = "#22FFFFFF", ["AccentBrush"] = "#A08CF6",
        ["AccentHoverBrush"] = "#B3A2F8", ["AccentSoftBrush"] = "#3A3259", ["AccentForegroundBrush"] = "#17151F",
        ["ErrorBrush"] = "#FF8A7E", ["HoverFillBrush"] = "#14FFFFFF", ["PressedFillBrush"] = "#24FFFFFF",
        ["SubtleFillBrush"] = "#10FFFFFF", ["SubtleHoverFillBrush"] = "#1CFFFFFF", ["TrackFillBrush"] = "#22FFFFFF",
        ["InputBackgroundBrush"] = "#1F1F23", ["InputBorderBrush"] = "#40FFFFFF", ["ScrollThumbBrush"] = "#40FFFFFF",
        ["ScrollThumbHoverBrush"] = "#66FFFFFF", ["ScrollThumbPressedBrush"] = "#88FFFFFF",
        ["OverlayBrush"] = "#99000000", ["GoodBrush"] = "#4CC38A", ["WarningBrush"] = "#F0B429",
        ["CriticalBrush"] = "#FF7A66",
    };

    public static bool IsDark { get; private set; }

    public static void Apply(AppTheme theme)
    {
        IsDark = theme == AppTheme.Dark || (theme == AppTheme.System && !ReadBool("AppsUseLightTheme", true));
        var resources = System.Windows.Application.Current.Resources;
        foreach (var (key, value) in IsDark ? Dark : Light)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
            brush.Freeze();
            resources[key] = brush;
        }
    }

    /// <summary>Om proceslinjen er lys – afgør tekstfarven i bakke-ikonet.</summary>
    public static bool IsTaskbarLight => ReadBool("SystemUsesLightTheme", false);

    private static bool ReadBool(string name, bool fallback)
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue(name) is int value ? value != 0 : fallback;
    }
}
