namespace Portfolio.Models.ExtraData;

/// <summary>
/// HomelabPost.NetworkTopology JSONB sütununun şeması.
/// Packet Tracer tarzı interaktif ağ haritası için.
/// </summary>
public class NetworkTopology
{
    public List<NetworkNode> Nodes { get; set; } = new();
    public List<NetworkEdge> Edges { get; set; } = new();
}

public class NetworkNode
{
    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public string IconUrl { get; set; } = string.Empty;         // Küçük ikon — haritada görünen
    public string DeviceType { get; set; } = "custom";          // router, switch, firewall, server, client, custom, sensor, audio

    // Bir Electronics projesine bağlıysa buradan fotoğraf/malzeme otomatik çekilir
    public string? LinkedProjectSlug { get; set; }

    // Bağlantısız cihazlar için (linkedProjectSlug boşsa) manuel girilecek alanlar
    public string? StandaloneImageUrl { get; set; }
    public string? StandaloneHardware { get; set; }

    // Her cihaz için ortak — Homelab'a özel bilgiler
    public string ShortInfo { get; set; } = string.Empty;       // Hover'da görünen kısa özet
    public string? IpAddress { get; set; }
    public string HomelabRole { get; set; } = string.Empty;     // Bu ağda ne işe yarıyor
    public string? HomelabNotes { get; set; }                    // Ekstra notlar
}

public class NetworkEdge
{
    public int From { get; set; }
    public int To { get; set; }
    public string ConnectionType { get; set; } = "ethernet";     // ethernet, wifi, usb, power, other
    public string? Label { get; set; }                           // "VLAN 10", "WAN" gibi
}