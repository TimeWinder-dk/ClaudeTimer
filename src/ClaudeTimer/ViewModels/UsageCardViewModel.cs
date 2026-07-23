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
    private double _utilization;

    [ObservableProperty]
    private string _percentage = "—";

    [ObservableProperty]
    private string _countdown = "—";

    [ObservableProperty]
    private string _localResetTime = "Afventer data";

    public DateTimeOffset? ResetsAt { get; private set; }

    /// <summary>Stabil identitet der bruges til at genfinde kortet ved opdatering.</summary>
    public string Key { get; }

    public UsageCardViewModel(string title, string eyebrow, string key = "")
    {
        _title = title;
        _eyebrow = eyebrow;
        Key = key;
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
        if (ResetsAt is null)
        {
            Countdown = "—";
            return;
        }

        var remaining = ResetsAt.Value - now;
        if (remaining <= TimeSpan.Zero)
        {
            Countdown = "00:00:00";
            return;
        }

        Countdown = remaining.TotalDays >= 1
            ? $"{(int)remaining.TotalDays}d {remaining.Hours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}"
            : $"{(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}";
    }
}
