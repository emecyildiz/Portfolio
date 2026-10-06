using System.Net;
using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;
using Portfolio.Controllers;
using Portfolio.Data;
using Portfolio.Models;
using Portfolio.Models.Enums;
using Portfolio.Models.ViewModels;
using Portfolio.Services;
using OptionsFactory = Microsoft.Extensions.Options.Options;

var baseline = args.Contains("--baseline", StringComparer.Ordinal);
var checks = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + description);
    checks++;
}
bool InvalidConfiguration(TurnstileOptions options, bool requireProtection)
{
    try { options.Validate(requireProtection); return false; }
    catch (InvalidOperationException) { return true; }
}
TurnstileOptions ValidOptions() => new()
{
    Enabled = true, SiteKey = "synthetic-site-key", SecretKey = "synthetic-secret-key",
    ExpectedHostname = "emecworks.com", ExpectedAction = "contact"
};

if (baseline)
{
    Check(!InvalidConfiguration(new TurnstileOptions(), true), "original production permits disabled verification");
    await using var f = await Fixture.Create(enabled: false);
    for (var i = 0; i < 60; i++) await f.Submit(i);
    Check(await f.Db.ContactMessages.CountAsync() == 60, "60 distributed proof-free forms consume shared capacity");
    await f.Submit(61);
    Check(await f.Db.ContactMessages.CountAsync() == 60 && f.Controller.TempData.ContainsKey("Error"),
        "independent valid form is denied by filled shared quota");
    f.Db.ContactMessages.Add(Fixture.Message(Guid.Empty, DateTime.UtcNow.AddDays(-90)));
    await f.Db.SaveChangesAsync();
    Check(await f.Controller.TrackTicket(Guid.Empty.ToString()) is ViewResult,
        "predictable legacy zero ticket returns metadata");
    var normal = await f.Db.ContactMessages.FirstAsync(m => m.TicketNumber != Guid.Empty);
    Check(await f.Controller.TrackTicket(normal.TicketNumber.ToString()) is ViewResult,
        "normal random ticket control works");
    Console.WriteLine($"PASS: {checks} baseline reproductions/controls (synthetic data; no network).");
    return;
}

Check(InvalidConfiguration(new TurnstileOptions(), true), "production cannot disable abuse verification");
Check(!InvalidConfiguration(new TurnstileOptions(), false), "local development opt-out is preserved");
Check(!InvalidConfiguration(ValidOptions(), true), "valid production widget configuration is preserved");
foreach (var environmentName in new[] { "Development", "Production", "Staging", "custom" })
{
    var environment = new FixtureEnvironment { EnvironmentName = environmentName };
    Check(InvalidConfiguration(new TurnstileOptions(), !environment.IsDevelopment()) ==
          !environment.IsDevelopment(), "only Development can opt out: " + environmentName);
}
var sourceRoot = new DirectoryInfo(AppContext.BaseDirectory);
while (sourceRoot is not null && !File.Exists(Path.Combine(sourceRoot.FullName, "Portfolio.csproj"))) sourceRoot = sourceRoot.Parent;
Check(sourceRoot is not null, "fixture can locate owning source package");
var startup = await File.ReadAllTextAsync(Path.Combine(sourceRoot!.FullName, "Program.cs"));
Check(startup.Contains("turnstileOptions.Validate(requireProtection: !builder.Environment.IsDevelopment());", StringComparison.Ordinal) &&
      startup.IndexOf("turnstileOptions.Validate", StringComparison.Ordinal) < startup.IndexOf("builder.Build()", StringComparison.Ordinal),
    "owning startup enforces non-Development proof before accepting requests");
Check((await File.ReadAllTextAsync(Path.Combine(sourceRoot.FullName, "docker-compose.prod.yml")))
      .Contains("Turnstile__Enabled=${TURNSTILE_ENABLED:-true}", StringComparison.Ordinal), "production Compose default enables verification");
