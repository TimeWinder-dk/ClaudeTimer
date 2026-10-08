using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ClaudeTimer.Models;
using ClaudeTimer.Services;
using ClaudeTimer.Themes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClaudeTimer.ViewModels;

/// <summary>Tema, bakke-ikon, notifikationer, kontonavne og log.</summary>
public sealed partial class MainViewModel
{
    [ObservableProperty]
    private AppTheme _selectedTheme;

    [ObservableProperty]
    private bool _showUsageInTrayIcon;

    [ObservableProperty]
    private bool _notificationsEnabled;

    [ObservableProperty]
    private string _notifyThresholdsText = string.Empty;

    [ObservableProperty]
    private bool _notifyOnReset;

    /// <summary>Procent til bakke-ikonet (mest pressede grænse), eller null for logoet.</summary>
    [ObservableProperty]
    private int? _trayIconPercent;

    /// <summary>Udløses med en besked, der skal vises som notifikation fra bakken.</summary>
    public event EventHandler<string>? NotificationRequested;

    private NotificationOptions CurrentNotificationOptions =>
        new(NotificationsEnabled, _settings.NotifyThresholds, NotifyOnReset);

    private void LoadPreferences()
    {
        SelectedTheme = _settings.Theme;
        ShowUsageInTrayIcon = _settings.ShowUsageInTrayIcon;
        NotificationsEnabled = _settings.NotificationsEnabled;
        NotifyOnReset = _settings.NotifyOnReset;
        NotifyThresholdsText = string.Join(", ", _settings.NotifyThresholds);
    }

    partial void OnSelectedThemeChanged(AppTheme value)
    {
        if (_isRevertingSetting)
        {
            return;
        }

        _settings.Theme = value;
        SaveSettings();
        ThemeManager.Apply(value);
    }

    partial void OnShowUsageInTrayIconChanged(bool value)
    {
        if (_isRevertingSetting)
        {
            return;
        }

        _settings.ShowUsageInTrayIcon = value;
        SaveSettings();
        UpdateTrayIcon();
    }

    partial void OnNotificationsEnabledChanged(bool value)
    {
        if (_isRevertingSetting)
        {
            return;
        }

        _settings.NotificationsEnabled = value;
        SaveSettings();
        ApplyNotificationOptions();
    }

    partial void OnNotifyOnResetChanged(bool value)
    {
        if (_isRevertingSetting)
        {
            return;
        }

        _settings.NotifyOnReset = value;
        SaveSettings();
        ApplyNotificationOptions();
    }

    partial void OnNotifyThresholdsTextChanged(string value)
    {
        if (_isRevertingSetting)
        {
            return;
        }

        if (ParseThresholds(value) is not { } thresholds)
        {
            SettingsMessage = "Skriv procenter mellem 1 og 100 adskilt af komma, fx 80, 95.";
            return;
        }

        SettingsMessage = null;
        _settings.NotifyThresholds = thresholds;
        SaveSettings();
        ApplyNotificationOptions();
    }

    /// <summary>"80, 95" → [80, 95]. Null ved ugyldigt input; tom tekst slår tærskler fra.</summary>
    internal static List<int>? ParseThresholds(string text)
    {
        var parts = text.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var values = new List<int>(parts.Length);
        foreach (var part in parts)
        {
            if (!int.TryParse(part.TrimEnd('%'), out var value) || value is < 1 or > 100)
            {
                return null;
            }

            values.Add(value);
        }

        return values.Distinct().Order().ToList();
    }

    [RelayCommand]
    private void OpenLog()
    {
        if (!File.Exists(AppLog.FilePath))
        {
            AppLog.Info("Log oprettet");
        }

        Process.Start(new ProcessStartInfo(AppLog.FilePath) { UseShellExecute = true });
    }

    private void ApplyNotificationOptions()
    {
        foreach (var account in Accounts)
        {
            account.Notifications = CurrentNotificationOptions;
        }
    }

    private void AttachAccount(AccountViewModel account)
    {
        account.Alias = _settings.AccountAliases.GetValueOrDefault(account.Key) ?? string.Empty;
        account.Notifications = CurrentNotificationOptions;
        account.NotificationRaised += OnAccountNotification;
        account.PropertyChanged += OnAccountPropertyChanged;
    }

    private void DetachAccount(AccountViewModel account)
    {
        account.NotificationRaised -= OnAccountNotification;
        account.PropertyChanged -= OnAccountPropertyChanged;
    }

    private void OnAccountNotification(object? sender, string message)
    {
        var text = sender is AccountViewModel account && Accounts.Count > 1
            ? $"{account.ShortName}: {message}"
            : message;
        AppLog.Info("Notifikation vist");
        NotificationRequested?.Invoke(this, text);
    }

    private void OnAccountPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AccountViewModel.Alias) || sender is not AccountViewModel account)
        {
            return;
        }

        var alias = account.Alias.Trim();
        if (alias.Length == 0)
        {
            _settings.AccountAliases.Remove(account.Key);
        }
        else
        {
            _settings.AccountAliases[account.Key] = alias;
        }

        SaveSettings();
        ApplyAccountNames(Accounts);
        UpdateTrayText();
    }

    private void UpdateTrayIcon()
    {
        var highest = Accounts
            .SelectMany(account => account.Cards)
            .Select(card => card.Utilization)
            .DefaultIfEmpty(-1)
            .Max();
        TrayIconPercent = ShowUsageInTrayIcon && highest >= 0 ? (int)Math.Round(highest) : null;
    }
}
