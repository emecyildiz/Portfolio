using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Portfolio.Models;
using Portfolio.Models.Enums;

namespace Portfolio.Services;

public static partial class AdminContentValidator
{
    private const int MaxImagesPerRequest = 20;
    private const int MaxTitleLength = 200;
    private const int MaxSummaryLength = 1_000;
    private const int MaxContentLength = 500_000;
    private const int MaxUrlLength = 2_048;
    private const int MaxListInputLength = 10_000;
    private const int MaxListItems = 100;
    private const int MaxListItemLength = 200;
    private const int MaxSortOrder = 100_000;

    private static readonly HashSet<string> AllowedSeverityLevels =
        ["critical", "high", "medium", "low", "info"];

    public static void ValidateProject(
        ModelStateDictionary modelState,
        Project model,
        ISlugService slugService)
    {
        model.Title = NormalizeRequired(model.Title);
        model.Summary = NormalizeRequired(model.Summary);
        model.Content = NormalizeRequired(model.Content);
        model.GithubUrl = NormalizeOptional(model.GithubUrl);
        model.LiveDemoUrl = NormalizeOptional(model.LiveDemoUrl);
        model.KnowledgeUrl = NormalizeOptional(model.KnowledgeUrl);

        ValidateContentCore(modelState, model.Title, model.Summary, model.Content, slugService);
        ValidateHttpsUrl(modelState, nameof(model.GithubUrl), model.GithubUrl);
        ValidateHttpsUrl(modelState, nameof(model.LiveDemoUrl), model.LiveDemoUrl);
        ValidateKnowledgeUrl(modelState, nameof(model.KnowledgeUrl), model.KnowledgeUrl);
        ValidateEnum(modelState, nameof(model.Status), model.Status);
    }

    public static void ValidateElectronicsFields(
        ModelStateDictionary modelState,
        string? microcontroller,
        string? components,
        string? schematicUrl,
        string? programmingLanguage)
    {
        ValidateOptionalText(modelState, "Microcontroller", microcontroller, 200);
        ValidateCommaSeparatedList(modelState, components, "component");
        ValidateHttpsOrRootRelativeUrl(modelState, "SchematicUrl", schematicUrl);
        ValidateOptionalText(modelState, "ProgrammingLanguage", programmingLanguage, 200);
    }

    public static void ValidateWebAppFields(
        ModelStateDictionary modelState,
        string? techStack,
        int? teamSize,
        string? myRole,
        string? subdomain)
    {
        ValidateCommaSeparatedList(modelState, techStack, "technology");

        if (teamSize is < 1 or > 1_000)
            modelState.AddModelError(string.Empty, "Team size must be between 1 and 1,000.");

        ValidateOptionalText(modelState, string.Empty, myRole, 300, "My role");

        var normalizedSubdomain = NormalizeOptional(subdomain);
        if (normalizedSubdomain != null &&
            (normalizedSubdomain.Length > 253 || !HostnameRegex().IsMatch(normalizedSubdomain)))
        {
            modelState.AddModelError(string.Empty, "Subdomain must be a valid hostname without a scheme or path.");
        }
    }

    public static void ValidateSecurity(
        ModelStateDictionary modelState,
        SecurityResearch model,
        ISlugService slugService,
        string? toolsUsed)
    {
        model.Title = NormalizeRequired(model.Title);
        model.Summary = NormalizeRequired(model.Summary);
        model.Content = NormalizeRequired(model.Content);
        model.TargetCategory = NormalizeOptional(model.TargetCategory);
        model.CveId = NormalizeOptional(model.CveId)?.ToUpperInvariant();
        model.SeverityLevel = NormalizeOptional(model.SeverityLevel)?.ToLowerInvariant();
        model.GithubUrl = NormalizeOptional(model.GithubUrl);
        model.KnowledgeUrl = NormalizeOptional(model.KnowledgeUrl);

        ValidateContentCore(modelState, model.Title, model.Summary, model.Content, slugService);
        ValidateOptionalText(modelState, nameof(model.TargetCategory), model.TargetCategory, 200);
        ValidateOptionalText(modelState, nameof(model.CveId), model.CveId, 50);

        if (model.CveId != null && !CveRegex().IsMatch(model.CveId))
            modelState.AddModelError(nameof(model.CveId), "Use the CVE-YYYY-NNNN format with at least four final digits.");

        if (model.SeverityLevel != null && !AllowedSeverityLevels.Contains(model.SeverityLevel))
            modelState.AddModelError(nameof(model.SeverityLevel), "Select a supported severity level.");

        ValidateCommaSeparatedList(modelState, toolsUsed, "tool");
        ValidateHttpsUrl(modelState, nameof(model.GithubUrl), model.GithubUrl);
        ValidateKnowledgeUrl(modelState, nameof(model.KnowledgeUrl), model.KnowledgeUrl);
        ValidateEnum(modelState, nameof(model.ResearchType), model.ResearchType);
        ValidateEnum(modelState, nameof(model.DisclosureStatus), model.DisclosureStatus);
        ValidateEnum(modelState, nameof(model.Status), model.Status);

        if (model.Status == VisibilityStatus.Public &&
            model.DisclosureStatus != DisclosureStatus.PubliclyDisclosed)
        {
            modelState.AddModelError(
                nameof(model.Status),
                "Research can be public only after it is marked as publicly disclosed.");
        }
    }

