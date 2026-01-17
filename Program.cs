using System.Globalization;
using KdyBylUklid.Components;
using KdyBylUklid.Data;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var czechCulture = new CultureInfo("cs-CZ");
CultureInfo.DefaultThreadCurrentCulture = czechCulture;
CultureInfo.DefaultThreadCurrentUICulture = czechCulture;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents();

// Pro reverse proxy (X-Forwarded-Proto header)
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var dbPath = Path.Combine(builder.Environment.ContentRootPath, "db", "uklid.db");
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

var app = builder.Build();

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

// Configure the HTTP request pipeline.
app.UseForwardedHeaders();

// Fallback: pokud proxy neposlal X-Forwarded-Proto, vynutíme HTTPS
if (!app.Environment.IsDevelopment())
{
    app.Use((ctx, nxt) =>
    {
        ctx.Request.Scheme = "https";
        return nxt();
    });
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>();

app.Run();