foreach (var key in new[] { "SiteKey", "SecretKey", "ExpectedHostname" })
{
    var options = ValidOptions();
    typeof(TurnstileOptions).GetProperty(key)!.SetValue(options, " ");
    Check(InvalidConfiguration(options, true), "production requires " + key);
}
foreach (var host in new[] { "https://emecworks.com", "emecworks.com/path", "bad host" })
{
    var options = ValidOptions(); options.ExpectedHostname = host;
    Check(InvalidConfiguration(options, true), "invalid expected hostname is rejected");
}
foreach (var action in new[] { "", "bad action", new string('a', 33) })
{
    var options = ValidOptions(); options.ExpectedAction = action;
    Check(InvalidConfiguration(options, true), "invalid expected action is rejected");
}
foreach (var flag in new[] { "false", "False", "FALSE" })
{
    var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    { ["Turnstile:Enabled"] = flag }).Build();
    var options = new TurnstileOptions(); config.GetSection("Turnstile").Bind(options);
    Check(InvalidConfiguration(options, true), "configuration binding cannot bypass required verification");
}

await using (var f = await Fixture.Create())
{
    foreach (var token in new string?[] { null, "", " \t", new string('x', 2049) })
    {
        Check(!await f.Validator.ValidateAsync(token, "192.0.2.1", default), "empty/oversized proof fails closed");
        Check(f.Handler.Calls == 0, "invalid proof does not call provider");
    }
    foreach (var response in new[]
    {
        "{\"success\":false}", "{}", "null", "not-json",
        "{\"success\":true,\"hostname\":\"attacker.example\",\"action\":\"contact\"}",
        "{\"success\":true,\"hostname\":\"emecworks.com\",\"action\":\"other\"}",
        "{\"success\":true,\"hostname\":\"emecworks.com\",\"action\":\"Contact\"}",
        "{\"success\":true}"
    })
    {
        f.Handler.Body = response;
        Check(!await f.Validator.ValidateAsync("synthetic-token", "192.0.2.1", default),
            "untrusted provider result is rejected");
    }
    foreach (var status in new[] { HttpStatusCode.BadRequest, HttpStatusCode.TooManyRequests, HttpStatusCode.ServiceUnavailable })
    {
        f.Handler.Status = status;
        Check(!await f.Validator.ValidateAsync("synthetic-token", null, default), "provider HTTP failure rejects proof");
    }
    f.Handler.Status = HttpStatusCode.OK;
    foreach (var error in new Exception[] { new HttpRequestException("synthetic"), new TaskCanceledException("synthetic timeout") })
    {
        f.Handler.Error = error;
        Check(!await f.Validator.ValidateAsync("synthetic-token", null, default), "network/timeout fails closed");
    }
    f.Handler.Error = null;
    f.Handler.Body = Fixture.Success;
    Check(await f.Validator.ValidateAsync(" synthetic-token ", "192.0.2.1", default), "valid proof succeeds");
    Check(f.Handler.LastForm?.Contains("response=synthetic-token") == true &&
          f.Handler.LastForm.Contains("remoteip=192.0.2.1"), "provider receives normalized proof and client IP");
    f.Handler.Body = Fixture.Success.Replace("emecworks.com", "EMECWORKS.COM");
    Check(await f.Validator.ValidateAsync("synthetic-token", null, default), "DNS hostname case remains compatible");
}

await using (var f = await Fixture.Create())
{
    f.Handler.Body = "{\"success\":false}";
    for (var i = 0; i < 80; i++) await f.Submit(i, "rejected-proof");
    Check(await f.Db.ContactMessages.CountAsync() == 0 && await f.Db.TicketEmailOutboxes.CountAsync() == 0,
        "distributed rejected proofs cannot consume shared database/email capacity");
    Check(!f.Controller.TempData.ContainsKey("TicketNumber"), "rejected proofs create no receipt");
    f.Handler.Body = Fixture.Success;
    await f.Submit(81, "accepted-proof");
    var accepted = await f.Db.ContactMessages.SingleAsync();
    Check(accepted.TicketNumber != Guid.Empty && accepted.IpAddress == "192.0.2.82", "independent legitimate inquiry remains accepted");
    Check(await f.Db.TicketEmailOutboxes.CountAsync() == 1 &&
          f.Controller.TempData["TicketNumber"]?.ToString() == accepted.TicketNumber.ToString(), "receipt and outbox remain coupled to accepted inquiry");
    Check(await f.Controller.TrackTicket(accepted.TicketNumber.ToString()) is ViewResult, "accepted inquiry can be tracked");
}

