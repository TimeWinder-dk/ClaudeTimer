using System.Security.Cryptography;
using System.Text;
using System.IO;

namespace ClaudeTimer.Services;

public sealed class DpapiTokenStore : ITokenStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ClaudeTimer.Token.v1");
    private readonly string _filePath;

    public DpapiTokenStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClaudeTimer");
        _filePath = Path.Combine(directory, "token.dat");
    }

    public async Task<string?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        try
        {
            var encrypted = await File.ReadAllBytesAsync(_filePath, cancellationToken);
            var clear = ProtectedData.Unprotect(
                encrypted,
                Entropy,
                DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(clear);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public async Task SaveAsync(string token, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(directory);

        var clear = Encoding.UTF8.GetBytes(token);
        var encrypted = ProtectedData.Protect(
            clear,
            Entropy,
            DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(_filePath, encrypted, cancellationToken);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }

        return Task.CompletedTask;
    }
}
