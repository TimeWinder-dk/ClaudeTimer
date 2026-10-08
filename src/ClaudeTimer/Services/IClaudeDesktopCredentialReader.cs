namespace ClaudeTimer.Services;

public interface IClaudeDesktopCredentialReader
{
    /// <summary>Claude Desktops datamappe, eller null hvis appen ikke er fundet.</summary>
    string? DataDirectory { get; }

    Task<string?> TryReadAccessTokenAsync(CancellationToken cancellationToken = default);
}
