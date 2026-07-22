using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Portfolio.Models.ExtraData;

namespace Portfolio.Services;

public static class NetworkTopologyJsonService
{
    private const int MaxJsonLength = 500_000;
    private const int MaxNodes = 200;
    private const int MaxEdges = 500;
    private const double MaxCoordinateMagnitude = 100_000;

    private static readonly HashSet<string> AllowedDeviceTypes =
        ["router", "switch", "firewall", "server", "pc", "laptop", "microcontroller",
         "mobile", "custom", "sensor", "audio", "antenna"];

    private static readonly HashSet<string> AllowedConnectionTypes =
        ["ethernet", "wifi", "usb", "power", "other"];

    private static readonly Regex Ipv4AddressPattern = new(
        @"(?<!\d)(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MacAddressPattern = new(
        @"(?i)(?<![0-9a-f])(?:[0-9a-f]{2}[:-]){5}[0-9a-f]{2}(?![0-9a-f])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static bool TryNormalize(
        string? json,
        out NetworkTopology? topology,
        out string? normalizedJson)
    {
        return TryNormalize(json, out topology, out normalizedJson, out _);
    }

    public static bool TryNormalize(
        string? json,
        out NetworkTopology? topology,
        out string? normalizedJson,
        out string? validationError)
    {
        topology = null;
        normalizedJson = null;
        validationError = null;

        if (string.IsNullOrWhiteSpace(json))
            return true;

        if (json.Length > MaxJsonLength)
        {
            validationError = "The network topology exceeds the maximum allowed size.";
            return false;
        }

        try
        {
            topology = JsonSerializer.Deserialize<NetworkTopology>(json, JsonOptions);
            if (topology == null || !IsValid(topology, out validationError))
            {
                topology = null;
                return false;
            }

            normalizedJson = JsonSerializer.Serialize(topology, JsonOptions);
            return true;
        }
        catch (JsonException)
        {
            topology = null;
            validationError = "The network topology contains invalid JSON.";
            return false;
        }
    }

    public static bool TryPrepareForEditing(string? json, out string? editableJson)
    {
        editableJson = null;

        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxJsonLength)
            return false;

        try
        {
            var topology = JsonSerializer.Deserialize<NetworkTopology>(json, JsonOptions);
            if (topology?.Nodes == null || topology.Edges == null)
                return false;

            // Re-serialize with the safe JSON encoder before embedding rejected input back into the admin page.
            editableJson = JsonSerializer.Serialize(topology, JsonOptions);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsValid(NetworkTopology topology, out string? validationError)
    {
        validationError = null;

        if (topology.Nodes == null || topology.Edges == null ||
            topology.Nodes.Count > MaxNodes || topology.Edges.Count > MaxEdges ||
            topology.Nodes.Any(node => node is null) ||
            topology.Edges.Any(edge => edge is null))
        {
            validationError = "The network topology structure or item count is invalid.";
            return false;
        }

        var nodeIds = topology.Nodes.Select(node => node.Id).ToHashSet();
        if (nodeIds.Count != topology.Nodes.Count || nodeIds.Any(id => id <= 0))
        {
            validationError = "Every network device must have a unique positive identifier.";
            return false;
        }

        foreach (var node in topology.Nodes)
        {
            if (!AllowedDeviceTypes.Contains(node.DeviceType) ||
                !HasMaxLength(node.Label, 200) ||
                !HasMaxLength(node.ShortInfo, 500) ||
                !HasMaxLength(node.IpAddress, 100) ||
                !HasMaxLength(node.HomelabRole, 500) ||
                !HasMaxLength(node.HomelabNotes, 5_000) ||
                !HasMaxLength(node.StandaloneHardware, 2_000) ||
                !HasMaxLength(node.LinkedProjectSlug, 200) ||
                !IsValidCoordinate(node.X) ||
                !IsValidCoordinate(node.Y) ||
                !SafeUrlPolicy.IsSafeWebResourceUrl(node.IconUrl) ||
                !SafeUrlPolicy.IsSafeWebResourceUrl(node.StandaloneImageUrl))
            {
                validationError = "A network device contains an unsupported type, unsafe URL, invalid position, or a value that exceeds the allowed length.";
                return false;
            }

            if (!IsPublicNetworkLabelSafe(node.IpAddress))
            {
                validationError = "Public network labels cannot contain a raw IPv4 address, IPv6 address, CIDR subnet, or MAC address. Use a redacted label such as 'Management VLAN'.";
                return false;
            }
        }

        var edgesAreValid = topology.Edges.All(edge =>
            nodeIds.Contains(edge.From) &&
            nodeIds.Contains(edge.To) &&
            edge.From != edge.To &&
            AllowedConnectionTypes.Contains(edge.ConnectionType) &&
            HasMaxLength(edge.Label, 200));

        if (!edgesAreValid)
            validationError = "A network connection references an invalid device or contains unsupported data.";

        return edgesAreValid;
    }

    private static bool HasMaxLength(string? value, int maxLength) =>
        value == null || value.Length <= maxLength;

    private static bool IsValidCoordinate(double? value) =>
        !value.HasValue ||
        (double.IsFinite(value.Value) && Math.Abs(value.Value) <= MaxCoordinateMagnitude);

    private static bool IsPublicNetworkLabelSafe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        var label = value.Trim();
        if (Ipv4AddressPattern.IsMatch(label) || MacAddressPattern.IsMatch(label))
            return false;

        var candidates = label.Split(
            [' ', '\t', '\r', '\n', ',', ';', '=', '(', ')'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return candidates.All(candidate =>
        {
            var addressCandidate = candidate.Trim('"', '\'', '[', ']');
            var cidrSeparator = addressCandidate.IndexOf('/');
            if (cidrSeparator > 0)
                addressCandidate = addressCandidate[..cidrSeparator];

            return !IPAddress.TryParse(addressCandidate, out _);
        });
    }
}
