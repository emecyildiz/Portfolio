using System.Text.Json;
using Portfolio.Models.ExtraData;

namespace Portfolio.Services;

public static class TeamMemberJsonService
{
    private const int MaxJsonLength = 100_000;
    private const int MaxMembers = 50;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static bool TryNormalize(
        string? json,
        out List<TeamMember> members,
        out string? normalizedJson)
    {
        members = [];
        normalizedJson = null;

        if (string.IsNullOrWhiteSpace(json))
            return true;

        if (json.Length > MaxJsonLength)
            return false;

        try
        {
            members = JsonSerializer.Deserialize<List<TeamMember>>(json, JsonOptions) ?? [];
            if (members.Count > MaxMembers || members.Any(member =>
                    member is null ||
                    !HasMaxLength(member.Name, 200) ||
                    !HasMaxLength(member.Role, 200) ||
                    !HasSafeExternalUrl(member.GithubUrl) ||
                    !HasSafeExternalUrl(member.LinkedinUrl)))
            {
                members = [];
                return false;
            }

            normalizedJson = JsonSerializer.Serialize(members, JsonOptions);
            return true;
        }
        catch (JsonException)
        {
            members = [];
            return false;
        }
    }

    private static bool HasMaxLength(string? value, int maxLength) =>
        value == null || value.Length <= maxLength;

    private static bool HasSafeExternalUrl(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        (value.Length <= 2_048 && SafeUrlPolicy.IsSafeAbsoluteHttpUrl(value));
}
