using System.Net.Http;
using System.Windows;
using ClaudeTimer.Services;
using ClaudeTimer.Themes;
using ClaudeTimer.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ClaudeTimer;

public partial class App : System.Windows.Application
{
    private readonly IHost _host = Host.CreateDefaultBuilder()
        .ConfigureServices(services =>
        {
            services.AddSingleton<IClock, SystemClock>();
            services.AddSingleton<ITokenStore, DpapiTokenStore>();
            services.AddSingleton<ISettingsStore, JsonSettingsStore>();
            services.AddSingleton<IStartupManager, WindowsStartupManager>();
            services.AddSingleton<IClaudeCodeCredentialReader, ClaudeCodeCredentialReader>();
            services.AddSingleton<IClaudeDesktopCredentialReader, ClaudeDesktopCredentialReader>();
            services.AddHttpClient<IClaudeUsageClient, ClaudeUsageClient>(client =>
            {
                client.BaseAddress = new Uri("https://api.anthropic.com/");
                client.Timeout = TimeSpan.FromSeconds(15);
                client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
                client.DefaultRequestHeaders.UserAgent.ParseAdd("ClaudeTimer/1.0");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(10)
            });
            services.AddHttpClient<IUpdateService, GitHubUpdateService>(client =>
            {
                client.BaseAddress = new Uri("https://api.github.com/");
                client.Timeout = TimeSpan.FromMinutes(10);
                client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                client.DefaultRequestHeaders.UserAgent.ParseAdd("ClaudeTimer");
            });
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<MainWindow>();
        })
        .Build();

    private SingleInstance? _singleInstance;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Kører ClaudeTimer allerede, vises dens vindue i stedet for en ny instans.
        DispatcherUnhandledException += (_, args) => AppLog.Error("Uhåndteret fejl i UI-tråden", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppLog.Error("Uhåndteret fejl", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) => AppLog.Error("Uobserveret fejl i baggrundsopgave", args.Exception);

        _singleInstance = SingleInstance.TryAcquire();
        if (_singleInstance is null)
        {
            AppLog.Info("En anden instans kører allerede – beder den vise sig");
            Shutdown();
            return;
        }

        var settings = _host.Services.GetRequiredService<ISettingsStore>().Load();
        var startupManager = _host.Services.GetRequiredService<IStartupManager>();
        AppLog.Info($"Start: version {typeof(App).Assembly.GetName().Version}, administrator={startupManager.IsElevated}, argumenter=[{string.Join(' ', e.Args)}]");
        ThemeManager.Apply(settings.Theme);
        if (settings.RunAsAdministrator && !startupManager.IsElevated)
        {
            _singleInstance.Release();
            if (startupManager.TryRelaunchElevated(e.Args))
            {
                // Den elevated instans overtager.
                Shutdown();
                return;
            }

            // UAC afvist: kør videre uden rettigheder, men som eneste instans.
            _singleInstance.Dispose();
            _singleInstance = SingleInstance.TryAcquire();
            if (_singleInstance is null)
            {
                Shutdown();
                return;
            }
        }

        await _host.StartAsync();
        var window = _host.Services.GetRequiredService<MainWindow>();
        _singleInstance.ListenForShowRequests(() => Dispatcher.BeginInvoke(window.ShowFromTrayRequest));
        var viewModel = _host.Services.GetRequiredService<MainViewModel>();
        var isAutostart = e.Args.Contains(WindowsStartupManager.AutostartArgument, StringComparer.OrdinalIgnoreCase);
        var isUpdated = e.Args.Contains(GitHubUpdateService.UpdatedArgument, StringComparer.OrdinalIgnoreCase);

        if (isUpdated)
        {
            // Efter en automatisk opdatering: tilbage i bakken med en kort besked.
            window.ShowTrayNotice($"Opdateret til version {viewModel.Updates.CurrentVersionText}.");
            await viewModel.InitializeAsync();
            if (!viewModel.NeedsToken)
            {
                return;
            }
        }
        else if (isAutostart && viewModel.StartHiddenOnAutostart)
        {
            await viewModel.InitializeAsync();
            if (!viewModel.NeedsToken)
            {
                return;
            }
        }

        window.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        _host.Services.GetService<MainViewModel>()?.Dispose();
        await _host.StopAsync(TimeSpan.FromSeconds(2));
        _host.Dispose();
        base.OnExit(e);
    }
}
