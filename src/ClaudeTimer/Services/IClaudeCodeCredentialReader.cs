namespace ClaudeTimer.Services;

/// <summary>
/// En ekstra Claude Code-konto i sin egen konfigurationsmappe, fx fra
/// VS Code-udvidelsen "Multi-Account Switcher for Claude Code".
/// <see cref="AccessToken"/> er null, når login mangler eller er udløbet.
/// </summary>
public sealed record ClaudeCodeProfile(string Name, string CredentialPath, string? AccessToken);

public interface IClaudeCodeCredentialReader
{
    string CredentialPath { get; }

    /// <summary>Mappen med ekstra kontoprofiler (én undermappe per konto).</summary>
    string ProfilesDirectory { get; }

    Task<string?> TryReadAccessTokenAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClaudeCodeProfile>> ReadProfilesAsync(CancellationToken cancellationToken = default);
}
