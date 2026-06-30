namespace Portfolio.Models.ExtraData;

public class ElectronicsExtraData
{
    public string? Microcontroller { get; set; }
    public List<string>? Components { get; set; }
    public string? SchematicUrl { get; set; }
    public string? ProgrammingLanguage { get; set; }
    public bool? IsOpenSource { get; set; }
}

public class WebAppExtraData
{
    public List<string>? TechStack { get; set; }
    public int? TeamSize { get; set; }
    public string? MyRole { get; set; }
    public string? Subdomain { get; set; }
    public bool? IsSchoolProject { get; set; }
}

public class TeamMember
{
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? GithubUrl { get; set; }
    public string? LinkedinUrl { get; set; }
}