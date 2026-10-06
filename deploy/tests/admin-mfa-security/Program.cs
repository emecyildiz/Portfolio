using System.Reflection;
using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OptionsFactory = Microsoft.Extensions.Options.Options;
using Portfolio.Areas.Admin.Controllers;
using Portfolio.Data;
using Portfolio.Models.ViewModels;
using Portfolio.Services;

const string ValidPassword = "valid-fixture-password";
var baseline = args.Contains("--baseline", StringComparer.Ordinal);
var assertions = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
    assertions++;
}
ManageTwoFactorViewModel Model(IActionResult result) =>
    (ManageTwoFactorViewModel)((ViewResult)result).Model!;
bool NoSecret(Fixture f, IActionResult result) =>
    result is not ViewResult view || view.Model is not ManageTwoFactorViewModel model ||
    (model.SharedKey is null && model.AuthenticatorUri is null);
async Task<IActionResult> Invoke(Fixture f, string action, string? password = null,
    ManageTwoFactorViewModel? model = null)
{
    var method = typeof(AccountSecurityController).GetMethod(action)!;
    var values = method.GetParameters().Select(p =>
        p.ParameterType == typeof(ManageTwoFactorViewModel) ? (object?)model : password).ToArray();
    return await (Task<IActionResult>)method.Invoke(f.Controller, values)!;
}

if (baseline)
{
    var f = new Fixture();
    Check(!NoSecret(f, await f.Controller.Index()), "Baseline GET exposes setup material.");
    f = new Fixture(enabled: true);
    await Invoke(f, "GenerateRecoveryCodes");
    Check(f.Users.CodesGenerated == 1 && f.Users.PasswordChecks == 0,
        "Baseline recovery rotation lacks fresh proof.");
    f = new Fixture();
    await Invoke(f, "EnableAuthenticator", model: new() { AuthenticatorCode = "123456" });
    Check(f.Users.MfaChanged == 1 && f.Users.PasswordChecks == 0,
        "Baseline enrollment lacks fresh proof.");
    Console.WriteLine($"PASS: {assertions} baseline vulnerability reproductions (synthetic Identity, no network).");
    return;
}

