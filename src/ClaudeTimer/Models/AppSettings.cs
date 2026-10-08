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

public enum AppTheme
{
    System,
    Light,
    Dark
}

public sealed class AppSettings
{
    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>Tegn den mest pressede grænse i bakke-ikonet i stedet for logoet.</summary>
    public bool ShowUsageInTrayIcon { get; set; } = true;

    public bool NotificationsEnabled { get; set; } = true;

    /// <summary>Procentgrænser der giver en notifikation, fx 80 og 95.</summary>
    public List<int> NotifyThresholds { get; set; } = [80, 95];

    public bool NotifyOnReset { get; set; } = true;

    /// <summary>Egne navne til konti, nøglet på konto + organisation.</summary>
    public Dictionary<string, string> AccountAliases { get; set; } = new();

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
