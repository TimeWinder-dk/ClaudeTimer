namespace ClaudeTimer.Models;

public sealed record ReleaseAsset(string Name, string DownloadUrl);

public sealed record ReleaseInfo(Version Version, string Tag, string PageUrl, IReadOnlyList<ReleaseAsset> Assets);

/// <summary>Hvordan den kørende app er installeret – afgør hvilken pakke der opdateres med.</summary>
public enum InstallKind
{
    /// <summary>Udviklings-build eller zip-mappe: kan kun give besked om ny version.</summary>
    Unknown,

    /// <summary>Inno Setup-installeren (ClaudeTimer-Setup-*.exe).</summary>
    Setup,

    /// <summary>Per-bruger MSI.</summary>
    MsiPerUser,

    /// <summary>All-users MSI i Program Files (kræver administrator).</summary>
    MsiAllUsers,

    /// <summary>Selv-indeholdt enkelt-fil exe.</summary>
    Portable
}
