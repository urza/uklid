using System.Globalization;
using KdyBylUklid.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var czechCulture = new CultureInfo("cs-CZ");
CultureInfo.DefaultThreadCurrentCulture = czechCulture;
CultureInfo.DefaultThreadCurrentUICulture = czechCulture;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAntiforgery();

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "db", "keys")));

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

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.Use((ctx, nxt) =>
    {
        ctx.Request.Scheme = "https";
        return nxt();
    });
}

app.Use(async (ctx, nxt) =>
{
    await nxt();
    if (ctx.Request.Method == "POST" && (ctx.Request.Path.StartsWithSegments("/add") || ctx.Request.Path.StartsWithSegments("/edit")))
    {
        var ip = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault() ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var action = ctx.Request.Path.StartsWithSegments("/add") ? "přidal" : "upravil";
        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] IP {ip} {action} záznam");
    }
});

app.UseStaticFiles();
app.UseAntiforgery();

// === ROUTES ===

app.MapGet("/", async (AppDbContext db, HttpContext ctx) =>
{
    var records = await db.CleaningRecords
        .OrderByDescending(r => r.Date)
        .ThenByDescending(r => r.TimeFrom)
        .ToListAsync();

    var unpaid = records.Where(r => !r.IsPaid).ToList();
    var unpaidHours = unpaid.Where(r => r.TotalHours.HasValue).Sum(r => r.TotalHours!.Value);
    var incompleteCount = unpaid.Count(r => !r.IsComplete);

    var recordsByMonth = records
        .GroupBy(r => r.Date.ToString("MMMM yyyy"))
        .ToDictionary(g => g.Key, g => g.ToList());

    var token = ctx.RequestServices.GetRequiredService<Microsoft.AspNetCore.Antiforgery.IAntiforgery>()
        .GetAndStoreTokens(ctx).RequestToken;

    return Results.Content(Layout("Kdy byl úklid", $$"""
        <h1>🧹 Kdy byl úklid</h1>

        {{(unpaid.Any() ? $$"""
        <section class="summary-section">
            <div class="summary-content">
                <span class="summary-label">Nezaplaceno:</span>
                <span class="summary-value">{{unpaidHours:0.#}} h</span>
                <span class="summary-count">({{unpaid.Count}} úklidů{{(incompleteCount > 0 ? $", {incompleteCount} probíhá" : "")}})</span>
            </div>
            <form method="post" action="/mark-all-paid">
                <input type="hidden" name="__RequestVerificationToken" value="{{token}}" />
                <button type="submit" class="btn btn-small" onclick="return confirm('Označit všechny nezaplacené jako zaplacené?')">
                    Označit zaplacené
                </button>
            </form>
        </section>
        """ : "")}}

        <a href="/add" class="btn btn-primary btn-block">+ Přidat záznam</a>

        <section class="list-section">
            {{(records.Any() ? string.Join("\n", recordsByMonth.Select(mg => $$"""
            <div class="month-group">
                <h3 class="month-header">{{mg.Key}}</h3>
                <ul class="record-list">
                    {{string.Join("\n", mg.Value.Select(r => $$"""
                    <li class="record-item {{(r.IsPaid ? "paid" : "unpaid")}} {{(!r.IsComplete ? "incomplete" : "")}}">
                        <a href="/edit/{{r.Id}}" class="record-link">
                            <div class="record-main">
                                <span class="record-date">{{r.Date:d.M.}}</span>
                                <span class="record-time">{{r.TimeFrom:H:mm}}–{{(r.TimeTo?.ToString("H:mm") ?? "?")}}</span>
                            </div>
                            <div class="record-right">
                                <span class="record-cleaners">{{r.CleanerCount}}×👩‍🦰</span>
                                {{(r.TotalHours.HasValue
                                    ? $"<span class=\"record-hours\">{r.TotalHours.Value:0.#}h</span>"
                                    : "<span class=\"record-hours incomplete-badge\">probíhá</span>")}}
                                {{(r.IsPaid ? "<span class=\"record-paid\">✓</span>" : "")}}
                            </div>
                        </a>
                    </li>
                    """))}}
                </ul>
            </div>
            """)) : "<p class=\"empty-state\">Zatím žádné záznamy</p>")}}
        </section>
    """), "text/html; charset=utf-8");
});

app.MapGet("/add", (HttpContext ctx) =>
{
    var token = ctx.RequestServices.GetRequiredService<Microsoft.AspNetCore.Antiforgery.IAntiforgery>()
        .GetAndStoreTokens(ctx).RequestToken;
    var today = DateOnly.FromDateTime(DateTime.Today);

    return Results.Content(Layout("Nový záznam", FormHtml(null, today, new TimeOnly(9, 0), null, 1, false, token!)), "text/html; charset=utf-8");
});

app.MapPost("/add", async (HttpRequest req, AppDbContext db) =>
{
    var form = await req.ReadFormAsync();

    var record = new CleaningRecord
    {
        Date = DateOnly.TryParseExact(form["date"], "d.M.yyyy", out var d) ? d : DateOnly.FromDateTime(DateTime.Today),
        TimeFrom = TimeOnly.TryParse(form["timeFrom"], out var tf) ? tf : new TimeOnly(9, 0),
        TimeTo = TimeOnly.TryParse(form["timeTo"], out var tt) ? tt : null,
        CleanerCount = int.TryParse(form["cleanerCount"], out var cc) ? cc : 1,
        IsPaid = form["isPaid"] == "true",
        CreatedAt = DateTime.UtcNow
    };

    db.CleaningRecords.Add(record);
    await db.SaveChangesAsync();

    return Results.Redirect("/");
});

app.MapGet("/edit/{id:int}", async (int id, AppDbContext db, HttpContext ctx) =>
{
    var record = await db.CleaningRecords.FindAsync(id);
    if (record == null)
        return Results.Redirect("/");

    var token = ctx.RequestServices.GetRequiredService<Microsoft.AspNetCore.Antiforgery.IAntiforgery>()
        .GetAndStoreTokens(ctx).RequestToken;

    return Results.Content(Layout("Upravit záznam", FormHtml(record.Id, record.Date, record.TimeFrom, record.TimeTo, record.CleanerCount, record.IsPaid, token!)), "text/html; charset=utf-8");
});

app.MapPost("/edit/{id:int}", async (int id, HttpRequest req, AppDbContext db) =>
{
    var form = await req.ReadFormAsync();

    if (form.ContainsKey("delete"))
    {
        var toDelete = await db.CleaningRecords.FindAsync(id);
        if (toDelete != null)
        {
            db.CleaningRecords.Remove(toDelete);
            await db.SaveChangesAsync();
        }
        return Results.Redirect("/");
    }

    var record = await db.CleaningRecords.FindAsync(id);
    if (record == null)
        return Results.Redirect("/");

    record.Date = DateOnly.TryParseExact(form["date"], "d.M.yyyy", out var d) ? d : record.Date;
    record.TimeFrom = TimeOnly.TryParse(form["timeFrom"], out var tf) ? tf : record.TimeFrom;
    record.TimeTo = TimeOnly.TryParse(form["timeTo"], out var tt) ? tt : null;
    record.CleanerCount = int.TryParse(form["cleanerCount"], out var cc) ? cc : record.CleanerCount;
    record.IsPaid = form["isPaid"] == "true";

    await db.SaveChangesAsync();

    return Results.Redirect("/");
});

app.MapPost("/mark-all-paid", async (AppDbContext db) =>
{
    await db.CleaningRecords
        .Where(r => !r.IsPaid)
        .ExecuteUpdateAsync(s => s.SetProperty(r => r.IsPaid, true));

    return Results.Redirect("/");
});

app.Run();

// === HTML HELPERS ===

static string Layout(string title, string content) => $$"""
    <!DOCTYPE html>
    <html lang="cs">
    <head>
        <meta charset="utf-8" />
        <meta name="viewport" content="width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no" />
        <meta name="theme-color" content="#4a90a4" />
        <meta name="mobile-web-app-capable" content="yes" />
        <meta name="apple-mobile-web-app-status-bar-style" content="default" />
        <meta name="apple-mobile-web-app-title" content="Úklid" />
        <meta property="og:type" content="website" />
        <meta property="og:title" content="Kdy byl úklid" />
        <meta property="og:description" content="Evidence úklidů" />
        <meta property="og:image" content="/icon-512.png" />
        <title>{{title}}</title>
        <link rel="manifest" href="/manifest.webmanifest" />
        <link rel="apple-touch-icon" sizes="192x192" href="/icon-192.png" />
        <link rel="icon" type="image/png" sizes="192x192" href="/icon-192.png" />
        <link rel="icon" type="image/svg+xml" href="/icon.svg" />
        <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/flatpickr/dist/flatpickr.min.css" />
        <link rel="stylesheet" href="/app.css" />
    </head>
    <body>
        <div class="container">
            {{content}}
        </div>
        <script src="https://cdn.jsdelivr.net/npm/flatpickr"></script>
        <script src="https://cdn.jsdelivr.net/npm/flatpickr/dist/l10n/cs.js"></script>
        <script>
            if ('serviceWorker' in navigator) {
                navigator.serviceWorker.register('/service-worker.js');
            }
            flatpickr(".datepicker", {
                locale: "cs",
                dateFormat: "d.m.Y",
                allowInput: true
            });
            flatpickr(".timepicker", {
                locale: "cs",
                enableTime: true,
                noCalendar: true,
                dateFormat: "H:i",
                time_24hr: true,
                allowInput: true
            });
        </script>
    </body>
    </html>
    """;

static string FormHtml(int? id, DateOnly date, TimeOnly timeFrom, TimeOnly? timeTo, int cleanerCount, bool isPaid, string token) => $$"""
    <h1>{{(id.HasValue ? "Upravit záznam" : "Nový záznam")}}</h1>

    <section class="form-section">
        <form method="post">
            <input type="hidden" name="__RequestVerificationToken" value="{{token}}" />

            <div class="form-group">
                <label for="date">Datum</label>
                <input type="text" id="date" name="date" class="datepicker" value="{{date:d.M.yyyy}}" placeholder="d.m.rrrr" required />
            </div>

            <div class="form-row">
                <div class="form-group">
                    <label for="timeFrom">Od</label>
                    <input type="text" id="timeFrom" name="timeFrom" class="timepicker" value="{{timeFrom:H:mm}}" placeholder="h:mm" required />
                </div>
                <div class="form-group">
                    <label for="timeTo">Do</label>
                    <input type="text" id="timeTo" name="timeTo" class="timepicker" value="{{(timeTo?.ToString("H:mm") ?? "")}}" placeholder="h:mm" />
                </div>
            </div>

            <div class="form-group">
                <label>Počet uklízeček</label>
                <div class="cleaner-picker">
                    <button type="button" class="cleaner-btn {{(cleanerCount == 1 ? "active" : "")}}"
                            onclick="document.getElementById('cleanerCount').value = 1; this.parentElement.querySelectorAll('.cleaner-btn').forEach(b => b.classList.remove('active')); this.classList.add('active');">
                        1×👩‍🦰
                    </button>
                    <button type="button" class="cleaner-btn {{(cleanerCount == 2 ? "active" : "")}}"
                            onclick="document.getElementById('cleanerCount').value = 2; this.parentElement.querySelectorAll('.cleaner-btn').forEach(b => b.classList.remove('active')); this.classList.add('active');">
                        2×👩‍🦰
                    </button>
                    <input type="number" id="cleanerCount" name="cleanerCount" value="{{cleanerCount}}" min="1" max="10" class="cleaner-input" />
                </div>
            </div>

            {{(id.HasValue ? $$"""
            <div class="form-group checkbox-group">
                <label class="checkbox-label">
                    <input type="checkbox" name="isPaid" value="true" {{(isPaid ? "checked" : "")}} />
                    <span>Zaplaceno</span>
                </label>
            </div>
            """ : "")}}

            <div class="form-actions">
                <button type="submit" class="btn btn-primary">
                    {{(id.HasValue ? "Uložit" : "Přidat")}}
                </button>
                <a href="/" class="btn btn-secondary">Zrušit</a>
            </div>
        </form>

        {{(id.HasValue ? $$"""
        <form method="post" class="delete-form">
            <input type="hidden" name="__RequestVerificationToken" value="{{token}}" />
            <button type="submit" name="delete" value="true" class="btn-link-danger" onclick="return confirm('Opravdu smazat tento záznam?')">
                Smazat záznam
            </button>
        </form>
        """ : "")}}
    </section>
    """;
