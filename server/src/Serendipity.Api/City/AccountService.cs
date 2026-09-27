using Microsoft.EntityFrameworkCore;
using Serendipity.Api.Data;
using Serendipity.Api.Domain;
using Serendipity.Api.GraphQL;
using Serendipity.Api.Identity;

namespace Serendipity.Api.City;

public class AccountService(IDbContextFactory<AppDbContext> dbf, TimeProvider clock)
{
    public const int DisplayNameMax = 20;

    public async Task<MeView> MeAsync(Viewer viewer, CancellationToken ct)
    {
        if (!viewer.HasDevice)
            return new MeView(false, false, null, null, TrustLevel.New, false, viewer.IsAdmin, null, 0);
        await using var db = await dbf.CreateDbContextAsync(ct);
        var saveCount = await CityService.OwnSaves(db, viewer).CountAsync(ct);
        Guid? shopId = viewer.User is null
            ? null
            : await db.Shops.Where(s => s.OwnerUserId == viewer.User.Id).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
        return new MeView(true, viewer.IsRegistered, viewer.User?.Id, viewer.User?.DisplayName, viewer.Trust,
            viewer.IsKidsMode, viewer.IsAdmin, shopId, saveCount);
    }

    /// <summary>
    /// 名前を登録する。この端末に保存していたものは、そのまま引き継ぐ。
    /// </summary>
    public async Task<MeView> RegisterAsync(Viewer viewer, string displayName, CancellationToken ct)
    {
        if (!viewer.HasDevice) throw AppError.NeedDevice();
        if (viewer.IsRegistered) throw AppError.Of("ALREADY_REGISTERED", "この端末はもう登録済みです。");
        var name = TextRules.Normalize(displayName);
        if (name.Length == 0) throw AppError.Of("NAME_REQUIRED", "名前を入れてください。");
        if (TextRules.Length(name) > DisplayNameMax) throw AppError.Of("NAME_TOO_LONG", $"名前は{DisplayNameMax}文字までです。");

        var now = clock.GetUtcNow();
        await using var db = await dbf.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var device = await db.Devices.FirstAsync(d => d.KeyHash == viewer.DeviceKeyHash, ct);
        if (device.UserId is not null) throw AppError.Of("ALREADY_REGISTERED", "この端末はもう登録済みです。");

        var user = new User
        {
            Id = Guid.NewGuid(),
            DisplayName = name,
            Trust = TrustLevel.New,
            IsKidsMode = device.IsKidsMode,
            CreatedAt = now,
        };
        db.Users.Add(user);
        device.UserId = user.Id;
        await db.SaveChangesAsync(ct);
        await MigrateDeviceSavesAsync(db, device.KeyHash, user.Id, ct);
        await tx.CommitAsync(ct);

        var registered = new Viewer { DeviceKeyHash = device.KeyHash, Device = device, User = user, IsAdmin = viewer.IsAdmin };
        return await MeAsync(registered, ct);
    }

    public async Task<MeView> UpdateSettingsAsync(Viewer viewer, bool isKidsMode, CancellationToken ct)
    {
        if (!viewer.HasDevice) throw AppError.NeedDevice();
        await using var db = await dbf.CreateDbContextAsync(ct);
        var device = await db.Devices.Include(d => d.User).FirstAsync(d => d.KeyHash == viewer.DeviceKeyHash, ct);
        device.IsKidsMode = isKidsMode;
        if (device.User is not null) device.User.IsKidsMode = isKidsMode;
        await db.SaveChangesAsync(ct);
        return await MeAsync(new Viewer { DeviceKeyHash = device.KeyHash, Device = device, User = device.User, IsAdmin = viewer.IsAdmin }, ct);
    }

    /// <summary>端末に紐づく保存をユーザーへ移す。同じマークを既に保存していれば端末側を捨てる。</summary>
    internal static async Task MigrateDeviceSavesAsync(AppDbContext db, string deviceKeyHash, Guid userId, CancellationToken ct)
    {
        var deviceSaves = await db.Saves.Where(s => s.DeviceKeyHash == deviceKeyHash && s.UserId == null).ToListAsync(ct);
        var already = (await db.Saves.Where(s => s.UserId == userId).Select(s => s.MarkId).ToListAsync(ct)).ToHashSet();
        foreach (var s in deviceSaves)
        {
            if (already.Contains(s.MarkId))
            {
                db.Saves.Remove(s);
                continue;
            }
            s.UserId = userId;
            s.DeviceKeyHash = null;
        }
        await db.SaveChangesAsync(ct);
    }
}
