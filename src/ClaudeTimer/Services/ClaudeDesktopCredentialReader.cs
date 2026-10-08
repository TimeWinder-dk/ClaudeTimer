using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClaudeTimer.Services;

/// <summary>
/// Læser OAuth-tokenet fra Claude Desktop-appen. Appen (Electron) gemmer sin
/// token-cache i config.json krypteret med safeStorage: "v10" + AES-256-GCM,
/// hvor nøglen ligger i "Local State" beskyttet med DPAPI for den aktuelle bruger.
/// </summary>
public sealed class ClaudeDesktopCredentialReader : IClaudeDesktopCredentialReader
{
    private const string RequiredScope = "user:profile";
    private static readonly string[] TokenCacheKeys = ["oauth:tokenCacheV2", "oauth:tokenCache"];
    private static readonly byte[] DpapiPrefix = "DPAPI"u8.ToArray();
    private static readonly byte[] SafeStoragePrefix = "v10"u8.ToArray();
    private const int NonceLength = 12;
    private const int TagLength = 16;

    public string? DataDirectory { get; } = FindDataDirectory();

    public async Task<string?> TryReadAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (DataDirectory is null)
        {
            return null;
        }

        try
        {
            var localState = await File.ReadAllTextAsync(Path.Combine(DataDirectory, "Local State"), cancellationToken);
            var config = await File.ReadAllTextAsync(Path.Combine(DataDirectory, "config.json"), cancellationToken);
            var key = ReadMasterKey(localState);
            if (key is null)
            {
                return null;
            }

            using var configDocument = JsonDocument.Parse(config);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            foreach (var cacheKey in TokenCacheKeys)
            {
                if (!configDocument.RootElement.TryGetProperty(cacheKey, out var encrypted) ||
                    encrypted.GetString() is not { } encryptedValue)
                {
                    continue;
                }

                var token = SelectToken(Decrypt(key, encryptedValue), now);
                if (token is not null)
                {
                    return token;
                }
            }

            return null;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or CryptographicException
            or FormatException
            or InvalidOperationException)
        {
            return null;
        }
    }

    internal static byte[]? ReadMasterKey(string localStateJson)
    {
        using var document = JsonDocument.Parse(localStateJson);
        if (!document.RootElement.TryGetProperty("os_crypt", out var osCrypt) ||
            !osCrypt.TryGetProperty("encrypted_key", out var encryptedKey) ||
            encryptedKey.GetString() is not { } value)
        {
            return null;
        }

        var protectedKey = Convert.FromBase64String(value);
        if (!protectedKey.AsSpan().StartsWith(DpapiPrefix))
        {
            return null;
        }

        return ProtectedData.Unprotect(
            protectedKey[DpapiPrefix.Length..],
            optionalEntropy: null,
            DataProtectionScope.CurrentUser);
    }

    internal static string Decrypt(byte[] key, string base64Value)
    {
        var data = Convert.FromBase64String(base64Value);
        if (data.Length < SafeStoragePrefix.Length + NonceLength + TagLength ||
            !data.AsSpan().StartsWith(SafeStoragePrefix))
        {
            throw new CryptographicException("Ukendt safeStorage-format.");
        }

        var nonce = data.AsSpan(SafeStoragePrefix.Length, NonceLength);
        var cipherStart = SafeStoragePrefix.Length + NonceLength;
        var cipher = data.AsSpan(cipherStart, data.Length - cipherStart - TagLength);
        var tag = data.AsSpan(data.Length - TagLength);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(key, TagLength);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }

    /// <summary>
    /// Token-cachen er et objekt med en nøgle pr. token, hvor nøglen ender med
    /// tokenets scopes. Vælg det gyldige token med user:profile, der udløber senest.
    /// </summary>
    internal static string? SelectToken(string tokenCacheJson, long nowUnixMs)
    {
        using var document = JsonDocument.Parse(tokenCacheJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string? best = null;
        long bestExpiry = long.MinValue;
        foreach (var entry in document.RootElement.EnumerateObject())
        {
            if (!HasScope(entry.Name, RequiredScope) ||
                entry.Value.ValueKind != JsonValueKind.Object ||
                !entry.Value.TryGetProperty("token", out var token) ||
                token.GetString() is not { Length: > 0 } tokenValue)
            {
                continue;
            }

            var expiresAt = entry.Value.TryGetProperty("expiresAt", out var expiry) && expiry.TryGetInt64(out var ms)
                ? ms
                : long.MaxValue;
            if (expiresAt <= nowUnixMs || expiresAt <= bestExpiry)
            {
                continue;
            }

            best = tokenValue;
            bestExpiry = expiresAt;
        }

        return best;
    }

    // Nøgleformat: "acct:<konto>|<klient>:<org>:https://api.anthropic.com:<scope> <scope>:"
    private static bool HasScope(string cacheKey, string scope)
    {
        const string Marker = "https://api.anthropic.com:";
        var index = cacheKey.IndexOf(Marker, StringComparison.Ordinal);
        if (index < 0)
        {
            return cacheKey.Contains(scope, StringComparison.Ordinal);
        }

        return cacheKey[(index + Marker.Length)..]
            .TrimEnd(':')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains(scope, StringComparer.Ordinal);
    }

    private static string? FindDataDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        // MSIX-installationen (Microsoft Store / winget) virtualiserer %APPDATA%.
        var packages = Path.Combine(localAppData, "Packages");
        if (Directory.Exists(packages))
        {
            foreach (var package in Directory.EnumerateDirectories(packages, "Claude_*"))
            {
                var candidate = Path.Combine(package, "LocalCache", "Roaming", "Claude");
                if (File.Exists(Path.Combine(candidate, "config.json")))
                {
                    return candidate;
                }
            }
        }

        var classic = Path.Combine(roaming, "Claude");
        return File.Exists(Path.Combine(classic, "config.json")) ? classic : null;
    }
}
