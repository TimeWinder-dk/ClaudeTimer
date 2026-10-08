namespace ClaudeTimer.Models;

public enum TokenSource
{
    /// <summary>Alle fundne logins; forskellige konti vises i hver sin fane.</summary>
    Automatic,

    /// <summary>Kun Claude Codes credentials (delt af CLI og VS Code-udvidelsen).</summary>
    ClaudeCode,

    /// <summary>Kun det manuelt indsatte, DPAPI-krypterede token.</summary>
    Manual,

    /// <summary>Kun Claude Desktop-appens login.</summary>
    ClaudeDesktop
}

public sealed class AppSettings
{
    public TokenSource TokenSource { get; set; } = TokenSource.Automatic;

    /// <summary>
    /// Læs Claude Desktops login ved Automatisk. Fra som standard, da det kræver
    /// DPAPI-dekryptering af en anden apps nøgle, som sikkerhedsværktøjer markerer.
    /// </summary>
    public bool UseClaudeDesktop { get; set; }

    public bool StartWithWindows { get; set; }

    public bool StartHiddenInTray { get; set; } = true;

    public bool RunAsAdministrator { get; set; }

    public bool CheckForUpdates { get; set; } = true;

    public bool AutoInstallUpdates { get; set; } = true;
}
