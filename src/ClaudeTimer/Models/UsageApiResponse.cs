using System.Text.Json.Serialization;

namespace ClaudeTimer.Models;

internal sealed class UsageApiResponse
{
    [JsonPropertyName("five_hour")]
    public UsageWindowResponse? FiveHour { get; init; }

    [JsonPropertyName("seven_day")]
    public UsageWindowResponse? SevenDay { get; init; }
}

internal sealed class UsageWindowResponse
{
    [JsonPropertyName("utilization")]
    public double Utilization { get; init; }

    [JsonPropertyName("resets_at")]
    public DateTimeOffset? ResetsAt { get; init; }
}
