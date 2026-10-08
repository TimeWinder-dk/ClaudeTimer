using System.Security.Cryptography;
using System.Text;
using ClaudeTimer.Models;
using ClaudeTimer.Services;
using ClaudeTimer.ViewModels;

namespace ClaudeTimer.Tests;

public sealed class MultiAccountTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-08T12:00:00Z");

    [Fact]
    public void Desktop_Decrypt_ReadsElectronSafeStorageFormat()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var encrypted = EncryptLikeElectron(key, """{"hello":"world"}""");

        Assert.Equal("""{"hello":"world"}""", ClaudeDesktopCredentialReader.Decrypt(key, encrypted));
    }

    [Fact]
    public void Desktop_SelectToken_PicksValidProfileScopedTokenWithLatestExpiry()
    {
        var now = Now.ToUnixTimeMilliseconds();
        var json = $$"""
            {
              "acct:a|c1:org:https://api.anthropic.com:user:inference user:profile user:sessions:claude_code:":
                { "token": "expired", "expiresAt": {{now - 1000}} },
              "acct:a|c2:org:https://api.anthropic.com:user:inference user:office:":
                { "token": "no-profile-scope", "expiresAt": {{now + 999999}} },
              "acct:a|c3:org:https://api.anthropic.com:user:profile:":
                { "token": "valid-early", "expiresAt": {{now + 1000}} },
              "acct:a|c4:org:https://api.anthropic.com:user:file_upload user:profile:":
                { "token": "valid-late", "expiresAt": {{now + 5000}} }
            }
            """;

        Assert.Equal("valid-late", ClaudeDesktopCredentialReader.SelectToken(json, now));
    }

    [Fact]
    public void Desktop_SelectToken_ReturnsNull_WhenAllTokensExpired()
    {
        var json = """{ "acct:a|c:o:https://api.anthropic.com:user:profile:": { "token": "t", "expiresAt": 1 } }""";

        Assert.Null(ClaudeDesktopCredentialReader.SelectToken(json, Now.ToUnixTimeMilliseconds()));
    }

    [Fact]
    public void AccountNames_UseEmailLocalPart_AndOrganization_WhenEmailRepeats()
    {
        var personal = Account("a", "thha@timewinder.dk", "o1", "Timewinder", TokenSource.ClaudeCode);
        var other = Account("b", "privat@example.com", "o2", "Privat", TokenSource.ClaudeDesktop);
        MainViewModel.ApplyAccountNames([personal, other]);

        Assert.Equal("thha", personal.ShortName);
        Assert.Equal("thha@timewinder.dk", personal.DisplayName);
        Assert.Equal("Timewinder · Claude Code", personal.Subtitle);
        Assert.Equal("privat", other.ShortName);

        var sameEmailOtherOrg = Account("a", "thha@timewinder.dk", "o3", "Kunde A/S", TokenSource.ClaudeDesktop);
        MainViewModel.ApplyAccountNames([personal, sameEmailOtherOrg]);

        Assert.Equal("Timewinder", personal.ShortName);
        Assert.Equal("Kunde A/S", sameEmailOtherOrg.ShortName);
        Assert.Equal("thha@timewinder.dk · Kunde A/S", sameEmailOtherOrg.DisplayName);
    }

    [Fact]
    public void TrayText_SingleAccount_UsesFullLines()
    {
        var account = AccountWithCards("thha", ("5 timer", "5t", 42, TimeSpan.FromMinutes(72)));

        Assert.Equal("5 timer · 42 % · 01:12:00", TrayTextBuilder.Build([account]));
    }

    [Fact]
    public void TrayText_MultipleAccounts_UsesOneCompactLinePerAccount()
    {
        var work = AccountWithCards(
            "thha",
            ("5 timer", "5t", 42, TimeSpan.FromMinutes(72)),
            ("7 døgn", "Uge", 18.4, TimeSpan.FromHours(76)),
            ("Fable · 7 døgn", "Fable", 5, TimeSpan.FromHours(76)));
        var personal = AccountWithCards(
            "privat",
            ("5 timer", "5t", 0, null),
            ("7 døgn", "Uge", 7, TimeSpan.FromMinutes(9)));

        var text = TrayTextBuilder.Build([work, personal]);

        // Ingen "—" for manglende nulstilling, og Fable gentager ikke ugens nedtælling.
        Assert.Equal("thha: 5t 42% (1t12m) · Uge 18% (3d4t) · Fable 5%\nprivat: 5t 0% · Uge 7% (9m)", text);
    }

    [Fact]
    public void TrayText_SingleAccount_OmitsCountdownWithoutReset()
    {
        var account = AccountWithCards("thha", ("5 timer", "5t", 0, null));

        Assert.Equal("5 timer · 0 %", TrayTextBuilder.Build([account]));
    }

    [Fact]
    public void TrayText_NeverExceedsWindowsLimit()
    {
        var accounts = Enumerable.Range(0, 4)
            .Select(i => AccountWithCards(
                $"meget-langt-kontonavn-{i}",
                ("5 timer", "5t", 42, TimeSpan.FromMinutes(72)),
                ("7 døgn", "Uge", 18, TimeSpan.FromHours(76)),
                ("Fable · 7 døgn", "Fable", 5, TimeSpan.FromHours(76))))
            .ToList();

        var text = TrayTextBuilder.Build(accounts);

        Assert.True(text.Length <= TrayTextBuilder.MaxLength);
        Assert.EndsWith("…", text);
    }

    [Theory]
    [InlineData(30, "30m")]
    [InlineData(72, "1t12m")]
    [InlineData(60 * 50, "2d2t")]
    public void CompactCountdown_IsShort(int minutes, string expected)
    {
        Assert.Equal(expected, UsageCardViewModel.FormatCompact(TimeSpan.FromMinutes(minutes)));
    }

    private static AccountViewModel Account(
        string accountId, string email, string orgId, string orgName, TokenSource source) =>
        new($"{accountId}|{orgId}", string.Empty)
        {
            Profile = new ClaudeAccount(accountId, email, null, orgId, orgName),
            Credentials = [new AccountCredential(source, "token")]
        };

    private static AccountViewModel AccountWithCards(
        string shortName,
        params (string Title, string ShortTitle, double Percent, TimeSpan? Remaining)[] cards)
    {
        var account = new AccountViewModel(shortName, shortName) { ShortName = shortName };
        foreach (var (title, shortTitle, percent, remaining) in cards)
        {
            var card = new UsageCardViewModel(title, string.Empty, title, shortTitle);
            card.Update(percent, remaining is { } value ? Now + value : null);
            card.Tick(Now);
            account.Cards.Add(card);
        }

        return account;
    }

    private static string EncryptLikeElectron(byte[] key, string plain)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plainBytes = Encoding.UTF8.GetBytes(plain);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plainBytes, cipher, tag);
        return Convert.ToBase64String([.. "v10"u8.ToArray(), .. nonce, .. cipher, .. tag]);
    }
}
