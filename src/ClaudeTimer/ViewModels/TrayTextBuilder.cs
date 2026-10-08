using System.Linq;

namespace ClaudeTimer.ViewModels;

/// <summary>Bygger bakke-ikonets tooltip, som Windows begrænser til 127 tegn.</summary>
internal static class TrayTextBuilder
{
    public const int MaxLength = 127;
    private const string AppName = "ClaudeTimer";

    public static string Build(IReadOnlyList<AccountViewModel> accounts)
    {
        if (accounts.Count == 0 || accounts.All(account => account.Cards.Count == 0 && account.ErrorText is null))
        {
            return AppName;
        }

        if (accounts.Count == 1)
        {
            var cards = accounts[0].Cards;
            if (cards.Count == 0)
            {
                return Fit($"{AppName}\n{accounts[0].ErrorText}");
            }

            var full = string.Join('\n', cards.Select(card => $"{card.Title} · {card.Percentage} · {card.Countdown}"));
            return full.Length <= MaxLength
                ? full
                : Fit(string.Join('\n', cards.Select(Compact)));
        }

        // Flere konti: én linje pr. konto, så de kan skelnes med et blik.
        return Fit(string.Join('\n', accounts.Select(account =>
            $"{account.ShortName}: " + (account.Cards.Count > 0
                ? string.Join(" · ", account.Cards.Select(Compact))
                : account.ErrorText is null ? "henter…" : "fejl"))));
    }

    private static string Compact(UsageCardViewModel card) =>
        $"{card.ShortTitle} {Math.Round(card.Utilization):0}% {card.CompactCountdown}";

    private static string Fit(string text) =>
        text.Length <= MaxLength ? text : text[..(MaxLength - 1)] + "…";
}
