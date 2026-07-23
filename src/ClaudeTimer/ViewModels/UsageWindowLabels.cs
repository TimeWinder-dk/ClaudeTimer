using System.Globalization;
using ClaudeTimer.Models;

namespace ClaudeTimer.ViewModels;

/// <summary>Oversætter et <see cref="UsageWindow"/> til danske overskrifter.</summary>
internal static class UsageWindowLabels
{
    private static readonly CultureInfo DanishCulture = CultureInfo.GetCultureInfo("da-DK");

    public static string Eyebrow(UsageWindow window) => window.Group switch
    {
        "session" => "AKTUEL SESSION",
        "weekly" => window.ScopeModelName is { Length: > 0 } model
            ? $"UGENTLIG · {model.ToUpper(DanishCulture)}"
            : "UGENTLIG GRÆNSE",
        _ => Humanize(window.Kind).ToUpper(DanishCulture)
    };

    public static string Title(UsageWindow window) => window.Kind switch
    {
        "session" => "5 timer",
        "weekly_all" => "7 døgn",
        "weekly_scoped" => window.ScopeModelName is { Length: > 0 } model
            ? $"{model} · 7 døgn"
            : "7 døgn (afgrænset)",
        _ => Humanize(window.Kind)
    };

    private static string Humanize(string kind) =>
        string.IsNullOrWhiteSpace(kind)
            ? "Grænse"
            : char.ToUpper(kind[0], DanishCulture) + kind[1..].Replace('_', ' ');
}
