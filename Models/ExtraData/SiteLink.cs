namespace Portfolio.Models.ExtraData;

public sealed class SiteLink
{
    public string Label { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public bool OpenInNewTab { get; set; } = true;
}
