namespace ClaudeTimer.Models;

public sealed record UsageWindow(double Utilization, DateTimeOffset? ResetsAt);
