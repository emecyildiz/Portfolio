using System.Globalization;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<NpgsqlDataSource>(_ =>
{
    var configuredConnectionString = builder.Configuration.GetConnectionString("Cti");
    var connectionString = configuredConnectionString ?? new NpgsqlConnectionStringBuilder
    {
        Host = builder.Configuration["CtiDatabase:Host"] ?? "cti-db",
        Port = 5432,
        Database = builder.Configuration["CtiDatabase:Name"] ?? "cti",
        Username = builder.Configuration["CtiDatabase:Username"] ?? "cti_dashboard",
        Password = builder.Configuration["CTI_DASHBOARD_PASSWORD"]
            ?? throw new InvalidOperationException("CTI_DASHBOARD_PASSWORD is required."),
        MaxPoolSize = 5,
        Timeout = 5,
        CommandTimeout = 10,
        ApplicationName = "emecworks-cti-dashboard"
    }.ConnectionString;
    var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
    return dataSourceBuilder.Build();
});

var app = builder.Build();
var environment = app.Environment;
var expectedAccessEmail = environment.IsDevelopment()
    ? null
    : builder.Configuration["CTI_ACCESS_EMAIL"]
        ?? throw new InvalidOperationException("CTI_ACCESS_EMAIL is required in production.");

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/health"))
    {
        await next();
        return;
    }

    var authenticatedEmail = context.Request.Headers["Cf-Access-Authenticated-User-Email"]
        .FirstOrDefault();
    if (!environment.IsDevelopment() &&
        !string.Equals(authenticatedEmail, expectedAccessEmail, StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsync("Cloudflare Access authentication is required.");
        return;
    }

    context.Response.Headers["Cache-Control"] = "private, no-store, max-age=0";
    context.Response.Headers["Pragma"] = "no-cache";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'none'; style-src 'self'; img-src 'self'; form-action 'self'; frame-ancestors 'none'; base-uri 'none'";
    await next();
});

app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = static context =>
    {
        context.Context.Response.Headers["Cache-Control"] = "private, max-age=3600";
        context.Context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    }
});

