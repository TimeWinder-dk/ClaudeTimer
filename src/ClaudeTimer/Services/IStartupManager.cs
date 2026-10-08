namespace ClaudeTimer.Services;

public interface IStartupManager
{
    /// <summary>Om den kørende proces har administrator-rettigheder.</summary>
    bool IsElevated { get; }

    /// <summary>
    /// Registrerer (eller fjerner) automatisk start ved logon. Med
    /// <paramref name="elevated"/> bruges en planlagt opgave med højeste
    /// rettigheder, da Windows ikke starter elevated apps fra Run-nøglen.
    /// Returnerer false hvis brugeren afviste UAC-prompten eller det fejlede.
    /// </summary>
    bool Apply(bool startWithWindows, bool elevated);

    /// <summary>
    /// Starter appen igen som administrator. Returnerer false hvis
    /// UAC-prompten blev afvist.
    /// </summary>
    bool TryRelaunchElevated(IEnumerable<string> arguments);
}
