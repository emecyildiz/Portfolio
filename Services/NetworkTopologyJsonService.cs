using System.Text.Json;
using Portfolio.Models.ExtraData;

namespace Portfolio.Services;

public static class NetworkTopologyJsonService
{
    private const int MaxJsonLength = 500_000;
    private const int MaxNodes = 200;
    private const int MaxEdges = 500;

    private static readonly HashSet<string> AllowedDeviceTypes =
        ["router", "switch", "firewall", "server", "pc", "laptop", "microcontroller",
         "mobile", "custom", "sensor", "audio", "antenna"];

    private static readonly HashSet<string> AllowedConnectionTypes =
        ["ethernet", "wifi", "usb", "power", "other"];

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
        topology = null;
        normalizedJson = null;

        if (string.IsNullOrWhiteSpace(json))
            return true;

        if (json.Length > MaxJsonLength)
            return false;

        try
        {
            topology = JsonSerializer.Deserialize<NetworkTopology>(json, JsonOptions);
            if (topology == null || !IsValid(topology))
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
            return false;
        }
    }

    private static bool IsValid(NetworkTopology topology)
    {
        if (topology.Nodes == null || topology.Edges == null ||
            topology.Nodes.Count > MaxNodes || topology.Edges.Count > MaxEdges ||
            topology.Nodes.Any(node => node is null) ||
            topology.Edges.Any(edge => edge is null))
        {
            return false;
        }

        var nodeIds = topology.Nodes.Select(node => node.Id).ToHashSet();
        if (nodeIds.Count != topology.Nodes.Count || nodeIds.Any(id => id <= 0))
            return false;

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
                !SafeUrlPolicy.IsSafeWebResourceUrl(node.IconUrl) ||
                !SafeUrlPolicy.IsSafeWebResourceUrl(node.StandaloneImageUrl))
            {
                return false;
            }
        }

        return topology.Edges.All(edge =>
            nodeIds.Contains(edge.From) &&
            nodeIds.Contains(edge.To) &&
            edge.From != edge.To &&
            AllowedConnectionTypes.Contains(edge.ConnectionType) &&
            HasMaxLength(edge.Label, 200));
    }

    private static bool HasMaxLength(string? value, int maxLength) =>
        value == null || value.Length <= maxLength;
}
