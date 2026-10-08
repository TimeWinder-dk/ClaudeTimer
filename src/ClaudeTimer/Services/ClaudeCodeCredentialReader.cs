using System.Text.Json;
using System.IO;

namespace ClaudeTimer.Services;

public sealed class ClaudeCodeCredentialReader : IClaudeCodeCredentialReader
{
    // Claude Code CLI og VS Code-udvidelsen deler samme konfigurationsmappe,
    // som kan flyttes med CLAUDE_CONFIG_DIR.
    public string CredentialPath { get; } = Path.Combine(
        ResolveConfigDirectory(),
        ".credentials.json");

    private static string ResolveConfigDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".claude")
            : Environment.ExpandEnvironmentVariables(configured.Trim());
    }

    public async Task<string?> TryReadAccessTokenAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(CredentialPath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(CredentialPath);
            using var document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken);

            var oauth = document.RootElement.GetProperty("claudeAiOauth");

            // Et udløbet token giver kun 401; så hellere lade Automatisk prøve
            // næste kilde. Claude Code fornyer selv tokenet næste gang den bruges.
            if (oauth.TryGetProperty("expiresAt", out var expiresAt) &&
                expiresAt.TryGetInt64(out var expiresAtMs) &&
                expiresAtMs <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            {
                return null;
            }

            return oauth.GetProperty("accessToken").GetString();
        }
        catch (JsonException)
        {
            return null;
        }
        catch (KeyNotFoundException)
        {
            return null;
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
}
