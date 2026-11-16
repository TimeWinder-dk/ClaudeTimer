using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using ClaudeTimer.Models;

namespace ClaudeTimer.Services;

/// <summary>
/// Læser ChatGPT-loginet, som Codex (CLI, VS Code-udvidelsen og Codex-appen) deler i
/// auth.json. Mappen kan flyttes med CODEX_HOME. Ligesom for Claude Code fornyer
/// ClaudeTimer aldrig selv tokenet: refresh-tokenet roterer, og Codex ville blive logget ud.
/// </summary>
public sealed class CodexCredentialReader : ICodexCredentialReader
{
    private const string AuthClaim = "https://api.openai.com/auth";
    private const string ProfileClaim = "https://api.openai.com/profile";

    public CodexCredentialReader()
        : this(ResolveCodexHome())
    {
    }

    internal CodexCredentialReader(string codexHome)
    {
        CredentialPath = Path.Combine(codexHome, "auth.json");
    }

    public string CredentialPath { get; }

    private static string ResolveCodexHome()
    {
        var configured = Environment.GetEnvironmentVariable("CODEX_HOME");
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex")
            : Environment.ExpandEnvironmentVariables(configured.Trim());
    }

    public async Task<CodexCredential?> TryReadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(CredentialPath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(CredentialPath, cancellationToken);
            return Parse(json, DateTimeOffset.UtcNow);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Login og konto fra en auth.json, eller null hvis den mangler, bruger en
    /// API-nøgle i stedet for ChatGPT-login, eller adgangstokenet er udløbet.
    /// </summary>
    internal static CodexCredential? Parse(string json, DateTimeOffset now)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("tokens", out var tokens) ||
                tokens.ValueKind != JsonValueKind.Object ||
                GetString(tokens, "access_token") is not { } accessToken)
            {
                return null;
            }

            var access = DecodeClaims(accessToken);
            if (access is { } accessClaims &&
                accessClaims.TryGetProperty("exp", out var exp) &&
                exp.TryGetInt64(out var expSeconds) &&
                DateTimeOffset.FromUnixTimeSeconds(expSeconds) <= now)
            {
                return null;
            }

            var id = GetString(tokens, "id_token") is { } idToken ? DecodeClaims(idToken) : null;
            var auth = Claim(id, AuthClaim) ?? Claim(access, AuthClaim);
            var workspaceId = GetString(tokens, "account_id") ?? GetString(auth, "chatgpt_account_id");
            var userId = GetString(auth, "chatgpt_user_id") ?? GetString(auth, "user_id") ?? GetString(id, "sub");
            var email = GetString(id, "email") ?? GetString(Claim(access, ProfileClaim), "email");
            if (userId is null && email is null)
            {
                return null;
            }

            var plan = GetString(Claim(access, AuthClaim), "chatgpt_plan_type") ?? GetString(auth, "chatgpt_plan_type");
            return new CodexCredential(
                accessToken,
                new ClaudeAccount(
                    $"chatgpt:{userId ?? email}",
                    email,
                    null,
                    workspaceId,
                    PlanName(plan)));
        }
        catch (Exception exception) when (exception is JsonException or FormatException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>"team" bliver til "ChatGPT Team", så kontoen kan skelnes fra et Claude-login med samme e-mail.</summary>
    internal static string PlanName(string? plan) =>
        string.IsNullOrWhiteSpace(plan)
            ? "ChatGPT"
            : $"ChatGPT {char.ToUpper(plan[0], CultureInfo.InvariantCulture)}{plan[1..]}";

    /// <summary>Payload-delen af en JWT. Signaturen tjekkes ikke; tokenet bruges kun mod OpenAI selv.</summary>
    private static JsonElement? DecodeClaims(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 2)
        {
            return null;
        }

        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
        return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
    }

    private static JsonElement? Claim(JsonElement? claims, string name) =>
        claims is { } value && value.TryGetProperty(name, out var claim) && claim.ValueKind == JsonValueKind.Object
            ? claim
            : null;

    private static string? GetString(JsonElement? element, string name) =>
        element is { ValueKind: JsonValueKind.Object } value &&
        value.TryGetProperty(name, out var property) &&
        property.ValueKind == JsonValueKind.String &&
        property.GetString() is { Length: > 0 } text
            ? text
            : null;
}
