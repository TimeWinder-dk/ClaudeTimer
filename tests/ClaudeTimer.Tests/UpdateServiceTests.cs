using System.IO;
using ClaudeTimer.Models;
using ClaudeTimer.Services;

namespace ClaudeTimer.Tests;

public sealed class UpdateServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ClaudeTimerUpdate-{Guid.NewGuid():N}");

    [Theory]
    [InlineData("v1.4.0", 1, 4, 0)]
    [InlineData("1.10.2", 1, 10, 2)]
    [InlineData("v2.0", 2, 0, 0)]
    public void TryParseVersion_ReadsTags(string tag, int major, int minor, int build)
    {
        Assert.True(GitHubUpdateService.TryParseVersion(tag, out var version));
        Assert.Equal(new Version(major, minor, build), version);
    }

    [Fact]
    public void TryParseVersion_RejectsGarbage() =>
        Assert.False(GitHubUpdateService.TryParseVersion("latest", out _));

    [Fact]
    public void FindChecksum_MatchesBothChecksumFormats()
    {
        var sums = "aaa *ClaudeTimer-Setup-1.4.0.exe\r\nbbb  ClaudeTimer-1.4.0-win-x64.msi\n";

        Assert.Equal("aaa", GitHubUpdateService.FindChecksum(sums, "ClaudeTimer-Setup-1.4.0.exe"));
        Assert.Equal("bbb", GitHubUpdateService.FindChecksum(sums, "ClaudeTimer-1.4.0-win-x64.msi"));
        Assert.Null(GitHubUpdateService.FindChecksum(sums, "ClaudeTimer-1.4.0-win-x64.zip"));
    }

    [Theory]
    [InlineData(InstallKind.Setup, "ClaudeTimer-Setup-1.4.0.exe")]
    [InlineData(InstallKind.MsiPerUser, "ClaudeTimer-1.4.0-win-x64.msi")]
    [InlineData(InstallKind.MsiAllUsers, "ClaudeTimer-1.4.0-win-x64-allusers.msi")]
    [InlineData(InstallKind.Portable, "ClaudeTimer-1.4.0-win-x64.exe")]
    [InlineData(InstallKind.Unknown, null)]
    public void PackageName_MatchesReleaseAssets(InstallKind kind, string? expected) =>
        Assert.Equal(expected, GitHubUpdateService.PackageName(kind, new Version(1, 4, 0)));

    [Fact]
    public void DetectInstallKind_RecognisesEachLayout()
    {
        var localAppData = Path.Combine(_root, "Local");
        var programFiles = Path.Combine(_root, "Program Files");
        var userInstall = Directory.CreateDirectory(Path.Combine(localAppData, "Programs", "ClaudeTimer")).FullName;
        var machineInstall = Directory.CreateDirectory(Path.Combine(programFiles, "ClaudeTimer")).FullName;
        var portable = Directory.CreateDirectory(Path.Combine(_root, "Downloads")).FullName;
        var zipFolder = Directory.CreateDirectory(Path.Combine(_root, "Zip")).FullName;
        File.WriteAllText(Path.Combine(zipFolder, "ClaudeTimer.dll"), string.Empty);

        Assert.Equal(InstallKind.MsiPerUser, GitHubUpdateService.DetectInstallKind(userInstall, localAppData, programFiles));
        Assert.Equal(InstallKind.MsiAllUsers, GitHubUpdateService.DetectInstallKind(machineInstall, localAppData, programFiles));
        Assert.Equal(InstallKind.Portable, GitHubUpdateService.DetectInstallKind(portable, localAppData, programFiles));
        Assert.Equal(InstallKind.Unknown, GitHubUpdateService.DetectInstallKind(zipFolder, localAppData, programFiles));

        File.WriteAllText(Path.Combine(userInstall, "unins000.exe"), string.Empty);
        Assert.Equal(InstallKind.Setup, GitHubUpdateService.DetectInstallKind(userInstall, localAppData, programFiles));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
