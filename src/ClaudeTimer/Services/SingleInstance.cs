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
    private readonly EventWaitHandle _showEvent;
    private RegisteredWaitHandle? _registration;
    private bool _ownsMutex;

    private SingleInstance(Mutex mutex, EventWaitHandle showEvent)
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

        return new SingleInstance(mutex, CreateShowEvent());
    }

    /// <summary>Kalder <paramref name="onShowRequested"/> når en ny start beder om vinduet.</summary>
    public void ListenForShowRequests(Action onShowRequested)
    {
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
        _showEvent.Dispose();
    }

    private static EventWaitHandle CreateShowEvent()
    {
        // Giv brugeren selv adgang, så en ikke-elevated start kan vække en elevated instans.
        var security = new EventWaitHandleSecurity();
        if (WindowsIdentity.GetCurrent().User is { } user)
        {
            security.AddAccessRule(new EventWaitHandleAccessRule(
                user,
                EventWaitHandleRights.Modify | EventWaitHandleRights.Synchronize,
                AccessControlType.Allow));
        }

        return EventWaitHandleAcl.Create(false, EventResetMode.AutoReset, ShowEventName, out _, security);
    }

    private static void SignalExistingInstance()
    {
        try
        {
            using var showEvent = EventWaitHandle.OpenExisting(ShowEventName);
            showEvent.Set();
        }
        catch (Exception exception) when (exception is WaitHandleCannotBeOpenedException or UnauthorizedAccessException)
        {
        }
    }
}
