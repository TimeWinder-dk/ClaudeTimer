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
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/oauth/usage");
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
                var result = await JsonSerializer.DeserializeAsync<UsageApiResponse>(
                    stream,
                    JsonOptions,
                    cancellationToken);

                if (result is null)
                {
                    throw new ClaudeUsageException("Claude returnerede et tomt svar.");
                }

                return new ClaudeUsage(ToDomain(result.FiveHour), ToDomain(result.SevenDay));
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

    private static UsageWindow? ToDomain(UsageWindowResponse? value) =>
        value is null ? null : new UsageWindow(value.Utilization, value.ResetsAt);

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
