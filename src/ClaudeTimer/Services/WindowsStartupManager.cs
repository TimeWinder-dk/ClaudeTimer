using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace ClaudeTimer.Services;

public sealed class WindowsStartupManager : IStartupManager
{
    public const string AutostartArgument = "--autostart";

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppKeyPath = @"Software\ClaudeTimer";
    private const string TaskMarkerValue = "ElevatedStartupTask";
    private const string AppName = "ClaudeTimer";
    private const int ErrorCancelled = 1223;

    private static readonly TimeSpan ElevatedCommandTimeout = TimeSpan.FromSeconds(60);

    private readonly string _executablePath =
        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "ClaudeTimer.exe");

    public bool IsElevated { get; } = DetectElevation();

    public bool Apply(bool startWithWindows, bool elevated)
    {
        if (startWithWindows && elevated)
        {
            if (!RegisterElevatedTask())
            {
                return false;
            }

            RemoveRunKey();
            return true;
        }

        // Fjern den planlagte opgave først: kræver UAC, og afviser brugeren den,
        // lader vi den eksisterende registrering være uændret.
        if (!RemoveElevatedTask())
        {
            return false;
        }

        if (startWithWindows)
        {
            SetRunKey();
        }
        else
        {
            RemoveRunKey();
        }

        return true;
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

    internal static string BuildTaskXml(string executablePath, string userId)
    {
        var command = SecurityElement.Escape(executablePath);
        var workingDirectory = SecurityElement.Escape(Path.GetDirectoryName(executablePath) ?? string.Empty);
        var user = SecurityElement.Escape(userId);

        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>Starter ClaudeTimer som administrator ved logon.</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{user}</UserId>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{user}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
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

    private bool RegisterElevatedTask()
    {
        var xmlPath = Path.Combine(Path.GetTempPath(), $"ClaudeTimer-task-{Guid.NewGuid():N}.xml");
        try
        {
            var userId = WindowsIdentity.GetCurrent().Name;
            File.WriteAllText(xmlPath, BuildTaskXml(_executablePath, userId), Encoding.Unicode);

            if (!RunSchtasks(["/Create", "/TN", AppName, "/XML", xmlPath, "/F"]))
            {
                return false;
            }

            using var key = Registry.CurrentUser.CreateSubKey(AppKeyPath);
            key.SetValue(TaskMarkerValue, 1, RegistryValueKind.DWord);
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

    private bool RemoveElevatedTask()
    {
        using var key = Registry.CurrentUser.OpenSubKey(AppKeyPath, writable: true);
        if (key?.GetValue(TaskMarkerValue) is null)
        {
            return true;
        }

        if (!RunSchtasks(["/Delete", "/TN", AppName, "/F"]))
        {
            return false;
        }

        key.DeleteValue(TaskMarkerValue, throwOnMissingValue: false);
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

    private bool RunSchtasks(IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo("schtasks.exe")
        {
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (!IsElevated)
        {
            // Opgaver med højeste rettigheder kan kun oprettes/slettes elevated.
            startInfo.UseShellExecute = true;
            startInfo.Verb = "runas";
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null || !process.WaitForExit(ElevatedCommandTimeout))
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
