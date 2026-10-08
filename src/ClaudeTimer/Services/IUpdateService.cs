using ClaudeTimer.Models;

namespace ClaudeTimer.Services;

public interface IUpdateService
{
    Version CurrentVersion { get; }

    InstallKind InstallKind { get; }

    /// <summary>Seneste udgivne (ikke-prerelease) version på GitHub.</summary>
    Task<ReleaseInfo> GetLatestReleaseAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Henter pakken for den aktuelle installationstype og kontrollerer dens SHA256
    /// mod release'ens SHA256SUMS.txt. Returnerer stien til den hentede fil.
    /// </summary>
    Task<string> DownloadVerifiedPackageAsync(ReleaseInfo release, CancellationToken cancellationToken);

    /// <summary>
    /// Starter en lille hjælper, der venter til appen er lukket, installerer
    /// pakken og starter appen igen. Kalderen skal derefter lukke appen.
    /// </summary>
    void StartInstaller(string packagePath);
}
