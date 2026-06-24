namespace Portfolio.Models.ExtraData
{
    public class ExtraDataSchemas
    {

        public class ElectronicsExtraData
        {
            public string? Microcontroller { get; set; }          // "ESP32-WROOM-32"
            public List<string>? Components { get; set; }         // ["SSD1306", "Li-Po 3.7V"]
            public string? SchematicUrl { get; set; }             // "/uploads/schematics/v1.pdf"
            public string? ProgrammingLanguage { get; set; }      // "C (Arduino Framework)"
            public bool? IsOpenSource { get; set; }
        }

        /// <summary>
        /// Kategori: Web uygulamaları (okul projeleri, ekip projeleri)
        /// </summary>
        public class WebAppExtraData
        {
            public List<string>? TechStack { get; set; }          // ["React", "FastAPI", "PostgreSQL"]
            public int? TeamSize { get; set; }
            public string? MyRole { get; set; }                   // "Backend Developer & DevOps"
            public string? Subdomain { get; set; }                // "muhasebe.siteadi.com"
            public bool? IsSchoolProject { get; set; }
        }

        // ── security_research tablosu — tools_used ────────────────────────────────

        /// <summary>
        /// Kullanılan araçlar basit string listesi olduğu için
        /// doğrudan List&lt;string&gt; serialize edilebilir.
        /// Örnek: ["Ghidra", "IDA Free", "Wireshark", "SDR++", "HackRF"]
        /// </summary>

        // ── homelab_posts tablosu ─────────────────────────────────────────────────

        /// <summary>
        /// hardware_used ve software_used ayrı sütunlar olduğu için
        /// doğrudan List&lt;string&gt; serialize edilir.
        /// hardware örnek: ["Raspberry Pi 4 8GB", "pfSense mini PC"]
        /// software örnek: ["pfSense 2.7", "Snort 3", "Grafana"]
        /// </summary>

        // ── team_projects tablosu — team_members ─────────────────────────────────

        /// <summary>
        /// Ekip üyesi bilgisi — JSON array içindeki her eleman bu sınıf
        /// </summary>
        public class TeamMember
        {
            public string Name { get; set; } = string.Empty;
            public string Role { get; set; } = string.Empty;
            public string? GithubUrl { get; set; }
            public string? LinkedinUrl { get; set; }
        }

    }
}
