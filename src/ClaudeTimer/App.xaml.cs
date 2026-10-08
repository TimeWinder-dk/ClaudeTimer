using System.Net.Http;
using System.Windows;
using ClaudeTimer.Services;
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
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<MainWindow>();
        })
        .Build();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var settings = _host.Services.GetRequiredService<ISettingsStore>().Load();
        var startupManager = _host.Services.GetRequiredService<IStartupManager>();
        if (settings.RunAsAdministrator &&
            !startupManager.IsElevated &&
            startupManager.TryRelaunchElevated(e.Args))
        {
            // Den elevated instans overtager. Afviser brugeren UAC, kører vi videre normalt.
            Shutdown();
            return;
        }

        await _host.StartAsync();
        var window = _host.Services.GetRequiredService<MainWindow>();
        var viewModel = _host.Services.GetRequiredService<MainViewModel>();
        var isAutostart = e.Args.Contains(WindowsStartupManager.AutostartArgument, StringComparer.OrdinalIgnoreCase);

        if (isAutostart && viewModel.StartHiddenOnAutostart)
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
        _host.Services.GetService<MainViewModel>()?.Dispose();
        await _host.StopAsync(TimeSpan.FromSeconds(2));
        _host.Dispose();
        base.OnExit(e);
    }
}
