using CommunityToolkit.Mvvm.ComponentModel;
using System.Globalization;

namespace ClaudeTimer.ViewModels;

public sealed partial class UsageCardViewModel : ObservableObject
{
    private static readonly CultureInfo DanishCulture = CultureInfo.GetCultureInfo("da-DK");

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private string _eyebrow;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Level))]
    private double _utilization;

    [ObservableProperty]
    private string _percentage = "—";

    [ObservableProperty]
    private string _countdown = "—";

    [ObservableProperty]
    private string _localResetTime = "Afventer data";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPace))]
    private string _paceText = string.Empty;

    [ObservableProperty]
    private bool _isPaceWarning;

    public DateTimeOffset? ResetsAt { get; private set; }

    /// <summary>Stabil identitet der bruges til at genfinde kortet ved opdatering.</summary>
    public string Key { get; }

    /// <summary>Kort titel til tooltip'en, fx "5t" eller "Uge".</summary>
    public string ShortTitle { get; set; }

    /// <summary>Vinduets samlede længde (5 timer, 7 døgn), hvis den kendes – bruges til tempo.</summary>
    public TimeSpan? WindowLength { get; set; }

    /// <summary>Kompakt nedtælling til tooltip'en, fx "1t12m" eller "3d4t".</summary>
    public string CompactCountdown { get; private set; } = "—";

    public UsageLevel Level => UsageLevels.For(Utilization);

    public bool HasPace => PaceText.Length > 0;

    public UsageCardViewModel(string title, string eyebrow, string key = "", string? shortTitle = null)
    {
        _title = title;
        _eyebrow = eyebrow;
        Key = key;
        ShortTitle = shortTitle ?? title;
    }

    public void Update(double utilization, DateTimeOffset? resetsAt)
    {
        Utilization = Math.Clamp(utilization, 0, 100);
        Percentage = $"{utilization.ToString("0.#", DanishCulture)} %";
        ResetsAt = resetsAt;
        LocalResetTime = resetsAt?.ToLocalTime().ToString(
            "dddd d. MMMM · HH:mm:ss",
            DanishCulture)
            ?? "Intet nulstillingstidspunkt";
    }

    public void Tick(DateTimeOffset now)
    {
        UpdatePace(now);

        if (ResetsAt is null)
        {
            Countdown = "—";
            CompactCountdown = "—";
            return;
        }

        var remaining = ResetsAt.Value - now;
        if (remaining <= TimeSpan.Zero)
        {
            Countdown = "00:00:00";
            CompactCountdown = "0m";
            return;
        }

        Countdown = remaining.TotalDays >= 1
            ? $"{(int)remaining.TotalDays}d {remaining.Hours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}"
            : $"{(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}";
        CompactCountdown = FormatCompact(remaining);
    }

    private void UpdatePace(DateTimeOffset now)
    {
        var (text, warning) = Pace(Utilization, ResetsAt, WindowLength, now);
        PaceText = text;
        IsPaceWarning = warning;
    }

    /// <summary>
    /// Fremskriver forbruget lineært ud fra den tid, der er gået af vinduet: holder
    /// tempoet til nulstilling, eller hvornår rammes 100 %?
    /// </summary>
    internal static (string Text, bool Warning) Pace(
        double utilization,
        DateTimeOffset? resetsAt,
        TimeSpan? windowLength,
        DateTimeOffset now)
    {
        if (utilization >= 100)
        {
            return ("Grænsen er nået", true);
        }

        if (resetsAt is not { } reset || windowLength is not { } length || reset <= now)
        {
            return (string.Empty, false);
        }

        var elapsed = length - (reset - now);

        // For tidligt i vinduet giver en prognose kun støj.
        var minimumElapsed = TimeSpan.FromTicks(Math.Max(length.Ticks / 20, TimeSpan.FromMinutes(10).Ticks));
        if (elapsed < minimumElapsed || utilization < 1)
        {
            return (string.Empty, false);
        }

        var projected = utilization * (length / elapsed);
        if (projected < 100)
        {
            return ($"Prognose ved nulstilling: {Math.Round(projected):0} %", false);
        }

        var hitsLimitAt = now + (elapsed * ((100 - utilization) / utilization));
        var local = hitsLimitAt.ToLocalTime();
        var when = local.Date == now.ToLocalTime().Date
            ? $"kl. {local:HH:mm}"
            : local.ToString("ddd 'kl.' HH:mm", DanishCulture);
        return ($"Med dette tempo: 100 % {when}", true);
    }

    internal static string FormatCompact(TimeSpan remaining) =>
        remaining.TotalDays >= 1 ? $"{(int)remaining.TotalDays}d{remaining.Hours}t"
        : remaining.TotalHours >= 1 ? $"{(int)remaining.TotalHours}t{remaining.Minutes:00}m"
        : $"{Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes))}m";
}
