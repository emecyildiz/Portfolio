using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Portfolio.Services;

public static class MarkdownContentRenderer
{
    private static readonly MarkdownPipeline SafePipeline = BuildPipeline();

    public static string ToHtml(string? markdown) =>
        Markdown.ToHtml(markdown ?? string.Empty, SafePipeline);

    private static MarkdownPipeline BuildPipeline()
    {
        var builder = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .DisableHtml();

        builder.DocumentProcessed += document =>
        {
            foreach (var link in document.Descendants<LinkInline>())
            {
                if (!SafeUrlPolicy.IsSafeNavigationUrl(link.Url))
                    link.Url = "#";
            }
        };

        return builder.Build();
    }
}
