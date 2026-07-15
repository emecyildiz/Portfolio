using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Models;

namespace Portfolio.Services;

public interface IMediaService
{
    /// <summary>
    /// Saves an uploaded file, adds it to the media table, and returns the Media entity.
    /// </summary>
    Task<Media> SaveAsync(IFormFile file, string entityType, int entityId,
                          string? altText = null, string? caption = null);

    /// <summary>
    /// Validates an image before an entity is saved so invalid uploads cannot leave partial records.
    /// Returns null when the upload is valid.
    /// </summary>
    Task<string?> GetUploadValidationErrorAsync(IFormFile file);

    /// <summary>
    /// Gets all images attached to a content item.
    /// </summary>
    Task<List<Media>> GetByEntityAsync(string entityType, int entityId);

    /// <summary>
    /// Deletes both the image file and its database record.
    /// </summary>
    Task DeleteAsync(int mediaId);

    /// <summary>
    /// Deletes an image only when it belongs to the specified content item.
    /// </summary>
    Task<bool> DeleteAsync(int mediaId, string entityType, int entityId);

    /// <summary>
    /// Marks one image as the cover and clears IsCover on the others.
    /// </summary>
    Task<bool> SetCoverAsync(int mediaId, string entityType, int entityId);
}

public class MediaService : IMediaService
{
    private static readonly HashSet<string> AllowedEntityTypes =
        ["project", "security_research", "homelab_post", "blog_post", "team_project"];

    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;

    public MediaService(AppDbContext db, IWebHostEnvironment env, IConfiguration config)
    {
        _db = db;
        _env = env;
        _config = config;
    }

    public async Task<Media> SaveAsync(IFormFile file, string entityType, int entityId,
                                       string? altText = null, string? caption = null)
    {
        if (!AllowedEntityTypes.Contains(entityType) || entityId <= 0)
            throw new InvalidOperationException("Invalid media target.");

        if (!await EntityExistsAsync(entityType, entityId))
            throw new InvalidOperationException("The content item for this media could not be found.");

        var (validatedUpload, validationError) = await ValidateUploadAsync(file);
        if (validatedUpload == null)
            throw new InvalidOperationException(validationError ?? "The image upload is invalid.");

        // Generate a unique filename; never include the user-supplied name in the physical path.
        var uniqueName = $"{Guid.NewGuid()}{validatedUpload.Extension}";

        // Storage path example: wwwroot/uploads/project/42/file.jpg
        var relativePath = Path.Combine("uploads", entityType, entityId.ToString(), uniqueName);
        var physicalPath = Path.Combine(_env.WebRootPath, relativePath);

        // Create the directory when it does not exist.
        Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);

        // Create the media record.
        var media = new Media
        {
            EntityType = entityType,
            EntityId = entityId,
            Url = "/" + relativePath.Replace('\\', '/'),
            Filename = UploadFileValidator.GetSafeFileName(file.FileName),
            AltText = altText,
            Caption = caption,
            MimeType = validatedUpload.MimeType,
            FileSizeBytes = file.Length,
            SortOrder = await GetNextSortOrderAsync(entityType, entityId),
            IsCover = false,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            // Do not leave a partial file behind when file or database operations fail.
            await using (var stream = new FileStream(
                             physicalPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await file.CopyToAsync(stream);
            }

            _db.Media.Add(media);
            await _db.SaveChangesAsync();
        }
        catch
        {
            if (_db.Entry(media).State != EntityState.Detached)
                _db.Entry(media).State = EntityState.Detached;

            TryDeletePhysicalFile(physicalPath);
            throw;
        }

        return media;
    }

    public async Task<string?> GetUploadValidationErrorAsync(IFormFile file)
    {
        var (_, error) = await ValidateUploadAsync(file);
        return error;
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
            ?? throw new InvalidOperationException("Media could not be found.");

        await DeleteMediaAsync(media);
    }

    public async Task<bool> DeleteAsync(int mediaId, string entityType, int entityId)
    {
        if (!AllowedEntityTypes.Contains(entityType) || entityId <= 0)
            return false;

        var media = await _db.Media.FirstOrDefaultAsync(m =>
            m.Id == mediaId && m.EntityType == entityType && m.EntityId == entityId);

        if (media == null)
            return false;

        await DeleteMediaAsync(media);
        return true;
    }