foreach (var existingKey in new[] { true, false })
{
    var f = new Fixture();
    if (!existingKey) f.Users.Key = null;
    var result = await f.Controller.Index();
    Check(NoSecret(f, result) && f.Users.KeysRead == 0 && f.Users.KeysReset == 0,
        "GET must neither disclose nor create authenticator material.");
}
foreach (var action in new[] { "ShowAuthenticatorSetup", "EnableAuthenticator", "GenerateRecoveryCodes", "ResetAuthenticator" })
{
    foreach (var password in new string?[] { null, "", "wrong", " valid-fixture-password " })
    {
        var f = new Fixture(enabled: action is "GenerateRecoveryCodes" or "ResetAuthenticator");
        f.Controller.ModelState.AddModelError("AuthenticatorCode", "invalid fixture");
        f.Controller.ModelState.SetModelValue("currentPassword", password, password);
        var result = await Invoke(f, action, password, new() { AuthenticatorCode = "123456" });
        Check(result is RedirectToActionResult && NoSecret(f, result), $"{action}: wrong proof returns no material.");
        Check(f.Users.KeysRead == 0 && f.Users.KeysReset == 0 && f.Users.MfaChanged == 0 &&
            f.Users.CodesGenerated == 0 && f.Users.TotpChecks == 0, $"{action}: denied request has no sensitive side effects.");
        Check(!f.Controller.ModelState.ContainsKey("currentPassword"), $"{action}: plaintext proof not kept in ModelState.");
    }
    var method = typeof(AccountSecurityController).GetMethod(action)!;
    Check(method.GetCustomAttribute<HttpPostAttribute>() is not null &&
        method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>() is not null &&
        method.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName == AdminMfaRateLimit.PolicyName,
        $"{action}: HTTP/antiforgery/rate limit protections remain.");
    Check(method.GetParameters().Single(p => p.Name == "currentPassword").GetCustomAttribute<FromFormAttribute>() is not null,
        $"{action}: password proof is form-only, not a query parameter.");
    var unauthenticated = new Fixture();
    unauthenticated.Users.CurrentUser = null;
    Check(await Invoke(unauthenticated, action, ValidPassword, new() { AuthenticatorCode = "123456" }) is ChallengeResult,
        $"{action}: unauthenticated identity rejected.");
}
{
    var f = new Fixture();
    var forged = new ManageTwoFactorViewModel
    {
        IsEnabled = true, SharedKey = "forged", AuthenticatorUri = "forged", AuthenticatorCode = "123456"
    };
    var result = await Invoke(f, "EnableAuthenticator", "wrong", forged);
    Check(NoSecret(f, result) && f.Users.KeysRead == 0 && f.Users.MfaChanged == 0,
        "Submitted setup/status fields cannot authorize secret inclusion or enrollment.");
    f = new Fixture();
    f.Users.ExpectedPassword = " valid-fixture-password ";
    Check(Model(await Invoke(f, "ShowAuthenticatorSetup", f.Users.ExpectedPassword)).SharedKey is not null,
        "Legitimate passwords retain leading/trailing whitespace semantics.");
}
{
    var f = new Fixture();
    f.Users.Key = null;
    var model = Model(await Invoke(f, "ShowAuthenticatorSetup", ValidPassword));
    Check(model.SharedKey is not null && model.AuthenticatorUri is not null && f.Users.KeysReset == 1,
        "Password-proven setup can create and reveal material.");
    Check(f.Users.MfaChanged == 0 && f.Users.CodesGenerated == 0, "Showing setup does not enable MFA.");
    Check(NoSecret(f, await f.Controller.Index()), "Verified POST does not make later GET reveal material.");
}
{
    var f = new Fixture(enabled: true);
    Check(NoSecret(f, await Invoke(f, "ShowAuthenticatorSetup", ValidPassword)) && f.Users.KeysRead == 0,
        "Enabled MFA never reveals setup key.");
    Check(Model(await f.Controller.Index()).RecoveryCodesLeft == 10, "Status/count remain readable.");
}
{
    var f = new Fixture();
    var result = await Invoke(f, "EnableAuthenticator", ValidPassword, new() { AuthenticatorCode = "123-456" });
    Check(result is ViewResult { ViewName: "RecoveryCodes" } && f.Users.Enabled && f.Users.CodesGenerated == 1 &&
        f.SignIn.Refreshes == 1 && f.Users.LastTotp == "123456", "Proven enrollment preserves normalized TOTP and codes.");
}
foreach (var errorKind in new[] { "model", "totp", "identity" })
{
    var f = new Fixture();
    if (errorKind == "model") f.Controller.ModelState.AddModelError("AuthenticatorCode", "invalid fixture");
    if (errorKind == "totp") f.Users.TotpValid = false;
    if (errorKind == "identity") f.Users.EnableSucceeds = false;
    var result = await Invoke(f, "EnableAuthenticator", ValidPassword, new() { AuthenticatorCode = "123456" });
    Check(result is ViewResult { ViewName: "Index" } && Model(result).SharedKey is not null &&
        f.Users.CodesGenerated == 0, $"Proven {errorKind} failure can safely retry setup without issuing codes.");
}
{
    var f = new Fixture(enabled: true);
    var result = await Invoke(f, "GenerateRecoveryCodes", ValidPassword);
    Check(result is ViewResult { ViewName: "RecoveryCodes" } &&
        ((RecoveryCodesViewModel)((ViewResult)result).Model!).RecoveryCodes.Count == 10,
        "Proven code rotation preserves one-time ten-code response.");
    f = new Fixture();
    Check(await Invoke(f, "GenerateRecoveryCodes", ValidPassword) is RedirectToActionResult && f.Users.CodesGenerated == 0,
        "Disabled MFA does not issue recovery codes.");
    f = new Fixture(enabled: true);
    Check(await Invoke(f, "ResetAuthenticator", ValidPassword) is RedirectToActionResult &&
        !f.Users.Enabled && f.Users.KeysReset == 1 && f.SignIn.Refreshes == 1, "Proven reset remains intact.");
}
Check(typeof(AdminBaseController).GetCustomAttribute<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>() is not null,
    "Admin authorization remains present.");