    public static void ValidateHomelab(
        ModelStateDictionary modelState,
        HomelabPost model,
        ISlugService slugService,
        string? hardwareUsed,
        string? softwareUsed)
    {
        model.Title = NormalizeRequired(model.Title);
        model.Summary = NormalizeRequired(model.Summary);
        model.Content = NormalizeRequired(model.Content);
        model.NetworkDiagramUrl = NormalizeOptional(model.NetworkDiagramUrl);
        model.KnowledgeUrl = NormalizeOptional(model.KnowledgeUrl);

        ValidateContentCore(modelState, model.Title, model.Summary, model.Content, slugService);
        ValidateCommaSeparatedList(modelState, hardwareUsed, "hardware item");
        ValidateCommaSeparatedList(modelState, softwareUsed, "software item");
        ValidateHttpsOrRootRelativeUrl(modelState, nameof(model.NetworkDiagramUrl), model.NetworkDiagramUrl);
        ValidateKnowledgeUrl(modelState, nameof(model.KnowledgeUrl), model.KnowledgeUrl);
        ValidateEnum(modelState, nameof(model.Topic), model.Topic);
        ValidateEnum(modelState, nameof(model.Status), model.Status);
    }

    public static void ValidateBlog(
        ModelStateDictionary modelState,
        BlogPost model,
        ISlugService slugService)
    {
        model.Title = NormalizeRequired(model.Title);
        model.Summary = NormalizeRequired(model.Summary);
        model.Content = NormalizeRequired(model.Content);

        ValidateContentCore(modelState, model.Title, model.Summary, model.Content, slugService);
        ValidateEnum(modelState, nameof(model.Status), model.Status);
    }

    public static void ValidateTeam(
        ModelStateDictionary modelState,
        TeamProject model,
        ISlugService slugService)
    {
        model.Title = NormalizeRequired(model.Title);
        model.Summary = NormalizeRequired(model.Summary);
        model.Content = NormalizeRequired(model.Content);
        model.EventName = NormalizeOptional(model.EventName);
        model.EventUrl = NormalizeOptional(model.EventUrl);
        model.MyRole = NormalizeRequired(model.MyRole);
        model.Outcome = NormalizeOptional(model.Outcome);
        model.GithubUrl = NormalizeOptional(model.GithubUrl);
        model.LiveDemoUrl = NormalizeOptional(model.LiveDemoUrl);

        ValidateContentCore(modelState, model.Title, model.Summary, model.Content, slugService);
        ValidateOptionalText(modelState, nameof(model.EventName), model.EventName, 300);
        ValidateRequiredText(modelState, nameof(model.MyRole), model.MyRole, 300, "My role");
        ValidateOptionalText(modelState, nameof(model.Outcome), model.Outcome, 500);
        ValidateHttpsUrl(modelState, nameof(model.EventUrl), model.EventUrl);
        ValidateHttpsUrl(modelState, nameof(model.GithubUrl), model.GithubUrl);
        ValidateHttpsUrl(modelState, nameof(model.LiveDemoUrl), model.LiveDemoUrl);
        ValidateEnum(modelState, nameof(model.Status), model.Status);
    }

    public static void ValidatePage(
        ModelStateDictionary modelState,
        Page model,
        ISlugService slugService)
    {
        model.Title = NormalizeRequired(model.Title);
        model.Content = NormalizeRequired(model.Content);
        model.CoverImageUrl = NormalizeOptional(model.CoverImageUrl);

        ValidateRequiredText(modelState, nameof(model.Title), model.Title, MaxTitleLength, "Title");
        ValidateRequiredText(modelState, nameof(model.Content), model.Content, MaxContentLength, "Content");
        ValidateGeneratedSlug(modelState, model.Title, slugService);
        ValidateHttpsOrRootRelativeUrl(modelState, nameof(model.CoverImageUrl), model.CoverImageUrl);
        ValidateEnum(modelState, nameof(model.Status), model.Status);
        ValidateSortOrder(modelState, nameof(model.SortOrder), model.SortOrder);
    }

