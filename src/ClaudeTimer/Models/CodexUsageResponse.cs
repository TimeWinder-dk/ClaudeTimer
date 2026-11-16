using System.Text.Json.Serialization;

namespace ClaudeTimer.Models;

/// <summary>Svaret fra ChatGPTs usage-endpoint, som Codex selv bruger til /status.</summary>
internal sealed class CodexUsageResponse
{
    [JsonPropertyName("plan_type")]
    public string? PlanType { get; init; }

    [JsonPropertyName("rate_limit")]
    public CodexRateLimitResponse? RateLimit { get; init; }
}

internal sealed class CodexRateLimitResponse
{
    /// <summary>Det korte vindue, i dag 5 timer.</summary>
    [JsonPropertyName("primary_window")]
    public CodexWindowResponse? PrimaryWindow { get; init; }

    /// <summary>Det lange vindue, i dag 7 døgn.</summary>
    [JsonPropertyName("secondary_window")]
    public CodexWindowResponse? SecondaryWindow { get; init; }
}

internal sealed class CodexWindowResponse
{
    [JsonPropertyName("used_percent")]
    public double UsedPercent { get; init; }

    [JsonPropertyName("limit_window_seconds")]
    public long? LimitWindowSeconds { get; init; }

    /// <summary>Unix-sekunder.</summary>
    [JsonPropertyName("reset_at")]
    public long? ResetAt { get; init; }
}