var cache = typeof(AccountSecurityController).GetCustomAttribute<ResponseCacheAttribute>();
Check(cache is { NoStore: true, Location: ResponseCacheLocation.None }, "Sensitive pages prohibit caching.");
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = [], ApplicationName = typeof(Fixture).Assembly.GetName().Name,
    EnvironmentName = "Production", ContentRootPath = AppContext.BaseDirectory
});
builder.Logging.ClearProviders();
builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
builder.Services.AddRateLimiter(options =>
{
    AdminMfaRateLimit.Configure(options);
    options.AddPolicy("AdminLoginLimit", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { Window = TimeSpan.FromMinutes(15), PermitLimit = 5, QueueLimit = 0, AutoReplenishment = true }));
    options.RejectionStatusCode = 429;
});
await using (var app = builder.Build())
{
    app.UseRouting();
    app.UseRateLimiter();
    app.MapPost("/fixture/login", () => Results.Ok()).RequireRateLimiting("AdminLoginLimit");
    app.MapPost("/fixture/mfa", () => Results.Ok()).RequireRateLimiting(AdminMfaRateLimit.PolicyName);
    await app.StartAsync();
    try
    {
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!;
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false })
        { BaseAddress = new Uri(addresses.Addresses.Single()) };
        for (var i = 0; i < 2; i++)
            Check((await client.PostAsync("/fixture/login", null)).IsSuccessStatusCode, "Login fixture succeeds.");
        for (var i = 0; i < 5; i++)
            Check((await client.PostAsync("/fixture/mfa", null)).IsSuccessStatusCode,
                "MFA reset/setup/enable with retry is independent of login quota.");
        Check((await client.PostAsync("/fixture/mfa", null)).StatusCode == HttpStatusCode.TooManyRequests,
            "Sixth MFA proof attempt remains rate-limited.");
        Check((await client.PostAsync("/fixture/login", null)).IsSuccessStatusCode,
            "MFA exhaustion does not consume login permits.");
    }
    finally { await app.StopAsync(); }
}
Console.WriteLine($"PASS: {assertions} MFA security assertions (synthetic Identity, actual controller, loopback limiter; no database/external services).");

