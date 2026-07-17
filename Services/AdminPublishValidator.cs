using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Portfolio.Models;
using Portfolio.Models.Enums;
using Portfolio.Models.ExtraData;

namespace Portfolio.Services;

public static class AdminPublishValidator
{
    private const int MaxStoredJsonLength = 500_000;
    private const string StoredDataError = "Stored structured data is invalid. Open the item in Edit and save it again.";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static bool CanPublishElectronics(
        ModelStateDictionary modelState,
        Project project,
        ISlugService slugService)
    {
        var originalStatus = BeginPublicValidation(modelState, project);
        AdminContentValidator.ValidateProject(modelState, project, slugService);

        if (TryDeserialize(project.ExtraData, modelState, out ElectronicsExtraData? extra))
        {
            AdminContentValidator.ValidateElectronicsFields(
                modelState,
                extra?.Microcontroller,
                JoinList(extra?.Components),
                extra?.SchematicUrl,
                extra?.ProgrammingLanguage);
        }

        project.Status = originalStatus;
        return modelState.IsValid;
    }

    public static bool CanPublishWebApp(
        ModelStateDictionary modelState,
        Project project,
        ISlugService slugService)
    {
        var originalStatus = BeginPublicValidation(modelState, project);
        AdminContentValidator.ValidateProject(modelState, project, slugService);

        if (TryDeserialize(project.ExtraData, modelState, out WebAppExtraData? extra))
        {
            AdminContentValidator.ValidateWebAppFields(
                modelState,
                JoinList(extra?.TechStack),
                extra?.TeamSize,
                extra?.MyRole,
                extra?.Subdomain);
        }

        project.Status = originalStatus;
        return modelState.IsValid;
    }

    public static bool CanPublishSecurity(
        ModelStateDictionary modelState,
        SecurityResearch research,
        ISlugService slugService)
    {
        var originalStatus = research.Status;
        modelState.Clear();
        research.Status = VisibilityStatus.Public;

        var tools = ReadStringList(research.ToolsUsed, modelState);
        AdminContentValidator.ValidateSecurity(modelState, research, slugService, JoinList(tools));

        research.Status = originalStatus;
        return modelState.IsValid;
    }

    public static bool CanPublishHomelab(
        ModelStateDictionary modelState,
        HomelabPost post,
        ISlugService slugService)
    {
        var originalStatus = post.Status;
        modelState.Clear();
        post.Status = VisibilityStatus.Public;

        var hardware = ReadStringList(post.HardwareUsed, modelState);
        var software = ReadStringList(post.SoftwareUsed, modelState);
        AdminContentValidator.ValidateHomelab(
            modelState,
            post,
            slugService,
            JoinList(hardware),
            JoinList(software));

        if (!NetworkTopologyJsonService.TryNormalize(
                post.NetworkTopology, out _, out _, out var topologyValidationError))
        {
            modelState.AddModelError(
                string.Empty,
                topologyValidationError ?? StoredDataError);
        }

        post.Status = originalStatus;
        return modelState.IsValid;
    }

    public static bool CanPublishBlog(
        ModelStateDictionary modelState,
        BlogPost post,
        ISlugService slugService)
    {
        var originalStatus = post.Status;
        modelState.Clear();
        post.Status = VisibilityStatus.Public;
        AdminContentValidator.ValidateBlog(modelState, post, slugService);
        post.Status = originalStatus;
        return modelState.IsValid;
    }

    public static bool CanPublishTeam(
        ModelStateDictionary modelState,
        TeamProject project,
        ISlugService slugService)
    {
        var originalStatus = project.Status;
        modelState.Clear();
        project.Status = VisibilityStatus.Public;
        AdminContentValidator.ValidateTeam(modelState, project, slugService);

        if (!TeamMemberJsonService.TryNormalize(project.TeamMembers, out _, out _))
            modelState.AddModelError(string.Empty, StoredDataError);

        project.Status = originalStatus;
        return modelState.IsValid;
    }

    public static bool CanPublishPage(
        ModelStateDictionary modelState,
        Page page,
        ISlugService slugService)
    {
        var originalStatus = page.Status;
        modelState.Clear();
        page.Status = VisibilityStatus.Public;
        AdminContentValidator.ValidatePage(modelState, page, slugService);
        page.Status = originalStatus;
        return modelState.IsValid;
    }

    private static VisibilityStatus BeginPublicValidation(
        ModelStateDictionary modelState,
        Project project)
    {
        var originalStatus = project.Status;
        modelState.Clear();
        project.Status = VisibilityStatus.Public;
        return originalStatus;
    }

    private static bool TryDeserialize<T>(
        string? json,
        ModelStateDictionary modelState,
        out T? result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(json))
            return true;

        if (json.Length > MaxStoredJsonLength)
        {
            modelState.AddModelError(string.Empty, StoredDataError);
            return false;
        }

        try
        {
            result = JsonSerializer.Deserialize<T>(json, JsonOptions);
            return true;
        }
        catch (JsonException)
        {
            modelState.AddModelError(string.Empty, StoredDataError);
            return false;
        }
    }

    private static List<string>? ReadStringList(
        string? json,
        ModelStateDictionary modelState)
    {
        return TryDeserialize(json, modelState, out List<string>? values)
            ? values
            : null;
    }

    private static string? JoinList(IEnumerable<string>? values) =>
        values == null ? null : string.Join(", ", values);
}
