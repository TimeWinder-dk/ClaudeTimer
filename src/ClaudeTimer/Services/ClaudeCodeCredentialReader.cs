using System.Text.Json;
using System.IO;

namespace ClaudeTimer.Services;

public sealed class ClaudeCodeCredentialReader : IClaudeCodeCredentialReader
{
    private readonly string _credentialPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude",
        ".credentials.json");

    public async Task<string?> TryReadAccessTokenAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_credentialPath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(_credentialPath);
            using var document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken);

            return document.RootElement
                .GetProperty("claudeAiOauth")
                .GetProperty("accessToken")
                .GetString();
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
