using System.Text.Json;
using Portfolio.Models.ExtraData;

namespace Portfolio.Services;

public static class SiteLinksJsonService
{
    private const int MaxJsonLength = 50_000;
    private const int MaxLinks = 20;

    private static readonly HashSet<string> AllowedSchemes =
        [Uri.UriSchemeHttps, Uri.UriSchemeMailto];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static bool TryNormalize(
        string? json,
        out List<SiteLink> links,
        out string normalizedJson)
    {
        links = [];
        normalizedJson = "[]";

        if (string.IsNullOrWhiteSpace(json))
            return true;

        if (json.Length > MaxJsonLength)
            return false;

        try
        {
            var parsedLinks = JsonSerializer.Deserialize<List<SiteLink>>(json, JsonOptions);
            if (parsedLinks == null || parsedLinks.Count > MaxLinks || parsedLinks.Any(link => link is null))
                return false;

            foreach (var link in parsedLinks)
            {
                link.Label = link.Label?.Trim() ?? string.Empty;
                link.Url = link.Url?.Trim() ?? string.Empty;

                if (link.Label.Length is < 1 or > 60 ||
                    link.Url.Length is < 1 or > 2_000 ||
                    link.Label.Any(char.IsControl) ||
                    link.Url.Any(char.IsControl) ||
                    !Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) ||
                    !AllowedSchemes.Contains(uri.Scheme) ||
                    (uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrWhiteSpace(uri.Host)) ||
                    (uri.Scheme == Uri.UriSchemeMailto && string.IsNullOrWhiteSpace(uri.AbsolutePath)))
                {
                    return false;
                }

                if (uri.Scheme == Uri.UriSchemeMailto)
                    link.OpenInNewTab = false;
            }

            links = parsedLinks;
            normalizedJson = JsonSerializer.Serialize(links, JsonOptions);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
