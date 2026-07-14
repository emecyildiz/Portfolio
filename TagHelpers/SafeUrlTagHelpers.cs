using Microsoft.AspNetCore.Razor.TagHelpers;
using Portfolio.Services;

namespace Portfolio.TagHelpers;

[HtmlTargetElement("a", Attributes = "href")]
public sealed class SafeAnchorTagHelper : TagHelper
{
    public override int Order => int.MaxValue;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var href = output.Attributes["href"]?.Value?.ToString();
        if (!SafeUrlPolicy.IsSafeNavigationUrl(href))
            output.Attributes.SetAttribute("href", "#");

        var target = output.Attributes["target"]?.Value?.ToString();
        if (!string.Equals(target, "_blank", StringComparison.OrdinalIgnoreCase))
            return;

        var relTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existingRel = output.Attributes["rel"]?.Value?.ToString();
        if (!string.IsNullOrWhiteSpace(existingRel))
        {
            foreach (var token in existingRel.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                relTokens.Add(token);
        }

        relTokens.Add("noopener");
        relTokens.Add("noreferrer");
        output.Attributes.SetAttribute("rel", string.Join(' ', relTokens));
    }
}

[HtmlTargetElement("img", Attributes = "src")]
public sealed class SafeImageSourceTagHelper : TagHelper
{
    public override int Order => int.MaxValue;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var source = output.Attributes["src"]?.Value?.ToString();
        if (!SafeUrlPolicy.IsSafeWebResourceUrl(source))
            output.Attributes.SetAttribute("src", string.Empty);
    }
}