sealed class Fixture
{
    public FakeUsers Users { get; }
    public FakeSignIn SignIn { get; }
    public AccountSecurityController Controller { get; }
    public Fixture(bool enabled = false)
    {
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity("fixture")) };
        Users = new FakeUsers { Enabled = enabled };
        SignIn = new FakeSignIn(Users, context);
        Controller = new AccountSecurityController(new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().Options), Users, SignIn)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
            TempData = new TempDataDictionary(context, new EmptyTempData())
        };
    }
}
sealed class EmptyTempData : ITempDataProvider
{
    public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
    public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
}
sealed class EmptyStore : IUserStore<IdentityUser>
{
    public void Dispose() { }
    public Task<string> GetUserIdAsync(IdentityUser user, CancellationToken token) => Task.FromResult(user.Id);
    public Task<string?> GetUserNameAsync(IdentityUser user, CancellationToken token) => Task.FromResult(user.UserName);
    public Task SetUserNameAsync(IdentityUser user, string? name, CancellationToken token) => Task.CompletedTask;
    public Task<string?> GetNormalizedUserNameAsync(IdentityUser user, CancellationToken token) => Task.FromResult(user.NormalizedUserName);
    public Task SetNormalizedUserNameAsync(IdentityUser user, string? name, CancellationToken token) => Task.CompletedTask;
    public Task<IdentityResult> CreateAsync(IdentityUser user, CancellationToken token) => Task.FromResult(IdentityResult.Success);
    public Task<IdentityResult> UpdateAsync(IdentityUser user, CancellationToken token) => Task.FromResult(IdentityResult.Success);
    public Task<IdentityResult> DeleteAsync(IdentityUser user, CancellationToken token) => Task.FromResult(IdentityResult.Success);
    public Task<IdentityUser?> FindByIdAsync(string id, CancellationToken token) => Task.FromResult<IdentityUser?>(null);
    public Task<IdentityUser?> FindByNameAsync(string name, CancellationToken token) => Task.FromResult<IdentityUser?>(null);
}
sealed class FakeUsers : UserManager<IdentityUser>
{
    public IdentityUser? CurrentUser = new("fixture-admin") { Email = "fixture@example.invalid" };
    public bool Enabled, TotpValid = true, EnableSucceeds = true;
    public string? Key = "ABCDEFGHIJKLMNOP";
    public string? LastTotp;
    public string ExpectedPassword = "valid-fixture-password";
    public int PasswordChecks, KeysRead, KeysReset, MfaChanged, CodesGenerated, TotpChecks;
    public FakeUsers() : base(new EmptyStore(), OptionsFactory.Create(new IdentityOptions()), new PasswordHasher<IdentityUser>(),
        [], [], new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(),
        new ServiceCollection().BuildServiceProvider(), NullLogger<UserManager<IdentityUser>>.Instance) { }
    public override Task<IdentityUser?> GetUserAsync(ClaimsPrincipal principal) => Task.FromResult(CurrentUser);
    public override Task<bool> GetTwoFactorEnabledAsync(IdentityUser user) => Task.FromResult(Enabled);
    public override Task<bool> CheckPasswordAsync(IdentityUser user, string password)
    { PasswordChecks++; return Task.FromResult(password == ExpectedPassword); }
    public override Task<int> CountRecoveryCodesAsync(IdentityUser user) => Task.FromResult(10);
    public override Task<string?> GetAuthenticatorKeyAsync(IdentityUser user)
    { KeysRead++; return Task.FromResult(Key); }
    public override Task<IdentityResult> ResetAuthenticatorKeyAsync(IdentityUser user)
    { KeysReset++; Key = "ABCDEFGHIJKLMNOP"; return Task.FromResult(IdentityResult.Success); }
    public override Task<string?> GetEmailAsync(IdentityUser user) => Task.FromResult(user.Email);
    public override Task<bool> VerifyTwoFactorTokenAsync(IdentityUser user, string provider, string token)
    { TotpChecks++; LastTotp = token; return Task.FromResult(TotpValid); }
    public override Task<IdentityResult> SetTwoFactorEnabledAsync(IdentityUser user, bool enabled)
    {
        MfaChanged++;
        if (enabled && !EnableSucceeds) return Task.FromResult(IdentityResult.Failed(new IdentityError { Description = "fixture failure" }));
        Enabled = enabled; return Task.FromResult(IdentityResult.Success);
    }
    public override Task<IEnumerable<string>?> GenerateNewTwoFactorRecoveryCodesAsync(IdentityUser user, int number)
    { CodesGenerated++; return Task.FromResult<IEnumerable<string>?>(Enumerable.Range(0, number).Select(i => $"fixture-code-{i}")); }
}
sealed class FakeSignIn : SignInManager<IdentityUser>
{
    public int Refreshes;
    public FakeSignIn(FakeUsers users, HttpContext context) : base(users,
        new HttpContextAccessor { HttpContext = context }, new UserClaimsPrincipalFactory<IdentityUser>(users, OptionsFactory.Create(new IdentityOptions())),
        OptionsFactory.Create(new IdentityOptions()), NullLogger<SignInManager<IdentityUser>>.Instance,
        new AuthenticationSchemeProvider(OptionsFactory.Create(new AuthenticationOptions())), new DefaultUserConfirmation<IdentityUser>()) { }
    public override Task RefreshSignInAsync(IdentityUser user) { Refreshes++; return Task.CompletedTask; }
}
