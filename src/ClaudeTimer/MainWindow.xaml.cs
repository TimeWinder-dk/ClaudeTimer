using System.Windows;
using System.Windows.Input;
using ClaudeTimer.ViewModels;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using System.IO;

namespace ClaudeTimer;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly Forms.NotifyIcon _notifyIcon;
    private bool _isExitRequested;
    private bool _hasShownTrayTip;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        Loaded += OnLoaded;
        Closing += OnClosing;
        StateChanged += OnStateChanged;
        Closed += OnClosed;

        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "ClaudeTimer",
            Icon = LoadTrayIcon(),
            Visible = true,
            ContextMenuStrip = BuildTrayMenu()
        };
        _notifyIcon.DoubleClick += (_, _) => ShowFromTray(refreshAfterShow: true);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await _viewModel.InitializeAsync();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            return;
        }

        DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void HideToTray_Click(object sender, RoutedEventArgs e) => HideToTray();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private Forms.ContextMenuStrip BuildTrayMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Vis ClaudeTimer", null, (_, _) => ShowFromTray());
        menu.Items.Add("Skjul til bakke", null, (_, _) => HideToTray());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Afslut", null, (_, _) => ExitApplication());
        return menu;
    }

    private static Drawing.Icon LoadTrayIcon()
    {
        var resourceUri = new Uri("pack://application:,,,/Logo/Logo.ico", UriKind.Absolute);
        var resource = System.Windows.Application.GetResourceStream(resourceUri);
        if (resource is null)
        {
            return Drawing.SystemIcons.Application;
        }

        using var source = resource.Stream;
        using var copy = new MemoryStream();
        source.CopyTo(copy);
        copy.Position = 0;
        return new Drawing.Icon(copy);
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            HideToTray();
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isExitRequested)
        {
            return;
        }

        e.Cancel = true;
        HideToTray();
    }

    private void HideToTray()
    {
        if (!IsVisible)
        {
            return;
        }

        Hide();
        ShowInTaskbar = false;

        if (_hasShownTrayTip)
        {
            return;
        }

        _notifyIcon.BalloonTipTitle = "ClaudeTimer";
        _notifyIcon.BalloonTipText = "Kører i systembakken ved uret.";
        _notifyIcon.ShowBalloonTip(2500);
        _hasShownTrayTip = true;
    }

    private void ShowFromTray(bool refreshAfterShow = false)
    {
        if (!IsVisible)
        {
            Show();
        }

        ShowInTaskbar = true;
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();

        if (refreshAfterShow && _viewModel.RefreshCommand.CanExecute(null))
        {
            _ = _viewModel.RefreshCommand.ExecuteAsync(null);
        }
    }

    private void ExitApplication()
    {
        _isExitRequested = true;
        _notifyIcon.Visible = false;
        Close();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
