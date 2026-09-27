using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Serendipity.Api.Data;
using Serendipity.Api.Domain;
using Serendipity.Api.GraphQL;
using Serendipity.Api.Identity;
using Serendipity.Api.Storage;

namespace Serendipity.Api.City;

public static class AppError
{
    public static GraphQLException Of(string code, string message) =>
        new(ErrorBuilder.New().SetMessage(message).SetCode(code).Build());

    public static GraphQLException NeedDevice() =>
        Of("DEVICE_REQUIRED", "この端末を識別できませんでした。ページを読み込み直してください。");

    public static GraphQLException NeedRegistration() =>
        Of("REGISTRATION_REQUIRED", "この操作には名前の登録が必要です。");

    public static GraphQLException NotFound() => Of("NOT_FOUND", "見つかりませんでした。");
}

public static class TextRules
{
    /// <summary>見た目の文字数(絵文字や結合文字を1文字と数える)。</summary>
    public static int Length(string s) => new StringInfo(s).LengthInTextElements;

    public static string Normalize(string? s) => (s ?? "").Trim().Normalize(System.Text.NormalizationForm.FormC);
}

public static class Engagements
{
    /// <summary>
    /// 反応を記録する。同じ端末からの閲覧・タップは一定時間内に重ねて数えない。
    /// 記録したら true。呼び出し側で SaveChanges する。
    /// </summary>
    public static async Task<bool> RecordAsync(
        AppDbContext db, Guid markId, EngagementKind kind, string actorKey, DateTimeOffset now, CityOptions o)
    {
        var window = kind switch
        {
            EngagementKind.View => TimeSpan.FromHours(o.ViewDedupHours),
            EngagementKind.Tap => TimeSpan.FromHours(o.TapDedupHours),
            _ => TimeSpan.Zero,
        };
        if (window > TimeSpan.Zero)
        {
            var since = now - window;
            var dup = await db.Engagements.AnyAsync(e =>
                e.ActorKey == actorKey && e.MarkId == markId && e.Kind == kind && e.CreatedAt > since);
            if (dup) return false;
        }
        db.Engagements.Add(new Engagement { MarkId = markId, Kind = kind, ActorKey = actorKey, CreatedAt = now });
        if (Vitality.RefreshesLife(kind))
        {
            await db.Marks.Where(m => m.Id == markId)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.LastEngagedAt, now));
        }
        return true;
    }
}

public static class Visibility
{
    /// <summary>街に見えるマーク: 公開中のもの + 自分の確認待ちのもの。</summary>
    public static IQueryable<Mark> VisibleTo(this IQueryable<Mark> marks, Viewer viewer)
    {
        var userId = viewer.User?.Id;
        return marks.Where(m => m.Status == MarkStatus.Public
            || (userId != null && m.Status == MarkStatus.Pending && m.OwnerUserId == userId));
    }
}

public static class Snapshots
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Serialize(SaveSnapshot s) => JsonSerializer.Serialize(s, Json);

    public static SaveSnapshot Deserialize(string json) => JsonSerializer.Deserialize<SaveSnapshot>(json, Json)!;

    /// <summary>子ども向けの状態では外部リンクを渡さない。</summary>
    public static SaveSnapshot ForViewer(SaveSnapshot s, Viewer viewer) =>
        viewer.IsKidsMode ? s with { LinkUrl = null } : s;

    /// <summary>保存時点のスナップショットを作る。ポップ、写真、リンク、日付、そのとき隣にあったマーク。</summary>
    public static async Task<SaveSnapshot> CaptureAsync(AppDbContext db, Guid markId, DateTimeOffset now)
    {
        var m = await db.Marks.AsNoTracking()
            .Where(x => x.Id == markId)
            .Select(x => new
            {
                x.Id, x.Shape, x.Genre, x.LinkUrl, x.Domain, x.HasPr,
                Preview = x.Preview == null ? null : new { x.Preview.Title, x.Preview.ImageKey, x.Preview.HasPr },
                Pop = x.Pops.Where(p => p.AuthorUserId == x.OwnerUserId).OrderBy(p => p.CreatedAt)
                    .Select(p => new { p.Text, p.PhotoKey, p.Stage, p.HasPr, p.AuthorUserId, Name = p.Author!.DisplayName })
                    .First(),
            })
            .FirstAsync();

        var neighbors = await Neighbors(db, [markId]);
        var list = neighbors.TryGetValue(markId, out var ids) ? ids : [];
        var neighborViews = await db.Marks.AsNoTracking()
            .Where(x => list.Contains(x.Id) && x.Status == MarkStatus.Public)
            .Select(x => new SnapshotNeighbor(
                x.Id,
                x.Pops.Where(p => p.AuthorUserId == x.OwnerUserId).OrderBy(p => p.CreatedAt).Select(p => p.Text).First(),
                x.Shape,
                x.Genre))
            .ToListAsync();

        return new SaveSnapshot(
            m.Id, m.Shape, m.Genre, m.Pop.Stage, m.LinkUrl, m.Domain,
            m.HasPr || m.Pop.HasPr || (m.Preview?.HasPr ?? false),
            m.Preview?.Title,
            m.Preview?.ImageKey is null ? null : ImageFiles.UrlFor(m.Preview.ImageKey),
            m.Pop.Text,
            m.Pop.PhotoKey is null ? null : ImageFiles.UrlFor(m.Pop.PhotoKey),
            m.Pop.Name,
            m.Pop.AuthorUserId,
            neighborViews,
            now);
    }

    /// <summary>マークごとの隣(向きは問わない)。</summary>
    public static async Task<Dictionary<Guid, List<Guid>>> Neighbors(AppDbContext db, IReadOnlyCollection<Guid> markIds)
    {
        var edges = await db.Adjacencies.AsNoTracking()
            .Where(a => markIds.Contains(a.MarkAId) || markIds.Contains(a.MarkBId))
            .Select(a => new { a.MarkAId, a.MarkBId })
            .ToListAsync();
        var result = new Dictionary<Guid, List<Guid>>();
        void Add(Guid from, Guid to)
        {
            if (!result.TryGetValue(from, out var list)) result[from] = list = [];
            if (!list.Contains(to)) list.Add(to);
        }
        foreach (var e in edges)
        {
            Add(e.MarkAId, e.MarkBId);
            Add(e.MarkBId, e.MarkAId);
        }
        return result;
    }
}
