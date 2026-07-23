using System.Windows.Threading;
using ClaudeTimer.Models;
using ClaudeTimer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClaudeTimer.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);

    private readonly IClaudeUsageClient _usageClient;
    private readonly ITokenStore _tokenStore;
    private readonly IClaudeCodeCredentialReader _credentialReader;
    private readonly IClock _clock;
    private readonly DispatcherTimer _countdownTimer;
    private readonly DispatcherTimer _refreshTimer;
    private string? _token;
    private bool _usesClaudeCodeCredential;
    private bool _isInitialized;
    private DateTimeOffset? _lastBoundaryRefresh;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveTokenCommand))]
    private string _tokenInput = string.Empty;

    [ObservableProperty]
    private bool _isTokenEditorVisible;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _hasData;

    [ObservableProperty]
    private string _statusText = "Klargør ClaudeTimer…";

    [ObservableProperty]
    private string _lastUpdatedText = "Ikke opdateret endnu";

    [ObservableProperty]
    private string? _errorText;

    public UsageCardViewModel FiveHour { get; } = new("5 timer", "AKTUEL SESSION");

    public UsageCardViewModel SevenDay { get; } = new("7 døgn", "UGENTLIG GRÆNSE");

    public IAsyncRelayCommand RefreshCommand { get; }

    public MainViewModel(
        IClaudeUsageClient usageClient,
        ITokenStore tokenStore,
        IClaudeCodeCredentialReader credentialReader,
        IClock clock)
    {
        _usageClient = usageClient;
        _tokenStore = tokenStore;
        _credentialReader = credentialReader;
        _clock = clock;

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

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy && _token is not null);
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
        _token = await _tokenStore.LoadAsync();
        if (string.IsNullOrWhiteSpace(_token))
        {
            _token = await _credentialReader.TryReadAccessTokenAsync();
            _usesClaudeCodeCredential = !string.IsNullOrWhiteSpace(_token);
        }

        if (string.IsNullOrWhiteSpace(_token))
        {
            StatusText = "Forbind din Claude-konto";
            IsTokenEditorVisible = true;
            return;
        }

        await RefreshAsync();
    }

    [RelayCommand]
    private void ShowTokenEditor()
    {
        TokenInput = string.Empty;
        ErrorText = null;
        IsTokenEditorVisible = true;
    }

    [RelayCommand]
    private void CancelTokenEditor()
    {
        TokenInput = string.Empty;
        ErrorText = null;
        IsTokenEditorVisible = _token is null;
    }

    private bool CanSaveToken() => !IsBusy && !string.IsNullOrWhiteSpace(TokenInput);

    [RelayCommand(CanExecute = nameof(CanSaveToken))]
    private async Task SaveTokenAsync()
    {
        var normalized = NormalizeToken(TokenInput);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            ErrorText = "Indsæt et gyldigt OAuth-token.";
            return;
        }

        await _tokenStore.SaveAsync(normalized);
        _token = normalized;
        _usesClaudeCodeCredential = false;
        TokenInput = string.Empty;
        IsTokenEditorVisible = false;
        RefreshCommand.NotifyCanExecuteChanged();
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task ForgetTokenAsync()
    {
        await _tokenStore.ClearAsync();
        _token = null;
        _usesClaudeCodeCredential = false;
        HasData = false;
        IsTokenEditorVisible = true;
        StatusText = "Forbind din Claude-konto";
        LastUpdatedText = "Ikke opdateret endnu";
        ErrorText = null;
        RefreshCommand.NotifyCanExecuteChanged();
    }

    private async Task RefreshAsync()
    {
        if (_token is null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorText = null;
        StatusText = HasData ? "Opdaterer…" : "Henter forbrug…";

        try
        {
            if (_usesClaudeCodeCredential)
            {
                _token = await _credentialReader.TryReadAccessTokenAsync() ?? _token;
            }

            var usage = await _usageClient.GetUsageAsync(_token, CancellationToken.None);
            Apply(usage);
            HasData = usage.FiveHour is not null || usage.SevenDay is not null;
            StatusText = HasData ? "Forbruget er opdateret" : "Ingen forbrugsgrænser fundet";
            LastUpdatedText = $"Opdateret {_clock.Now:HH:mm:ss}";
            _refreshTimer.Start();
        }
        catch (ClaudeUsageException exception)
        {
            ErrorText = exception.Message;
            StatusText = HasData ? "Viser senest hentede data" : "Kunne ikke hente forbruget";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Apply(ClaudeUsage usage)
    {
        if (usage.FiveHour is { } fiveHour)
        {
            FiveHour.Update(fiveHour.Utilization, fiveHour.ResetsAt);
        }

        if (usage.SevenDay is { } sevenDay)
        {
            SevenDay.Update(sevenDay.Utilization, sevenDay.ResetsAt);
        }

        if (_lastBoundaryRefresh != FiveHour.ResetsAt &&
            _lastBoundaryRefresh != SevenDay.ResetsAt)
        {
            _lastBoundaryRefresh = null;
        }

        UpdateCountdowns();
    }

    private void UpdateCountdowns()
    {
        var now = _clock.Now;
        FiveHour.Tick(now);
        SevenDay.Tick(now);

        var reachedBoundary = new[] { FiveHour.ResetsAt, SevenDay.ResetsAt }
            .Where(value => value is not null && value <= now)
            .Max();

        if (reachedBoundary is not null && reachedBoundary != _lastBoundaryRefresh)
        {
            _lastBoundaryRefresh = reachedBoundary;
            _ = RefreshAsync();
        }
    }

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
