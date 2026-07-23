using System.Text.Json.Serialization;

namespace ClaudeTimer.Models;

internal sealed class UsageApiResponse
{
    /// <summary>Ældre, faste felter. Bruges kun som fallback hvis <see cref="Limits"/> mangler.</summary>
    [JsonPropertyName("five_hour")]
    public UsageWindowResponse? FiveHour { get; init; }

    [JsonPropertyName("seven_day")]
    public UsageWindowResponse? SevenDay { get; init; }

    /// <summary>
    /// Den moderne, generelle liste af grænser. Rummer session-, ugentlige og
    /// model-afgrænsede vinduer (fx Fable) og kan vokse uden kodeændringer.
    /// </summary>
    [JsonPropertyName("limits")]
    public IReadOnlyList<UsageLimitResponse>? Limits { get; init; }
}

internal sealed class UsageWindowResponse
{
    [JsonPropertyName("utilization")]
    public double Utilization { get; init; }

    [JsonPropertyName("resets_at")]
    public DateTimeOffset? ResetsAt { get; init; }
}

internal sealed class UsageLimitResponse
{
    [JsonPropertyName("kind")]
    public string? Kind { get; init; }

    [JsonPropertyName("group")]
    public string? Group { get; init; }

    [JsonPropertyName("percent")]
    public double Percent { get; init; }

    [JsonPropertyName("resets_at")]
    public DateTimeOffset? ResetsAt { get; init; }

    [JsonPropertyName("scope")]
    public UsageLimitScopeResponse? Scope { get; init; }
}

internal sealed class UsageLimitScopeResponse
{
    [JsonPropertyName("model")]
    public UsageLimitScopeModelResponse? Model { get; init; }
}

internal sealed class UsageLimitScopeModelResponse
{
    [JsonPropertyName("display_name")]
    public string? DisplayName { get; init; }
}
