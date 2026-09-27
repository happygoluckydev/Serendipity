using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serendipity.Api.City;
using Serendipity.Api.Data;
using Serendipity.Api.Domain;
using Serendipity.Api.Safety;
using Serendipity.Api.Storage;

namespace Serendipity.Api.Jobs;

/// <summary>
/// 置いたときの審査と、リンク先の定期再チェック。
/// 流れ: プレビュー取り込み → PR検出 → AI判定 → 許可リストなら公開 / 未知ドメインは確認待ち。
/// </summary>
public class ReviewService(
    IDbContextFactory<AppDbContext> dbf,
    IPreviewFetcher fetcher,
    IContentModerator moderator,
    IObjectStorage storage,
    IOptions<SafetyOptions> safetyOptions,
    TimeProvider clock,
    ILogger<ReviewService> log)
{
    public async Task<int> ReviewPendingMarksAsync(int batch, CancellationToken ct)
    {
        await using var db = await dbf.CreateDbContextAsync(ct);
        var ids = await db.Marks
            .Where(m => m.Status == MarkStatus.Pending && m.PendingReason == PendingReason.AutoReview)
            .OrderBy(m => m.CreatedAt).Take(batch).Select(m => m.Id).ToListAsync(ct);
        var done = 0;
        foreach (var id in ids)
        {
            try
            {
                await ReviewMarkAsync(id, ct);
                done++;
            }
            catch (ModerationUnavailableException e)
            {
                log.LogWarning(e, "AI判定が使えないため、あとで審査します");
                break;
            }
        }
        return done;
    }

    public async Task ReviewMarkAsync(Guid markId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await using var db = await dbf.CreateDbContextAsync(ct);
        var mark = await db.Marks.Include(m => m.Owner).Include(m => m.Pops).Include(m => m.Preview)
            .FirstAsync(m => m.Id == markId, ct);
        var pop = mark.Pops.Where(p => p.AuthorUserId == mark.OwnerUserId).OrderBy(p => p.CreatedAt).First();

        if (mark.Owner!.IsBlocked)
        {
            SafetyService.HideMark(mark, "投稿者をブロック");
            pop.Status = PopStatus.Hidden;
            await db.SaveChangesAsync(ct);
            return;
        }

        DomainRuleKind? rule = null;
        FetchedPreview? fetched = null;
        if (mark.LinkUrl is not null)
        {
            var rules = await CityService.DomainRules(db, ct);
            rule = LinkPolicy.Resolve(mark.Domain!, rules);
            if (rule == DomainRuleKind.Deny)
            {
                SafetyService.HideMark(mark, "拒否ドメイン");
                pop.Status = PopStatus.Hidden;
                await db.SaveChangesAsync(ct);
                return;
            }
            fetched = await FetchIntoAsync(db, mark, now, ct);
        }

        var result = await moderator.ModerateAsync(new ModerationInput(
            pop.Text, fetched?.Title, fetched?.SiteName, fetched?.BodyExcerpt, mark.Domain), ct);

        switch (result.Verdict)
        {
            case ModerationVerdict.Reject:
                SafetyService.HideMark(mark, $"AI判定: {result.Reason}");
                pop.Status = PopStatus.Hidden;
                break;
            case ModerationVerdict.Review:
                mark.PendingReason = PendingReason.AiFlagged;
                mark.ReviewNote = $"AI判定: {result.Reason}";
                break;
            default:
                if (mark.LinkUrl is not null && rule != DomainRuleKind.Allow)
                {
                    // 未知ドメインは本人のみ可視。運営の確認後に公開する。
                    mark.PendingReason = PendingReason.UnknownDomain;
                    mark.ReviewNote = $"許可リスト外のドメイン: {mark.Domain}";
                }
                else
                {
                    mark.Status = MarkStatus.Public;
                    mark.PendingReason = null;
                    mark.ReviewNote = null;
                    mark.LastEngagedAt = now;
                    pop.Status = PopStatus.Visible;
                }
                break;
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>公開中のマークに後から付いたポップの審査。</summary>
    public async Task<int> ReviewPendingPopsAsync(int batch, CancellationToken ct)
    {
        await using var db = await dbf.CreateDbContextAsync(ct);
        var pops = await db.Pops.Include(p => p.Author)
            .Where(p => p.Status == PopStatus.Pending && p.ReviewNote == null && p.Mark!.Status == MarkStatus.Public)
            .OrderBy(p => p.CreatedAt).Take(batch).ToListAsync(ct);
        foreach (var pop in pops)
        {
            if (pop.Author!.IsBlocked)
            {
                pop.Status = PopStatus.Hidden;
                continue;
            }
            ModerationResult result;
            try
            {
                result = await moderator.ModerateAsync(new ModerationInput(pop.Text), ct);
            }
            catch (ModerationUnavailableException e)
            {
                log.LogWarning(e, "AI判定が使えないため、あとで審査します");
                break;
            }
            switch (result.Verdict)
            {
                case ModerationVerdict.Ok:
                    pop.Status = PopStatus.Visible;
                    break;
                case ModerationVerdict.Reject:
                    pop.Status = PopStatus.Hidden;
                    pop.ReviewNote = $"AI判定: {result.Reason}";
                    break;
                default:
                    // 確認待ちのまま、運営の一覧に出す。
                    pop.ReviewNote = $"AI判定: {result.Reason}";
                    break;
            }
        }
        await db.SaveChangesAsync(ct);
        return pops.Count;
    }

    /// <summary>
    /// リンク先の定期再チェック。ドメイン失効後の乗っ取りや中身のすり替えを見つける。
    /// 中身が変わっていたら判定し直し、PR 表示も更新する。
    /// </summary>
    public async Task<int> RecheckLinksAsync(int batch, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var due = now - TimeSpan.FromHours(safetyOptions.Value.RecheckIntervalHours);
        await using var db = await dbf.CreateDbContextAsync(ct);
        var ids = await db.Marks
            .Where(m => m.Status == MarkStatus.Public && m.LinkUrl != null
                && (m.Preview == null || m.Preview.FetchedAt == null || m.Preview.FetchedAt < due))
            .OrderBy(m => m.Preview == null ? (DateTimeOffset?)null : m.Preview.FetchedAt)
            .Take(batch).Select(m => m.Id).ToListAsync(ct);

        var done = 0;
        foreach (var id in ids)
        {
            try
            {
                await RecheckMarkAsync(id, ct);
                done++;
            }
            catch (ModerationUnavailableException e)
            {
                log.LogWarning(e, "AI判定が使えないため、再チェックを中断します");
                break;
            }
        }
        return done;
    }

    public async Task RecheckMarkAsync(Guid markId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await using var db = await dbf.CreateDbContextAsync(ct);
        var mark = await db.Marks.Include(m => m.Preview).Include(m => m.Pops)
            .FirstAsync(m => m.Id == markId, ct);

        var rules = await CityService.DomainRules(db, ct);
        if (LinkPolicy.Resolve(mark.Domain!, rules) == DomainRuleKind.Deny)
        {
            SafetyService.HideMark(mark, "拒否ドメイン(再チェック)");
            await db.SaveChangesAsync(ct);
            return;
        }

        var previousHash = mark.Preview?.ContentHash;
        var fetched = await FetchIntoAsync(db, mark, now, ct);
        if (fetched is not null && previousHash is not null && fetched.ContentHash != previousHash)
        {
            var pop = mark.Pops.Where(p => p.AuthorUserId == mark.OwnerUserId).OrderBy(p => p.CreatedAt).First();
            var result = await moderator.ModerateAsync(new ModerationInput(
                pop.Text, fetched.Title, fetched.SiteName, fetched.BodyExcerpt, mark.Domain), ct);
            if (result.Verdict == ModerationVerdict.Reject)
            {
                SafetyService.HideMark(mark, $"再チェックで中身の変化を検出: {result.Reason}");
            }
            else if (result.Verdict == ModerationVerdict.Review)
            {
                mark.Status = MarkStatus.Pending;
                mark.PendingReason = PendingReason.AiFlagged;
                mark.ReviewNote = $"再チェックで中身の変化を検出: {result.Reason}";
            }
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>プレビューを取り込み、自サーバーに写しを保存する。取れなければ null。</summary>
    private async Task<FetchedPreview?> FetchIntoAsync(AppDbContext db, Mark mark, DateTimeOffset now, CancellationToken ct)
    {
        var preview = mark.Preview;
        if (preview is null)
        {
            preview = new Preview { Id = Guid.NewGuid(), MarkId = mark.Id };
            db.Previews.Add(preview);
            mark.Preview = preview;
        }

        FetchedPreview fetched;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            fetched = await fetcher.FetchAsync(new Uri(mark.LinkUrl!), timeout.Token);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidDataException or UriFormatException)
        {
            preview.FailureCount++;
            preview.FetchedAt = now;
            preview.FetchError = e.Message.Length > 500 ? e.Message[..500] : e.Message;
            if (preview.FailureCount >= safetyOptions.Value.LinkBrokenAfterFailures) mark.LinkBroken = true;
            return null;
        }

        preview.Title = fetched.Title;
        preview.SiteName = fetched.SiteName;
        preview.ContentHash = fetched.ContentHash;
        preview.FetchedAt = now;
        preview.FetchError = null;
        preview.FailureCount = 0;
        preview.HasPr = AffiliateDetector.IsAffiliateUrl(fetched.FinalUrl) || AffiliateDetector.AnyAffiliate(fetched.Links);
        mark.LinkBroken = false;
        if (fetched.Image is not null && fetched.ImageExtension is not null)
        {
            preview.ImageKey = await storage.PutAsync(fetched.Image, fetched.ImageExtension, ct);
        }
        return fetched;
    }
}
