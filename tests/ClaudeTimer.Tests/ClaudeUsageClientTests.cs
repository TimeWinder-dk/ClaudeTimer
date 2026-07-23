using System.Net;
using System.Net.Http;
using ClaudeTimer.Services;

namespace ClaudeTimer.Tests;

public sealed class ClaudeUsageClientTests
{
    [Fact]
    public async Task GetUsageAsync_ParsesBothWindowsAndSendsRequiredHeaders()
    {
        var handler = new RecordingHandler(
            HttpStatusCode.OK,
            """
            {
              "five_hour": {
                "utilization": 37.5,
                "resets_at": "2026-07-23T14:30:00+00:00"
              },
              "seven_day": {
                "utilization": 62,
                "resets_at": "2026-07-28T08:15:12.123456+00:00"
              }
            }
            """);
        var client = CreateClient(handler);

        var result = await client.GetUsageAsync("secret-token", CancellationToken.None);

        Assert.Equal(37.5, result.FiveHour?.Utilization);
        Assert.Equal(DateTimeOffset.Parse("2026-07-23T14:30:00Z"), result.FiveHour?.ResetsAt);
        Assert.Equal(62, result.SevenDay?.Utilization);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("secret-token", handler.AuthorizationParameter);
        Assert.Equal("oauth-2025-04-20", handler.BetaHeader);
    }

    [Fact]
    public async Task GetUsageAsync_AllowsNullWindows()
    {
        var client = CreateClient(new RecordingHandler(
            HttpStatusCode.OK,
            """{"five_hour":null,"seven_day":null}"""));

        var result = await client.GetUsageAsync("token", CancellationToken.None);

        Assert.Null(result.FiveHour);
        Assert.Null(result.SevenDay);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "udløbet")]
    [InlineData(HttpStatusCode.TooManyRequests, "For mange")]
    [InlineData(HttpStatusCode.InternalServerError, "HTTP 500")]
    public async Task GetUsageAsync_MapsApiErrors(
        HttpStatusCode statusCode,
        string expectedMessage)
    {
        var client = CreateClient(new RecordingHandler(statusCode, "{}"));

        var exception = await Assert.ThrowsAsync<ClaudeUsageException>(
            () => client.GetUsageAsync("token", CancellationToken.None));

        Assert.Contains(expectedMessage, exception.Message);
        Assert.Equal(statusCode, exception.StatusCode);
    }

    [Fact]
    public async Task GetUsageAsync_MapsInvalidJson()
    {
        var client = CreateClient(new RecordingHandler(HttpStatusCode.OK, "{invalid"));

        var exception = await Assert.ThrowsAsync<ClaudeUsageException>(
            () => client.GetUsageAsync("token", CancellationToken.None));

        Assert.Contains("ukendt svarformat", exception.Message);
    }

    private static ClaudeUsageClient CreateClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com/") });

    private sealed class RecordingHandler(
        HttpStatusCode statusCode,
        string content) : HttpMessageHandler
    {
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }
        public string? BetaHeader { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            BetaHeader = request.Headers.GetValues("anthropic-beta").Single();

            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content)
            });
        }
    }
}
