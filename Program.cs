using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Services;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);
var adminPath = builder.Configuration["AdminPath"] ?? "panel";

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        npgsql =>
        {
            npgsql.MigrationsAssembly("Portfolio");
            npgsql.EnableRetryOnFailure(
                maxRetryCount: 3,
                maxRetryDelay: TimeSpan.FromSeconds(5),
                errorCodesToAdd: null
            );
        }
    )
    
);

builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
{
    // Þifre kurallarý
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 12;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;

    // Hesap kilitleme — 5 yanlýþ denemede 15 dakika kilt
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

// Cookie ayarlarý
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = $"/{adminPath}/account/login";
    options.LogoutPath = $"/{adminPath}/account/logout";
    options.AccessDeniedPath = $"/{adminPath}/account/login";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
});

// Add services to the container.
builder.Services.AddControllersWithViews();

builder.Services.AddScoped<ISlugService, SlugService>();
builder.Services.AddScoped<IReadingTimeService, ReadingTimeService>();
builder.Services.AddScoped<IViewCountService, ViewCountService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IMediaService, MediaService>();
builder.Services.AddMemoryCache();
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo("/app/dataprotection-keys"));


var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();

    // Admin kullanýcýsý seed
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

    var adminEmail = builder.Configuration["AdminEmail"] ?? "admin@portfolio.local";
    var adminPassword = builder.Configuration["AdminPassword"] ?? "Admin123!@#";

    if (await userManager.FindByEmailAsync(adminEmail) is null)
    {
        var admin = new IdentityUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            EmailConfirmed = true
        };
        var result = await userManager.CreateAsync(admin, adminPassword);


        if (!result.Succeeded)
        {
            // Hatalarý log'a yaz
            foreach (var error in result.Errors)
            {
                Console.WriteLine($"SEED HATASI: {error.Code} — {error.Description}");
            }
        }
        else
        {
            Console.WriteLine("Admin kullanýcýsý oluþturuldu.");
        }

    }

    // Kategorileri seed et — yoksa oluþtur
    if (!await db.Categories.AnyAsync())
    {
        var categories = new List<Portfolio.Models.Category>
    {
        new() { Name = "Siber Güvenlik", Slug = "security", IconClass = "ti ti-shield", SortOrder = 1, Status = Portfolio.Models.Enums.VisibilityStatus.Public },
        new() { Name = "Elektronik", Slug = "electronics", IconClass = "ti ti-cpu", SortOrder = 2, Status = Portfolio.Models.Enums.VisibilityStatus.Public },
        new() { Name = "Web Uygulamalarý", Slug = "webapps", IconClass = "ti ti-browser", SortOrder = 3, Status = Portfolio.Models.Enums.VisibilityStatus.Public },
        new() { Name = "Homelab", Slug = "homelab", IconClass = "ti ti-server", SortOrder = 4, Status = Portfolio.Models.Enums.VisibilityStatus.Public },
        new() { Name = "Blog", Slug = "blog", IconClass = "ti ti-pencil", SortOrder = 5, Status = Portfolio.Models.Enums.VisibilityStatus.Public },
        new() { Name = "Ekip & Hackathon", Slug = "team", IconClass = "ti ti-users", SortOrder = 6, Status = Portfolio.Models.Enums.VisibilityStatus.Public },
        new() { Name = "Notlar", Slug = "notes", IconClass = "ti ti-notes", SortOrder = 7, Status = Portfolio.Models.Enums.VisibilityStatus.Private, IsPrivate = true },
    };

        db.Categories.AddRange(categories);
        await db.SaveChangesAsync();
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();


app.MapControllerRoute(
    name: "admin",
    pattern: $"{adminPath}/{{controller=Dashboard}}/{{action=Index}}/{{id?}}",
    defaults: new { area = "Admin" }
);
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}"
);



app.Run();