    public static void ValidateCategory(ModelStateDictionary modelState, Category model)
    {
        model.Name = NormalizeRequired(model.Name);
        model.Slug = NormalizeRequired(model.Slug).ToLowerInvariant();
        model.Description = NormalizeOptional(model.Description);
        model.IconClass = NormalizeOptional(model.IconClass);

        ValidateRequiredText(modelState, nameof(model.Name), model.Name, 200, "Name");
        ValidateRequiredText(modelState, nameof(model.Slug), model.Slug, 100, "Slug");
        ValidateOptionalText(modelState, nameof(model.Description), model.Description, 1_000);
        ValidateOptionalText(modelState, nameof(model.IconClass), model.IconClass, 100);

        if (!string.IsNullOrWhiteSpace(model.Slug) && !SlugRegex().IsMatch(model.Slug))
            modelState.AddModelError(nameof(model.Slug), "Use lowercase letters, numbers, and single hyphens only.");

        if (model.IconClass != null && !IconClassRegex().IsMatch(model.IconClass))
            modelState.AddModelError(nameof(model.IconClass), "Icon classes may contain lowercase letters, numbers, spaces, underscores, and hyphens only.");

        ValidateEnum(modelState, nameof(model.Status), model.Status);
        ValidateSortOrder(modelState, nameof(model.SortOrder), model.SortOrder);
    }

    public static void ValidateCertificate(ModelStateDictionary modelState, Certificate model)
    {
        model.Title = NormalizeRequired(model.Title);
        model.Issuer = NormalizeRequired(model.Issuer);
        model.CredentialId = NormalizeOptional(model.CredentialId);
        model.CredentialUrl = NormalizeOptional(model.CredentialUrl);
        model.ImageUrl = NormalizeOptional(model.ImageUrl);

        ValidateRequiredText(modelState, nameof(model.Title), model.Title, 200, "Title");
        ValidateRequiredText(modelState, nameof(model.Issuer), model.Issuer, 200, "Issuer");
        ValidateOptionalText(modelState, nameof(model.CredentialId), model.CredentialId, 300);
        ValidateHttpsUrl(modelState, nameof(model.CredentialUrl), model.CredentialUrl);
        ValidateHttpsOrRootRelativeUrl(modelState, nameof(model.ImageUrl), model.ImageUrl);
        ValidateEnum(modelState, nameof(model.Status), model.Status);
        ValidateSortOrder(modelState, nameof(model.SortOrder), model.SortOrder);

        if (model.IssuedDate == default)
            modelState.AddModelError(nameof(model.IssuedDate), "Issued date is required.");

        if (model.ExpiryDate.HasValue && model.ExpiryDate.Value < model.IssuedDate)
            modelState.AddModelError(nameof(model.ExpiryDate), "Expiry date cannot be earlier than the issued date.");
    }

    public static void ValidateService(ModelStateDictionary modelState, Service model)
    {
        model.Title = NormalizeRequired(model.Title);
        model.Description = NormalizeRequired(model.Description);
        model.IconClass = NormalizeOptional(model.IconClass);

        ValidateRequiredText(modelState, nameof(model.Title), model.Title, 200, "Title");
        ValidateRequiredText(modelState, nameof(model.Description), model.Description, 5_000, "Description");
        ValidateOptionalText(modelState, nameof(model.IconClass), model.IconClass, 100);

        if (model.IconClass != null && !IconClassRegex().IsMatch(model.IconClass))
            modelState.AddModelError(nameof(model.IconClass), "Icon classes may contain lowercase letters, numbers, spaces, underscores, and hyphens only.");

        ValidateEnum(modelState, nameof(model.Status), model.Status);
        ValidateSortOrder(modelState, nameof(model.SortOrder), model.SortOrder);
    }

    public static void ValidateNote(ModelStateDictionary modelState, Note model)
    {
        model.Title = NormalizeRequired(model.Title);
        model.Content = NormalizeRequired(model.Content);
        model.RelatedUrl = NormalizeOptional(model.RelatedUrl);

        ValidateRequiredText(modelState, nameof(model.Title), model.Title, 200, "Title");
        ValidateRequiredText(modelState, nameof(model.Content), model.Content, 100_000, "Content");
        ValidateHttpsOrRootRelativeUrl(modelState, nameof(model.RelatedUrl), model.RelatedUrl);
        ValidateEnum(modelState, nameof(model.NoteType), model.NoteType);
        ValidateEnum(modelState, nameof(model.Priority), model.Priority);

        if (!model.IsTodo)
        {
            model.IsCompleted = false;
            model.DueDate = null;
        }
    }

