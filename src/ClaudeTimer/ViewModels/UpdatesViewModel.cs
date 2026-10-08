using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows.Threading;
using ClaudeTimer.Models;
using ClaudeTimer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClaudeTimer.ViewModels;

/// <summary>Søger efter nye versioner på GitHub og installerer dem.</summary>
public sealed partial class UpdatesViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private readonly IUpdateService _updateService;
    private readonly AppSettings _settings;
    private readonly Action _saveSettings;
    private readonly DispatcherTimer _timer;
    private ReleaseInfo? _availableRelease;
    private bool _isLoading;

    [ObservableProperty]
    private bool _checkForUpdates;

    [ObservableProperty]
    private bool _autoInstallUpdates;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckNowCommand), nameof(InstallCommand))]
    private bool _isWorking;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    private string _statusText;

    public UpdatesViewModel(IUpdateService updateService, AppSettings settings, Action saveSettings)
    {
        _updateService = updateService;
        _settings = settings;
        _saveSettings = saveSettings;

        _isLoading = true;
        CheckForUpdates = settings.CheckForUpdates;
        AutoInstallUpdates = settings.AutoInstallUpdates;
        _isLoading = false;

        _statusText = $"Du kører version {VersionText(updateService.CurrentVersion)}.";

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = FirstCheckDelay };
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = CheckInterval;
            if (CheckForUpdates)
            {
                await CheckAsync(installAutomatically: AutoInstallUpdates);
            }
        };
    }

    /// <summary>Udløses når en opdatering er sat i gang, og appen skal lukke.</summary>
    public event EventHandler? ExitRequested;

    public string CurrentVersionText => VersionText(_updateService.CurrentVersion);

    public bool CanInstallAutomatically => _updateService.InstallKind != InstallKind.Unknown;

    public void Start() => _timer.Start();

    partial void OnCheckForUpdatesChanged(bool value)
    {
        if (_isLoading)
        {
            return;
        }

        _settings.CheckForUpdates = value;
        _saveSettings();
    }

    partial void OnAutoInstallUpdatesChanged(bool value)
    {
        if (_isLoading)
        {
            return;
        }

        _settings.AutoInstallUpdates = value;
        _saveSettings();
    }

    private bool CanCheckNow() => !IsWorking;

    [RelayCommand(CanExecute = nameof(CanCheckNow))]
    private Task CheckNowAsync() => CheckAsync(installAutomatically: false);

    private bool CanInstall() => !IsWorking && IsUpdateAvailable && CanInstallAutomatically;

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private Task InstallAsync() => _availableRelease is { } release ? InstallReleaseAsync(release) : Task.CompletedTask;

    [RelayCommand]
    private void OpenReleasePage()
    {
        var url = _availableRelease?.PageUrl is { Length: > 0 } page
            ? page
            : "https://github.com/TimeWinder-dk/ClaudeTimer/releases/latest";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private async Task CheckAsync(bool installAutomatically)
    {
        if (IsWorking)
        {
            return;
        }

        IsWorking = true;
        StatusText = "Søger efter opdateringer…";
        try
        {
            var release = await _updateService.GetLatestReleaseAsync(CancellationToken.None);
            if (release.Version <= _updateService.CurrentVersion)
            {
                _availableRelease = null;
                IsUpdateAvailable = false;
                StatusText = $"Du har den nyeste version ({CurrentVersionText}).";
                return;
            }

            _availableRelease = release;
            IsUpdateAvailable = true;
            StatusText = CanInstallAutomatically
                ? $"Version {VersionText(release.Version)} er klar til installation."
                : $"Version {VersionText(release.Version)} er udkommet – hent den fra GitHub.";
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException
            or InvalidOperationException or System.Text.Json.JsonException)
        {
            StatusText = "Kunne ikke søge efter opdateringer (ingen forbindelse til GitHub).";
            return;
        }
        finally
        {
            IsWorking = false;
        }

        if (installAutomatically && CanInstallAutomatically && _availableRelease is { } available)
        {
            await InstallReleaseAsync(available);
        }
    }

    private async Task InstallReleaseAsync(ReleaseInfo release)
    {
        if (IsWorking)
        {
            return;
        }

        IsWorking = true;
        StatusText = $"Henter version {VersionText(release.Version)}…";
        try
        {
            var package = await _updateService.DownloadVerifiedPackageAsync(release, CancellationToken.None);
            StatusText = $"Installerer version {VersionText(release.Version)} – ClaudeTimer genstarter.";
            _updateService.StartInstaller(package);
            ExitRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException
            or InvalidOperationException or IOException or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception)
        {
            StatusText = $"Opdateringen mislykkedes: {exception.Message}";
        }
        finally
        {
            IsWorking = false;
        }
    }

    private static string VersionText(Version version) => $"{version.Major}.{version.Minor}.{version.Build}";

    public void Dispose() => _timer.Stop();
}