app.MapGet("/health/ready", async (NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
{
    await using var command = dataSource.CreateCommand("SELECT 1;");
    await command.ExecuteScalarAsync(cancellationToken);
    return Results.Text("ready", "text/plain; charset=utf-8");
});

app.MapGet("/", async (
    HttpContext context,
    NpgsqlDataSource dataSource,
    string? q,
    string? category,
    string? severity,
    int? page,
    CancellationToken cancellationToken) =>
{
    const int pageSize = 20;
    var currentPage = Math.Clamp(page ?? 1, 1, 1000);
    var normalizedQuery = NormalizeFilter(q, 120);
    var normalizedCategory = NormalizeEnum(category,
        ["malware", "vulnerability", "data_breach", "threat_intelligence", "other"]);
    var normalizedSeverity = NormalizeEnum(severity,
        ["critical", "high", "medium", "low", "unknown"]);

    await using var command = dataSource.CreateCommand("""
        SELECT id, title, category, severity, summary_tr, canonical_url, published_at,
               count(*) OVER() AS total_count
        FROM cti.dashboard_articles
        WHERE (@query = '' OR title ILIKE '%' || @query || '%' OR summary_tr ILIKE '%' || @query || '%')
          AND (@category = '' OR category = @category)
          AND (@severity = '' OR severity = @severity)
        ORDER BY published_at DESC, id DESC
        LIMIT @limit OFFSET @offset;
        """);
    command.Parameters.AddWithValue("query", normalizedQuery);
    command.Parameters.AddWithValue("category", normalizedCategory);
    command.Parameters.AddWithValue("severity", normalizedSeverity);
    command.Parameters.AddWithValue("limit", pageSize);
    command.Parameters.AddWithValue("offset", (currentPage - 1) * pageSize);

    var articles = new List<ArticleListItem>();
    long totalCount = 0;
    await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
    {
        while (await reader.ReadAsync(cancellationToken))
        {
            totalCount = reader.GetInt64(7);
            articles.Add(new ArticleListItem(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetDateTime(6)));
        }
    }

    var model = new ArticleIndexModel(
        articles,
        normalizedQuery,
        normalizedCategory,
        normalizedSeverity,
        currentPage,
        (int)Math.Ceiling(totalCount / (double)pageSize),
        totalCount,
        GetAuthenticatedIdentity(context));
    return Results.Content(HtmlPages.Index(model), "text/html; charset=utf-8");
});

app.MapGet("/articles/{id:long}", async (
    HttpContext context,
    NpgsqlDataSource dataSource,
    long id,
    CancellationToken cancellationToken) =>
{
    await using var command = dataSource.CreateCommand("""
        SELECT id, title, category, severity, summary_tr, canonical_url, published_at,
               analyzed_at, source_names
        FROM cti.dashboard_articles
        WHERE id = @id;
        """);
    command.Parameters.AddWithValue("id", id);

    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    if (!await reader.ReadAsync(cancellationToken))
    {
        return Results.NotFound();
    }

    var article = new ArticleDetail(
        reader.GetInt64(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetString(5),
        reader.GetDateTime(6),
        reader.IsDBNull(7) ? null : reader.GetDateTime(7),
        reader.GetFieldValue<string[]>(8));
    return Results.Content(HtmlPages.Article(article, GetAuthenticatedIdentity(context)), "text/html; charset=utf-8");
});

app.MapGet("/reports", async (
    HttpContext context,
    NpgsqlDataSource dataSource,
    CancellationToken cancellationToken) =>
{
    await using var command = dataSource.CreateCommand("""
        SELECT id, title, content, status, window_start, window_end, generated_at, sent_at
        FROM cti.dashboard_reports
        ORDER BY generated_at DESC, id DESC
        LIMIT 24;
        """);
    var reports = new List<ReportItem>();
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    while (await reader.ReadAsync(cancellationToken))
    {
        reports.Add(new ReportItem(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetDateTime(4),
            reader.GetDateTime(5),
            reader.GetDateTime(6),
            reader.IsDBNull(7) ? null : reader.GetDateTime(7)));
    }

    return Results.Content(
        HtmlPages.Reports(reports, GetAuthenticatedIdentity(context)),
        "text/html; charset=utf-8");
});

app.Run();

static string NormalizeFilter(string? value, int maxLength)
{
    if (string.IsNullOrWhiteSpace(value)) return string.Empty;
    var normalized = value.Trim();
    return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
}

static string NormalizeEnum(string? value, IReadOnlyCollection<string> allowed)
{
    var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
    return allowed.Contains(normalized) ? normalized : string.Empty;
}

static string GetAuthenticatedIdentity(HttpContext context) =>
    string.IsNullOrWhiteSpace(context.Request.Headers["Cf-Access-Authenticated-User-Email"].FirstOrDefault())
        ? "Local development"
        : "Authenticated owner";

internal sealed record ArticleListItem(
    long Id, string Title, string Category, string Severity, string Summary,
    string Url, DateTime PublishedAt);

internal sealed record ArticleIndexModel(
    IReadOnlyList<ArticleListItem> Articles, string Query, string Category, string Severity,
    int Page, int TotalPages, long TotalCount, string AuthenticatedEmail);

internal sealed record ArticleDetail(
    long Id, string Title, string Category, string Severity, string Summary,
    string Url, DateTime PublishedAt, DateTime? AnalyzedAt, string[] Sources);

internal sealed record ReportItem(
    long Id, string Title, string Content, string Status, DateTime WindowStart,
    DateTime WindowEnd, DateTime GeneratedAt, DateTime? SentAt);

internal static class HtmlPages
{
    private static string E(string? value) => System.Net.WebUtility.HtmlEncode(value ?? string.Empty);
    private static string D(DateTime value) => value.ToUniversalTime().ToString("dd MMM yyyy HH:mm 'UTC'", CultureInfo.InvariantCulture);

    internal static string Index(ArticleIndexModel model)
    {
        var cards = model.Articles.Count == 0
            ? "<div class=\"empty\">No matching analyzed articles were found.</div>"
            : string.Join("", model.Articles.Select(article => $$"""
                <article class="record">
                  <div class="record-meta"><span class="tag {{E(article.Category)}}">{{E(Label(article.Category))}}</span><span class="severity {{E(article.Severity)}}">{{E(article.Severity)}}</span><time>{{D(article.PublishedAt)}}</time></div>
                  <h2><a href="/articles/{{article.Id}}">{{E(article.Title)}}</a></h2>
                  <p>{{E(article.Summary)}}</p>
                  <a class="source" href="{{E(article.Url)}}" target="_blank" rel="noopener noreferrer">Open original source ↗</a>
                </article>
                """));
        var previous = model.Page > 1 ? PageLink("Previous", model, model.Page - 1) : string.Empty;
        var next = model.Page < model.TotalPages ? PageLink("Next", model, model.Page + 1) : string.Empty;
        return Layout("CTI Intelligence", model.AuthenticatedEmail, $$"""
            <section class="hero"><p class="eyebrow">PRIVATE THREAT INTELLIGENCE</p><h1>Intelligence inbox</h1><p>Analyzed security news retained for the current research window.</p></section>
            <form class="filters" method="get">
              <label>Search<input type="search" name="q" value="{{E(model.Query)}}" maxlength="120" placeholder="Title or executive summary"></label>
              <label>Category<select name="category">{{Options(CategoryOptions, model.Category)}}</select></label>
              <label>Severity<select name="severity">{{Options(SeverityOptions, model.Severity)}}</select></label>
              <button type="submit">Apply filters</button><a class="reset" href="/">Reset</a>
            </form>
            <div class="count">{{model.TotalCount}} analyzed records</div>
            <section class="records">{{cards}}</section>
            <nav class="pagination">{{previous}}<span>Page {{model.Page}} / {{Math.Max(model.TotalPages, 1)}}</span>{{next}}</nav>
            """);
    }

    internal static string Article(ArticleDetail article, string email) => Layout(article.Title, email, $$"""
        <a class="back" href="/">← Back to intelligence inbox</a>
        <article class="detail">
          <div class="record-meta"><span class="tag {{E(article.Category)}}">{{E(Label(article.Category))}}</span><span class="severity {{E(article.Severity)}}">{{E(article.Severity)}}</span></div>
          <h1>{{E(article.Title)}}</h1>
          <p class="summary">{{E(article.Summary)}}</p>
          <dl><div><dt>Published</dt><dd>{{D(article.PublishedAt)}}</dd></div><div><dt>Analyzed</dt><dd>{{(article.AnalyzedAt is null ? "Unknown" : D(article.AnalyzedAt.Value))}}</dd></div><div><dt>Sources</dt><dd>{{E(string.Join(", ", article.Sources))}}</dd></div></dl>
          <a class="primary-link" href="{{E(article.Url)}}" target="_blank" rel="noopener noreferrer">Read original report ↗</a>
        </article>
        """);

    internal static string Reports(IReadOnlyList<ReportItem> reports, string email)
    {
        var body = reports.Count == 0
            ? "<div class=\"empty\">No completed reports are available.</div>"
            : string.Join("", reports.Select(report => $$"""
                <details class="report">
                  <summary><span><strong>{{E(report.Title)}}</strong><small>{{D(report.WindowStart)}} — {{D(report.WindowEnd)}}</small></span><span class="status">{{E(report.Status)}}</span></summary>
                  <div class="report-body"><pre>{{E(report.Content)}}</pre><p>Generated {{D(report.GeneratedAt)}}{{(report.SentAt is null ? "" : $" · Sent {D(report.SentAt.Value)}")}}</p></div>
                </details>
                """));
        return Layout("Weekly reports", email, $$"""
            <section class="hero"><p class="eyebrow">PRIVATE REPORT ARCHIVE</p><h1>Weekly assessments</h1><p>Generated reports are retained for eight weeks.</p></section>
            <section class="reports">{{body}}</section>
            """);
    }

    private static string Layout(string title, string email, string content) => $$"""
        <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta name="robots" content="noindex,nofollow,noarchive"><title>{{E(title)}} — Emecworks CTI</title><link rel="stylesheet" href="/app.css"></head>
        <body><header><a class="brand" href="/"><b>Emecworks</b><span>CTI OPERATIONS</span></a><nav><a href="/">Articles</a><a href="/reports">Reports</a></nav><span class="identity">{{E(email)}}</span></header><main>{{content}}</main><footer>Private research system · Content remains untrusted until independently verified.</footer></body></html>
        """;

    private static string PageLink(string label, ArticleIndexModel model, int page)
    {
        var values = new Dictionary<string, string>
        {
            ["page"] = page.ToString(CultureInfo.InvariantCulture),
            ["q"] = model.Query,
            ["category"] = model.Category,
            ["severity"] = model.Severity
        };
        var query = string.Join("&", values.Where(x => x.Value.Length > 0)
            .Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
        return $"<a href=\"/?{E(query)}\">{E(label)}</a>";
    }

    private static string Options(IEnumerable<(string Value, string Label)> options, string selected) =>
        string.Join("", options.Select(option =>
            $"<option value=\"{E(option.Value)}\"{(option.Value == selected ? " selected" : "")}>{E(option.Label)}</option>"));

    private static string Label(string value) => value switch
    {
        "data_breach" => "Data breach",
        "threat_intelligence" => "Threat intelligence",
        _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.Replace('_', ' '))
    };

    private static readonly (string Value, string Label)[] CategoryOptions =
    [
        ("", "All categories"), ("malware", "Malware"), ("vulnerability", "Vulnerability"),
        ("data_breach", "Data breach"), ("threat_intelligence", "Threat intelligence"), ("other", "Other")
    ];

    private static readonly (string Value, string Label)[] SeverityOptions =
    [
        ("", "All severities"), ("critical", "Critical"), ("high", "High"),
        ("medium", "Medium"), ("low", "Low"), ("unknown", "Unknown")
    ];
}
