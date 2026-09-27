using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serendipity.Api.City;
using Serendipity.Api.Data;
using Serendipity.Api.Domain;

namespace Serendipity.Api.Jobs;

/// <summary>街の無常を進める: 重みの集計、薄れて消えるマーク、種の配置のならし、信頼度の昇格。</summary>
public class MaintenanceService(IDbContextFactory<AppDbContext> dbf, IOptions<CityOptions> cityOptions, TimeProvider clock)
{
    private CityOptions O => cityOptions.Value;

    public async Task RunAsync(CancellationToken ct)
    {
        await RecomputeWeightsAsync(ct);
        await FadeAsync(ct);
        await RelaxLayoutAsync(30, ct);
        await PromoteTrustAsync(ct);
    }

    public async Task RecomputeWeightsAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        // 半減期の8倍より古い反応は 1/256 以下なので無視する。
        var cutoff = now - TimeSpan.FromDays(O.WeightHalfLifeDays * 8);
        await using var db = await dbf.CreateDbContextAsync(ct);
        var samples = await db.Engagements.AsNoTracking()
            .Where(e => e.CreatedAt > cutoff)
            .Select(e => new { e.MarkId, e.Kind, e.CreatedAt })
            .ToListAsync(ct);
        var byMark = samples.GroupBy(s => s.MarkId)
            .ToDictionary(g => g.Key, g => Vitality.Weight(g.Select(s => new EngagementSample(s.Kind, s.CreatedAt)), now, O));

        var marks = await db.Marks.Where(m => m.Status == MarkStatus.Public || m.Status == MarkStatus.Pending).ToListAsync(ct);
        foreach (var m in marks)
        {
            var w = byMark.GetValueOrDefault(m.Id, 1);
            if (Math.Abs(m.Weight - w) > 0.001) m.Weight = w;
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>反応のないマークは少しずつ薄れ、やがて街から消える。保存した人の手元には残る。</summary>
    public async Task<int> FadeAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var threshold = now - TimeSpan.FromDays(O.GoneAfterDays);
        await using var db = await dbf.CreateDbContextAsync(ct);
        return await db.Marks.Where(m => m.Status == MarkStatus.Public && m.LastEngagedAt < threshold)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, MarkStatus.Gone).SetProperty(m => m.GoneAt, now), ct);
    }

    public async Task RelaxLayoutAsync(int iterations, CancellationToken ct)
    {
        await using var db = await dbf.CreateDbContextAsync(ct);
        var marks = await db.Marks.Where(m => m.Status == MarkStatus.Public || m.Status == MarkStatus.Pending).ToListAsync(ct);
        if (marks.Count < 2) return;
        var index = marks.Select((m, i) => (m.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var ids = index.Keys.ToList();
        var edges = await db.Adjacencies.AsNoTracking()
            .Where(a => ids.Contains(a.MarkAId) && ids.Contains(a.MarkBId))
            .Select(a => new { a.MarkAId, a.MarkBId })
            .ToListAsync(ct);

        var nodes = marks.Select(m => new LayoutNode(m.Id, m.SeedX, m.SeedY, CityLayout.Radius(m.Weight))).ToList();
        CityLayout.Relax(nodes, edges.Select(e => (index[e.MarkAId], index[e.MarkBId])).ToList(), iterations);
        for (var i = 0; i < marks.Count; i++)
        {
            if (Math.Abs(marks[i].SeedX - nodes[i].X) + Math.Abs(marks[i].SeedY - nodes[i].Y) < 0.01) continue;
            marks[i].SeedX = nodes[i].X;
            marks[i].SeedY = nodes[i].Y;
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>登録から7日以上、3つ以上公開され、隠されたものがない利用者を「通常」に上げる。</summary>
    public async Task<int> PromoteTrustAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var since = now - TimeSpan.FromDays(7);
        await using var db = await dbf.CreateDbContextAsync(ct);
        return await db.Users
            .Where(u => u.Trust == TrustLevel.New && !u.IsBlocked && u.CreatedAt < since
                && db.Marks.Count(m => m.OwnerUserId == u.Id && (m.Status == MarkStatus.Public || m.Status == MarkStatus.Gone)) >= 3
                && !db.Marks.Any(m => m.OwnerUserId == u.Id && m.Status == MarkStatus.Hidden))
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.Trust, TrustLevel.Normal), ct);
    }
}
