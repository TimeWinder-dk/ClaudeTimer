using System.IO;
using System.Xml.Linq;
using ClaudeTimer.Models;
using ClaudeTimer.Services;

namespace ClaudeTimer.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"ClaudeTimerTests-{Guid.NewGuid():N}");

    [Fact]
    public void Load_ReturnsDefaults_WhenFileIsMissing()
    {
        var settings = new JsonSettingsStore(Path.Combine(_directory, "settings.json")).Load();

        Assert.Equal(TokenSource.Automatic, settings.TokenSource);
        Assert.False(settings.StartWithWindows);
        Assert.True(settings.StartHiddenInTray);
        Assert.False(settings.RunAsAdministrator);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsAllValues()
    {
        var store = new JsonSettingsStore(Path.Combine(_directory, "settings.json"));
        store.Save(new AppSettings
        {
            TokenSource = TokenSource.ClaudeCode,
            StartWithWindows = true,
            StartHiddenInTray = false,
            RunAsAdministrator = true
        });

        var loaded = store.Load();

        Assert.Equal(TokenSource.ClaudeCode, loaded.TokenSource);
        Assert.True(loaded.StartWithWindows);
        Assert.False(loaded.StartHiddenInTray);
        Assert.True(loaded.RunAsAdministrator);
    }

    [Fact]
    public void Load_ReturnsDefaults_WhenFileIsCorrupt()
    {
        var path = Path.Combine(_directory, "settings.json");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, "{ not json");

        Assert.Equal(TokenSource.Automatic, new JsonSettingsStore(path).Load().TokenSource);
    }

    [Fact]
    public void BuildTaskXml_IsValidAndRunsElevatedWithoutTimeLimit()
    {
        var xml = WindowsStartupManager.BuildTaskXml(@"C:\Program Files\A&B\ClaudeTimer.exe", @"PC\user");
        var document = XDocument.Parse(xml);
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

        Assert.Equal("HighestAvailable", document.Descendants(ns + "RunLevel").Single().Value);
        Assert.Equal("PT0S", document.Descendants(ns + "ExecutionTimeLimit").Single().Value);
        Assert.Equal("false", document.Descendants(ns + "DisallowStartIfOnBatteries").Single().Value);
        Assert.Equal(@"C:\Program Files\A&B\ClaudeTimer.exe", document.Descendants(ns + "Command").Single().Value);
        Assert.Equal(WindowsStartupManager.AutostartArgument, document.Descendants(ns + "Arguments").Single().Value);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
