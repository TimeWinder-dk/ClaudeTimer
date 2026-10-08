using System.Net;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Text.Json;
using ClaudeTimer.Models;

namespace ClaudeTimer.Services;

public sealed class ClaudeUsageClient(HttpClient httpClient) : IClaudeUsageClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ClaudeUsage> GetUsageAsync(
        string oauthToken,
        CancellationToken cancellationToken)
    {
        var result = await GetJsonAsync<UsageApiResponse>("api/oauth/usage", oauthToken, cancellationToken);
        return new ClaudeUsage(BuildWindows(result));
    }

    public async Task<ClaudeAccount> GetProfileAsync(
        string oauthToken,
        CancellationToken cancellationToken)
    {
        var result = await GetJsonAsync<ProfileApiResponse>("api/oauth/profile", oauthToken, cancellationToken);
        if (result.Account?.Uuid is not { Length: > 0 } accountId)
        {
            throw new ClaudeUsageException("Claude returnerede en profil uden konto.");
        }

        return new ClaudeAccount(
            accountId,
            result.Account.Email,
            result.Account.DisplayName ?? result.Account.FullName,
            result.Organization?.Uuid,
            result.Organization?.Name);
    }

    private async Task<T> GetJsonAsync<T>(
        string path,
        string oauthToken,
        CancellationToken cancellationToken)
        where T : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", oauthToken);
        request.Headers.TryAddWithoutValidation("anthropic-beta", "oauth-2025-04-20");

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ClaudeUsageException("Claude svarede ikke inden for tidsfristen.");
        }
        catch (HttpRequestException exception)
        {
            throw new ClaudeUsageException(
                "Claude kunne ikke kontaktes. Kontrollér internetforbindelsen.",
                innerException: exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw CreateApiException(response.StatusCode);
            }

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var result = await JsonSerializer.DeserializeAsync<T>(
                    stream,
                    JsonOptions,
                    cancellationToken);

                return result ?? throw new ClaudeUsageException("Claude returnerede et tomt svar.");
            }
            catch (JsonException exception)
            {
                throw new ClaudeUsageException(
                    "Claude returnerede et ukendt svarformat.",
                    response.StatusCode,
                    exception);
            }
        }
    }

    private static IReadOnlyList<UsageWindow> BuildWindows(UsageApiResponse response)
    {
        // Foretræk den moderne limits-liste, så nye grænser (fx yderligere
        // model-afgrænsede uger) dukker op uden kodeændringer.
        if (response.Limits is { Count: > 0 })
        {
            var windows = new List<UsageWindow>(response.Limits.Count);
            foreach (var limit in response.Limits)
            {
                if (limit is null || string.IsNullOrWhiteSpace(limit.Kind))
                {
                    continue;
                }

                windows.Add(new UsageWindow(
                    limit.Kind,
                    limit.Group ?? limit.Kind,
                    limit.Percent,
                    limit.ResetsAt,
                    limit.Scope?.Model?.DisplayName));
            }

            if (windows.Count > 0)
            {
                return windows;
            }
        }

        // Fallback for ældre svar uden limits-array.
        var legacy = new List<UsageWindow>(2);
        if (response.FiveHour is { } fiveHour)
        {
            legacy.Add(new UsageWindow(
                "session", "session", fiveHour.Utilization, fiveHour.ResetsAt, null));
        }

        if (response.SevenDay is { } sevenDay)
        {
            legacy.Add(new UsageWindow(
                "weekly_all", "weekly", sevenDay.Utilization, sevenDay.ResetsAt, null));
        }

        return legacy;
    }

    private static ClaudeUsageException CreateApiException(HttpStatusCode statusCode) =>
        statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new("OAuth-tokenet er udløbet eller har ikke adgang.", statusCode),
            HttpStatusCode.TooManyRequests =>
                new("For mange forespørgsler. Prøv igen om lidt.", statusCode),
            _ => new($"Claude returnerede HTTP {(int)statusCode}.", statusCode)
        };
}