    private async Task DeleteMediaAsync(Media media)
    {

        await ClearCoverReferenceAsync(media);

        // Delete the physical file.
        var physicalPath = GetSafeUploadPhysicalPath(media.Url);
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

    public async Task<bool> SetCoverAsync(int mediaId, string entityType, int entityId)
    {
        if (!AllowedEntityTypes.Contains(entityType) || entityId <= 0)
            return false;

        // Clear the cover flag on all images first.
        var all = await _db.Media
            .Where(m => m.EntityType == entityType && m.EntityId == entityId)
            .ToListAsync();

        var target = all.FirstOrDefault(m => m.Id == mediaId);
        if (target == null || !await SetCoverReferenceAsync(target))
            return false;

        foreach (var m in all)
            m.IsCover = false;

        target.IsCover = true;

        await _db.SaveChangesAsync();
        return true;
    }

    private async Task<bool> SetCoverReferenceAsync(Media media)
    {
        switch (media.EntityType)
        {
            case "project":
                var project = await _db.Projects.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(p => p.Id == media.EntityId);
                if (project == null) return false;
                project.CoverImageUrl = media.Url;
                return true;

            case "security_research":
                var research = await _db.SecurityResearches.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(s => s.Id == media.EntityId);
                if (research == null) return false;
                research.CoverImageUrl = media.Url;
                return true;

            case "homelab_post":
                var homelabPost = await _db.HomelabPosts.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(h => h.Id == media.EntityId);
                if (homelabPost == null) return false;
                homelabPost.CoverImageUrl = media.Url;
                return true;

            case "blog_post":
                var blogPost = await _db.BlogPosts.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(b => b.Id == media.EntityId);
                if (blogPost == null) return false;
                blogPost.CoverImageUrl = media.Url;
                return true;

            case "team_project":
                var teamProject = await _db.TeamProjects.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(t => t.Id == media.EntityId);
                if (teamProject == null) return false;
                teamProject.CoverImageUrl = media.Url;
                return true;

            default:
                return false;
        }
    }

    private Task<bool> EntityExistsAsync(string entityType, int entityId) => entityType switch
    {
        "project" => _db.Projects.IgnoreQueryFilters().AnyAsync(p => p.Id == entityId),
        "security_research" => _db.SecurityResearches.IgnoreQueryFilters().AnyAsync(s => s.Id == entityId),
        "homelab_post" => _db.HomelabPosts.IgnoreQueryFilters().AnyAsync(h => h.Id == entityId),
        "blog_post" => _db.BlogPosts.IgnoreQueryFilters().AnyAsync(b => b.Id == entityId),
        "team_project" => _db.TeamProjects.IgnoreQueryFilters().AnyAsync(t => t.Id == entityId),
        _ => Task.FromResult(false)
    };

    private string GetSafeUploadPhysicalPath(string mediaUrl)
    {
        var uploadsRoot = Path.GetFullPath(Path.Combine(_env.WebRootPath, "uploads"));
        var relativePath = mediaUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var physicalPath = Path.GetFullPath(Path.Combine(_env.WebRootPath, relativePath));
        var rootPrefix = uploadsRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!physicalPath.StartsWith(rootPrefix, comparison))
            throw new InvalidOperationException("Invalid media file path.");

        return physicalPath;
    }

    private static void TryDeletePhysicalFile(string physicalPath)
    {
        try
        {
            if (File.Exists(physicalPath))
                File.Delete(physicalPath);
        }
        catch (IOException)
        {
            // Preserve the original upload or database error; cleanup can happen during maintenance.
        }
        catch (UnauthorizedAccessException)
        {
            // Preserve the original upload or database error; cleanup can happen during maintenance.
        }
    }

    private async Task<(ValidatedUpload? Upload, string? Error)> ValidateUploadAsync(IFormFile file)
    {
        var maxSize = _config.GetValue<long>("MediaStorage:MaxFileSizeBytes", 10_485_760);
        if (file.Length <= 0)
            return (null, "Empty image files cannot be uploaded.");

        if (file.Length > maxSize)
            return (null, $"Each image must be no larger than {maxSize / 1_048_576} MB.");

        var validatedUpload = await UploadFileValidator.ValidateImageAsync(file);
        return validatedUpload == null
            ? (null, "Images must be valid JPEG, PNG, WebP, or GIF files whose extension, MIME type, and contents match.")
            : (validatedUpload, null);
    }

    private async Task<int> GetNextSortOrderAsync(string entityType, int entityId)
    {
        var max = await _db.Media
            .Where(m => m.EntityType == entityType && m.EntityId == entityId)
            .MaxAsync(m => (int?)m.SortOrder);
        return (max ?? 0) + 1;
    }
}
