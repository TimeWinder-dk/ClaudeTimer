using System.Linq;

namespace ClaudeTimer.ViewModels;

/// <summary>Bygger bakke-ikonets tooltip, som Windows begrænser til 127 tegn.</summary>
internal static class TrayTextBuilder
{
    public const int MaxLength = 127;
    private const string AppName = "ClaudeTimer";
    private static readonly TimeSpan SameResetTolerance = TimeSpan.FromMinutes(1);

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

            var full = string.Join('\n', cards.Select(card => card.ResetsAt is null
                ? $"{card.Title} · {card.Percentage}"
                : $"{card.Title} · {card.Percentage} · {card.Countdown}"));
            return full.Length <= MaxLength
                ? full
                : Fit(BuildCompact(accounts, includeName: false, includeScoped: true));
        }

        // Flere konti: én linje pr. konto. Bliver det for langt, droppes de
        // model-afgrænsede uger (fx Fable), før teksten afkortes.
        var text = BuildCompact(accounts, includeName: true, includeScoped: true);
        return Fit(text.Length <= MaxLength
            ? text
            : BuildCompact(accounts, includeName: true, includeScoped: false));
    }

    private static string BuildCompact(
        IReadOnlyList<AccountViewModel> accounts,
        bool includeName,
        bool includeScoped) =>
        string.Join('\n', accounts.Select(account =>
        {
            var prefix = includeName ? $"{account.ShortName}: " : string.Empty;
            if (account.Cards.Count == 0)
            {
                return prefix + (account.ErrorText is null ? "henter…" : "kunne ikke hente");
            }

            var cards = account.Cards.Where(card => includeScoped || !card.Key.StartsWith("weekly_scoped", StringComparison.Ordinal));
            return prefix + string.Join(" · ", CompactParts(cards));
        }));

    private static IEnumerable<string> CompactParts(IEnumerable<UsageCardViewModel> cards)
    {
        // Gentag ikke en nedtælling, der svarer til en tidligere grænses. De ugentlige
        // grænser nulstiller typisk samtidig, men API'et kan give dem tidspunkter, der
        // ligger få sekunder fra hinanden (fx 02:59:59 og 03:00:00) – derfor en tolerance.
        var shownResets = new List<DateTimeOffset>();
        foreach (var card in cards)
        {
            var percent = $"{card.ShortTitle} {Math.Round(card.Utilization):0}%";
            if (card.ResetsAt is not { } resetsAt ||
                shownResets.Any(shown => (shown - resetsAt).Duration() < SameResetTolerance))
            {
                yield return percent;
                continue;
            }

            shownResets.Add(resetsAt);
            yield return $"{percent} ({card.CompactCountdown})";
        }
    }

    private static string Fit(string text) =>
        text.Length <= MaxLength ? text : text[..(MaxLength - 1)] + "…";
}
