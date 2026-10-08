namespace ClaudeTimer.Models;

/// <summary>Identiteten bag et OAuth-token, fra Claudes profil-endpoint.</summary>
public sealed record ClaudeAccount(
    string AccountId,
    string? Email,
    string? DisplayName,
    string? OrganizationId,
    string? OrganizationName)
{
    /// <summary>Forbrug gælder pr. konto og organisation, så begge indgår.</summary>
    public string Key => $"{AccountId}|{OrganizationId}";
}
