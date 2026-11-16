using System.Text;
using System.Text.Json;
using ClaudeTimer.Models;
using ClaudeTimer.Services;
using ClaudeTimer.ViewModels;

namespace ClaudeTimer.Tests;

public sealed class CodexTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-16T12:00:00Z");

    private static string Jwt(object claims)
    {
        static string Encode(string text) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{Encode("""{"alg":"none"}""")}.{Encode(JsonSerializer.Serialize(claims))}.sig";
    }

    private static string AuthJson(DateTimeOffset expires, string? accountId = "ws-1") =>
        JsonSerializer.Serialize(new
        {
            auth_mode = "chatgpt",
            OPENAI_API_KEY = (string?)null,
            tokens = new
            {
                access_token = Jwt(new Dictionary<string, object>
                {
                    ["exp"] = expires.ToUnixTimeSeconds(),
                    ["https://api.openai.com/auth"] = new { chatgpt_plan_type = "team", chatgpt_account_id = "ws-1" }
                }),
                id_token = Jwt(new Dictionary<string, object>
                {
                    ["email"] = "tha@timewinder.dk",
                    ["https://api.openai.com/auth"] = new { chatgpt_user_id = "user-7", chatgpt_account_id = "ws-1" }
                }),
                refresh_token = "r",
                account_id = accountId
            }
        });

    [Fact]
    public void Parse_ReadsTokenAndAccountFromAuthJson()
    {
        var credential = CodexCredentialReader.Parse(AuthJson(Now.AddDays(5)), Now);

        Assert.NotNull(credential);
        Assert.StartsWith("eyJ", credential.AccessToken);
        Assert.Equal(new ClaudeAccount("chatgpt:user-7", "tha@timewinder.dk", null, "ws-1", "ChatGPT Team"), credential.Account);
    }

    [Fact]
    public void Parse_FallsBackToTheWorkspaceInTheToken()
    {
        var credential = CodexCredentialReader.Parse(AuthJson(Now.AddDays(5), accountId: null), Now);

        Assert.Equal("ws-1", credential?.Account.OrganizationId);
    }

    [Fact]
    public void Parse_SkipsExpiredApiKeyAndMalformedLogins()
    {
        Assert.Null(CodexCredentialReader.Parse(AuthJson(Now.AddSeconds(-1)), Now));
        Assert.Null(CodexCredentialReader.Parse("""{"auth_mode":"apikey","OPENAI_API_KEY":"sk-x","tokens":null}""", Now));
        Assert.Null(CodexCredentialReader.Parse("""{"tokens":{"access_token":"ikke-en-jwt"}}""", Now));
        Assert.Null(CodexCredentialReader.Parse("ikke json", Now));
    }

    [Fact]
    public void PlanName_IsCapitalized()
    {
        Assert.Equal("ChatGPT Plus", CodexCredentialReader.PlanName("plus"));
        Assert.Equal("ChatGPT", CodexCredentialReader.PlanName(null));
    }

    [Fact]
    public void BuildWindows_MapsKnownWindowLengthsToClaudesKinds()
    {
        var response = JsonSerializer.Deserialize<CodexUsageResponse>("""
            {
              "plan_type": "team",
              "rate_limit": {
                "primary_window": { "used_percent": 56, "limit_window_seconds": 18000, "reset_at": 1794824092 },
                "secondary_window": { "used_percent": 12, "limit_window_seconds": 604800, "reset_at": 1795159217 }
              }
            }
            """)!;

        var windows = CodexUsageClient.BuildWindows(response);

        Assert.Equal(["session", "weekly_all"], windows.Select(window => window.Key));
        Assert.Equal([56d, 12d], windows.Select(window => window.Utilization));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1794824092), windows[0].ResetsAt);
        Assert.Equal("5 timer", UsageWindowLabels.Title(windows[0]));
        Assert.Equal(TimeSpan.FromDays(7), UsageWindowLabels.WindowLength(windows[1]));
    }

    [Fact]
    public void BuildWindows_KeepsUnknownLengths_AndSkipsMissingWindows()
    {
        var response = JsonSerializer.Deserialize<CodexUsageResponse>("""
            { "rate_limit": { "primary_window": null, "secondary_window": { "used_percent": 3, "limit_window_seconds": 86400 } } }
            """)!;

        var window = Assert.Single(CodexUsageClient.BuildWindows(response));
        Assert.Equal("limit_1d", window.Key);
        Assert.Null(window.ResetsAt);
        Assert.Empty(CodexUsageClient.BuildWindows(new CodexUsageResponse()));
    }

    [Fact]
    public void AccountNames_MarkTheChatGptAccount()
    {
        var chatGpt = new AccountViewModel("chatgpt:user-7|ws-1", string.Empty)
        {
            Profile = new ClaudeAccount("chatgpt:user-7", "tha@timewinder.dk", null, "ws-1", "ChatGPT Team"),
            Credentials = [new AccountCredential(TokenSource.Codex, "token")]
        };

        MainViewModel.ApplyAccountNames([chatGpt]);

        Assert.Equal("ChatGPT", chatGpt.ShortName);
        Assert.Equal("tha@timewinder.dk · ChatGPT Team", chatGpt.DisplayName);
        Assert.Equal("Codex", chatGpt.Subtitle);
    }
}
