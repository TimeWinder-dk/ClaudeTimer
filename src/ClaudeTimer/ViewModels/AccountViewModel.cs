using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using ClaudeTimer.Models;
using ClaudeTimer.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClaudeTimer.ViewModels;

/// <summary>Et token fra en bestemt kilde.</summary>
public sealed record AccountCredential(TokenSource Source, string Token);

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
                existing.Title = UsageWindowLabels.Title(window);
                existing.Eyebrow = UsageWindowLabels.Eyebrow(window);
                existing.ShortTitle = UsageWindowLabels.ShortTitle(window);
                existing.Update(window.Utilization, window.ResetsAt);

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

    private static UsageCardViewModel CreateCard(UsageWindow window)
    {
        var card = new UsageCardViewModel(
            UsageWindowLabels.Title(window),
            UsageWindowLabels.Eyebrow(window),
            window.Key,
            UsageWindowLabels.ShortTitle(window));
        card.Update(window.Utilization, window.ResetsAt);
        return card;
    }
}
