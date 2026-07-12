using Microsoft.EntityFrameworkCore;
using Portfolio.Models;
using Portfolio.Models.Enums;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace Portfolio.Data
{
    public class AppDbContext : IdentityDbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        // ── DbSet'ler ──────────────────────────────────────────────────────────
        public DbSet<Category> Categories => Set<Category>();
        public DbSet<Project> Projects => Set<Project>();
        public DbSet<SecurityResearch> SecurityResearches => Set<SecurityResearch>();
        public DbSet<HomelabPost> HomelabPosts => Set<HomelabPost>();
        public DbSet<BlogPost> BlogPosts => Set<BlogPost>();
        public DbSet<TeamProject> TeamProjects => Set<TeamProject>();
        public DbSet<Note> Notes => Set<Note>();
        public DbSet<Media> Media => Set<Media>();
        public DbSet<Tag> Tags => Set<Tag>();
        public DbSet<Service> Services => Set<Service>();
        public DbSet<ServiceReference> ServiceReferences => Set<ServiceReference>();
        public DbSet<ContactMessage> ContactMessages => Set<ContactMessage>();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<Page> Pages => Set<Page>();
        public DbSet<SiteSettings> SiteSettings => Set<SiteSettings>();
        public DbSet<Certificate> Certificates => Set<Certificate>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ── Global Query Filters ───────────────────────────────────────────
            // Public kullanıcılar sadece Public içerikleri görür.
            // Admin controller'larında .IgnoreQueryFilters() ile bypass et.
            // NOT: Notes ve audit tabloları dahil edilmedi — public endpoint yok.

            modelBuilder.Entity<Project>().HasQueryFilter(p =>
                p.Status == VisibilityStatus.Public &&
                p.Category.Status == VisibilityStatus.Public);

            modelBuilder.Entity<HomelabPost>().HasQueryFilter(h =>
                h.Status == VisibilityStatus.Public &&
                h.Category.Status == VisibilityStatus.Public);

            modelBuilder.Entity<BlogPost>().HasQueryFilter(b =>
                b.Status == VisibilityStatus.Public &&
                b.Category.Status == VisibilityStatus.Public);

            modelBuilder.Entity<TeamProject>().HasQueryFilter(t =>
                t.Status == VisibilityStatus.Public &&
                t.Category.Status == VisibilityStatus.Public);

            modelBuilder.Entity<Page>().HasQueryFilter(p =>
                p.Status == VisibilityStatus.Public);

            modelBuilder.Entity<Certificate>().HasQueryFilter(c =>
                c.Status == VisibilityStatus.Public);

            // SecurityResearch için çift kontrol — DisclosureStatus da zorunlu
            modelBuilder.Entity<SecurityResearch>().HasQueryFilter(s =>
                s.Status == VisibilityStatus.Public &&
                s.Category.Status == VisibilityStatus.Public &&
                s.DisclosureStatus == DisclosureStatus.PubliclyDisclosed);

            // ── Enum → String dönüşümleri ─────────────────────────────────────
            // Veritabanında okunabilir string saklanır, int değil.

            modelBuilder.Entity<Category>()
                .Property(c => c.Status)
                .HasConversion<string>();

            modelBuilder.Entity<Project>()
                .Property(p => p.Status)
                .HasConversion<string>();

            modelBuilder.Entity<SecurityResearch>()
                .Property(s => s.Status).HasConversion<string>();
            modelBuilder.Entity<SecurityResearch>()
                .Property(s => s.ResearchType).HasConversion<string>();
            modelBuilder.Entity<SecurityResearch>()
                .Property(s => s.DisclosureStatus).HasConversion<string>();

            modelBuilder.Entity<HomelabPost>()
                .Property(h => h.Status).HasConversion<string>();
            modelBuilder.Entity<HomelabPost>()
                .Property(h => h.Topic).HasConversion<string>();

            modelBuilder.Entity<BlogPost>()
                .Property(b => b.Status).HasConversion<string>();

            modelBuilder.Entity<TeamProject>()
                .Property(t => t.Status).HasConversion<string>();

            modelBuilder.Entity<Note>()
                .Property(n => n.NoteType).HasConversion<string>();
            modelBuilder.Entity<Note>()
                .Property(n => n.Priority).HasConversion<string>();

            modelBuilder.Entity<Service>()
                .Property(s => s.Status).HasConversion<string>();

            modelBuilder.Entity<ContactMessage>()
                .Property(c => c.Status).HasConversion<string>();

            modelBuilder.Entity<AuditLog>()
                .Property(a => a.Action).HasConversion<string>();

            modelBuilder.Entity<Page>()
                .Property(p => p.Status).HasConversion<string>();

            modelBuilder.Entity<Certificate>()
                .Property(c => c.Status).HasConversion<string>();

            // ── JSONB sütunları (PostgreSQL) ──────────────────────────────────
            // EF Core bu sütunları string olarak görür, PostgreSQL jsonb tipi
            // migration'da elle belirtilmeli: .HasColumnType("jsonb")

            modelBuilder.Entity<Project>()
                .Property(p => p.ExtraData).HasColumnType("jsonb");

            modelBuilder.Entity<SecurityResearch>()
                .Property(s => s.ToolsUsed).HasColumnType("jsonb");

            modelBuilder.Entity<HomelabPost>()
                .Property(h => h.HardwareUsed).HasColumnType("jsonb");
            modelBuilder.Entity<HomelabPost>()
                .Property(h => h.SoftwareUsed).HasColumnType("jsonb");

            modelBuilder.Entity<TeamProject>()
                .Property(t => t.TeamMembers).HasColumnType("jsonb");

            modelBuilder.Entity<Note>()
                .Property(n => n.TagsJson).HasColumnType("jsonb");

            modelBuilder.Entity<HomelabPost>()
                .Property(h => h.NetworkTopology).HasColumnType("jsonb");

            // ── Many-to-Many ilişkiler ─────────────────────────────────────────
            // EF Core ara tabloyu otomatik oluşturur.

            modelBuilder.Entity<Project>()
                .HasMany(p => p.Tags)
                .WithMany(t => t.Projects)
                .UsingEntity("ProjectTags");

            modelBuilder.Entity<SecurityResearch>()
                .HasMany(s => s.Tags)
                .WithMany(t => t.SecurityResearches)
                .UsingEntity("SecurityResearchTags");

            modelBuilder.Entity<HomelabPost>()
                .HasMany(h => h.Tags)
                .WithMany(t => t.HomelabPosts)
                .UsingEntity("HomelabPostTags");

            modelBuilder.Entity<BlogPost>()
                .HasMany(b => b.Tags)
                .WithMany(t => t.BlogPosts)
                .UsingEntity("BlogPostTags");

            modelBuilder.Entity<TeamProject>()
                .HasMany(t => t.Tags)
                .WithMany(tag => tag.TeamProjects)
                .UsingEntity("TeamProjectTags");

            // ── ServiceReference bileşik PK ───────────────────────────────────
            modelBuilder.Entity<ServiceReference>()
                .HasKey(sr => new { sr.ServiceId, sr.RefType, sr.RefId });

            modelBuilder.Entity<ServiceReference>()
                .HasOne(sr => sr.Service)
                .WithMany(s => s.References)
                .HasForeignKey(sr => sr.ServiceId);

            // ── Unique index'ler ──────────────────────────────────────────────
            modelBuilder.Entity<Category>()
                .HasIndex(c => c.Slug).IsUnique();

            modelBuilder.Entity<Project>()
                .HasIndex(p => p.Slug).IsUnique();

            modelBuilder.Entity<SecurityResearch>()
                .HasIndex(s => s.Slug).IsUnique();

            modelBuilder.Entity<HomelabPost>()
                .HasIndex(h => h.Slug).IsUnique();

            modelBuilder.Entity<BlogPost>()
                .HasIndex(b => b.Slug).IsUnique();

            modelBuilder.Entity<TeamProject>()
                .HasIndex(t => t.Slug).IsUnique();

            modelBuilder.Entity<Tag>()
                .HasIndex(t => t.Slug).IsUnique();
            modelBuilder.Entity<Tag>()
                .HasIndex(t => t.Name).IsUnique();
            modelBuilder.Entity<Page>()
                .HasIndex(p => p.Slug).IsUnique();

            // ── Composite index'ler ───────────────────────────────────────────
            // Ana sayfa sorgusu — featured + public + category
            modelBuilder.Entity<Project>()
                .HasIndex(p => new { p.IsFeatured, p.Status, p.CategoryId })
                .HasDatabaseName("idx_projects_featured");

            // Kategori listesi sorgusu
            modelBuilder.Entity<Project>()
                .HasIndex(p => new { p.CategoryId, p.Status })
                .HasDatabaseName("idx_projects_category");

            // Media getirme — entity_type + entity_id çok sık sorgulanır
            modelBuilder.Entity<Media>()
                .HasIndex(m => new { m.EntityType, m.EntityId })
                .HasDatabaseName("idx_media_entity");

            // ── Ek kısıtlamalar ───────────────────────────────────────────────
            modelBuilder.Entity<ContactMessage>()
                .Property(c => c.Email).HasMaxLength(300);

            modelBuilder.Entity<ContactMessage>()
                .Property(c => c.IpAddress).HasMaxLength(45); // IPv6 max

            
        }

        // ── SaveChanges Override — AuditLog otomasyonu ────────────────────────
        public override int SaveChanges()
        {
            UpdateTimestamps();
            // AuditService burada çağrılır — bkz. Services/AuditService.cs
            return base.SaveChanges();
        }

        public override Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            UpdateTimestamps();
            return base.SaveChangesAsync(ct);
        }

        /// <summary>
        /// UpdatedAt sütununu otomatik günceller.
        /// Her entity için ayrı property set etmeye gerek kalmaz.
        /// </summary>
        private void UpdateTimestamps()
        {
            var entries = ChangeTracker.Entries()
                .Where(e => e.State == EntityState.Modified);

            foreach (var entry in entries)
            {
                if (entry.Entity is Project p) p.UpdatedAt = DateTime.UtcNow;
                else if (entry.Entity is SecurityResearch s) s.UpdatedAt = DateTime.UtcNow;
                else if (entry.Entity is HomelabPost h) h.UpdatedAt = DateTime.UtcNow;
                else if (entry.Entity is BlogPost b) b.UpdatedAt = DateTime.UtcNow;
                else if (entry.Entity is TeamProject t) t.UpdatedAt = DateTime.UtcNow;
                else if (entry.Entity is Category c) c.UpdatedAt = DateTime.UtcNow;
                else if (entry.Entity is Service sv) sv.UpdatedAt = DateTime.UtcNow;
                else if (entry.Entity is Note n) n.UpdatedAt = DateTime.UtcNow;
                else if (entry.Entity is Page pg) pg.UpdatedAt = DateTime.UtcNow;
                else if (entry.Entity is Certificate cert) cert.UpdatedAt = DateTime.UtcNow;
            }
        }
    }
}
