namespace Portfolio.Models.Enums
{
    public enum ResearchType
    {
        MalwareAnalysis,   // Zararlı yazılım analizi / tersine mühendislik
        HardwareVuln,      // Donanım güvenlik açıkları (araç, IoT vs.)
        RFSecurity,        // Radyo frekansı — SDR, replay attack, jamming
        WebSecurity,       // Web uygulama güvenliği
        NetworkSecurity,   // Ağ katmanı — MITM, ARP spoofing
        MobileSecurity,    // Android APK analizi, SMS spoofing
        Other
    }

    public enum DisclosureStatus
    {
        Private,             // Yayınlanmadı — admin panelinde bile dikkatli davran
        Coordinated,         // Üreticiye bildirildi, henüz kamuya açık değil
        PubliclyDisclosed    // Full write-up yayında
    }

    public enum HomelabTopic
    {
        NetworkSetup,        // VLAN, routing, switch
        Firewall,            // pfSense, iptables
        Monitoring,          // Grafana, Prometheus, log yönetimi
        ServerManagement,    // SSH hardening, cron, servis yönetimi
        VPN,                 // WireGuard, OpenVPN
        Virtualization,      // Proxmox, Docker, VM
        Other
    }

    public enum NoteType
    {
        Idea,      // Proje veya araştırma fikri
        Todo,      // Yapılacaklar
        Research,  // Araştırma notları, linkler
        Roadmap,   // Gelecek planları
        Snippet    // Kod veya komut parçacığı
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
