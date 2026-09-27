using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serendipity.Api.City;
using Serendipity.Api.Data;
using Serendipity.Api.Domain;
using Serendipity.Api.GraphQL;
using Serendipity.Api.Identity;

namespace Serendipity.Api.Safety;

public class SafetyService(IDbContextFactory<AppDbContext> dbf, IOptions<SafetyOptions> options, TimeProvider clock)
{
    private SafetyOptions O => options.Value;

    /// <summary>通報の重み。使い捨ての端末ほど軽く、信頼された利用者ほど重い。</summary>
    public static double ReportWeight(Viewer viewer) => viewer.User switch
    {
        null => 0.5,
        { Trust: TrustLevel.Trusted } => 3,
        { Trust: TrustLevel.Normal } => 1.5,
        _ => 1,
    };

    public async Task<ReportResult> ReportAsync(Viewer viewer, Guid? markId, Guid? popId, string reason, CancellationToken ct)
    {
        if (!viewer.HasDevice) throw AppError.NeedDevice();
        if ((markId is null) == (popId is null)) throw AppError.Of("INVALID_TARGET", "通報する対象を一つ選んでください。");
        var cleanReason = Clip(TextRules.Normalize(reason), 500);
        if (cleanReason.Length == 0) throw AppError.Of("REASON_REQUIRED", "理由を選んでください。");

        await using var db = await dbf.CreateDbContextAsync(ct);
        Pop? pop = null;
        if (popId is not null)
        {
            pop = await db.Pops.FirstOrDefaultAsync(p => p.Id == popId, ct) ?? throw AppError.NotFound();
        }
        else if (!await db.Marks.AnyAsync(m => m.Id == markId, ct))
        {
            throw AppError.NotFound();
        }

        var dup = await db.Reports.AnyAsync(r => r.ReporterKey == viewer.DeviceKeyHash
            && r.Kind == ReportKind.Abuse && r.MarkId == markId && r.PopId == popId, ct);
        if (dup) return new ReportResult(true, "通報は受け付け済みです。ありがとうございます。");

        db.Reports.Add(new Report
        {
            Id = Guid.NewGuid(),
            Kind = ReportKind.Abuse,
            MarkId = markId,
            PopId = popId,
            ReporterKey = viewer.DeviceKeyHash!,
            ReporterUserId = viewer.User?.Id,
            Reason = cleanReason,
            Weight = ReportWeight(viewer),
            Status = ReportStatus.Open,
            CreatedAt = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);

        var total = await db.Reports
            .Where(r => r.Kind == ReportKind.Abuse && r.Status == ReportStatus.Open && r.MarkId == markId && r.PopId == popId)
            .SumAsync(r => r.Weight, ct);
        if (total >= O.ReportHideThreshold)
        {
            if (pop is not null)
            {
                pop.Status = PopStatus.Hidden;
                pop.ReviewNote = "通報により自動で非表示";
                // 置いた人のポップが隠れたら、マークごと隠す。
                var mark = await db.Marks.FirstAsync(m => m.Id == pop.MarkId, ct);
                if (mark.OwnerUserId == pop.AuthorUserId) HideMark(mark, "ポップへの通報により自動で非表示");
            }
            else
            {
                var mark = await db.Marks.FirstAsync(m => m.Id == markId, ct);
                HideMark(mark, "通報により自動で非表示");
            }
            await db.SaveChangesAsync(ct);
        }
        return new ReportResult(true, "通報を受け付けました。ありがとうございます。");
    }

    /// <summary>記事の書き手からの削除依頼。運営が確認するまで自動では隠さない。</summary>
    public async Task<ReportResult> RequestTakedownAsync(Viewer viewer, Guid markId, string contact, string reason, CancellationToken ct)
    {
        if (!viewer.HasDevice) throw AppError.NeedDevice();
        var cleanContact = Clip(TextRules.Normalize(contact), 200);
        var cleanReason = Clip(TextRules.Normalize(reason), 500);
        if (cleanContact.Length == 0 || cleanReason.Length == 0)
            throw AppError.Of("FIELDS_REQUIRED", "連絡先と理由を書いてください。");
        await using var db = await dbf.CreateDbContextAsync(ct);
        if (!await db.Marks.AnyAsync(m => m.Id == markId, ct)) throw AppError.NotFound();
        db.Reports.Add(new Report
        {
            Id = Guid.NewGuid(),
            Kind = ReportKind.Takedown,
            MarkId = markId,
            ReporterKey = viewer.DeviceKeyHash!,
            ReporterUserId = viewer.User?.Id,
            Reason = cleanReason,
            Contact = cleanContact,
            Weight = 0,
            Status = ReportStatus.Open,
            CreatedAt = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);
        return new ReportResult(true, "削除のご依頼を受け付けました。確認して連絡します。");
    }

    // ---- 運営 ----

    public async Task<AdminQueue> QueueAsync(Viewer viewer, CancellationToken ct)
    {
        RequireAdmin(viewer);
        await using var db = await dbf.CreateDbContextAsync(ct);
        var reportWeights = await db.Reports
            .Where(r => r.Status == ReportStatus.Open && r.MarkId != null)
            .GroupBy(r => r.MarkId!.Value)
            .Select(g => new { MarkId = g.Key, Weight = g.Sum(r => r.Weight) })
            .ToDictionaryAsync(x => x.MarkId, x => x.Weight, ct);
        var reportedIds = reportWeights.Keys.ToList();

        var marks = await db.Marks.AsNoTracking()
            .Where(m => (m.Status == MarkStatus.Pending && m.PendingReason != PendingReason.AutoReview)
                || (m.Status == MarkStatus.Hidden && reportedIds.Contains(m.Id))
                || (m.Status == MarkStatus.Public && reportedIds.Contains(m.Id)))
            .OrderBy(m => m.CreatedAt)
            .Take(200)
            .Select(m => new
            {
                m.Id, m.Status, m.PendingReason, m.ReviewNote, m.LinkUrl, m.Domain, m.CreatedAt,
                OwnerName = m.Owner!.DisplayName, m.OwnerUserId,
                PopText = m.Pops.Where(p => p.AuthorUserId == m.OwnerUserId).Select(p => p.Text).FirstOrDefault() ?? "",
            })
            .ToListAsync(ct);

        var pops = await db.Pops.AsNoTracking()
            .Where(p => p.Status == PopStatus.Pending && p.ReviewNote != null && p.Mark!.Status == MarkStatus.Public)
            .OrderBy(p => p.CreatedAt).Take(200)
            .Select(p => new AdminPopView(p.Id, p.MarkId, p.Author!.DisplayName, p.Text, p.ReviewNote, p.CreatedAt))
            .ToListAsync(ct);

        var reports = await db.Reports.AsNoTracking()
            .Where(r => r.Status == ReportStatus.Open)
            .OrderBy(r => r.CreatedAt).Take(200)
            .Select(r => new AdminReportView(r.Id, r.Kind, r.MarkId, r.PopId, r.Reason, r.Contact, r.Weight, r.CreatedAt))
            .ToListAsync(ct);

        var domains = await db.DomainRules.AsNoTracking().OrderBy(d => d.Domain)
            .Select(d => new AdminDomainView(d.Domain, d.Kind, d.Note)).ToListAsync(ct);

        return new AdminQueue(
            marks.Select(m => new AdminMarkView(m.Id, m.Status, m.PendingReason, m.ReviewNote, m.LinkUrl, m.Domain,
                m.OwnerName, m.OwnerUserId, m.PopText, reportWeights.GetValueOrDefault(m.Id), m.CreatedAt)).ToList(),
            pops, reports, domains);
    }

    /// <summary>確認待ちのマークを公開する(許可リスト外のドメインや、AIが迷ったもの)。</summary>
    public async Task<bool> ApproveMarkAsync(Viewer viewer, Guid markId, CancellationToken ct)
    {
        RequireAdmin(viewer);
        var now = clock.GetUtcNow();
        await using var db = await dbf.CreateDbContextAsync(ct);
        var mark = await db.Marks.Include(m => m.Pops).FirstOrDefaultAsync(m => m.Id == markId, ct) ?? throw AppError.NotFound();
        mark.Status = MarkStatus.Public;
        mark.PendingReason = null;
        mark.ReviewNote = $"運営が公開 {now:yyyy-MM-dd}";
        mark.LastEngagedAt = now;
        foreach (var p in mark.Pops.Where(p => p.AuthorUserId == mark.OwnerUserId)) p.Status = PopStatus.Visible;
        await ResolveReportsAsync(db, markId, ReportStatus.Dismissed, ct);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> HideMarkAsync(Viewer viewer, Guid markId, string? note, CancellationToken ct)
    {
        RequireAdmin(viewer);
        await using var db = await dbf.CreateDbContextAsync(ct);
        var mark = await db.Marks.FirstOrDefaultAsync(m => m.Id == markId, ct) ?? throw AppError.NotFound();
        HideMark(mark, string.IsNullOrWhiteSpace(note) ? "運営が非表示" : Clip(note, 500));
        await ResolveReportsAsync(db, markId, ReportStatus.Resolved, ct);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> ApprovePopAsync(Viewer viewer, Guid popId, CancellationToken ct)
    {
        RequireAdmin(viewer);
        await using var db = await dbf.CreateDbContextAsync(ct);
        var pop = await db.Pops.FirstOrDefaultAsync(p => p.Id == popId, ct) ?? throw AppError.NotFound();
        pop.Status = PopStatus.Visible;
        pop.ReviewNote = "運営が公開";
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> HidePopAsync(Viewer viewer, Guid popId, CancellationToken ct)
    {
        RequireAdmin(viewer);
        await using var db = await dbf.CreateDbContextAsync(ct);
        var pop = await db.Pops.FirstOrDefaultAsync(p => p.Id == popId, ct) ?? throw AppError.NotFound();
        pop.Status = PopStatus.Hidden;
        pop.ReviewNote = "運営が非表示";
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> ResolveReportAsync(Viewer viewer, Guid reportId, bool dismiss, CancellationToken ct)
    {
        RequireAdmin(viewer);
        await using var db = await dbf.CreateDbContextAsync(ct);
        var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == reportId, ct) ?? throw AppError.NotFound();
        report.Status = dismiss ? ReportStatus.Dismissed : ReportStatus.Resolved;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>許可リストとブラックリストの管理。拒否にしたドメインのマークは隠す。</summary>
    public async Task<AdminDomainView> SetDomainRuleAsync(Viewer viewer, string domain, DomainRuleKind kind, string? note, CancellationToken ct)
    {
        RequireAdmin(viewer);
        var key = LinkPolicy.HostKey(TextRules.Normalize(domain));
        if (key.Length == 0 || key.Length > 255 || !key.Contains('.')) throw AppError.Of("INVALID_DOMAIN", "ドメインが正しくありません。");
        await using var db = await dbf.CreateDbContextAsync(ct);
        var rule = await db.DomainRules.FirstOrDefaultAsync(r => r.Domain == key, ct);
        if (rule is null)
        {
            rule = new DomainRule { Domain = key };
            db.DomainRules.Add(rule);
        }
        rule.Kind = kind;
        rule.Note = note is null ? null : Clip(note, 200);
        rule.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);

        if (kind == DomainRuleKind.Deny)
        {
            var suffix = "." + key;
            await db.Marks.Where(m => m.Domain != null && (m.Domain == key || m.Domain.EndsWith(suffix))
                    && (m.Status == MarkStatus.Public || m.Status == MarkStatus.Pending))
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, MarkStatus.Hidden)
                    .SetProperty(m => m.PendingReason, (PendingReason?)null)
                    .SetProperty(m => m.ReviewNote, "拒否ドメイン"), ct);
        }
        return new AdminDomainView(rule.Domain, rule.Kind, rule.Note);
    }

    /// <summary>繰り返す悪質な投稿者をブラックリストに入れ、置いたマークを隠す。</summary>
    public async Task<bool> BlockUserAsync(Viewer viewer, Guid userId, bool blocked, CancellationToken ct)
    {
        RequireAdmin(viewer);
        await using var db = await dbf.CreateDbContextAsync(ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw AppError.NotFound();
        user.IsBlocked = blocked;
        await db.SaveChangesAsync(ct);
        if (blocked)
        {
            await db.Marks.Where(m => m.OwnerUserId == userId && (m.Status == MarkStatus.Public || m.Status == MarkStatus.Pending))
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, MarkStatus.Hidden)
                    .SetProperty(m => m.PendingReason, (PendingReason?)null)
                    .SetProperty(m => m.ReviewNote, "投稿者をブロック"), ct);
            await db.Pops.Where(p => p.AuthorUserId == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PopStatus.Hidden), ct);
        }
        return true;
    }

    public async Task<bool> SetTrustAsync(Viewer viewer, Guid userId, TrustLevel trust, CancellationToken ct)
    {
        RequireAdmin(viewer);
        await using var db = await dbf.CreateDbContextAsync(ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw AppError.NotFound();
        user.Trust = trust;
        await db.SaveChangesAsync(ct);
        return true;
    }

    internal static void HideMark(Mark mark, string note)
    {
        mark.Status = MarkStatus.Hidden;
        mark.PendingReason = null;
        mark.ReviewNote = note;
    }

    private static async Task ResolveReportsAsync(AppDbContext db, Guid markId, ReportStatus status, CancellationToken ct) =>
        await db.Reports.Where(r => r.MarkId == markId && r.Status == ReportStatus.Open && r.Kind == ReportKind.Abuse)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, status), ct);

    private static void RequireAdmin(Viewer viewer)
    {
        if (!viewer.IsAdmin) throw AppError.Of("FORBIDDEN", "運営だけができる操作です。");
    }

    private static string Clip(string s, int max) => s.Length > max ? s[..max] : s;
}