await using (var f = await Fixture.Create(enabled: false))
{
    await f.Submit(0);
    Check(await f.Db.ContactMessages.CountAsync() == 1 && f.Handler.Calls == 0, "offline development form still works");
}
foreach (var limit in new[] { "global", "ip", "email" })
{
    await using var f = await Fixture.Create();
    var count = limit == "global" ? 60 : limit == "ip" ? 8 : 3;
    for (var i = 0; i < count; i++)
    {
        var message = Fixture.Message(Guid.NewGuid(), DateTime.UtcNow.AddHours(-1));
        message.IpAddress = limit == "ip" ? "192.0.2.1" : "198.51.100." + (i + 1);
        message.Email = limit == "email" ? "PERSON0@example.test" : "seed" + i + "@example.test";
        f.Db.ContactMessages.Add(message);
    }
    await f.Db.SaveChangesAsync();
    await f.Submit(0, "valid-proof");
    Check(await f.Db.ContactMessages.CountAsync() == count && await f.Db.TicketEmailOutboxes.CountAsync() == 0,
        limit + " quota remains bounded; no email queued on rejection");
}
await using (var f = await Fixture.Create())
{
    var old = Fixture.Message(Guid.NewGuid(), DateTime.UtcNow.AddDays(-2));
    old.IpAddress = "192.0.2.1"; old.Email = "person0@example.test";
    f.Db.ContactMessages.Add(old);
    for (var i = 0; i < 120; i++) f.Db.TicketEmailOutboxes.Add(new() { ContactMessage = old, CreatedAt = old.CreatedAt, NextAttemptAt = old.CreatedAt });
    await f.Db.SaveChangesAsync();
    await f.Submit(0, "valid-proof");
    Check(await f.Db.ContactMessages.CountAsync() == 2 && await f.Db.TicketEmailOutboxes.CountAsync() == 120,
        "outbox saturation does not discard an otherwise valid inquiry");
    Check(f.Controller.TempData["ConfirmationEmailQueued"] is false &&
          f.Controller.TempData["ConfirmationEmailUnavailable"] is true, "saturated outbox preserves honest receipt flags");
}
await using (var f = await Fixture.Create())
{
    f.Controller.ModelState.AddModelError("Email", "synthetic invalid email");
    await f.Submit(0, "valid-proof");
    Check(await f.Db.ContactMessages.CountAsync() == 0 && f.Handler.Calls == 0, "invalid model has no provider/storage side effects");
    f.Controller.ModelState.Clear();
    var request = Fixture.Request(0); request.Website = "honeypot";
    await f.Controller.Contact(request, "valid-proof");
    Check(await f.Db.ContactMessages.CountAsync() == 0 && f.Handler.Calls == 0, "honeypot remains silent and side-effect free");
    request.Website = null; request.ServiceId = 999;
    await f.Controller.Contact(request, "valid-proof");
    Check(await f.Db.ContactMessages.CountAsync() == 0 && f.Handler.Calls == 0, "unknown service cannot create a ticket");
}

