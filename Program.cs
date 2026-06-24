using Microsoft.EntityFrameworkCore;
using Portfolio.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        npgsqlOptions =>
        {
            // Migration assembly — proje adýnla eþleþmeli
            npgsqlOptions.MigrationsAssembly("Portfolio");

            // Baðlantý koptuðunda otomatik yeniden dene (VPS için önemli)
            npgsqlOptions.EnableRetryOnFailure(
                maxRetryCount: 3,
                maxRetryDelay: TimeSpan.FromSeconds(5),
                errorCodesToAdd: null
            );
        }
    )
);

// Add services to the container.
builder.Services.AddControllersWithViews();

// builder.Services.AddScoped<ISlugService, SlugService>();
// builder.Services.AddScoped<IMediaService, MediaService>();
// builder.Services.AddScoped<IReadingTimeService, ReadingTimeService>();
// builder.Services.AddScoped<IAuditService, AuditService>();
// builder.Services.AddScoped<IViewCountService, ViewCountService>();
// builder.Services.AddMemoryCache(); // Kategori listesi için


var app = builder.Build();

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

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Migration'ý startup'ta otomatik uygula (opsiyonel — prod'da dikkatli kullan)
// using var scope = app.Services.CreateScope();
// var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
// db.Database.Migrate();

app.Run();
