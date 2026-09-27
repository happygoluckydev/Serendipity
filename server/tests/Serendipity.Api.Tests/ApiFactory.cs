using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Serendipity.Api.Data;
using Serendipity.Api.Jobs;
using Serendipity.Api.Safety;

namespace Serendipity.Api.Tests;

/// <summary>リンク先を実際には取りに行かない。URL ごとに返す中身を決めておける。</summary>
public class FakePreviewFetcher : IPreviewFetcher
{
    public Dictionary<string, FetchedPreview> Pages { get; } = [];
    public HashSet<string> Failing { get; } = [];

    public Task<FetchedPreview> FetchAsync(Uri url, CancellationToken ct)
    {
        if (Failing.Contains(url.AbsoluteUri)) throw new HttpRequestException("接続できません");
        if (Pages.TryGetValue(url.AbsoluteUri, out var page)) return Task.FromResult(page);
        return Task.FromResult(Page(url.AbsoluteUri, "ふつうのページ", "特に問題のない本文"));
    }

    public static FetchedPreview Page(string url, string title, string body, params string[] links) =>
        new(title, null, null, null, body, HttpPreviewFetcher.Hash($"{title}\n{body}"), links, url);
}

/// <summary>
/// テストごとに使い捨ての PostgreSQL データベースを作る。
/// 接続先は TEST_DATABASE_URL(Database を除いた接続文字列)で変えられる。
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminKey = "test-admin-key";
    private readonly string _database = $"serendipity_test_{Guid.NewGuid():N}";
    private readonly string _storage = Path.Combine(Path.GetTempPath(), $"serendipity-test-{Guid.NewGuid():N}");

    public FakePreviewFetcher Previews { get; } = new();

    private static string ServerConnection =>
        Environment.GetEnvironmentVariable("TEST_DATABASE_URL")
        ?? "Host=localhost;Port=5432;Username=postgres;Password=postgres";

    public string ConnectionString => $"{ServerConnection};Database={_database}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        builder.UseSetting("Jobs:Enabled", "false");
        builder.UseSetting("Moderation:Provider", "rules");
        builder.UseSetting("Admin:Key", AdminKey);
        builder.UseSetting("Seed:Demo", "false");
        builder.UseSetting("Storage:Root", _storage);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IPreviewFetcher>();
            services.AddSingleton<IPreviewFetcher>(Previews);
        });
    }

    public Task InitializeAsync()
    {
        // 最初のリクエストの前にホストを作り、マイグレーションを流しておく。
        _ = Server;
        return Task.CompletedTask;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await using var conn = new NpgsqlConnection($"{ServerConnection};Database=postgres");
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)", conn);
        await cmd.ExecuteNonQueryAsync();
        if (Directory.Exists(_storage)) Directory.Delete(_storage, recursive: true);
    }

    public async Task WithScopeAsync(Func<IServiceProvider, Task> action)
    {
        await using var scope = Services.CreateAsyncScope();
        await action(scope.ServiceProvider);
    }

    public Task ReviewAsync() => WithScopeAsync(async sp =>
    {
        var review = sp.GetRequiredService<ReviewService>();
        await review.ReviewPendingMarksAsync(100, default);
        await review.ReviewPendingPopsAsync(100, default);
    });

    public async Task DbAsync(Func<AppDbContext, Task> action)
    {
        await using var scope = Services.CreateAsyncScope();
        var dbf = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await dbf.CreateDbContextAsync();
        await action(db);
    }

    /// <summary>ある端末として GraphQL を呼ぶクライアント。</summary>
    public Device As(string name, bool admin = false) => new(CreateClient(), $"device-{name}-{_database[^12..]}", admin);

    public sealed class Device(HttpClient http, string id, bool admin)
    {
        public string Id => id;

        public async Task<GqlResult> Gql(string query, object? variables = null)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/graphql")
            {
                Content = JsonContent.Create(new { query, variables }),
            };
            req.Headers.Add("X-Device-Id", id);
            if (admin) req.Headers.Add("X-Admin-Key", AdminKey);
            using var res = await http.SendAsync(req);
            var body = await res.Content.ReadFromJsonAsync<JsonElement>();
            return new GqlResult(body);
        }

        /// <summary>エラーがないことを確かめて data を返す。</summary>
        public async Task<JsonElement> Ok(string query, object? variables = null)
        {
            var r = await Gql(query, variables);
            if (r.ErrorCode is not null) throw new Xunit.Sdk.XunitException($"GraphQL error {r.ErrorCode}: {r.Body}");
            return r.Data;
        }
    }

    public sealed record GqlResult(JsonElement Body)
    {
        public JsonElement Data => Body.GetProperty("data");

        public string? ErrorCode =>
            Body.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0
                ? errors[0].TryGetProperty("extensions", out var ext) && ext.TryGetProperty("code", out var code)
                    ? code.GetString()
                    : "UNKNOWN"
                : null;
    }
}
