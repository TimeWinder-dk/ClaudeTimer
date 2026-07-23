namespace ClaudeTimer.Models;

public sealed record UsageWindow(
    string Kind,
    string Group,
    double Utilization,
    DateTimeOffset? ResetsAt,
    string? ScopeModelName)
{
    /// <summary>
    /// Stabil identitet på tværs af opdateringer. Flere vinduer kan dele samme
    /// <see cref="Kind"/> (fx flere <c>weekly_scoped</c>), så modelnavnet indgår.
    /// </summary>
    public string Key => ScopeModelName is { Length: > 0 } model
        ? $"{Kind}|{model}"
        : Kind;
}
