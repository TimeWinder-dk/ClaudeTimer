using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace ClaudeTimer.Services;

/// <summary>
/// Sikrer at kun én ClaudeTimer kører pr. bruger. En ny start beder den
/// kørende instans vise sit vindue og lukker selv.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\ClaudeTimer.SingleInstance";
    private const string ShowEventName = @"Local\ClaudeTimer.Show";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _registration;
    private bool _ownsMutex;

    private SingleInstance(Mutex mutex, EventWaitHandle? showEvent)
    {
        _mutex = mutex;
        _showEvent = showEvent;
        _ownsMutex = true;
    }

    /// <summary>
    /// Forsøger at blive den kørende instans. Returnerer null, hvis en anden
    /// instans allerede kører; den er så bedt om at vise sit vindue.
    /// </summary>
    public static SingleInstance? TryAcquire()
    {
        Mutex mutex;
        try
        {
            mutex = new Mutex(initiallyOwned: false, MutexName);
        }
        catch (UnauthorizedAccessException)
        {
            // Ejet af en elevated instans uden adgang for os – den kører altså.
            SignalExistingInstance();
            return null;
        }

        bool acquired;
        try
        {
            // Kort ventetid dækker en instans, der netop er ved at lukke
            // (fx ved genstart som administrator eller efter en opdatering).
            acquired = mutex.WaitOne(TimeSpan.FromSeconds(3));
        }
        catch (AbandonedMutexException)
        {
            acquired = true;
        }

        if (!acquired)
        {
            mutex.Dispose();
            SignalExistingInstance();
            return null;
        }

        return new SingleInstance(mutex, TryCreateShowEvent());
    }

    /// <summary>Kalder <paramref name="onShowRequested"/> når en ny start beder om vinduet.</summary>
    public void ListenForShowRequests(Action onShowRequested)
    {
        if (_showEvent is null)
        {
            return;
        }

        _registration = ThreadPool.RegisterWaitForSingleObject(
            _showEvent,
            (_, _) => onShowRequested(),
            state: null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    /// <summary>Slipper låsen før appen starter en afløser (fx elevated).</summary>
    public void Release()
    {
        if (_ownsMutex)
        {
            _mutex.ReleaseMutex();
            _ownsMutex = false;
        }
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        try
        {
            Release();
        }
        catch (ApplicationException)
        {
            // Frigives fra en anden tråd end den der tog den; Windows rydder op ved exit.
        }

        _mutex.Dispose();
        _showEvent?.Dispose();
    }

    /// <summary>
    /// Opretter signalet en ny start bruger til at vække os. Fejler det, kører appen
    /// videre uden – en ny start lukker så bare stille, men opstarten vælter aldrig.
    /// </summary>
    private static EventWaitHandle? TryCreateShowEvent()
    {
        try
        {
            // Standard-DACL'en for en elevated proces giver kun Administrators adgang;
            // giv brugeren selv lov til at signalere.
            var security = new EventWaitHandleSecurity();
            if (WindowsIdentity.GetCurrent().User is { } user)
            {
                security.AddAccessRule(new EventWaitHandleAccessRule(
                    user,
                    EventWaitHandleRights.Modify | EventWaitHandleRights.Synchronize,
                    AccessControlType.Allow));
            }

            var showEvent = EventWaitHandleAcl.Create(false, EventResetMode.AutoReset, ShowEventName, out _, security);
            LowerIntegrityLabel(showEvent);
            return showEvent;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception or WaitHandleCannotBeOpenedException)
        {
            AppLog.Warn("Vis-signal kunne ikke oprettes – en ny start kan ikke hente vinduet frem", exception);
            return null;
        }
    }

    /// <summary>
    /// Sætter signalets integritets-label til medium, så en almindelig (medium)
    /// proces må signalere en elevated instans. LABEL_SECURITY_INFORMATION kræver
    /// kun ejer-ret til objektet, ikke SeSecurityPrivilege. Bedst-muligt.
    /// </summary>
    private static void LowerIntegrityLabel(EventWaitHandle handle)
    {
        const uint SddlRevision = 1;
        const int KernelObject = 6;
        const uint LabelSecurityInformation = 0x10;

        if (!ConvertStringSecurityDescriptorToSecurityDescriptor("S:(ML;;NW;;;ME)", SddlRevision, out var descriptor, out _))
        {
            return;
        }

        try
        {
            if (GetSecurityDescriptorSacl(descriptor, out var present, out var sacl, out _) && present)
            {
                var result = SetSecurityInfo(handle.SafeWaitHandle, KernelObject, LabelSecurityInformation,
                    IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, sacl);
                if (result != 0)
                {
                    AppLog.Warn($"Integritets-label på vis-signal kunne ikke sættes (fejl {result})");
                }
            }
        }
        finally
        {
            LocalFree(descriptor);
        }
    }

    private static void SignalExistingInstance()
    {
        // Den nye start har forgrundsretten (brugeren klikkede); giv den videre,
        // ellers må den kørende instans ikke lægge sit vindue øverst.
        AllowSetForegroundWindow(AsfwAny);

        try
        {
            using var showEvent = EventWaitHandle.OpenExisting(ShowEventName);
            showEvent.Set();
        }
        catch (Exception exception) when (exception is WaitHandleCannotBeOpenedException
            or UnauthorizedAccessException or IOException)
        {
            AppLog.Warn("Kunne ikke bede den kørende instans om at vise sig", exception);
        }
    }

    private const int AsfwAny = -1;

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW")]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(
        string sddl, uint revision, out IntPtr descriptor, out uint size);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetSecurityDescriptorSacl(
        IntPtr descriptor, out bool present, out IntPtr sacl, out bool defaulted);

    [DllImport("advapi32.dll")]
    private static extern uint SetSecurityInfo(
        SafeHandle handle, int objectType, uint securityInformation,
        IntPtr owner, IntPtr group, IntPtr dacl, IntPtr sacl);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
