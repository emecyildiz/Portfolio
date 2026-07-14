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
                if (!IsSafeLink(link.Url))
                    link.Url = "#";
            }
        };

        return builder.Build();
    }

    private static bool IsSafeLink(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return true;

        var value = url.Trim();
        if (value.StartsWith('#') || value.StartsWith('/') || value.StartsWith("./") ||
            value.StartsWith("../") || value.StartsWith('?'))
        {
            return true;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var absoluteUri))
            return absoluteUri.Scheme is "http" or "https" or "mailto";

        // Şemasız göreli bağlantılara izin ver; bilinmeyen "scheme:" değerlerini reddet.
        return !value.Contains(':');
    }
}
