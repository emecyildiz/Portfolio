using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Models;

namespace Portfolio.Services;

public interface IMediaService
{
    /// <summary>
    /// Yüklenen dosyayı kaydeder, media tablosuna ekler ve Media nesnesini döner.
    /// </summary>
    Task<Media> SaveAsync(IFormFile file, string entityType, int entityId,
                          string? altText = null, string? caption = null);

    /// <summary>
    /// Belirli bir içeriğe ait tüm görselleri getirir.
    /// </summary>
    Task<List<Media>> GetByEntityAsync(string entityType, int entityId);

    /// <summary>
    /// Görseli siler — hem dosyayı hem veritabanı kaydını.
    /// </summary>
    Task DeleteAsync(int mediaId);

    /// <summary>
    /// Bir görseli cover olarak işaretle, diğerlerinin IsCover'ını false yap.
    /// </summary>
    Task SetCoverAsync(int mediaId, string entityType, int entityId);
}

public class MediaService : IMediaService
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;

    // İzin verilen dosya tipleri
    private readonly string[] _allowedMimeTypes = ["image/jpeg", "image/png", "image/webp", "image/gif"];

    public MediaService(AppDbContext db, IWebHostEnvironment env, IConfiguration config)
    {
        _db = db;
        _env = env;
        _config = config;
    }

    public async Task<Media> SaveAsync(IFormFile file, string entityType, int entityId,
                                       string? altText = null, string? caption = null)
    {
        // Dosya tipi kontrolü
        if (!_allowedMimeTypes.Contains(file.ContentType.ToLower()))
            throw new InvalidOperationException($"Desteklenmeyen dosya tipi: {file.ContentType}");

        // Boyut kontrolü (appsettings'ten alır — varsayılan 10MB)
        var maxSize = _config.GetValue<long>("MediaStorage:MaxFileSizeBytes", 10_485_760);
        if (file.Length > maxSize)
            throw new InvalidOperationException($"Dosya çok büyük. Maksimum: {maxSize / 1_048_576}MB");

        // Benzersiz dosya adı — orijinal adı sakla ama çakışma olmasın
        var extension = Path.GetExtension(file.FileName).ToLower();
        var uniqueName = $"{Guid.NewGuid()}{extension}";

        // Kayıt yolu: wwwroot/uploads/project/42/dosya.jpg
        var relativePath = Path.Combine("uploads", entityType, entityId.ToString(), uniqueName);
        var physicalPath = Path.Combine(_env.WebRootPath, relativePath);

        // Klasörü oluştur (yoksa)
        Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);

        // Dosyayı kaydet
        await using var stream = new FileStream(physicalPath, FileMode.Create);
        await file.CopyToAsync(stream);

        // Media kaydı oluştur
        var media = new Media
        {
            EntityType = entityType,
            EntityId = entityId,
            Url = "/" + relativePath.Replace('\\', '/'),
            Filename = file.FileName,
            AltText = altText,
            Caption = caption,
            MimeType = file.ContentType,
            FileSizeBytes = file.Length,
            SortOrder = await GetNextSortOrderAsync(entityType, entityId),
            IsCover = false,
            CreatedAt = DateTime.UtcNow
        };

        _db.Media.Add(media);
        await _db.SaveChangesAsync();

        return media;
    }

    public async Task<List<Media>> GetByEntityAsync(string entityType, int entityId)
    {
        return await _db.Media
            .Where(m => m.EntityType == entityType && m.EntityId == entityId)
            .OrderBy(m => m.SortOrder)
            .ToListAsync();
    }

    public async Task DeleteAsync(int mediaId)
    {
        var media = await _db.Media.FindAsync(mediaId)
            ?? throw new InvalidOperationException("Medya bulunamadı.");

        await ClearCoverReferenceAsync(media);

        // Fiziksel dosyayı sil
        var physicalPath = Path.Combine(_env.WebRootPath, media.Url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(physicalPath))
            File.Delete(physicalPath);

        _db.Media.Remove(media);
        await _db.SaveChangesAsync();
    }

    private async Task ClearCoverReferenceAsync(Media media)
    {
        switch (media.EntityType)
        {
            case "project":
                var project = await _db.Projects.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(p => p.Id == media.EntityId);
                if (project?.CoverImageUrl == media.Url)
                    project.CoverImageUrl = null;
                break;

            case "security_research":
                var research = await _db.SecurityResearches.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(s => s.Id == media.EntityId);
                if (research?.CoverImageUrl == media.Url)
                    research.CoverImageUrl = null;
                break;

            case "homelab_post":
                var homelabPost = await _db.HomelabPosts.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(h => h.Id == media.EntityId);
                if (homelabPost?.CoverImageUrl == media.Url)
                    homelabPost.CoverImageUrl = null;
                break;

            case "blog_post":
                var blogPost = await _db.BlogPosts.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(b => b.Id == media.EntityId);
                if (blogPost?.CoverImageUrl == media.Url)
                    blogPost.CoverImageUrl = null;
                break;

            case "team_project":
                var teamProject = await _db.TeamProjects.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(t => t.Id == media.EntityId);
                if (teamProject?.CoverImageUrl == media.Url)
                    teamProject.CoverImageUrl = null;
                break;
        }
    }

    public async Task SetCoverAsync(int mediaId, string entityType, int entityId)
    {
        // Önce hepsini false yap
        var all = await _db.Media
            .Where(m => m.EntityType == entityType && m.EntityId == entityId)
            .ToListAsync();

        foreach (var m in all)
            m.IsCover = false;

        // Seçileni true yap
        var target = all.FirstOrDefault(m => m.Id == mediaId)
            ?? throw new InvalidOperationException("Medya bulunamadı.");
        target.IsCover = true;

        await _db.SaveChangesAsync();
    }

    private async Task<int> GetNextSortOrderAsync(string entityType, int entityId)
    {
        var max = await _db.Media
            .Where(m => m.EntityType == entityType && m.EntityId == entityId)
            .MaxAsync(m => (int?)m.SortOrder);
        return (max ?? 0) + 1;
    }
}
