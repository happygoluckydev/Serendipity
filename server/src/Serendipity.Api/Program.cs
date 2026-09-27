using System.Text;
using System.Threading.RateLimiting;
using Anthropic;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serendipity.Api.City;
using Serendipity.Api.Data;
using Serendipity.Api.Domain;
using Serendipity.Api.GraphQL;
using Serendipity.Api.Identity;
using Serendipity.Api.Jobs;
using Serendipity.Api.Safety;
using Serendipity.Api.Storage;

// Shift_JIS などで書かれたリンク先を読めるようにする。
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

builder.Services.Configure<CityOptions>(config.GetSection("City"));
builder.Services.Configure<SafetyOptions>(config.GetSection("Safety"));
builder.Services.Configure<JobOptions>(config.GetSection("Jobs"));
builder.Services.Configure<AdminOptions>(config.GetSection("Admin"));
builder.Services.Configure<ModerationOptions>(config.GetSection("Moderation"));
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddDbContextFactory<AppDbContext>(o =>
    o.UseNpgsql(config.GetConnectionString("Default")
        ?? throw new InvalidOperationException("ConnectionStrings:Default が未設定です")));

var storageRoot = config["Storage:Root"] ?? Path.Combine(builder.Environment.ContentRootPath, "data", "files");
builder.Services.AddSingleton<IObjectStorage>(new LocalDiskStorage(storageRoot));

builder.Services.AddHttpClient<IPreviewFetcher, HttpPreviewFetcher>(c =>
    {
        c.Timeout = TimeSpan.FromSeconds(15);
        c.DefaultRequestHeaders.UserAgent.ParseAdd("MachiarukiPreview/0.1 (+link preview)");
    })
    .ConfigurePrimaryHttpMessageHandler(NetworkGuard.CreateHandler);

var moderationProvider = config["Moderation:Provider"]
    ?? (string.IsNullOrEmpty(config["ANTHROPIC_API_KEY"]) ? "rules" : "claude");
if (moderationProvider.Equals("claude", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton(_ => new AnthropicClient());
    builder.Services.AddSingleton<IContentModerator, ClaudeModerator>();
}
else
{
    builder.Services.AddSingleton<IContentModerator, RuleBasedModerator>();
}

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ViewerAccessor>();
builder.Services.AddScoped<CityService>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<SaveService>();
builder.Services.AddScoped<ShopService>();
builder.Services.AddScoped<SafetyService>();
builder.Services.AddScoped<ReviewService>();
builder.Services.AddScoped<MaintenanceService>();

if (config.GetValue("Jobs:Enabled", true))
{
    builder.Services.AddHostedService<ReviewWorker>();
    builder.Services.AddHostedService<MaintenanceWorker>();
    builder.Services.AddHostedService<RecheckWorker>();
}

builder.Services
    .AddGraphQLServer()
    .AddQueryType<Query>()
    .AddMutationType<Mutation>()
    .ModifyRequestOptions(o => o.IncludeExceptionDetails = builder.Environment.IsDevelopment())
    .ModifyCostOptions(o => o.EnforceCostLimits = false)
    .AddMaxExecutionDepthRule(8);

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    static string Partition(HttpContext ctx) =>
        ctx.Request.Headers.TryGetValue(DeviceKeys.Header, out var id) && DeviceKeys.IsValid(id)
            ? "d:" + DeviceKeys.Hash(id!)
            : "ip:" + ctx.Connection.RemoteIpAddress;
    o.AddPolicy("api", ctx => RateLimitPartition.GetFixedWindowLimiter(Partition(ctx),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 300, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("upload", ctx => RateLimitPartition.GetFixedWindowLimiter(Partition(ctx),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(10) }));
});
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = ImageFiles.MaxBytes + 64 * 1024);

var app = builder.Build();

if (config.GetValue("Database:MigrateOnStartup", true))
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbf = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    await using var db = await dbf.CreateDbContextAsync();
    await db.Database.MigrateAsync();
    var now = TimeProvider.System.GetUtcNow();
    await Seeder.SeedDomainRulesAsync(db, now);
    if (config.GetValue("Seed:Demo", false)) await Seeder.SeedDemoCityAsync(db, now);
}

app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    // 外部サイトへ移るとき、街のどこから来たかを渡しすぎない。
    h["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});

app.UseRateLimiter();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGraphQL("/graphql").RequireRateLimiting("api");

app.MapPost("/api/photos", async (HttpRequest req, ViewerAccessor viewers, IObjectStorage storage, CancellationToken ct) =>
{
    var viewer = await viewers.GetAsync();
    if (!viewer.HasDevice) return Results.BadRequest(new { error = "この端末を識別できませんでした。" });
    if (viewer.User is null) return Results.BadRequest(new { error = "写真を付けるには名前の登録が必要です。" });
    if (viewer.User.IsBlocked) return Results.StatusCode(StatusCodes.Status403Forbidden);
    if (!req.HasFormContentType) return Results.BadRequest(new { error = "写真を選んでください。" });

    var form = await req.ReadFormAsync(ct);
    var file = form.Files.GetFile("file");
    if (file is null || file.Length == 0) return Results.BadRequest(new { error = "写真を選んでください。" });
    if (file.Length > ImageFiles.MaxBytes) return Results.BadRequest(new { error = "写真は5MBまでです。" });

    using var ms = new MemoryStream();
    await file.CopyToAsync(ms, ct);
    var bytes = ms.ToArray();
    var ext = ImageFiles.DetectExtension(bytes);
    if (ext is null) return Results.BadRequest(new { error = "JPEG・PNG・WebP・GIF の写真を選んでください。" });
    var key = await storage.PutAsync(bytes, ext, ct);
    return Results.Ok(new { key, url = ImageFiles.UrlFor(key) });
}).RequireRateLimiting("upload").DisableAntiforgery();

app.MapGet("/files/{key}", async (string key, IObjectStorage storage, HttpContext ctx, CancellationToken ct) =>
{
    var file = await storage.OpenAsync(key, ct);
    if (file is null) return Results.NotFound();
    ctx.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
    ctx.Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
    return Results.Stream(file.Value.Stream, file.Value.ContentType);
});

app.MapGet("/healthz", () => Results.Ok(new { ok = true }));

// クライアントのビルドを同じオリジンで配る(wwwroot があるとき)。
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
