using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using ClaudeTimer.Models;
using ClaudeTimer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClaudeTimer.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);
    private static readonly TokenSource[] AllSources =
        [TokenSource.Manual, TokenSource.ClaudeCode, TokenSource.ClaudeDesktop];

    private readonly IClaudeUsageClient _usageClient;
    private readonly ITokenStore _tokenStore;
    private readonly IClaudeCodeCredentialReader _credentialReader;
    private readonly IClaudeDesktopCredentialReader _desktopReader;
    private readonly ISettingsStore _settingsStore;
    private readonly IStartupManager _startupManager;
    private readonly IClock _clock;
    private readonly DispatcherTimer _countdownTimer;
    private readonly DispatcherTimer _refreshTimer;
    private readonly AppSettings _settings;

    // Profil pr. token, så vi ikke slår samme token op ved hver opdatering.
    private readonly Dictionary<string, ClaudeAccount> _profileCache = new(StringComparer.Ordinal);
    private bool _isInitialized;
    private bool _isRevertingSetting;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveTokenCommand))]
    private string _tokenInput = string.Empty;

    [ObservableProperty]
    private bool _isSettingsVisible;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "Klargør ClaudeTimer…";

    [ObservableProperty]
    private AccountViewModel? _selectedAccount;

    [ObservableProperty]
    private string _trayText = "ClaudeTimer";

    [ObservableProperty]
    private TokenSource _selectedTokenSource;

    [ObservableProperty]
    private string _claudeCodeCredentialStatus = string.Empty;

    [ObservableProperty]
    private string _claudeDesktopCredentialStatus = string.Empty;

    [ObservableProperty]
    private string _manualTokenStatus = string.Empty;

    [ObservableProperty]
    private string _accountsSummaryText = string.Empty;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private bool _startHiddenInTray;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRestartElevated))]
    [NotifyCanExecuteChangedFor(nameof(RestartElevatedCommand))]
    private bool _runAsAdministrator;

    [ObservableProperty]
    private string? _settingsMessage;

    /// <summary>Én post pr. særskilt konto; vises som faner når der er flere.</summary>
    public ObservableCollection<AccountViewModel> Accounts { get; } = new();

    public bool HasAccounts => Accounts.Count > 0;

    public bool ShowTabs => Accounts.Count > 1;

    public IAsyncRelayCommand RefreshCommand { get; }

    public bool IsElevated => _startupManager.IsElevated;

    public string ElevationStatusText => IsElevated
        ? "Kører lige nu som administrator."
        : "Kører lige nu uden administrator-rettigheder.";

    public bool CanRestartElevated => RunAsAdministrator && !IsElevated;

    /// <summary>Om der ikke er fundet noget login, så indstillingerne skal vises.</summary>
    public bool NeedsToken => Accounts.Count == 0;

    public bool StartHiddenOnAutostart => _settings.StartHiddenInTray;

    /// <summary>Udløses når appen skal lukke, fx efter genstart som administrator.</summary>
    public event EventHandler? ExitRequested;

    public MainViewModel(
        IClaudeUsageClient usageClient,
        ITokenStore tokenStore,
        IClaudeCodeCredentialReader credentialReader,
        IClaudeDesktopCredentialReader desktopReader,
        ISettingsStore settingsStore,
        IStartupManager startupManager,
        IClock clock)
    {
        _usageClient = usageClient;
        _tokenStore = tokenStore;
        _credentialReader = credentialReader;
        _desktopReader = desktopReader;
        _settingsStore = settingsStore;
        _startupManager = startupManager;
        _clock = clock;

        _settings = settingsStore.Load();
        Revert(() =>
        {
            SelectedTokenSource = _settings.TokenSource;
            StartWithWindows = _settings.StartWithWindows;
            StartHiddenInTray = _settings.StartHiddenInTray;
            RunAsAdministrator = _settings.RunAsAdministrator;
        });

        Accounts.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasAccounts));
            OnPropertyChanged(nameof(ShowTabs));
            OnPropertyChanged(nameof(NeedsToken));
        };

        _countdownTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _countdownTimer.Tick += (_, _) => UpdateCountdowns();

        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = RefreshInterval
        };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
    }

    partial void OnIsBusyChanged(bool value) => RefreshCommand.NotifyCanExecuteChanged();

    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        _countdownTimer.Start();
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        TokenInput = string.Empty;
        SettingsMessage = null;
        await ReadAllTokensAsync();
        IsSettingsVisible = true;
    }

    [RelayCommand]
    private void CloseSettings()
    {
        TokenInput = string.Empty;
        SettingsMessage = null;
        IsSettingsVisible = false;
    }

    private bool CanSaveToken() => !IsBusy && !string.IsNullOrWhiteSpace(TokenInput);

    [RelayCommand(CanExecute = nameof(CanSaveToken))]
    private async Task SaveTokenAsync()
    {
        var normalized = NormalizeToken(TokenInput);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            SettingsMessage = "Indsæt et gyldigt OAuth-token.";
            return;
        }

        await _tokenStore.SaveAsync(normalized);
        TokenInput = string.Empty;

        // Et manuelt token bruges ikke, hvis kilden er låst til en af apps'ene.
        if (SelectedTokenSource is TokenSource.ClaudeCode or TokenSource.ClaudeDesktop)
        {
            Revert(() => SelectedTokenSource = TokenSource.Manual);
            _settings.TokenSource = TokenSource.Manual;
            SaveSettings();
        }

        await RefreshAsync();
        SettingsMessage ??= "Token gemt.";
    }

    [RelayCommand]
    private async Task ForgetTokenAsync()
    {
        await _tokenStore.ClearAsync();
        await RefreshAsync();
        SettingsMessage ??= "Gemt token er slettet.";
    }

    private bool CanRestartElevatedNow() => CanRestartElevated;

    [RelayCommand(CanExecute = nameof(CanRestartElevatedNow))]
    private void RestartElevated()
    {
        if (_startupManager.TryRelaunchElevated([]))
        {
            ExitRequested?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            SettingsMessage = "Genstart som administrator blev annulleret.";
        }
    }

    partial void OnSelectedTokenSourceChanged(TokenSource value)
    {
        if (_isRevertingSetting)
        {
            return;
        }

        _settings.TokenSource = value;
        SaveSettings();
        _ = RefreshAsync();
    }

    partial void OnStartHiddenInTrayChanged(bool value)
    {
        if (_isRevertingSetting)
        {
            return;
        }

        _settings.StartHiddenInTray = value;
        SaveSettings();
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_isRevertingSetting)
        {
            return;
        }

        if (!ApplyStartup(value, RunAsAdministrator))
        {
            Revert(() => StartWithWindows = !value);
            return;
        }

        _settings.StartWithWindows = value;
        SaveSettings();
    }

    partial void OnRunAsAdministratorChanged(bool value)
    {
        if (_isRevertingSetting)
        {
            return;
        }

        // Opstartsregistreringen skifter mellem Run-nøgle og planlagt opgave.
        if (StartWithWindows && !ApplyStartup(true, value))
        {
            Revert(() => RunAsAdministrator = !value);
            return;
        }

        _settings.RunAsAdministrator = value;
        SaveSettings();
        SettingsMessage = value && !IsElevated
            ? "Træder i kraft ved næste start – eller genstart nu."
            : null;
    }

    private bool ApplyStartup(bool startWithWindows, bool elevated)
    {
        SettingsMessage = null;
        bool applied;
        try
        {
            applied = _startupManager.Apply(startWithWindows, elevated);
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            applied = false;
        }

        if (!applied)
        {
            SettingsMessage = "Kunne ikke ændre automatisk start (administrator-godkendelse afvist eller fejlet).";
        }

        return applied;
    }

    private void Revert(Action revert)
    {
        _isRevertingSetting = true;
        try
        {
            revert();
        }
        finally
        {
            _isRevertingSetting = false;
        }
    }

    private void SaveSettings()
    {
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SettingsMessage = "Indstillingerne kunne ikke gemmes.";
        }
    }

    private async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            // Find konti hver gang: tokens fornyes af Claude Code/Desktop, og et
            // login kan være skiftet til en anden konto siden sidst.
            await DiscoverAccountsAsync();
            if (Accounts.Count == 0)
            {
                ShowMissingToken();
                return;
            }

            await Task.WhenAll(Accounts.ToList().Select(account => account.RefreshAsync(_usageClient, _clock)));
            _refreshTimer.Start();
        }
        finally
        {
            IsBusy = false;
            UpdateTrayText();
        }
    }

    private void ShowMissingToken()
    {
        _refreshTimer.Stop();
        StatusText = "Forbind din Claude-konto";
        IsSettingsVisible = true;
    }

    private async Task DiscoverAccountsAsync()
    {
        var tokens = await ReadAllTokensAsync();
        var sources = SelectedTokenSource == TokenSource.Automatic ? AllSources : [SelectedTokenSource];

        var discovered = new List<(string Key, ClaudeAccount? Profile, List<AccountCredential> Credentials)>();
        foreach (var source in sources)
        {
            if (tokens[source] is not { } token)
            {
                continue;
            }

            var profile = await TryGetProfileAsync(token);

            // Uden profil (fx netværksfejl eller afvist token) kan vi ikke vide,
            // hvilken konto tokenet tilhører, så kilden står for sig selv.
            var key = profile?.Key ?? $"source:{source}";
            var group = discovered.FirstOrDefault(entry => entry.Key == key);
            if (group.Credentials is null)
            {
                group = (key, profile, new List<AccountCredential>());
                discovered.Add(group);
            }

            group.Credentials.Add(new AccountCredential(source, token));
        }

        MergeAccounts(discovered);
    }

    private void MergeAccounts(
        IReadOnlyList<(string Key, ClaudeAccount? Profile, List<AccountCredential> Credentials)> discovered)
    {
        for (var index = 0; index < discovered.Count; index++)
        {
            var (key, profile, credentials) = discovered[index];
            var account = Accounts.FirstOrDefault(existing => existing.Key == key);
            if (account is null)
            {
                account = new AccountViewModel(key, string.Empty);
                Accounts.Insert(Math.Min(index, Accounts.Count), account);
            }
            else if (Accounts.IndexOf(account) != index)
            {
                Accounts.Move(Accounts.IndexOf(account), index);
            }

            account.Profile = profile;
            account.Credentials = credentials;
        }

        var liveKeys = discovered.Select(entry => entry.Key).ToHashSet();
        for (var i = Accounts.Count - 1; i >= 0; i--)
        {
            if (!liveKeys.Contains(Accounts[i].Key))
            {
                Accounts.RemoveAt(i);
            }
        }

        ApplyAccountNames(Accounts);

        if (SelectedAccount is null || !Accounts.Contains(SelectedAccount))
        {
            SelectedAccount = Accounts.FirstOrDefault();
        }

        AccountsSummaryText = Accounts.Count switch
        {
            0 => "Ingen konti fundet.",
            1 => $"1 konto: {Accounts[0].DisplayName} ({Accounts[0].Subtitle})",
            _ => $"{Accounts.Count} forskellige konti – vises i hver sin fane."
        };
    }

    internal static void ApplyAccountNames(IReadOnlyList<AccountViewModel> accounts)
    {
        // Samme e-mail i flere organisationer skelnes på organisationsnavnet.
        var duplicateEmails = accounts
            .Select(account => account.Profile?.Email)
            .Where(email => email is not null)
            .GroupBy(email => email, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var account in accounts)
        {
            var sourceNames = string.Join(", ", account.Credentials.Select(credential => SourceLabel(credential.Source)));
            var profile = account.Profile;
            if (profile is null)
            {
                account.DisplayName = sourceNames;
                account.ShortName = sourceNames;
                account.Subtitle = "konto ukendt";
                continue;
            }

            var email = profile.Email ?? profile.DisplayName ?? profile.AccountId;
            var isDuplicate = profile.Email is not null && duplicateEmails.Contains(profile.Email);
            account.DisplayName = isDuplicate && profile.OrganizationName is { Length: > 0 } org
                ? $"{email} · {org}"
                : email;
            account.ShortName = isDuplicate && profile.OrganizationName is { Length: > 0 } organization
                ? organization
                : email.Split('@')[0];
            account.Subtitle = profile.OrganizationName is { Length: > 0 } orgName
                ? $"{orgName} · {sourceNames}"
                : sourceNames;
        }
    }

    internal static string SourceLabel(TokenSource source) => source switch
    {
        TokenSource.Manual => "Manuelt token",
        TokenSource.ClaudeCode => "Claude Code",
        TokenSource.ClaudeDesktop => "Claude Desktop",
        _ => source.ToString()
    };

    private async Task<ClaudeAccount?> TryGetProfileAsync(string token)
    {
        if (_profileCache.TryGetValue(token, out var cached))
        {
            return cached;
        }

        try
        {
            var profile = await _usageClient.GetProfileAsync(token, CancellationToken.None);
            if (_profileCache.Count > 16)
            {
                _profileCache.Clear();
            }

            _profileCache[token] = profile;
            return profile;
        }
        catch (ClaudeUsageException)
        {
            return null;
        }
    }

    private async Task<Dictionary<TokenSource, string?>> ReadAllTokensAsync()
    {
        var manual = Clean(await _tokenStore.LoadAsync());
        var claudeCode = Clean(await _credentialReader.TryReadAccessTokenAsync());
        var desktop = Clean(await _desktopReader.TryReadAccessTokenAsync());

        ManualTokenStatus = manual is not null
            ? "Manuelt token: gemt (krypteret med Windows DPAPI)."
            : "Manuelt token: intet gemt.";
        ClaudeCodeCredentialStatus = claudeCode is not null
            ? $"Claude Code: fundet ({_credentialReader.CredentialPath})."
            : $"Claude Code: intet gyldigt login ({_credentialReader.CredentialPath}). Log ind i terminalen eller VS Code.";
        ClaudeDesktopCredentialStatus = desktop is not null
            ? "Claude Desktop: fundet."
            : _desktopReader.DataDirectory is null
                ? "Claude Desktop: appen er ikke fundet."
                : "Claude Desktop: intet gyldigt login. Åbn appen og log ind.";

        return new Dictionary<TokenSource, string?>
        {
            [TokenSource.Manual] = manual,
            [TokenSource.ClaudeCode] = claudeCode,
            [TokenSource.ClaudeDesktop] = desktop
        };

        static string? Clean(string? token) => string.IsNullOrWhiteSpace(token) ? null : token;
    }

    private void UpdateCountdowns()
    {
        var now = _clock.Now;
        foreach (var account in Accounts)
        {
            if (account.Tick(now))
            {
                _ = account.RefreshAsync(_usageClient, _clock);
            }
        }

        UpdateTrayText();
    }

    private void UpdateTrayText() => TrayText = TrayTextBuilder.Build(Accounts);

    internal static string NormalizeToken(string token)
    {
        var trimmed = token.Trim();
        return trimmed.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? trimmed[7..].Trim()
            : trimmed;
    }

    public void Dispose()
    {
        _countdownTimer.Stop();
        _refreshTimer.Stop();
    }
}
