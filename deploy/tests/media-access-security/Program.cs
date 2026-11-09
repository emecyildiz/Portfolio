using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Portfolio.Data;
using Portfolio.Models;
using Portfolio.Models.Enums;
using Portfolio.Services;

var baseline = args.Contains("--baseline");
var root = Path.Combine(Path.GetTempPath(), "media-access-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(root, "uploads", "project", "1"));
await File.WriteAllTextAsync(Path.Combine(root, "uploads", "project", "1", "sample.png"), "synthetic-private-image");
await File.WriteAllTextAsync(Path.Combine(root, "style.css"), "body{}");
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { WebRootPath = root });
builder.Logging.ClearProviders();
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite("Data Source=" + Path.Combine(root, "fixture.db")));
builder.Configuration["MediaStorage:PublicOrigins:0"] = "https://emecworks.com";
builder.Configuration["MediaStorage:PublicOrigins:1"] = "https://www.emecworks.com";
if (!baseline)
{
    var policy = typeof(MediaService).Assembly.GetType("Portfolio.Services.UploadAccessPolicy", throwOnError: true)!;
    builder.Services.AddScoped(policy);
}
await using var app = builder.Build();
app.Use(async (context, next) =>
{
    // Test-only identity; never added to the production application.
    if (context.Request.Headers["X-Fixture-Admin"] == "yes")
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "fixture-admin") }, "fixture"));
    await next();
});
if (!baseline)
    app.UseMiddleware(typeof(MediaService).Assembly.GetType("Portfolio.Middleware.UploadAccessMiddleware", throwOnError: true)!);
