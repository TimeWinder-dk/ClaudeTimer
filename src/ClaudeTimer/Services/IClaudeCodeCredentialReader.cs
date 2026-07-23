namespace ClaudeTimer.Services;

public interface IClaudeCodeCredentialReader
{
    Task<string?> TryReadAccessTokenAsync(CancellationToken cancellationToken = default);
}
