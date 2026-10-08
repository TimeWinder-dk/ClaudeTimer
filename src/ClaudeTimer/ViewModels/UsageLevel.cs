namespace ClaudeTimer.ViewModels;

public enum UsageLevel
{
    Good,
    Warning,
    Critical
}

public static class UsageLevels
{
    public const double WarningFrom = 50;
    public const double CriticalFrom = 80;

    public static UsageLevel For(double utilization) =>
        utilization >= CriticalFrom ? UsageLevel.Critical
        : utilization >= WarningFrom ? UsageLevel.Warning
        : UsageLevel.Good;
}
