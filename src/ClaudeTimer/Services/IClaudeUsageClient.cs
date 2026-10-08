using ClaudeTimer.Models;

namespace ClaudeTimer.Services;

public interface IClaudeUsageClient
{
    Task<ClaudeUsage> GetUsageAsync(string oauthToken, CancellationToken cancellationToken);

    Task<ClaudeAccount> GetProfileAsync(string oauthToken, CancellationToken cancellationToken);
}
