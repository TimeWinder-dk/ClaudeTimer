using System.Text.Json.Serialization;

namespace ClaudeTimer.Models;

internal sealed class ProfileApiResponse
{
    [JsonPropertyName("account")]
    public ProfileAccountResponse? Account { get; init; }

    [JsonPropertyName("organization")]
    public ProfileOrganizationResponse? Organization { get; init; }
}

internal sealed class ProfileAccountResponse
{
    [JsonPropertyName("uuid")]
    public string? Uuid { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("full_name")]
    public string? FullName { get; init; }
}

internal sealed class ProfileOrganizationResponse
{
    [JsonPropertyName("uuid")]
    public string? Uuid { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }
}
