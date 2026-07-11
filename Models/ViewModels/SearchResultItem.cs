namespace Portfolio.Models.ViewModels;

public class SearchResultItem
{
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string TypeLabel { get; set; } = string.Empty;
    public string ColorClass { get; set; } = string.Empty;
    public DateTime? Date { get; set; }
}