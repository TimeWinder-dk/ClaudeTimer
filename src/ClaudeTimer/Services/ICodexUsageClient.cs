using ClaudeTimer.Models;

namespace ClaudeTimer.Services;

public interface ICodexUsageClient
{
    /// <param name="workspaceId">ChatGPT-arbejdsområdet (ChatGPT-Account-Id), eller null for standard.</param>
    Task<ClaudeUsage> GetUsageAsync(string accessToken, string? workspaceId, CancellationToken cancellationToken);
}
