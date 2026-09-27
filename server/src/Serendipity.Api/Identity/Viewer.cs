using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serendipity.Api.Data;
using Serendipity.Api.Domain;

namespace Serendipity.Api.Identity;

/// <summary>いま操作している人。未登録なら端末だけ、登録済みならユーザーも持つ。</summary>
public sealed class Viewer
{
    public string? DeviceKeyHash { get; init; }
    public Device? Device { get; init; }
    public User? User { get; init; }
    public bool IsAdmin { get; init; }

    public bool HasDevice => DeviceKeyHash is not null;
    public bool IsRegistered => User is not null;
    public bool IsKidsMode => User?.IsKidsMode ?? Device?.IsKidsMode ?? false;
    public TrustLevel Trust => User?.Trust ?? TrustLevel.New;
}

public static partial class DeviceKeys
{
    public const string Header = "X-Device-Id";
    public const string AdminHeader = "X-Admin-Key";

    public static bool IsValid(string? raw) => raw is not null && DeviceIdRegex().IsMatch(raw);

    /// <summary>Device ID は資格情報なので、DB にはハッシュだけを残す。</summary>
    public static string Hash(string raw) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    [GeneratedRegex("^[A-Za-z0-9_-]{16,128}$")]
    private static partial Regex DeviceIdRegex();
}

public class ViewerAccessor(
    IHttpContextAccessor http,
    IDbContextFactory<AppDbContext> dbf,
    IOptions<AdminOptions> admin,
    TimeProvider clock)
{
    private static readonly object ItemKey = new();

    /// <summary>
    /// リクエストごとに一度だけ読み込む。GraphQL は複数のフィールドを並行して解決するので、
    /// 結果はスコープではなく HttpContext に置いて共有する。
    /// </summary>
    public Task<Viewer> GetAsync()
    {
        var ctx = http.HttpContext;
        if (ctx is null) return LoadAsync(null, dbf, admin.Value, clock);
        lock (ctx.Items)
        {
            if (ctx.Items.TryGetValue(ItemKey, out var cached) && cached is Task<Viewer> task) return task;
            var load = LoadAsync(ctx, dbf, admin.Value, clock);
            ctx.Items[ItemKey] = load;
            return load;
        }
    }

    private static async Task<Viewer> LoadAsync(HttpContext? ctx, IDbContextFactory<AppDbContext> dbf, AdminOptions admin, TimeProvider clock)
    {
        var isAdmin = false;
        if (ctx is not null && !string.IsNullOrEmpty(admin.Key)
            && ctx.Request.Headers.TryGetValue(DeviceKeys.AdminHeader, out var adminKey))
        {
            isAdmin = CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(adminKey.ToString()), Encoding.UTF8.GetBytes(admin.Key));
        }

        var raw = ctx?.Request.Headers[DeviceKeys.Header].ToString();
        if (!DeviceKeys.IsValid(raw)) return new Viewer { IsAdmin = isAdmin };

        var hash = DeviceKeys.Hash(raw!);
        var now = clock.GetUtcNow();
        await using var db = await dbf.CreateDbContextAsync();
        var device = await db.Devices.Include(d => d.User).FirstOrDefaultAsync(d => d.KeyHash == hash);
        if (device is null)
        {
            device = new Device { KeyHash = hash, CreatedAt = now, LastSeenAt = now };
            db.Devices.Add(device);
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // 同じ端末からの同時リクエストが先に作った。
                db.ChangeTracker.Clear();
                device = await db.Devices.Include(d => d.User).FirstAsync(d => d.KeyHash == hash);
            }
        }
        else if (now - device.LastSeenAt > TimeSpan.FromHours(1))
        {
            device.LastSeenAt = now;
            await db.SaveChangesAsync();
        }

        return new Viewer { DeviceKeyHash = hash, Device = device, User = device.User, IsAdmin = isAdmin };
    }
}
