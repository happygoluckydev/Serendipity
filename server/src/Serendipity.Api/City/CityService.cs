using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serendipity.Api.Data;
using Serendipity.Api.Domain;
using Serendipity.Api.GraphQL;
using Serendipity.Api.Identity;
using Serendipity.Api.Safety;
using Serendipity.Api.Storage;

namespace Serendipity.Api.City;

public sealed record PlaceMarkInput(
    Guid? NextToMarkId,
    string? Url,
    MarkShape Shape,
    Genre Genre,
    ExperienceStage Stage,
    string Text,
    string? PhotoKey);

public sealed record AddPopInput(Guid MarkId, string Text, ExperienceStage Stage, string? PhotoKey);

public sealed record Bounds(double MinX, double MinY, double MaxX, double MaxY);

public class CityService(
    IDbContextFactory<AppDbContext> dbf,
    IObjectStorage storage,
    IOptions<CityOptions> cityOptions,
    IOptions<SafetyOptions> safetyOptions,
    TimeProvider clock)
{
    private CityOptions City => cityOptions.Value;
    private SafetyOptions Safety => safetyOptions.Value;

    public async Task<CityView> GetCityAsync(Viewer viewer, Bounds? bounds, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await using var db = await dbf.CreateDbContextAsync(ct);
        var q = db.Marks.AsNoTracking().VisibleTo(viewer);
        if (bounds is not null)
        {
            // 範囲の外側に種があっても、縄張りが範囲に掛かることがあるので少し広めに取る。
            var margin = CityLayout.Radius(City.WeightCap) * 2;
            q = q.Where(m => m.SeedX >= bounds.MinX - margin && m.SeedX <= bounds.MaxX + margin
                && m.SeedY >= bounds.MinY - margin && m.SeedY <= bounds.MaxY + margin);
        }

        var userId = viewer.User?.Id;
        var rows = await q.Select(m => new
        {
            m.Id, m.SeedX, m.SeedY, m.Weight, m.Shape, m.Genre, m.Status, m.OwnerUserId, m.HasPr, m.ShopId,
            m.CreatedAt, m.LastEngagedAt,
            Pop = m.Pops.Where(p => p.AuthorUserId == m.OwnerUserId).OrderBy(p => p.CreatedAt)
                .Select(p => new { p.Text, p.PhotoKey, p.Stage, p.HasPr }).FirstOrDefault(),
            PopCount = m.Pops.Count(p => p.Status == PopStatus.Visible),
            AnyPopPr = m.Pops.Any(p => p.Status == PopStatus.Visible && p.HasPr),
            PreviewPr = m.Preview != null && m.Preview.HasPr,
        }).ToListAsync(ct);

        var ids = rows.Select(r => r.Id).ToHashSet();
        var neighbors = await Snapshots.Neighbors(db, ids);

        var marks = rows.Where(r => r.Pop is not null).Select(r => new CityMark(
            r.Id,
            r.SeedX,
            r.SeedY,
            r.Weight,
            r.Shape,
            r.Genre,
            r.Pop!.Stage,
            r.Status,
            userId != null && r.OwnerUserId == userId,
            (now - r.CreatedAt).TotalDays <= City.NewGlowDays,
            Vitality.Opacity(r.LastEngagedAt, now, City),
            r.Pop.Text,
            r.Pop.PhotoKey is null ? null : ImageFiles.UrlFor(r.Pop.PhotoKey),
            Math.Max(1, r.PopCount),
            r.HasPr || r.Pop.HasPr || r.AnyPopPr || r.PreviewPr,
            r.ShopId is not null,
            r.ShopId,
            neighbors.TryGetValue(r.Id, out var n) ? n.Where(ids.Contains).ToList() : [],
            r.CreatedAt)).ToList();

        return new CityView(marks, CityLayout.BaseRadius, now);
    }

    public async Task<MarkDetail> GetMarkAsync(Viewer viewer, Guid id, CancellationToken ct)
    {
        await using var db = await dbf.CreateDbContextAsync(ct);
        var mark = await db.Marks.AsNoTracking().VisibleTo(viewer)
            .Include(m => m.Owner)
            .Include(m => m.Preview)
            .Include(m => m.Pops).ThenInclude(p => p.Author)
            .FirstOrDefaultAsync(m => m.Id == id, ct)
            ?? throw AppError.NotFound();

        var userId = viewer.User?.Id;
        var isMine = userId == mark.OwnerUserId;
        var pops = mark.Pops
            .Where(p => p.Status == PopStatus.Visible
                || (p.Status == PopStatus.Pending && (p.AuthorUserId == userId || (isMine && p.AuthorUserId == mark.OwnerUserId))))
            .OrderBy(p => p.AuthorUserId == mark.OwnerUserId ? 0 : 1)
            .ThenBy(p => p.CreatedAt)
            .Select(p => new PopView(
                p.Id, p.Author!.DisplayName, p.Text,
                p.PhotoKey is null ? null : ImageFiles.UrlFor(p.PhotoKey),
                p.Stage, p.HasPr, p.AuthorUserId == mark.OwnerUserId, p.Status, p.CreatedAt))
            .ToList();
        var original = pops.FirstOrDefault(p => p.IsOriginal) ?? throw AppError.NotFound();

        var neighborIds = (await Snapshots.Neighbors(db, [id])).GetValueOrDefault(id) ?? [];
        var neighbors = await db.Marks.AsNoTracking()
            .Where(m => neighborIds.Contains(m.Id) && m.Status == MarkStatus.Public)
            .Select(m => new NeighborView(
                m.Id,
                m.Pops.Where(p => p.AuthorUserId == m.OwnerUserId).OrderBy(p => p.CreatedAt).Select(p => p.Text).First(),
                m.Shape,
                m.Genre))
            .ToListAsync(ct);

        Guid? saveId = null;
        if (viewer.HasDevice)
        {
            saveId = await OwnSaves(db, viewer).Where(s => s.MarkId == id).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
        }

        var canAddPop = viewer.IsRegistered && mark.Status == MarkStatus.Public && mark.ShopId is null
            && mark.Pops.All(p => p.AuthorUserId != userId);

        return new MarkDetail(
            mark.Id, mark.Shape, mark.Genre, original.Stage, mark.Status,
            isMine ? mark.PendingReason : null,
            isMine,
            mark.Owner!.DisplayName,
            viewer.IsKidsMode ? null : mark.LinkUrl,
            mark.Domain,
            mark.LinkUrl is not null,
            mark.HasPr || pops.Any(p => p.HasPr) || (mark.Preview?.HasPr ?? false),
            mark.LinkBroken,
            pops,
            mark.Preview is null || (mark.Preview.Title is null && mark.Preview.ImageKey is null)
                ? null
                : new PreviewView(mark.Preview.Title, mark.Preview.SiteName,
                    mark.Preview.ImageKey is null ? null : ImageFiles.UrlFor(mark.Preview.ImageKey)),
            neighbors,
            saveId,
            canAddPop,
            mark.ShopId,
            mark.CreatedAt);
    }

    public async Task<PlaceResult> PlaceMarkAsync(Viewer viewer, PlaceMarkInput input, CancellationToken ct)
    {
        var user = RequireActiveUser(viewer);
        var text = await ValidatePopAsync(input.Text, input.PhotoKey, ct);
        var now = clock.GetUtcNow();
        await using var db = await dbf.CreateDbContextAsync(ct);

        var since = now - TimeSpan.FromDays(1);
        var placedToday = await db.Marks.CountAsync(m => m.OwnerUserId == user.Id && m.CreatedAt > since && m.ShopId == null, ct);
        var limit = user.Trust switch
        {
            TrustLevel.Trusted => Safety.DailyMarkLimitTrusted,
            TrustLevel.Normal => Safety.DailyMarkLimitNormal,
            _ => Safety.DailyMarkLimitNew,
        };
        if (placedToday >= limit)
            throw AppError.Of("DAILY_LIMIT", $"今日置けるのは{limit}個までです。また明日置いてください。");

        string? url = null, domain = null;
        if (!string.IsNullOrWhiteSpace(input.Url))
        {
            var (uri, host, error) = LinkPolicy.Normalize(input.Url);
            if (uri is null) throw AppError.Of("INVALID_URL", error!);
            var rules = await DomainRules(db, ct);
            if (LinkPolicy.Resolve(host!, rules) == DomainRuleKind.Deny)
                throw AppError.Of("DOMAIN_DENIED", "このサイトへのリンクは置けません。");
            url = uri.AbsoluteUri;
            domain = host;
        }

        var (x, y) = await ChoosePositionAsync(db, viewer, input.NextToMarkId, CityLayout.Radius(1), ct);

        var mark = new Mark
        {
            Id = Guid.NewGuid(),
            Shape = input.Shape,
            Genre = input.Genre,
            LinkUrl = url,
            Domain = domain,
            OwnerUserId = user.Id,
            Status = MarkStatus.Pending,
            PendingReason = PendingReason.AutoReview,
            Weight = 1,
            SeedX = x,
            SeedY = y,
            HasPr = (url is not null && AffiliateDetector.IsAffiliateUrl(url)) || AffiliateDetector.TextHasPr(text),
            CreatedAt = now,
            LastEngagedAt = now,
        };
        db.Marks.Add(mark);
        db.Pops.Add(new Pop
        {
            Id = Guid.NewGuid(),
            MarkId = mark.Id,
            AuthorUserId = user.Id,
            Text = text,
            PhotoKey = input.PhotoKey,
            Stage = input.Stage,
            HasPr = AffiliateDetector.TextHasPr(text),
            Status = PopStatus.Pending,
            CreatedAt = now,
        });
        if (input.NextToMarkId is { } anchorId)
        {
            db.Adjacencies.Add(new Adjacency { MarkAId = mark.Id, MarkBId = anchorId, CreatedAt = now });
            await Engagements.RecordAsync(db, anchorId, EngagementKind.PlacedNext, viewer.DeviceKeyHash!, now, City);
        }
        await db.SaveChangesAsync(ct);

        return new PlaceResult(mark.Id, mark.Status, mark.PendingReason,
            "置きました。確認が済むと街に並びます。それまではあなたにだけ見えます。");
    }

    public async Task<PopView> AddPopAsync(Viewer viewer, AddPopInput input, CancellationToken ct)
    {
        var user = RequireActiveUser(viewer);
        var text = await ValidatePopAsync(input.Text, input.PhotoKey, ct);
        var now = clock.GetUtcNow();
        await using var db = await dbf.CreateDbContextAsync(ct);
        var mark = await db.Marks.FirstOrDefaultAsync(m => m.Id == input.MarkId && m.Status == MarkStatus.Public, ct)
            ?? throw AppError.NotFound();
        if (mark.ShopId is not null) throw AppError.Of("NOT_ALLOWED", "お店のマークにはポップを付けられません。");
        if (await db.Pops.AnyAsync(p => p.MarkId == mark.Id && p.AuthorUserId == user.Id, ct))
            throw AppError.Of("ALREADY_POPPED", "このマークにはもうポップを付けています。");

        var pop = new Pop
        {
            Id = Guid.NewGuid(),
            MarkId = mark.Id,
            AuthorUserId = user.Id,
            Text = text,
            PhotoKey = input.PhotoKey,
            Stage = input.Stage,
            HasPr = AffiliateDetector.TextHasPr(text),
            Status = PopStatus.Pending,
            CreatedAt = now,
        };
        db.Pops.Add(pop);
        await Engagements.RecordAsync(db, mark.Id, EngagementKind.Tap, viewer.DeviceKeyHash!, now, City);
        await db.SaveChangesAsync(ct);
        return new PopView(pop.Id, user.DisplayName, pop.Text,
            pop.PhotoKey is null ? null : ImageFiles.UrlFor(pop.PhotoKey),
            pop.Stage, pop.HasPr, false, pop.Status, pop.CreatedAt);
    }

    /// <summary>クライアントから届く閲覧・タップ。保存と隣への配置はサーバー側で記録する。</summary>
    public async Task<int> RecordEngagementsAsync(Viewer viewer, IReadOnlyList<Guid> markIds, EngagementKind kind, CancellationToken ct)
    {
        if (!viewer.HasDevice) throw AppError.NeedDevice();
        if (kind is not (EngagementKind.View or EngagementKind.Tap))
            throw AppError.Of("INVALID_KIND", "記録できるのは閲覧とタップだけです。");
        if (markIds.Count > 200) throw AppError.Of("TOO_MANY", "一度に記録できるのは200件までです。");

        var now = clock.GetUtcNow();
        await using var db = await dbf.CreateDbContextAsync(ct);
        var distinct = markIds.Distinct().ToList();
        var existing = await db.Marks.Where(m => distinct.Contains(m.Id) && m.Status == MarkStatus.Public)
            .Select(m => m.Id).ToListAsync(ct);
        var recorded = 0;
        foreach (var id in existing)
        {
            if (await Engagements.RecordAsync(db, id, kind, viewer.DeviceKeyHash!, now, City)) recorded++;
        }
        await db.SaveChangesAsync(ct);
        return recorded;
    }

    /// <summary>
    /// 新しいマークの種の位置。必ず何かの隣に置く。
    /// 街が空のとき(最初の一個)だけ、隣なしで中心に置ける。
    /// </summary>
    internal async Task<(double X, double Y)> ChoosePositionAsync(
        AppDbContext db, Viewer viewer, Guid? nextToMarkId, double radius, CancellationToken ct)
    {
        if (nextToMarkId is null)
        {
            if (await db.Marks.AnyAsync(m => m.Status == MarkStatus.Public, ct))
                throw AppError.Of("NEIGHBOR_REQUIRED", "隣に置くマークを選んでください。");
            return (0, 0);
        }

        var anchor = await db.Marks.AsNoTracking().VisibleTo(viewer)
            .FirstOrDefaultAsync(m => m.Id == nextToMarkId, ct)
            ?? throw AppError.Of("NEIGHBOR_NOT_FOUND", "隣に選んだマークが見つかりませんでした。");

        var window = CityLayout.Radius(City.WeightCap) * 6;
        var nearby = await db.Marks.AsNoTracking()
            .Where(m => (m.Status == MarkStatus.Public || m.Status == MarkStatus.Pending)
                && m.SeedX > anchor.SeedX - window && m.SeedX < anchor.SeedX + window
                && m.SeedY > anchor.SeedY - window && m.SeedY < anchor.SeedY + window)
            .Select(m => new { m.Id, m.SeedX, m.SeedY, m.Weight })
            .ToListAsync(ct);
        var others = nearby.Select(m => new LayoutNode(m.Id, m.SeedX, m.SeedY, CityLayout.Radius(m.Weight))).ToList();
        var anchorNode = new LayoutNode(anchor.Id, anchor.SeedX, anchor.SeedY, CityLayout.Radius(anchor.Weight));
        return CityLayout.PlaceNear(anchorNode, radius, others, Random.Shared.Next());
    }

    internal static IQueryable<Save> OwnSaves(AppDbContext db, Viewer viewer)
    {
        var userId = viewer.User?.Id;
        var device = viewer.DeviceKeyHash;
        return userId is not null
            ? db.Saves.Where(s => s.UserId == userId)
            : db.Saves.Where(s => s.DeviceKeyHash == device && s.UserId == null);
    }

    internal static async Task<Dictionary<string, DomainRuleKind>> DomainRules(AppDbContext db, CancellationToken ct) =>
        await db.DomainRules.AsNoTracking().ToDictionaryAsync(r => r.Domain, r => r.Kind, ct);

    internal static User RequireActiveUser(Viewer viewer)
    {
        if (!viewer.HasDevice) throw AppError.NeedDevice();
        var user = viewer.User ?? throw AppError.NeedRegistration();
        if (user.IsBlocked) throw AppError.Of("BLOCKED", "この操作はできません。");
        return user;
    }

    internal async Task<string> ValidatePopAsync(string rawText, string? photoKey, CancellationToken ct)
    {
        var text = TextRules.Normalize(rawText);
        if (text.Length == 0) throw AppError.Of("POP_REQUIRED", "ポップ(一言)を書いてください。");
        var max = Safety.PopMaxLength;
        if (TextRules.Length(text) > max) throw AppError.Of("POP_TOO_LONG", $"ポップは{max}文字までです。");
        if (photoKey is not null && !await storage.ExistsAsync(photoKey, ct))
            throw AppError.Of("PHOTO_NOT_FOUND", "写真が見つかりませんでした。もう一度選んでください。");
        return text;
    }
}
