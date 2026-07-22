namespace Portfolio.Models.ExtraData;

/// <summary>
/// Schema for the HomelabPost.NetworkTopology JSONB column.
/// Used by the interactive Packet Tracer-style network map.
/// </summary>
public class NetworkTopology
{
    public List<NetworkNode> Nodes { get; set; } = new();
    public List<NetworkEdge> Edges { get; set; } = new();
    public bool IsLayoutLocked { get; set; }
}

public class NetworkNode
{
    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public string IconUrl { get; set; } = string.Empty;         // Small icon shown on the map
    public string DeviceType { get; set; } = "custom";          // router, switch, firewall, server, client, custom, sensor, audio
    public double? X { get; set; }
    public double? Y { get; set; }

    // When linked to an Electronics project, image and hardware details are loaded automatically.
    public string? LinkedProjectSlug { get; set; }

    // Optional uploaded image and manual hardware details for devices without a linked project.
    public string? StandaloneImageUrl { get; set; }
    public string? StandaloneHardware { get; set; }

    // Homelab-specific information shared by every device.
    public string ShortInfo { get; set; } = string.Empty;       // Short summary shown on hover
    // Kept as IpAddress in JSON for compatibility; only public, redacted network labels are accepted.
    public string? IpAddress { get; set; }
    public string HomelabRole { get; set; } = string.Empty;     // Purpose on this network
    public string? HomelabNotes { get; set; }                    // Additional notes
}

public class NetworkEdge
{
    public int From { get; set; }
    public int To { get; set; }
    public string ConnectionType { get; set; } = "ethernet";     // ethernet, wifi, usb, power, other
    public string? Label { get; set; }                           // For example, "VLAN 10" or "WAN"
}
