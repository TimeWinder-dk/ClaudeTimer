using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using ClaudeTimer.Models;
using ClaudeTimer.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClaudeTimer.ViewModels;

/// <summary>Et token fra en bestemt kilde.</summary>
public sealed record AccountCredential(TokenSource Source, string Token);

/// <summary>Hvornår der skal vises notifikationer for en konto.</summary>
public sealed record NotificationOptions(bool Enabled, IReadOnlyList<int> Thresholds, bool OnReset)
{
    public static NotificationOptions None { get; } = new(false, [], false);
}

/// <summary>Én Claude-konto (konto + organisation) med sine forbrugskort – én fane.</summary>
public sealed partial class AccountViewModel : ObservableObject
{
    private DateTimeOffset? _lastBoundaryRefresh;

    [ObservableProperty]
    private string _displayName;

    [ObservableProperty]
    private string _shortName;

    [ObservableProperty]
    private string _subtitle = string.Empty;

    [ObservableProperty]
    private string _statusText = "Henter forbrug…";

    [ObservableProperty]
    private string _lastUpdatedText = "Ikke opdateret endnu";

    [ObservableProperty]
    private string? _errorText;

    [ObservableProperty]
    private bool _hasData;

    /// <summary>Brugerens eget navn for kontoen; tomt betyder e-mailen.</summary>
    [ObservableProperty]
    private string _alias = string.Empty;

    public NotificationOptions Notifications { get; set; } = NotificationOptions.None;

    /// <summary>Udløses med en kort besked, når en grænse krydses eller nulstilles.</summary>
    public event EventHandler<string>? NotificationRaised;

    public AccountViewModel(string key, string displayName)
    {
        Key = key;
        _displayName = displayName;
        _shortName = displayName;
    }

    public string Key { get; }

    public ClaudeAccount? Profile { get; set; }

    /// <summary>Tokens for kontoen i prioriteret rækkefølge (manuelt, Claude Code, Desktop).</summary>
    public IReadOnlyList<AccountCredential> Credentials { get; set; } = [];

    public ObservableCollection<UsageCardViewModel> Cards { get; } = new();

    public bool IsBusy { get; private set; }

    public async Task RefreshAsync(IClaudeUsageClient client, IClock clock)
    {
        if (IsBusy || Credentials.Count == 0)
        {
            return;
        }

        IsBusy = true;
        ErrorText = null;
        StatusText = HasData ? "Opdaterer…" : "Henter forbrug…";

        try
        {
            ClaudeUsageException? lastError = null;
            foreach (var credential in Credentials)
            {
                try
                {
                    var usage = await client.GetUsageAsync(credential.Token, CancellationToken.None);
                    Apply(usage, clock.Now);
                    HasData = usage.Windows.Count > 0;
                    StatusText = HasData ? "Forbruget er opdateret" : "Ingen forbrugsgrænser fundet";
                    LastUpdatedText = $"Opdateret {clock.Now:HH:mm:ss}";
                    return;
                }
                catch (ClaudeUsageException exception) when (
                    exception.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    // Prøv kontoens næste kilde, fx Desktop hvis Claude Codes token er afvist.
                    lastError = exception;
                }
            }

            throw lastError!;
        }
        catch (ClaudeUsageException exception)
        {
            AppLog.Warn($"Forbrug kunne ikke hentes for en konto (HTTP {(int?)exception.StatusCode})", exception);
            ErrorText = exception.Message;
            StatusText = HasData ? "Viser senest hentede data" : "Kunne ikke hente forbruget";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Opdaterer nedtællinger. Returnerer true når et vindue lige er nulstillet.</summary>
    public bool Tick(DateTimeOffset now)
    {
        foreach (var card in Cards)
        {
            card.Tick(now);
        }

        var reachedBoundary = Cards
            .Select(card => card.ResetsAt)
            .Where(value => value is not null && value <= now)
            .DefaultIfEmpty(null)
            .Max();

        if (reachedBoundary is null || reachedBoundary == _lastBoundaryRefresh)
        {
            return false;
        }

        _lastBoundaryRefresh = reachedBoundary;
        return true;
    }

    private void Apply(ClaudeUsage usage, DateTimeOffset now)
    {
        // Sammenflet indkommende vinduer med de eksisterende kort ud fra Key, så
        // nedtællinger fortsætter uafbrudt og nye/fjernede grænser afspejles.
        var index = 0;
        foreach (var window in usage.Windows)
        {
            var existing = Cards.FirstOrDefault(card => card.Key == window.Key);
            if (existing is null)
            {
                Cards.Insert(
                    Math.Min(index, Cards.Count),
                    CreateCard(window));
            }
            else
            {
                var previousUtilization = existing.Utilization;
                var previousReset = existing.ResetsAt;

                existing.Title = UsageWindowLabels.Title(window);
                existing.Eyebrow = UsageWindowLabels.Eyebrow(window);
                existing.ShortTitle = UsageWindowLabels.ShortTitle(window);
                existing.WindowLength = UsageWindowLabels.WindowLength(window);
                existing.Update(window.Utilization, window.ResetsAt);

                if (DescribeChange(existing.Title, previousUtilization, previousReset, window, Notifications, now) is { } message)
                {
                    NotificationRaised?.Invoke(this, message);
                }

                var currentIndex = Cards.IndexOf(existing);
                if (currentIndex != index)
                {
                    Cards.Move(currentIndex, index);
                }
            }

            index++;
        }

        var liveKeys = usage.Windows.Select(window => window.Key).ToHashSet();
        for (var i = Cards.Count - 1; i >= 0; i--)
        {
            if (!liveKeys.Contains(Cards[i].Key))
            {
                Cards.RemoveAt(i);
            }
        }

        // Nulstil grænse-sporingen når en opdatering har flyttet nulstillingstiderne
        // frem, så næste passage kan udløse en ny hentning.
        if (_lastBoundaryRefresh is not null &&
            usage.Windows.All(window => window.ResetsAt != _lastBoundaryRefresh))
        {
            _lastBoundaryRefresh = null;
        }

        Tick(now);
    }

    /// <summary>
    /// Afgør om en opdatering af et kendt kort skal give en notifikation: et nyt
    /// vindue (nulstillet) eller en krydset tærskel. Første indlæsning giver ingen.
    /// </summary>
    internal static string? DescribeChange(
        string title,
        double previousUtilization,
        DateTimeOffset? previousReset,
        UsageWindow window,
        NotificationOptions options,
        DateTimeOffset now)
    {
        if (!options.Enabled)
        {
            return null;
        }

        var isNewWindow = previousReset is { } oldReset &&
            window.ResetsAt is { } newReset &&
            newReset - oldReset > TimeSpan.FromMinutes(1) &&
            oldReset <= now + TimeSpan.FromMinutes(1);
        if (isNewWindow)
        {
            return options.OnReset ? $"{title} er nulstillet – fuld kvote igen." : null;
        }

        var crossed = options.Thresholds
            .Where(threshold => previousUtilization < threshold && window.Utilization >= threshold)
            .DefaultIfEmpty(-1)
            .Max();
        return crossed > 0 ? $"{title} har nået {crossed} %." : null;
    }

    private static UsageCardViewModel CreateCard(UsageWindow window)
    {
        var card = new UsageCardViewModel(
            UsageWindowLabels.Title(window),
            UsageWindowLabels.Eyebrow(window),
            window.Key,
            UsageWindowLabels.ShortTitle(window))
        {
            WindowLength = UsageWindowLabels.WindowLength(window)
        };
        card.Update(window.Utilization, window.ResetsAt);
        return card;
    }
}
