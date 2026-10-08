namespace ClaudeTimer.Services;

public interface IClaudeCodeCredentialReader
{
    string CredentialPath { get; }

    Task<string?> TryReadAccessTokenAsync(CancellationToken cancellationToken = default);
}
