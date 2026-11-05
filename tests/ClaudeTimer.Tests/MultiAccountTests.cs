using System.Security.Cryptography;
using System.Text;
using ClaudeTimer.Models;
using ClaudeTimer.Services;
using ClaudeTimer.ViewModels;

namespace ClaudeTimer.Tests;

public sealed class MultiAccountTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-08T12:00:00Z");

    private static string Credentials(string token, DateTimeOffset expires) =>
        $$$"""{"claudeAiOauth":{"accessToken":"{{{token}}}","refreshToken":"r","expiresAt":{{{expires.ToUnixTimeMilliseconds()}}}}}""";

    [Fact]
    public void ParseAccessToken_SkipsExpiredAndMalformed()
    {
        var now = Now.ToUnixTimeMilliseconds();
        Assert.Equal("a", ClaudeCodeCredentialReader.ParseAccessToken(Credentials("a", Now.AddHours(1)), now));
        Assert.Null(ClaudeCodeCredentialReader.ParseAccessToken(Credentials("a", Now.AddSeconds(-1)), now));
        Assert.Null(ClaudeCodeCredentialReader.ParseAccessToken("""{"claudeAiOauth":{}}""", now));
        Assert.Null(ClaudeCodeCredentialReader.ParseAccessToken("""{"claudeAiOauth":{"accessToken":42}}""", now));
        Assert.Null(ClaudeCodeCredentialReader.ParseAccessToken("ikke json", now));
    }

    [Fact]
    public async Task ReadProfiles_FindsAccountFolders_AndSkipsTheMainLogin()
    {
        var root = Directory.CreateTempSubdirectory("ct-profiles-");
        try
        {
            var main = Directory.CreateDirectory(Path.Combine(root.FullName, "main"));
            var accounts = Directory.CreateDirectory(Path.Combine(root.FullName, "accounts"));
            await File.WriteAllTextAsync(Path.Combine(main.FullName, ".credentials.json"), Credentials("main", DateTimeOffset.UtcNow.AddHours(1)));
            foreach (var (name, token, expires) in new[]
            {
                ("privat", "p", DateTimeOffset.UtcNow.AddHours(1)),
                ("arbejde", "w", DateTimeOffset.UtcNow.AddHours(1)),
                ("gammel", "g", DateTimeOffset.UtcNow.AddHours(-1)),
            })
            {
                var dir = Directory.CreateDirectory(Path.Combine(accounts.FullName, name));
                await File.WriteAllTextAsync(Path.Combine(dir.FullName, ".credentials.json"), Credentials(token, expires));
            }

            Directory.CreateDirectory(Path.Combine(accounts.FullName, "uden-login"));

            var reader = new ClaudeCodeCredentialReader(main.FullName, accounts.FullName);
            var profiles = await reader.ReadProfilesAsync();

            Assert.Equal(["arbejde", "gammel", "privat"], profiles.Select(p => p.Name));
            Assert.Equal(["w", null, "p"], profiles.Select(p => p.AccessToken));
            Assert.Equal("main", await reader.TryReadAccessTokenAsync());
            Assert.Empty(await new ClaudeCodeCredentialReader(main.FullName, Path.Combine(root.FullName, "findes-ikke")).ReadProfilesAsync());
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Profiles_AreNamedInSubtitle_AndStatus()
    {
        Assert.Equal("Claude Code (arbejde)",
            MainViewModel.CredentialLabel(new AccountCredential(TokenSource.ClaudeCode, "t", "arbejde")));
        Assert.Equal("Claude Code", MainViewModel.CredentialLabel(new AccountCredential(TokenSource.ClaudeCode, "t")));

        Assert.Null(MainViewModel.DescribeProfiles("dir", []));
        Assert.Equal(
            "Kontoprofiler (dir): 2 fundet – aktive: arbejde. Udløbet login: gammel – brug kontoen i Claude Code, så fornyes det.",
            MainViewModel.DescribeProfiles("dir", [new("arbejde", "x", "t"), new("gammel", "y", null)]));
    }

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
    public void TrayText_DoesNotRepeatCountdown_WhenResetsDifferBySeconds()
    {
        var account = AccountWithCards(
            "thha",
            ("5 timer", "5t", 9, TimeSpan.FromMinutes(238)),
            ("7 døgn", "Uge", 2, TimeSpan.FromHours(162) - TimeSpan.FromSeconds(1)),
            ("Fable · 7 døgn", "Fable", 1, TimeSpan.FromHours(162)));
        var other = AccountWithCards("tha", ("5 timer", "5t", 1, TimeSpan.FromMinutes(278)));

        Assert.Equal(
            "thha: 5t 9% (3t58m) · Uge 2% (6d17t) · Fable 1%\ntha: 5t 1% (4t38m)",
            TrayTextBuilder.Build([account, other]));
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
