using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serendipity.Api.Data;
using Serendipity.Api.Domain;
using Serendipity.Api.GraphQL;
using Serendipity.Api.Identity;

namespace Serendipity.Api.City;

/// <summary>
/// 保存。ワンタップで完了し、ログインは要らない。
/// 保存時点のスナップショットを持つので、元のマークが街から消えても手元に残る。
/// </summary>
public class SaveService(IDbContextFactory<AppDbContext> dbf, IOptions<CityOptions> cityOptions, TimeProvider clock)
{
    public async Task<SaveView> SaveAsync(Viewer viewer, Guid markId, CancellationToken ct)
    {
        if (!viewer.HasDevice) throw AppError.NeedDevice();
        var now = clock.GetUtcNow();
        await using var db = await dbf.CreateDbContextAsync(ct);

        var existing = await CityService.OwnSaves(db, viewer).FirstOrDefaultAsync(s => s.MarkId == markId, ct);
        if (existing is not null) return (await ToViewsAsync(db, viewer, [existing], ct))[0];

        if (!await db.Marks.VisibleTo(viewer).AnyAsync(m => m.Id == markId, ct)) throw AppError.NotFound();

        var snapshot = await Snapshots.CaptureAsync(db, markId, now);
        var save = new Save
        {
            Id = Guid.NewGuid(),
            UserId = viewer.User?.Id,
            DeviceKeyHash = viewer.User is null ? viewer.DeviceKeyHash : null,
            MarkId = markId,
            SnapshotJson = Snapshots.Serialize(snapshot),
            CreatedAt = now,
        };
        db.Saves.Add(save);
        await Engagements.RecordAsync(db, markId, EngagementKind.Save, viewer.DeviceKeyHash!, now, cityOptions.Value);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // 二度押しで同時に保存された。先に入った方を返す。
            db.ChangeTracker.Clear();
            var winner = await CityService.OwnSaves(db, viewer).FirstAsync(s => s.MarkId == markId, ct);
            return (await ToViewsAsync(db, viewer, [winner], ct))[0];
        }
        return (await ToViewsAsync(db, viewer, [save], ct))[0];
    }

    public async Task<bool> UnsaveAsync(Viewer viewer, Guid saveId, CancellationToken ct)
    {
        if (!viewer.HasDevice) throw AppError.NeedDevice();
        await using var db = await dbf.CreateDbContextAsync(ct);
        var save = await CityService.OwnSaves(db, viewer).FirstOrDefaultAsync(s => s.Id == saveId, ct)
            ?? throw AppError.NotFound();
        db.Saves.Remove(save);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<SaveView>> ListAsync(Viewer viewer, CancellationToken ct)
    {
        if (!viewer.HasDevice) return [];
        await using var db = await dbf.CreateDbContextAsync(ct);
        var saves = await CityService.OwnSaves(db, viewer).AsNoTracking().OrderByDescending(s => s.CreatedAt).ToListAsync(ct);
        return await ToViewsAsync(db, viewer, saves, ct);
    }

    private static async Task<IReadOnlyList<SaveView>> ToViewsAsync(AppDbContext db, Viewer viewer, IReadOnlyList<Save> saves, CancellationToken ct)
    {
        var markIds = saves.Select(s => s.MarkId).ToList();
        var alive = await db.Marks.AsNoTracking()
            .Where(m => markIds.Contains(m.Id) && m.Status == MarkStatus.Public)
            .Select(m => new { m.Id, m.SeedX, m.SeedY })
            .ToDictionaryAsync(m => m.Id, ct);
        var saveIds = saves.Select(s => s.Id).ToList();
        var inShop = viewer.User is null
            ? []
            : (await db.ShopItems.Where(i => saveIds.Contains(i.SaveId)).Select(i => i.SaveId).ToListAsync(ct)).ToHashSet();

        return saves.Select(s =>
        {
            var here = alive.GetValueOrDefault(s.MarkId);
            return new SaveView(
                s.Id,
                s.CreatedAt,
                Snapshots.ForViewer(Snapshots.Deserialize(s.SnapshotJson), viewer),
                here is null ? MarkPresence.Gone : MarkPresence.InCity,
                here?.SeedX,
                here?.SeedY,
                inShop.Contains(s.Id));
        }).ToList();
    }
}
