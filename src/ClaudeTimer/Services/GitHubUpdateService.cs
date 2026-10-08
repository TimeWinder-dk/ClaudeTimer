using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClaudeTimer.Models;

namespace ClaudeTimer.Services;

/// <summary>Finder, henter og installerer nye versioner fra GitHub Releases.</summary>
public sealed class GitHubUpdateService(HttpClient httpClient) : IUpdateService
{
    public const string UpdatedArgument = "--updated";
    private const string LatestReleasePath = "repos/TimeWinder-dk/ClaudeTimer/releases/latest";
    private const string ChecksumAsset = "SHA256SUMS.txt";

    private readonly string _executablePath =
        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "ClaudeTimer.exe");

    public Version CurrentVersion { get; } = Normalize(
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0));

    public InstallKind InstallKind => DetectInstallKind(
        Path.GetDirectoryName(_executablePath) ?? AppContext.BaseDirectory,
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));

    public async Task<ReleaseInfo> GetLatestReleaseAsync(CancellationToken cancellationToken)
    {
        var release = await httpClient.GetFromJsonAsyncSafe<GitHubRelease>(LatestReleasePath, cancellationToken);
        if (release?.TagName is not { } tag || !TryParseVersion(tag, out var version))
        {
            throw new InvalidOperationException("GitHub returnerede ingen gyldig release.");
        }

        var assets = (release.Assets ?? [])
            .Where(asset => asset.Name is not null && asset.BrowserDownloadUrl is not null)
            .Select(asset => new ReleaseAsset(asset.Name!, asset.BrowserDownloadUrl!))
            .ToList();

        return new ReleaseInfo(version, tag, release.HtmlUrl ?? string.Empty, assets);
    }

    public async Task<string> DownloadVerifiedPackageAsync(ReleaseInfo release, CancellationToken cancellationToken)
    {
        var packageName = PackageName(InstallKind, release.Version)
            ?? throw new InvalidOperationException("Denne installation kan ikke opdateres automatisk.");
        var package = release.Assets.FirstOrDefault(asset => asset.Name == packageName)
            ?? throw new InvalidOperationException($"Release {release.Tag} mangler {packageName}.");
        var checksums = release.Assets.FirstOrDefault(asset => asset.Name == ChecksumAsset)
            ?? throw new InvalidOperationException($"Release {release.Tag} mangler {ChecksumAsset}.");

        var sums = await httpClient.GetStringAsync(checksums.DownloadUrl, cancellationToken);
        var expected = FindChecksum(sums, packageName)
            ?? throw new InvalidOperationException($"{ChecksumAsset} har ingen checksum for {packageName}.");

        var directory = Path.Combine(Path.GetTempPath(), "ClaudeTimer-update");
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, packageName);

        await using (var source = await httpClient.GetStreamAsync(package.DownloadUrl, cancellationToken))
        await using (var file = File.Create(target))
        {
            await source.CopyToAsync(file, cancellationToken);
        }

        string actual;
        await using (var file = File.OpenRead(target))
        {
            actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, cancellationToken));
        }

        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(target);
            throw new InvalidOperationException("Den hentede fil matcher ikke SHA256-checksummen – opdatering afbrudt.");
        }

        return target;
    }

    public void StartInstaller(string packagePath)
    {
        var directory = Path.GetDirectoryName(packagePath)!;
        var script = Path.Combine(directory, "install-update.ps1");
        File.WriteAllText(script, InstallerScript);

        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        foreach (var argument in new[]
        {
            "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", script,
            "-ProcessId", Environment.ProcessId.ToString(),
            "-Package", packagePath,
            "-Kind", InstallKind.ToString(),
            "-AppPath", _executablePath,
            "-LogPath", AppLog.FilePath
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process.Start(startInfo);
    }

    internal static InstallKind DetectInstallKind(string appDirectory, string localAppData, string programFiles)
    {
        var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(appDirectory));
        if (File.Exists(Path.Combine(directory, "unins000.exe")))
        {
            return InstallKind.Setup;
        }

        if (directory.StartsWith(Path.TrimEndingDirectorySeparator(programFiles), StringComparison.OrdinalIgnoreCase))
        {
            return InstallKind.MsiAllUsers;
        }

        if (string.Equals(directory, Path.Combine(localAppData, "Programs", "ClaudeTimer"), StringComparison.OrdinalIgnoreCase))
        {
            return InstallKind.MsiPerUser;
        }

        // Enkelt-fil exe'en har ingen separat ClaudeTimer.dll ved siden af sig.
        return File.Exists(Path.Combine(directory, "ClaudeTimer.dll"))
            ? InstallKind.Unknown
            : InstallKind.Portable;
    }

    internal static string? PackageName(InstallKind kind, Version version)
    {
        var v = $"{version.Major}.{version.Minor}.{version.Build}";
        return kind switch
        {
            InstallKind.Setup => $"ClaudeTimer-Setup-{v}.exe",
            InstallKind.MsiPerUser => $"ClaudeTimer-{v}-win-x64.msi",
            InstallKind.MsiAllUsers => $"ClaudeTimer-{v}-win-x64-allusers.msi",
            InstallKind.Portable => $"ClaudeTimer-{v}-win-x64.exe",
            _ => null
        };
    }

    internal static string? FindChecksum(string sums, string fileName)
    {
        // Formatet er "<sha256> *<filnavn>" (eller med to mellemrum i stedet for *).
        foreach (var line in sums.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = line.IndexOf(' ');
            if (separator <= 0)
            {
                continue;
            }

            var name = line[(separator + 1)..].TrimStart(' ', '*');
            if (string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase))
            {
                return line[..separator];
            }
        }

        return null;
    }

    internal static bool TryParseVersion(string tag, out Version version)
    {
        var parsed = Version.TryParse(tag.TrimStart('v', 'V'), out var result);
        version = parsed ? Normalize(result!) : new Version(0, 0, 0);
        return parsed;
    }

    private static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(0, version.Build));

    // Venter på at appen lukker, installerer stille og starter appen igen.
    private const string InstallerScript = """
        param([int]$ProcessId, [string]$Package, [string]$Kind, [string]$AppPath, [string]$LogPath)
        $ErrorActionPreference = 'Continue'
        function Log([string]$Message) {
            try { Add-Content -LiteralPath $LogPath -Value ("{0} INFO  [opdatering] {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff zzz'), $Message) } catch {}
        }
        Log "Venter på at ClaudeTimer (PID $ProcessId) lukker"
        Wait-Process -Id $ProcessId -Timeout 60 -ErrorAction SilentlyContinue
        try {
            $exit = 0
            switch ($Kind) {
                'Setup'       { $exit = (Start-Process -FilePath $Package -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -Wait -PassThru).ExitCode }
                'MsiPerUser'  { $exit = (Start-Process -FilePath 'msiexec.exe' -ArgumentList '/i',"`"$Package`"",'/qn','/norestart' -Wait -PassThru).ExitCode }
                'MsiAllUsers' { $exit = (Start-Process -FilePath 'msiexec.exe' -ArgumentList '/i',"`"$Package`"",'/qn','/norestart' -Verb RunAs -Wait -PassThru).ExitCode }
                'Portable'    { Copy-Item -LiteralPath $Package -Destination $AppPath -Force }
            }
            Log "Installation ($Kind) afsluttet med kode $exit"
        } catch {
            Log "Installation ($Kind) fejlede: $($_.Exception.Message)"
        }
        Start-Process -FilePath $AppPath -ArgumentList '--updated'
        Remove-Item -LiteralPath $Package -Force -ErrorAction SilentlyContinue
        """;

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; init; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; init; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset>? Assets { get; init; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; init; }
    }
}

internal static class HttpClientJsonExtensions
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static async Task<T?> GetFromJsonAsyncSafe<T>(this HttpClient client, string path, CancellationToken cancellationToken)
    {
        await using var stream = await client.GetStreamAsync(path, cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, Options, cancellationToken);
    }
}