    public static async Task ValidateImagesAsync(
        ModelStateDictionary modelState,
        IMediaService mediaService,
        IEnumerable<IFormFile>? images)
    {
        var files = images?.Where(file => file.Length > 0).ToList() ?? [];
        if (files.Count > MaxImagesPerRequest)
        {
            modelState.AddModelError(
                string.Empty,
                $"Upload at most {MaxImagesPerRequest} images in one request.");
            return;
        }

        foreach (var file in files)
        {
            var error = await mediaService.GetUploadValidationErrorAsync(file);
            if (error == null)
                continue;

            var safeName = UploadFileValidator.GetSafeFileName(file.FileName);
            modelState.AddModelError(string.Empty, $"{safeName}: {error}");
        }
    }

    private static void ValidateContentCore(
        ModelStateDictionary modelState,
        string title,
        string summary,
        string content,
        ISlugService slugService)
    {
        ValidateRequiredText(modelState, "Title", title, MaxTitleLength, "Title");
        ValidateRequiredText(modelState, "Summary", summary, MaxSummaryLength, "Summary");
        ValidateRequiredText(modelState, "Content", content, MaxContentLength, "Content");
        ValidateGeneratedSlug(modelState, title, slugService);
    }

    private static void ValidateGeneratedSlug(
        ModelStateDictionary modelState,
        string title,
        ISlugService slugService)
    {
        if (!string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(slugService.Generate(title)))
        {
            modelState.AddModelError(
                "Title",
                "Title must contain at least one letter or number that can be used in the public URL.");
        }
    }

    private static void ValidateRequiredText(
        ModelStateDictionary modelState,
        string key,
        string? value,
        int maxLength,
        string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            modelState.AddModelError(key, $"{label} is required.");
            return;
        }

        if (value.Length > maxLength)
            modelState.AddModelError(key, $"{label} cannot exceed {maxLength:N0} characters.");
    }

    private static void ValidateOptionalText(
        ModelStateDictionary modelState,
        string key,
        string? value,
        int maxLength,
        string? label = null)
    {
        if (value != null && value.Length > maxLength)
            modelState.AddModelError(key, $"{label ?? key} cannot exceed {maxLength:N0} characters.");
    }

    private static void ValidateCommaSeparatedList(
        ModelStateDictionary modelState,
        string? value,
        string itemLabel)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (value.Length > MaxListInputLength)
        {
            modelState.AddModelError(string.Empty, $"The {itemLabel} list is too long.");
            return;
        }

        var items = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (items.Length > MaxListItems || items.Any(item => item.Length > MaxListItemLength))
        {
            modelState.AddModelError(
                string.Empty,
                $"Use at most {MaxListItems} {itemLabel} entries, with no entry longer than {MaxListItemLength} characters.");
        }
    }

    private static void ValidateHttpsUrl(ModelStateDictionary modelState, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (value.Length > MaxUrlLength ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            modelState.AddModelError(key, "Use a valid HTTPS URL.");
        }
    }

    private static void ValidateKnowledgeUrl(ModelStateDictionary modelState, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (value.Length > MaxUrlLength ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, "knowledge.emecworks.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            modelState.AddModelError(
                key,
                "Use a clean https://knowledge.emecworks.com/... documentation URL without query parameters or fragments.");
        }
    }

    private static void ValidateHttpsOrRootRelativeUrl(
        ModelStateDictionary modelState,
        string key,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (value.Length > MaxUrlLength)
        {
            modelState.AddModelError(key, "URL cannot exceed 2,048 characters.");
            return;
        }

        var isRootRelative = value.StartsWith('/') &&
                             !value.StartsWith("//", StringComparison.Ordinal) &&
                             !value.Contains('\\') &&
                             !value.Contains(':') &&
                             Uri.TryCreate(value, UriKind.Relative, out _);

        var isHttps = Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
                      uri.Scheme == Uri.UriSchemeHttps &&
                      !string.IsNullOrWhiteSpace(uri.Host);

        if (!isRootRelative && !isHttps)
            modelState.AddModelError(key, "Use a root-relative path or a valid HTTPS URL.");
    }

    private static void ValidateEnum<TEnum>(ModelStateDictionary modelState, string key, TEnum value)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
            modelState.AddModelError(key, "Select a valid option.");
    }

    private static void ValidateSortOrder(ModelStateDictionary modelState, string key, int value)
    {
        if (value is < -MaxSortOrder or > MaxSortOrder)
            modelState.AddModelError(key, $"Sort order must be between {-MaxSortOrder:N0} and {MaxSortOrder:N0}.");
    }

    private static string NormalizeRequired(string? value) => value?.Trim() ?? string.Empty;

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugRegex();

    [GeneratedRegex("^[a-z0-9 _-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex IconClassRegex();

    [GeneratedRegex("^(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\\.)+[A-Za-z]{2,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex HostnameRegex();

    [GeneratedRegex("^CVE-[0-9]{4}-[0-9]{4,}$", RegexOptions.CultureInvariant)]
    private static partial Regex CveRegex();
}
