using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace ClaudeTimer.Services;

/// <summary>
/// Automatisk start via en planlagt opgave med logon-trigger. Den starter appen
/// straks ved login – Run-nøglen kan forsinkes flere minutter af Windows efter en
/// genstart. Uden administrator oprettes opgaven med brugerens egne rettigheder
/// (ingen UAC); med administrator kræver oprettelse og sletning én godkendelse.
/// </summary>
public sealed class WindowsStartupManager : IStartupManager
{
    public const string AutostartArgument = "--autostart";

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppKeyPath = @"Software\ClaudeTimer";
    private const string ElevatedTaskMarker = "ElevatedStartupTask";
    private const string AppName = "ClaudeTimer";
    private const int ErrorCancelled = 1223;

    private static readonly TimeSpan SchtasksTimeout = TimeSpan.FromSeconds(60);

    private readonly string _executablePath =
        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "ClaudeTimer.exe");

    public bool IsElevated { get; } = DetectElevation();

    public bool Apply(bool startWithWindows, bool elevated)
    {
        if (!startWithWindows)
        {
            if (!DeleteTask())
            {
                return false;
            }

            RemoveRunKey();
            return true;
        }

        if (elevated)
        {
            if (!RegisterTask(elevated: true))
            {
                return false;
            }

            RemoveRunKey();
            return true;
        }

        // En eksisterende administrator-opgave kan kun overskrives/slettes elevated.
        if (HasElevatedTask && !DeleteTask())
        {
            return false;
        }

        if (RegisterTask(elevated: false))
        {
            RemoveRunKey();
        }
        else
        {
            // Fx hvis Task Scheduler er låst af en politik: Run-nøglen virker altid.
            AppLog.Warn("Planlagt opgave kunne ikke oprettes – bruger Run-nøglen i stedet");
            SetRunKey();
        }

        return true;
    }

    public void EnsureRegistered(bool startWithWindows, bool elevated)
    {
        if (!startWithWindows)
        {
            return;
        }

        // Flyt stille (uden UAC) en ældre Run-nøgle-opsætning over på opgaven. Stien
        // tages fra Run-nøglen, så en test-kørsel fra en anden mappe ikke "kaprer"
        // den installerede apps autostart.
        if (elevated || HasElevatedTask || ReadRunKeyPath() is not { } registeredPath)
        {
            return;
        }

        if (RegisterTask(elevated: false, registeredPath))
        {
            RemoveRunKey();
            AppLog.Info("Automatisk start flyttet fra Run-nøglen til en planlagt logon-opgave");
        }
    }

    private static string? ReadRunKeyPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        if (key?.GetValue(AppName) is not string value || value.Length < 3 || value[0] != '"')
        {
            return null;
        }

        var end = value.IndexOf('"', 1);
        var path = end > 1 ? value[1..end] : null;
        return path is not null && File.Exists(path) ? path : null;
    }

    public bool TryRelaunchElevated(IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo(_executablePath)
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = AppContext.BaseDirectory
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            return Process.Start(startInfo) is not null;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == ErrorCancelled)
        {
            return false;
        }
    }

    internal static string BuildTaskXml(string executablePath, string userId, bool elevated)
    {
        var command = SecurityElement.Escape(executablePath);
        var workingDirectory = SecurityElement.Escape(Path.GetDirectoryName(executablePath) ?? string.Empty);
        var user = SecurityElement.Escape(userId);
        var runLevel = elevated ? "HighestAvailable" : "LeastPrivilege";
        var description = elevated
            ? "Starter ClaudeTimer som administrator ved logon."
            : "Starter ClaudeTimer ved logon.";

        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>{description}</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{user}</UserId>
                  <Delay>PT5S</Delay>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{user}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>{runLevel}</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings>
                  <StopOnIdleEnd>false</StopOnIdleEnd>
                  <RestartOnIdle>false</RestartOnIdle>
                </IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <WakeToRun>false</WakeToRun>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>5</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{command}</Command>
                  <Arguments>{AutostartArgument}</Arguments>
                  <WorkingDirectory>{workingDirectory}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    private static bool HasElevatedTask
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(AppKeyPath);
            return key?.GetValue(ElevatedTaskMarker) is not null;
        }
    }

    private bool RegisterTask(bool elevated, string? executablePath = null)
    {
        var xmlPath = Path.Combine(Path.GetTempPath(), $"ClaudeTimer-task-{Guid.NewGuid():N}.xml");
        try
        {
            var userId = WindowsIdentity.GetCurrent().Name;
            File.WriteAllText(xmlPath, BuildTaskXml(executablePath ?? _executablePath, userId, elevated), Encoding.Unicode);

            if (!RunSchtasks(["/Create", "/TN", AppName, "/XML", xmlPath, "/F"], requireElevation: elevated))
            {
                AppLog.Warn($"Planlagt opgave kunne ikke oprettes (administrator={elevated})");
                return false;
            }

            using var key = Registry.CurrentUser.CreateSubKey(AppKeyPath);
            if (elevated)
            {
                key.SetValue(ElevatedTaskMarker, 1, RegistryValueKind.DWord);
            }
            else
            {
                key.DeleteValue(ElevatedTaskMarker, throwOnMissingValue: false);
            }

            return true;
        }
        finally
        {
            try
            {
                File.Delete(xmlPath);
            }
            catch (IOException)
            {
            }
        }
    }

    private bool DeleteTask()
    {
        var elevated = HasElevatedTask;
        if (!RunSchtasks(["/Query", "/TN", AppName], requireElevation: false) && !elevated)
        {
            return true;
        }

        if (!RunSchtasks(["/Delete", "/TN", AppName, "/F"], requireElevation: elevated))
        {
            return false;
        }

        using var key = Registry.CurrentUser.OpenSubKey(AppKeyPath, writable: true);
        key?.DeleteValue(ElevatedTaskMarker, throwOnMissingValue: false);
        return true;
    }

    private void SetRunKey()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key.SetValue(AppName, $"\"{_executablePath}\" {AutostartArgument}");
    }

    private static void RemoveRunKey()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(AppName, throwOnMissingValue: false);
    }

    private bool RunSchtasks(IReadOnlyList<string> arguments, bool requireElevation)
    {
        var startInfo = new ProcessStartInfo("schtasks.exe")
        {
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (requireElevation && !IsElevated)
        {
            // Opgaver med højeste rettigheder kan kun oprettes/slettes elevated.
            startInfo.UseShellExecute = true;
            startInfo.RedirectStandardOutput = false;
            startInfo.RedirectStandardError = false;
            startInfo.Verb = "runas";
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null || !process.WaitForExit(SchtasksTimeout))
            {
                return false;
            }

            return process.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private static bool DetectElevation()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
