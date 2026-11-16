using ClaudeTimer.Models;

namespace ClaudeTimer.Services;

/// <summary>Et gyldigt ChatGPT-login fra Codex og kontoen, det tilhører.</summary>
public sealed record CodexCredential(string AccessToken, ClaudeAccount Account);

public interface ICodexCredentialReader
{
    string CredentialPath { get; }

    Task<CodexCredential?> TryReadAsync(CancellationToken cancellationToken = default);
}