await using (var f = await Fixture.Create())
{
    var random = Guid.NewGuid();
    f.Db.ContactMessages.AddRange(Fixture.Message(Guid.Empty, DateTime.UtcNow.AddDays(-90)), Fixture.Message(random, DateTime.UtcNow));
    await f.Db.SaveChangesAsync();
    foreach (var format in new[] { "D", "N", "B", "P", "X" })
    {
        foreach (var input in new[] { Guid.Empty.ToString(format), " \t" + Guid.Empty.ToString(format) + "\r\n" })
        {
            f.Controller.ViewData.Clear();
            Check(await f.Controller.TrackTicket(input) is RedirectToActionResult &&
                  !f.Controller.ViewData.ContainsKey("TrackedMessage"), "parsed zero GUID alias exposes no legacy record");
        }
        f.Controller.ViewData.Clear();
        Check(await f.Controller.TrackTicket(random.ToString(format)) is ViewResult &&
              f.Controller.ViewData["TrackedTicketNumber"]?.ToString() == random.ToString("D"), "nonzero GUID alias remains compatible");
    }
    foreach (var input in new string?[] { null, "", "not-a-ticket", Guid.NewGuid().ToString() })
    {
        f.Controller.ViewData.Clear();
        Check(await f.Controller.TrackTicket(input!) is RedirectToActionResult &&
              !f.Controller.ViewData.ContainsKey("TrackedMessage"), "invalid/unknown ticket has no metadata result");
    }
}
foreach (var action in new[] { "Contact", "TrackTicket" })
{
    var method = typeof(HireController).GetMethod(action)!;
    Check(method.GetCustomAttribute<HttpPostAttribute>() is not null &&
          method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>() is not null, action + " keeps POST + antiforgery");
    Check(method.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName ==
          (action == "Contact" ? "ContactFormLimit" : "TicketTrackingLimit"), action + " keeps rate-limit policy");
}
var cache = typeof(HireController).GetMethod("TrackTicket")!.GetCustomAttribute<ResponseCacheAttribute>();
Check(cache?.NoStore == true && cache.Location == ResponseCacheLocation.None && cache.Duration == 0,
    "public ticket metadata remains no-store");
Console.WriteLine($"PASS: {checks} contact/security controls (real controller + SQLite + simulated Siteverify; no network).");

sealed class Fixture : IAsyncDisposable
{
    public const string Success = "{\"success\":true,\"hostname\":\"emecworks.com\",\"action\":\"contact\"}";
    private readonly SqliteConnection _connection;
    private readonly HttpClient _client;
    public AppDbContext Db { get; }
    public SiteverifyHandler Handler { get; } = new();
    public TurnstileValidator Validator { get; }
    public HireController Controller { get; }
    private Fixture(SqliteConnection connection, bool enabled)
    {
        _connection = connection;
        Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        _client = new HttpClient(Handler) { BaseAddress = new Uri("https://challenges.cloudflare.com/") };
        Validator = new TurnstileValidator(_client, OptionsFactory.Create(new TurnstileOptions
        { Enabled = enabled, SiteKey = "synthetic", SecretKey = "synthetic", ExpectedHostname = "emecworks.com", ExpectedAction = "contact" }),
            NullLogger<TurnstileValidator>.Instance);
        var context = new DefaultHttpContext();
        Controller = new HireController(Db, Validator, OptionsFactory.Create(new TicketEmailOptions { MaxPendingItems = 120 }))
        { ControllerContext = new ControllerContext { HttpContext = context }, TempData = new TempDataDictionary(context, new MemoryTempData()) };
    }
    public static async Task<Fixture> Create(bool enabled = true)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var fixture = new Fixture(connection, enabled);
        await fixture.Db.Database.EnsureCreatedAsync();
        return fixture;
    }
    public static ContactRequestViewModel Request(int i) => new()
    { Name = "Synthetic sender", Email = $"person{i}@example.test", Subject = "Fixture", Message = "Synthetic inquiry" };
    public static ContactMessage Message(Guid ticket, DateTime created) => new()
    { TicketNumber = ticket, Name = "Synthetic", Email = "fixture@example.test", Message = "Synthetic only", CreatedAt = created, Status = ContactStatus.New };
    public async Task<IActionResult> Submit(int i, string? token = null)
    {
        Controller.TempData.Clear();
        Controller.HttpContext.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2." + (i + 1));
        return await Controller.Contact(Request(i), token);
    }
    public async ValueTask DisposeAsync() { await Db.DisposeAsync(); _client.Dispose(); await _connection.DisposeAsync(); }
}
sealed class SiteverifyHandler : HttpMessageHandler
{
    public int Calls { get; private set; }
    public string? LastForm { get; private set; }
    public string Body { get; set; } = Fixture.Success;
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
    public Exception? Error { get; set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        LastForm = await request.Content!.ReadAsStringAsync(cancellationToken);
        if (Error is not null) throw Error;
        return new HttpResponseMessage(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") };
    }
}
sealed class MemoryTempData : ITempDataProvider
{
    public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
    public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
}
sealed class FixtureEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Development";
    public string ApplicationName { get; set; } = "ContactSecurity";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
