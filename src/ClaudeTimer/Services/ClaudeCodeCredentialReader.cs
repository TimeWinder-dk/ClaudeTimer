using System.Text.Json;
using System.IO;

namespace ClaudeTimer.Services;

public sealed class ClaudeCodeCredentialReader : IClaudeCodeCredentialReader
{
    private const string CredentialFileName = ".credentials.json";

    private static readonly string UserProfile =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    // Claude Code CLI og VS Code-udvidelsen deler samme konfigurationsmappe,
    // som kan flyttes med CLAUDE_CONFIG_DIR.
    // Multi-Account Switcher for Claude Code lægger hver ekstra konto i sin egen
    // mappe under ~/.claude-accounts og peger Claude Code på den med
    // CLAUDE_CONFIG_DIR – men kun inde i VS Code, så ClaudeTimer kigger selv efter dem.
    public ClaudeCodeCredentialReader()
        : this(ResolveConfigDirectory(), Path.Combine(UserProfile, ".claude-accounts"))
    {
    }

    internal ClaudeCodeCredentialReader(string configDirectory, string profilesDirectory)
    {
        CredentialPath = Path.Combine(configDirectory, CredentialFileName);
        ProfilesDirectory = profilesDirectory;
    }

    public string CredentialPath { get; }

    public string ProfilesDirectory { get; }

    private static string ResolveConfigDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(UserProfile, ".claude")
            : Environment.ExpandEnvironmentVariables(configured.Trim());
    }

    public Task<string?> TryReadAccessTokenAsync(CancellationToken cancellationToken = default) =>
        ReadTokenAsync(CredentialPath, cancellationToken);

    public async Task<IReadOnlyList<ClaudeCodeProfile>> ReadProfilesAsync(
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(ProfilesDirectory))
        {
            return [];
        }

        string[] directories;
        try
        {
            directories = Directory.GetDirectories(ProfilesDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        var mainPath = Path.GetFullPath(CredentialPath);
        var profiles = new List<ClaudeCodeProfile>();
        foreach (var directory in directories.Order(StringComparer.OrdinalIgnoreCase))
        {
            var path = Path.Combine(directory, CredentialFileName);
            if (!File.Exists(path) ||
                string.Equals(Path.GetFullPath(path), mainPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            profiles.Add(new ClaudeCodeProfile(
                Path.GetFileName(directory),
                path,
                await ReadTokenAsync(path, cancellationToken)));
        }

        return profiles;
    }

    private static async Task<string?> ReadTokenAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            return ParseAccessToken(json, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Adgangstokenet fra en .credentials.json, eller null hvis det mangler eller er
    /// udløbet. Et udløbet token giver kun 401; Claude Code fornyer det selv næste
    /// gang kontoen bruges, og ClaudeTimer fornyer aldrig selv (refresh-tokenet roterer).
    /// </summary>
    internal static string? ParseAccessToken(string json, long nowMs)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("claudeAiOauth", out var oauth))
            {
                return null;
            }

            if (oauth.TryGetProperty("expiresAt", out var expiresAt) &&
                expiresAt.TryGetInt64(out var expiresAtMs) &&
                expiresAtMs <= nowMs)
            {
                return null;
            }

            return oauth.TryGetProperty("accessToken", out var token) &&
                token.ValueKind == JsonValueKind.String &&
                token.GetString() is { Length: > 0 } value
                ? value
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
