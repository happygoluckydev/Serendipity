using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serendipity.Api.Data;
using Serendipity.Api.Domain;
using Serendipity.Api.GraphQL;
using Serendipity.Api.Identity;

namespace Serendipity.Api.City;

/// <summary>
/// 自分の店。保存は非公開が前提で、店に並べるのは別の明示操作。
/// 並んだものは元の置き手のポップと元のマークに辿れる。店そのものも街のマーク(形は「お店」)になる。
/// </summary>
public class ShopService(
    IDbContextFactory<AppDbContext> dbf,
    CityService city,
    IOptions<SafetyOptions> safetyOptions,
    TimeProvider clock)
{
    public const int NameMax = 20;

    public async Task<ShopView> OpenShopAsync(Viewer viewer, string name, CancellationToken ct)
    {
        var user = CityService.RequireActiveUser(viewer);
        var clean = ValidateName(name);
        await using var db = await dbf.CreateDbContextAsync(ct);
        if (await db.Shops.AnyAsync(s => s.OwnerUserId == user.Id, ct))
            throw AppError.Of("SHOP_EXISTS", "店はもう開いています。");
        var shop = new Shop { Id = Guid.NewGuid(), OwnerUserId = user.Id, Name = clean, CreatedAt = clock.GetUtcNow() };
        db.Shops.Add(shop);
        await db.SaveChangesAsync(ct);
        return await GetShopAsync(viewer, shop.Id, ct);
    }

    public async Task<ShopView> RenameShopAsync(Viewer viewer, string name, CancellationToken ct)
    {
        var user = CityService.RequireActiveUser(viewer);
        var clean = ValidateName(name);
        await using var db = await dbf.CreateDbContextAsync(ct);
        var shop = await db.Shops.FirstOrDefaultAsync(s => s.OwnerUserId == user.Id, ct) ?? throw AppError.NotFound();
        shop.Name = clean;
        await db.SaveChangesAsync(ct);
        return await GetShopAsync(viewer, shop.Id, ct);
    }

    public async Task<ShopView> AddItemAsync(Viewer viewer, Guid saveId, ExperienceStage stage, CancellationToken ct)
    {
        var user = CityService.RequireActiveUser(viewer);
        await using var db = await dbf.CreateDbContextAsync(ct);
        var shop = await db.Shops.FirstOrDefaultAsync(s => s.OwnerUserId == user.Id, ct)
            ?? throw AppError.Of("NO_SHOP", "先に店を開いてください。");
        if (!await db.Saves.AnyAsync(s => s.Id == saveId && s.UserId == user.Id, ct)) throw AppError.NotFound();
        if (await db.ShopItems.AnyAsync(i => i.ShopId == shop.Id && i.SaveId == saveId, ct))
            throw AppError.Of("ALREADY_IN_SHOP", "もう店に並んでいます。");
        var position = await db.ShopItems.Where(i => i.ShopId == shop.Id).MaxAsync(i => (int?)i.Position, ct) ?? -1;
        db.ShopItems.Add(new ShopItem
        {
            Id = Guid.NewGuid(),
            ShopId = shop.Id,
            SaveId = saveId,
            Stage = stage,
            Position = position + 1,
            CreatedAt = clock.GetUtcNow(),
        });
        await TouchShopMarkAsync(db, shop, ct);
        await db.SaveChangesAsync(ct);
        return await GetShopAsync(viewer, shop.Id, ct);
    }

    public async Task<ShopView> UpdateItemAsync(Viewer viewer, Guid itemId, ExperienceStage stage, CancellationToken ct)
    {
        var user = CityService.RequireActiveUser(viewer);
        await using var db = await dbf.CreateDbContextAsync(ct);
        var shop = await db.Shops.FirstOrDefaultAsync(s => s.OwnerUserId == user.Id, ct) ?? throw AppError.NotFound();
        var item = await db.ShopItems.FirstOrDefaultAsync(i => i.Id == itemId && i.ShopId == shop.Id, ct) ?? throw AppError.NotFound();
        item.Stage = stage;
        await db.SaveChangesAsync(ct);
        return await GetShopAsync(viewer, shop.Id, ct);
    }

    public async Task<ShopView> RemoveItemAsync(Viewer viewer, Guid itemId, CancellationToken ct)
    {
        var user = CityService.RequireActiveUser(viewer);
        await using var db = await dbf.CreateDbContextAsync(ct);
        var shop = await db.Shops.FirstOrDefaultAsync(s => s.OwnerUserId == user.Id, ct) ?? throw AppError.NotFound();
        var item = await db.ShopItems.FirstOrDefaultAsync(i => i.Id == itemId && i.ShopId == shop.Id, ct) ?? throw AppError.NotFound();
        db.ShopItems.Remove(item);
        await db.SaveChangesAsync(ct);
        return await GetShopAsync(viewer, shop.Id, ct);
    }

    /// <summary>
    /// 店を公開する。店のマークを街に置く(隣は選んだマーク、なければ最初に並べたものの元のマーク)。
    /// 閉じると店のマークは街から消える。
    /// </summary>
    public async Task<ShopView> SetPublishedAsync(Viewer viewer, bool published, Guid? nextToMarkId, CancellationToken ct)
    {
        var user = CityService.RequireActiveUser(viewer);
        var now = clock.GetUtcNow();
        await using var db = await dbf.CreateDbContextAsync(ct);
        var shop = await db.Shops.Include(s => s.Items).ThenInclude(i => i.Save)
            .FirstOrDefaultAsync(s => s.OwnerUserId == user.Id, ct) ?? throw AppError.NotFound();

        if (!published)
        {
            shop.IsPublished = false;
            if (shop.MarkId is { } markId)
            {
                await db.Marks.Where(m => m.Id == markId && m.Status != MarkStatus.Hidden)
                    .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, MarkStatus.Gone).SetProperty(m => m.GoneAt, now), ct);
            }
            await db.SaveChangesAsync(ct);
            return await GetShopAsync(viewer, shop.Id, ct);
        }

        if (shop.Items.Count == 0) throw AppError.Of("SHOP_EMPTY", "店に一つ以上並べてから公開してください。");
        shop.IsPublished = true;

        var current = shop.MarkId is null ? null : await db.Marks.FirstOrDefaultAsync(m => m.Id == shop.MarkId, ct);
        if (current is null || current.Status == MarkStatus.Gone)
        {
            var anchor = nextToMarkId;
            if (anchor is null)
            {
                var candidates = shop.Items.OrderBy(i => i.Position).Select(i => i.Save!.MarkId).ToList();
                anchor = await db.Marks.Where(m => candidates.Contains(m.Id) && m.Status == MarkStatus.Public)
                    .Select(m => (Guid?)m.Id).FirstOrDefaultAsync(ct);
                anchor ??= await db.Marks.Where(m => m.Status == MarkStatus.Public)
                    .OrderByDescending(m => m.LastEngagedAt).Select(m => (Guid?)m.Id).FirstOrDefaultAsync(ct);
            }
            var (x, y) = await city.ChoosePositionAsync(db, viewer, anchor, CityLayout.Radius(1), ct);
            var markId = Guid.NewGuid();
            if (anchor is not null) (x, y) = await city.SettleAsync(db, markId, anchor.Value, x, y, ct);

            var genre = shop.Items
                .Select(i => Snapshots.Deserialize(i.Save!.SnapshotJson).Genre)
                .GroupBy(g => g).OrderByDescending(g => g.Count()).First().Key;
            var mark = new Mark
            {
                Id = markId,
                Shape = MarkShape.Store,
                Genre = genre,
                OwnerUserId = user.Id,
                Status = MarkStatus.Pending,
                PendingReason = PendingReason.AutoReview,
                SeedX = x,
                SeedY = y,
                ShopId = shop.Id,
                CreatedAt = now,
                LastEngagedAt = now,
            };
            db.Marks.Add(mark);
            db.Pops.Add(new Pop
            {
                Id = Guid.NewGuid(),
                MarkId = mark.Id,
                AuthorUserId = user.Id,
                Text = ShopPopText(shop.Name, user.DisplayName, safetyOptions.Value.PopMaxLength),
                Stage = ExperienceStage.Used,
                Status = PopStatus.Pending,
                CreatedAt = now,
            });
            if (anchor is not null)
                db.Adjacencies.Add(new Adjacency { MarkAId = mark.Id, MarkBId = anchor.Value, CreatedAt = now });
            shop.MarkId = mark.Id;
        }
        await db.SaveChangesAsync(ct);
        return await GetShopAsync(viewer, shop.Id, ct);
    }

    public async Task<ShopView?> MyShopAsync(Viewer viewer, CancellationToken ct)
    {
        if (viewer.User is null) return null;
        await using var db = await dbf.CreateDbContextAsync(ct);
        var id = await db.Shops.Where(s => s.OwnerUserId == viewer.User.Id).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
        return id is null ? null : await GetShopAsync(viewer, id.Value, ct);
    }

    public async Task<ShopView> GetShopAsync(Viewer viewer, Guid shopId, CancellationToken ct)
    {
        await using var db = await dbf.CreateDbContextAsync(ct);
        var shop = await db.Shops.AsNoTracking()
            .Include(s => s.Owner)
            .Include(s => s.Items).ThenInclude(i => i.Save)
            .FirstOrDefaultAsync(s => s.Id == shopId, ct) ?? throw AppError.NotFound();
        var isMine = viewer.User?.Id == shop.OwnerUserId;
        if (!shop.IsPublished && !isMine) throw AppError.NotFound();
        if (shop.Owner!.IsBlocked && !isMine) throw AppError.NotFound();

        var snapshots = shop.Items.ToDictionary(i => i.Id, i => Snapshots.ForViewer(Snapshots.Deserialize(i.Save!.SnapshotJson), viewer));
        var originalIds = snapshots.Values.Select(s => s.MarkId).Append(shop.MarkId ?? Guid.Empty).ToList();
        var alive = (await db.Marks.Where(m => originalIds.Contains(m.Id) && m.Status == MarkStatus.Public)
            .Select(m => m.Id).ToListAsync(ct)).ToHashSet();

        var items = shop.Items.OrderBy(i => i.Position).Select(i =>
        {
            var snap = snapshots[i.Id];
            return new ShopItemView(i.Id, i.Stage, snap, snap.PopAuthorName, snap.MarkId, alive.Contains(snap.MarkId), i.Position);
        }).ToList();

        return new ShopView(shop.Id, shop.Name, shop.Owner!.DisplayName, shop.IsPublished, isMine, shop.MarkId,
            shop.MarkId is not null && alive.Contains(shop.MarkId.Value), items);
    }

    internal static string ShopPopText(string shopName, string ownerName, int max)
    {
        var text = $"{shopName}（{ownerName}の店）";
        return TextRules.Length(text) <= max ? text : shopName;
    }

    private static string ValidateName(string name)
    {
        var clean = TextRules.Normalize(name);
        if (clean.Length == 0) throw AppError.Of("NAME_REQUIRED", "店の名前を入れてください。");
        if (TextRules.Length(clean) > NameMax) throw AppError.Of("NAME_TOO_LONG", $"店の名前は{NameMax}文字までです。");
        return clean;
    }

    /// <summary>店を手入れしている間は、店のマークを薄れさせない。</summary>
    private async Task TouchShopMarkAsync(AppDbContext db, Shop shop, CancellationToken ct)
    {
        if (shop.MarkId is not { } markId) return;
        var now = clock.GetUtcNow();
        await db.Marks.Where(m => m.Id == markId).ExecuteUpdateAsync(s => s.SetProperty(m => m.LastEngagedAt, now), ct);
    }
}
