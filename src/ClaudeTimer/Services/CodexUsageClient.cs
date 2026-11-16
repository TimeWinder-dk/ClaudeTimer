using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using ClaudeTimer.Models;

namespace ClaudeTimer.Services;

/// <summary>Henter ChatGPT-/Codex-forbruget (5 timer og 7 døgn) fra samme endpoint som Codex.</summary>
public sealed class CodexUsageClient(HttpClient httpClient) : ICodexUsageClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ClaudeUsage> GetUsageAsync(
        string accessToken,
        string? workspaceId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "wham/usage");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (workspaceId is { Length: > 0 })
        {
            request.Headers.TryAddWithoutValidation("ChatGPT-Account-Id", workspaceId);
        }

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ClaudeUsageException("ChatGPT svarede ikke inden for tidsfristen.");
        }
        catch (HttpRequestException exception)
        {
            throw new ClaudeUsageException(
                "ChatGPT kunne ikke kontaktes. Kontrollér internetforbindelsen.",
                innerException: exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                        new ClaudeUsageException("ChatGPT-loginet er udløbet. Brug Codex, så fornyes det.", response.StatusCode),
                    HttpStatusCode.TooManyRequests =>
                        new ClaudeUsageException("For mange forespørgsler til ChatGPT. Prøv igen om lidt.", response.StatusCode),
                    _ => new ClaudeUsageException($"ChatGPT returnerede HTTP {(int)response.StatusCode}.", response.StatusCode)
                };
            }

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var result = await JsonSerializer.DeserializeAsync<CodexUsageResponse>(stream, JsonOptions, cancellationToken)
                    ?? throw new ClaudeUsageException("ChatGPT returnerede et tomt svar.");
                return new ClaudeUsage(BuildWindows(result));
            }
            catch (JsonException exception)
            {
                throw new ClaudeUsageException("ChatGPT returnerede et ukendt svarformat.", response.StatusCode, exception);
            }
        }
    }

    internal static IReadOnlyList<UsageWindow> BuildWindows(CodexUsageResponse response)
    {
        var windows = new List<UsageWindow>(2);
        foreach (var window in new[] { response.RateLimit?.PrimaryWindow, response.RateLimit?.SecondaryWindow })
        {
            if (window is null)
            {
                continue;
            }

            var (kind, group) = Classify(window.LimitWindowSeconds);
            windows.Add(new UsageWindow(
                kind,
                group,
                window.UsedPercent,
                window.ResetAt is { } resetAt ? DateTimeOffset.FromUnixTimeSeconds(resetAt) : null,
                null));
        }

        return windows;
    }

    /// <summary>
    /// Vinduerne beskrives kun ved deres længde. De kendte længder får samme
    /// typer som Claudes grænser, så overskrifter og prognose genbruges.
    /// </summary>
    private static (string Kind, string Group) Classify(long? seconds) => seconds switch
    {
        18_000 => ("session", "session"),
        604_800 => ("weekly_all", "weekly"),
        { } other when other % 86_400 == 0 => ($"limit_{other / 86_400}d", "other"),
        { } other => ($"limit_{Math.Round(other / 3600.0)}h", "other"),
        null => ("limit", "other")
    };
}