app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(root) });
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
    db.Categories.Add(new Category { Id = 1, Name = "Fixture", Slug = "fixture", Status = VisibilityStatus.Public });
    db.Projects.Add(new Project { Id = 1, CategoryId = 1, Title = "Fixture", Slug = "fixture", Status = VisibilityStatus.Private });
    db.Media.Add(new Media { EntityType = "project", EntityId = 1, Url = "/uploads/project/1/sample.png", Filename = "sample.png", MimeType = "image/png" });
    await db.SaveChangesAsync();
}
var checks = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + description);
    checks++;
}
await app.StartAsync();
try
{
    var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    builder.Configuration["MediaStorage:PublicOrigins:2"] = "https://" + new Uri(address).Authority;
    using var client = new HttpClient { BaseAddress = new Uri(address) };
    var response = await client.GetAsync("/uploads/project/1/sample.png");
    if (baseline)
    {
        Check(response.StatusCode == HttpStatusCode.OK, "original static pipeline serves a private parent's image");
        Check(await response.Content.ReadAsStringAsync() == "synthetic-private-image", "original response exposes file bytes");
        using var css = await client.GetAsync("/style.css");
        Check(css.StatusCode == HttpStatusCode.OK, "ordinary public asset control");
        Console.WriteLine($"PASS: {checks} baseline reproductions/controls.");
    }
    else
    {
        Check(response.StatusCode == HttpStatusCode.NotFound, "private media denied");
        Check(response.Headers.CacheControl?.NoStore == true, "private denial is not cacheable");
        foreach (var rawPath in new[] { "//uploads/project/1/sample.png", "///uploads/project/1/sample.png", "/uploads\\project\\1\\sample.png", "/%75ploads/project/1/sample.png", "/uploads%2Fproject%2F1%2Fsample.png", "/uploads//project/1/sample.png" })
        {
            using var rawRequest = new HttpRequestMessage(HttpMethod.Get,
                new Uri(client.BaseAddress!.AbsoluteUri.TrimEnd('/') + rawPath,
                    new UriCreationOptions { DangerousDisablePathAndQueryCanonicalization = true }));
            using var rawResult = await client.SendAsync(rawRequest);
            Check(rawResult.StatusCode == HttpStatusCode.NotFound, "raw private path denied: " + rawPath);
            Check(!(await rawResult.Content.ReadAsStringAsync()).Contains("synthetic-private-image"), "raw path returns no private bytes");
            Check(rawResult.Headers.CacheControl?.NoStore == true, "raw alias denial is not cacheable");
        }
        async Task Edit(Func<AppDbContext, Task> action)
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await action(db);
            await db.SaveChangesAsync();
        }
        async Task WriteAsset(string path)
        {
            var file = Path.Combine(root, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllTextAsync(file, "synthetic-private-image");
        }
        async Task<HttpResponseMessage> Expect(string path, HttpStatusCode status, bool admin = false, HttpMethod? method = null,
            string? range = null, string? etag = null)
        {
            using var request = new HttpRequestMessage(method ?? HttpMethod.Get, path);
            if (admin) request.Headers.Add("X-Fixture-Admin", "yes");
            if (range != null) request.Headers.Add("Range", range);
            if (etag != null) request.Headers.Add("If-None-Match", etag);
            var result = await client.SendAsync(request);
            Check(result.StatusCode == status, $"{method ?? HttpMethod.Get} {path}: expected {status}, received {result.StatusCode}");
            Check(result.Headers.CacheControl?.NoStore == true, "upload allow/deny response has no-store");
            if (status == HttpStatusCode.NotFound)
                Check(!(await result.Content.ReadAsStringAsync()).Contains("synthetic-private-image"), "denied response contains no file bytes");
            return result;
        }
        await Expect("/uploads/project/1/sample.png", HttpStatusCode.OK, admin: true);
        await Expect("/uploads/project/1/sample.png", HttpStatusCode.NotFound, method: HttpMethod.Head);
        await Expect("/uploads/project/1/sample.png?cache=old", HttpStatusCode.NotFound, range: "bytes=0-5");
        await Edit(async db => (await db.Projects.IgnoreQueryFilters().SingleAsync()).Status = VisibilityStatus.Public);
        var visible = await Expect("/uploads/project/1/sample.png", HttpStatusCode.OK);
        Check(await visible.Content.ReadAsStringAsync() == "synthetic-private-image", "public media serves original bytes");
        await Expect("/uploads/project/1/sample.png?cache=new", HttpStatusCode.OK);
        await Expect("/uploads/project/1/sample%2Epng", HttpStatusCode.OK);
        await Expect("/uploads/project/1/sample.png", HttpStatusCode.OK, method: HttpMethod.Head);
        var ranged = await Expect("/uploads/project/1/sample.png", HttpStatusCode.PartialContent, range: "bytes=0-5");
        Check((await ranged.Content.ReadAsStringAsync()).Length == 6, "public byte range is preserved");
        var etag = visible.Headers.ETag!.ToString();
        await Expect("/uploads/project/1/sample.png", HttpStatusCode.NotModified, etag: etag);
        await Edit(async db => (await db.Categories.SingleAsync()).IsPrivate = true);
        await Expect("/uploads/project/1/sample.png", HttpStatusCode.NotFound, etag: etag);
        await Edit(async db => { var c = await db.Categories.SingleAsync(); c.IsPrivate = false; c.Status = VisibilityStatus.Draft; });
        await Expect("/uploads/project/1/sample.png", HttpStatusCode.NotFound);
        await Edit(async db => (await db.Categories.SingleAsync()).Status = VisibilityStatus.Public);
        await Expect("/uploads/project/1/sample.png", HttpStatusCode.OK);

        var types = new[] { "security_research", "homelab_post", "blog_post", "team_project" };
        await Edit(db =>
        {
            db.SecurityResearches.Add(new SecurityResearch { Id = 1, CategoryId = 1, Title = "Research", Slug = "research", Status = VisibilityStatus.Public, DisclosureStatus = DisclosureStatus.PubliclyDisclosed });
            db.HomelabPosts.Add(new HomelabPost { Id = 1, CategoryId = 1, Title = "Lab", Slug = "lab", Status = VisibilityStatus.Public });
            db.BlogPosts.Add(new BlogPost { Id = 1, CategoryId = 1, Title = "Blog", Slug = "blog", Status = VisibilityStatus.Public });
            db.TeamProjects.Add(new TeamProject { Id = 1, CategoryId = 1, Title = "Team", Slug = "team", Status = VisibilityStatus.Public });
            foreach (var type in types)
                db.Media.Add(new Media { EntityType = type, EntityId = 1, Url = $"/uploads/{type}/1/sample.png", Filename = "sample.png" });
            return Task.CompletedTask;
        });
        foreach (var type in types)
        {
            await WriteAsset($"/uploads/{type}/1/sample.png");
            await Expect($"/uploads/{type}/1/sample.png", HttpStatusCode.OK);
        }
        foreach (var disclosure in new[] { DisclosureStatus.Private, DisclosureStatus.Coordinated })
        {
            await Edit(async db => (await db.SecurityResearches.IgnoreQueryFilters().SingleAsync()).DisclosureStatus = disclosure);
            await Expect("/uploads/security_research/1/sample.png", HttpStatusCode.NotFound);
            await Expect("/uploads/security_research/1/sample.png", HttpStatusCode.OK, admin: true);
        }
        await Edit(async db =>
        {
            (await db.SecurityResearches.IgnoreQueryFilters().SingleAsync()).DisclosureStatus = DisclosureStatus.PubliclyDisclosed;
            (await db.HomelabPosts.IgnoreQueryFilters().SingleAsync()).Status = VisibilityStatus.Private;
            (await db.BlogPosts.IgnoreQueryFilters().SingleAsync()).Status = VisibilityStatus.Draft;
            (await db.TeamProjects.IgnoreQueryFilters().SingleAsync()).Status = VisibilityStatus.Private;
        });
        foreach (var type in types.Skip(1)) await Expect($"/uploads/{type}/1/sample.png", HttpStatusCode.NotFound);

        // A public manual alias cannot override authoritative private ownership.
        await Edit(db => { db.Pages.Add(new Page { Id = 1, Title = "Page", Slug = "page", Status = VisibilityStatus.Public,
            CoverImageUrl = "/uploads/other/../homelab_post/1/sample%2Epng?size=1#photo" }); return Task.CompletedTask; });
        await Expect("/uploads/homelab_post/1/sample.png", HttpStatusCode.NotFound);
        await WriteAsset("/uploads/project/1/orphan.png");
        await Expect("/uploads/project/1/orphan.png", HttpStatusCode.NotFound);
        await WriteAsset("/uploads/unknown/orphan.png");
        await Expect("/uploads/unknown/orphan.png", HttpStatusCode.NotFound);
        await Expect("/uploads/project/1/sample.png", HttpStatusCode.NotFound, method: HttpMethod.Post);
        await Expect("/uploads/project/1/sample.png%20", HttpStatusCode.NotFound);
        await Expect("/uploads/project/1/sample.png.", HttpStatusCode.NotFound);
        await Expect("/UPLOADS/project/1/sample.png", OperatingSystem.IsWindows() ? HttpStatusCode.OK : HttpStatusCode.NotFound);

        // Public CV only while it is the currently selected CV, never a temporary sibling.
        await WriteAsset("/uploads/cv/cv.pdf"); await WriteAsset("/uploads/cv/cv.pdf.backup.tmp");
        await Edit(db => { db.SiteSettings.Add(new SiteSettings { CvFileUrl = "/uploads/cv/cv.pdf" }); return Task.CompletedTask; });
        await Expect("/uploads/cv/cv.pdf", HttpStatusCode.OK);
        await Expect("/uploads/cv/cv.pdf.backup.tmp", HttpStatusCode.NotFound);
        await Edit(async db => (await db.SiteSettings.SingleAsync()).CvFileUrl = null);
        await Expect("/uploads/cv/cv.pdf", HttpStatusCode.NotFound);

        // Legacy URL fields, aliases, same-origin absolute URLs and Markdown.
        foreach (var path in new[] { "/uploads/pages/cover.png", "/uploads/certificates/badge.png", "/uploads/schematics/circuit.pdf", "/uploads/diagrams/map.png", "/uploads/markdown/image.png" }) await WriteAsset(path);
        await Edit(async db =>
        {
            var page = await db.Pages.IgnoreQueryFilters().SingleAsync();
            page.CoverImageUrl = "/uploads/pages/../pages/co%76er.png?width=20#photo";
            page.Content = $"![photo](https://{client.BaseAddress!.Authority}/uploads/markdown/image.png?x=1#photo)";
            db.Certificates.Add(new Certificate { Id = 1, Title = "Certificate", ImageUrl = "/uploads/certificates/badge.png", Status = VisibilityStatus.Public });
            (await db.Projects.IgnoreQueryFilters().SingleAsync()).ExtraData = "{\"SchematicUrl\":\"/uploads/schematics/circuit.pdf\"}";
            var lab = await db.HomelabPosts.IgnoreQueryFilters().SingleAsync(); lab.Status = VisibilityStatus.Public; lab.NetworkDiagramUrl = "/uploads/diagrams/map.png";
        });
        foreach (var path in new[] { "/uploads/pages/cover.png", "/uploads/certificates/badge.png", "/uploads/schematics/circuit.pdf", "/uploads/diagrams/map.png", "/uploads/markdown/image.png" }) await Expect(path, HttpStatusCode.OK);
        await Edit(async db =>
        {
            (await db.Pages.IgnoreQueryFilters().SingleAsync()).Status = VisibilityStatus.Draft;
            (await db.Certificates.IgnoreQueryFilters().SingleAsync()).Status = VisibilityStatus.Private;
            (await db.Projects.IgnoreQueryFilters().SingleAsync()).Status = VisibilityStatus.Private;
            (await db.HomelabPosts.IgnoreQueryFilters().SingleAsync()).Status = VisibilityStatus.Draft;
        });
        foreach (var path in new[] { "/uploads/pages/cover.png", "/uploads/certificates/badge.png", "/uploads/schematics/circuit.pdf", "/uploads/diagrams/map.png", "/uploads/markdown/image.png" }) await Expect(path, HttpStatusCode.NotFound);

        // Legacy ownership is canonicalized independently of request Host. A private
        // relative/absolute alias must not disappear from a shared file's owners.
        await WriteAsset("/uploads/legacy/shared.png");
        await Edit(db => { db.Pages.Add(new Page { Id = 2, Title = "Relative", Slug = "relative", Status = VisibilityStatus.Public,
            Content = "![photo](../uploads/legacy/shared.png)" }); return Task.CompletedTask; });
        await Expect("/uploads/legacy/shared.png", HttpStatusCode.OK);
        await Edit(async db =>
        {
            (await db.Pages.IgnoreQueryFilters().SingleAsync(p => p.Id == 2)).Status = VisibilityStatus.Private;
            db.Pages.Add(new Page { Id = 3, Title = "Root", Slug = "root", Status = VisibilityStatus.Public, CoverImageUrl = "/uploads/legacy/shared.png" });
        });
        await Expect("/uploads/legacy/shared.png", HttpStatusCode.NotFound);
        await Edit(async db =>
        {
            var p = await db.Pages.IgnoreQueryFilters().SingleAsync(p => p.Id == 2);
            p.Content = ""; p.CoverImageUrl = "https://www.emecworks.com/uploads/legacy/shared.png";
        });
        foreach (var host in new[] { "other.example", "emecworks.com", "www.emecworks.com" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/uploads/legacy/shared.png");
            request.Headers.Host = host;
            using var result = await client.SendAsync(request);
            Check(result.StatusCode == HttpStatusCode.NotFound, "private absolute owner enforced under Host " + host);
            Check(!(await result.Content.ReadAsStringAsync()).Contains("synthetic-private-image"), "alternate Host contains no private bytes");
        }
        await Edit(async db =>
        {
            db.Pages.Remove(await db.Pages.IgnoreQueryFilters().SingleAsync(p => p.Id == 3));
            (await db.Pages.IgnoreQueryFilters().SingleAsync(p => p.Id == 2)).Status = VisibilityStatus.Public;
        });
        await Expect("/uploads/legacy/shared.png", HttpStatusCode.OK);
        await Edit(async db => (await db.Pages.IgnoreQueryFilters().SingleAsync(p => p.Id == 2)).CoverImageUrl = "https://external.example/uploads/legacy/shared.png");
        await Expect("/uploads/legacy/shared.png", HttpStatusCode.NotFound);
        await Edit(async db => db.Pages.Remove(await db.Pages.IgnoreQueryFilters().SingleAsync(p => p.Id == 2)));

        // Legacy topology is accessible only through valid, currently public references.
        await WriteAsset("/uploads/network-devices/legacy.png");
        await WriteAsset("/uploads/network-icons/legacy.png");
        var topologyJson = "{\"nodes\":[{\"id\":1,\"label\":\"Fixture\",\"deviceType\":\"custom\",\"iconUrl\":\"/uploads/network-icons/legacy.png\",\"standaloneImageUrl\":\"/uploads/network-devices/legacy.png\"}],\"edges\":[]}";
        await Edit(async db => { var lab = await db.HomelabPosts.IgnoreQueryFilters().SingleAsync(); lab.Status = VisibilityStatus.Public; lab.NetworkTopology = topologyJson; });
        await Expect("/uploads/network-devices/legacy.png", HttpStatusCode.OK);
        await Expect("/uploads/network-icons/legacy.png", HttpStatusCode.OK);
        await Edit(async db => (await db.HomelabPosts.IgnoreQueryFilters().SingleAsync()).NetworkTopology = "{}");
        await Expect("/uploads/network-devices/legacy.png", HttpStatusCode.NotFound);
        await Edit(async db => (await db.HomelabPosts.IgnoreQueryFilters().SingleAsync()).NetworkTopology = topologyJson);

        // New topology uploads use real controller validation + real MediaService storage.
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var service = new MediaService(db, app.Environment, new ConfigurationBuilder().Build());
            var controller = new Portfolio.Areas.Admin.Controllers.HomelabController(db, new SlugService(db), new ReadingTimeService(), service, app.Environment);
            IFormFile Png(string extension = ".png", string contentType = "image/png") => new FormFile(new MemoryStream(new byte[] {137,80,78,71,13,10,26,10,0,0,0,0}), 0, 12, "file", "fixture" + extension) { Headers = new HeaderDictionary(), ContentType = contentType };
            Check(await controller.UploadIcon(Png(), 999) is NotFoundResult, "topology icon requires an existing owner");
            Check(await controller.UploadDeviceImage(Png(), 0) is NotFoundResult, "topology photo requires owner ID");
            var invalid = (JsonResult)await controller.UploadIcon(Png(".gif", "image/gif"), 1);
            Check(invalid.Value!.GetType().GetProperty("success")!.GetValue(invalid.Value) is false, "icon format restriction is preserved");
            var icon = (JsonResult)await controller.UploadIcon(Png(), 1);
            var iconUrl = (string)icon.Value!.GetType().GetProperty("url")!.GetValue(icon.Value)!;
            Check(iconUrl.StartsWith("/uploads/homelab_topology/1/"), "valid icon uses owner-specific namespace");
            await Expect(iconUrl, HttpStatusCode.NotFound);
            await Expect(iconUrl, HttpStatusCode.OK, admin: true);
            var tooLargeIcon = new FormFile(new MemoryStream(), 0, 500_001, "icon", "large.png") { Headers = new HeaderDictionary(), ContentType = "image/png" };
            var iconLimit = (JsonResult)await controller.UploadIcon(tooLargeIcon, 1);
            Check(iconLimit.Value!.GetType().GetProperty("success")!.GetValue(iconLimit.Value) is false, "icon 500 KB limit retained");
            var tooLargePhoto = new FormFile(new MemoryStream(), 0, 5_000_001, "image", "large.png") { Headers = new HeaderDictionary(), ContentType = "image/png" };
            var photoLimit = (JsonResult)await controller.UploadDeviceImage(tooLargePhoto, 1);
            Check(photoLimit.Value!.GetType().GetProperty("success")!.GetValue(photoLimit.Value) is false, "device photo 5 MB limit retained");
            var uploaded = (JsonResult)await controller.UploadDeviceImage(Png(), 1);
            var url = (string)uploaded.Value!.GetType().GetProperty("url")!.GetValue(uploaded.Value)!;
            Check(url.StartsWith("/uploads/homelab_topology/1/"), "topology file uses owner-specific namespace");
            Check(await db.Media.AnyAsync(m => m.Url == url && m.EntityType == "homelab_topology" && m.EntityId == 1), "new upload has persisted ownership");
            Check(!(await service.GetByEntityAsync("homelab_post", 1)).Any(m => m.Url == url), "topology asset does not pollute gallery");
            await Expect(url, HttpStatusCode.NotFound);
            await Expect(url, HttpStatusCode.OK, admin: true);
            var lab = await db.HomelabPosts.IgnoreQueryFilters().SingleAsync();
            lab.NetworkTopology = $"{{\"nodes\":[{{\"id\":1,\"label\":\"Fixture\",\"deviceType\":\"custom\",\"iconUrl\":\"/icons/network/router.svg\",\"standaloneImageUrl\":\"{url}\"}}],\"edges\":[]}}";
            await db.SaveChangesAsync();
            await Expect(url, HttpStatusCode.OK);
            lab.NetworkTopology = null; await db.SaveChangesAsync();
            await Expect(url, HttpStatusCode.NotFound);
            Check(!await service.SetCoverAsync((await db.Media.SingleAsync(m => m.Url == url)).Id, "homelab_topology", 1), "topology asset cannot become a gallery cover");
            await controller.Delete(1);
            Check(!await db.Media.AnyAsync(m => m.Url == url), "parent deletion removes topology media record");
            Check(!File.Exists(Path.Combine(root, url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar))), "parent deletion removes topology file");
        }
        // Unknown record types and deleted owners cannot authorize a file.
        await WriteAsset("/uploads/unknown/record.png");
        await Edit(db => { db.Media.Add(new Media { EntityType = "invalid", EntityId = 1, Url = "/uploads/unknown/record.png", Filename = "record.png" }); return Task.CompletedTask; });
        await Expect("/uploads/unknown/record.png", HttpStatusCode.NotFound);
        await Edit(async db => { db.Projects.Remove(await db.Projects.IgnoreQueryFilters().SingleAsync()); });
        await Expect("/uploads/project/1/sample.png", HttpStatusCode.NotFound);
        using var css = await client.GetAsync("/style.css");
        Check(css.StatusCode == HttpStatusCode.OK, "ordinary CSS remains public");

        var source = await File.ReadAllTextAsync(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../Program.cs")));
        Check(source.IndexOf("app.UseAuthentication();", StringComparison.Ordinal) < source.IndexOf("app.UseMiddleware<UploadAccessMiddleware>();", StringComparison.Ordinal), "production auth precedes upload guard");
        Check(source.IndexOf("app.UseMiddleware<UploadAccessMiddleware>();", StringComparison.Ordinal) < source.IndexOf("app.UseStaticFiles();", StringComparison.Ordinal), "production upload guard precedes static serving");
        Check(source.Split("app.UseStaticFiles();").Length == 2, "production has no earlier unguarded static server");
        using (var postgres = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                   .UseNpgsql("Host=127.0.0.1;Database=synthetic_translation_only;Username=synthetic;Password=synthetic").Options))
        {
            // No connection is opened: check production-provider translation too.
            var sql = postgres.SecurityResearches.Where(s => s.Id == 1).ToQueryString();
            Check(sql.Contains("PubliclyDisclosed") && sql.Contains("IsPrivate"), "Npgsql translates disclosure and category visibility filters");
            Check(postgres.Projects.Where(p => p.Id == 1).ToQueryString().Contains("IsPrivate"), "Npgsql translates project visibility filter");
        }
        Console.WriteLine($"PASS: {checks} focused checks.");
    }
}
finally
{
    await app.StopAsync();
    // Leave the disposable fixture directory to OS temp cleanup; no recursive delete.
}
