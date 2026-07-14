namespace Portfolio.Models.Enums
{
    public enum ResearchType
    {
        MalwareAnalysis,   // Malware analysis and reverse engineering
        HardwareVuln,      // Hardware vulnerabilities in vehicles, IoT devices, and similar systems
        RFSecurity,        // Radio frequency security: SDR, replay attacks, and jamming
        WebSecurity,       // Web application security
        NetworkSecurity,   // Network-layer security: MITM and ARP spoofing
        MobileSecurity,    // Android APK analysis and SMS spoofing
        Other
    }

    public enum DisclosureStatus
    {
        Private,             // Not published; handle carefully even in the admin panel
        Coordinated,         // Reported to the vendor but not yet public
        PubliclyDisclosed    // Full write-up is public
    }

    public enum HomelabTopic
    {
        NetworkSetup,        // VLAN, routing, switch
        Firewall,            // pfSense, iptables
        Monitoring,          // Grafana, Prometheus, and log management
        ServerManagement,    // SSH hardening, cron, and service management
        VPN,                 // WireGuard, OpenVPN
        Virtualization,      // Proxmox, Docker, VM
        Other
    }

    public enum NoteType
    {
        Idea,      // Project or research idea
        Todo,      // Tasks to complete
        Research,  // Research notes and links
        Roadmap,   // Future plans
        Snippet    // Code or command snippet
    }

    public enum NotePriority
    {
        Low,
        Normal,
        High,
        Critical
    }

    public enum ContactStatus
    {
        New,
        Read,
        Replied,
        Archived,
        Spam
    }

    public enum AuditAction
    {
        Created,
        Updated,
        Deleted,
        StatusChanged
    }
}
