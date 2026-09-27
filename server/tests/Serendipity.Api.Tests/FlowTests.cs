using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serendipity.Api.City;
using Serendipity.Api.Domain;
using Serendipity.Api.Jobs;

namespace Serendipity.Api.Tests;

internal static class Q
{
    public const string Place = """
        mutation($i: PlaceMarkInput!) { placeMark(input: $i) { markId status pendingReason } }
        """;

    public const string Mark = """
        query($id: UUID!) { mark(id: $id) { id status pendingReason isMine linkUrl hasLink hasPr saveId
          pops { text status isOriginal } neighbors { id } } }
        """;

    public const string City = "{ city { marks { id status isMine neighborIds } } }";

    public static object PlaceInput(string text, Guid? nextTo = null, string? url = null) => new
    {
        i = new
        {
            nextToMarkId = nextTo,
            url,
            shape = "GOODS",
            genre = "LIVING",
            stage = "USED",
            text,
            photoKey = (string?)null,
        },
    };

    public static async Task<Guid> Register(this ApiFactory.Device d, string name)
    {
        var me = await d.Ok("mutation($n: String!) { register(displayName: $n) { userId } }", new { n = name });
        return me.GetProperty("register").GetProperty("userId").GetGuid();
    }

    public static async Task<Guid> PlaceOk(this ApiFactory.Device d, string text, Guid? nextTo = null, string? url = null)
    {
        var data = await d.Ok(Place, PlaceInput(text, nextTo, url));
        return data.GetProperty("placeMark").GetProperty("markId").GetGuid();
    }

    public static async Task<JsonElement> GetMark(this ApiFactory.Device d, Guid id) =>
        (await d.Ok(Mark, new { id })).GetProperty("mark");

    /// <summary>公開中のマークを一つ返す。街が空なら最初の一つを置く。</summary>
    public static async Task<Guid> Anchor(this ApiFactory api, ApiFactory.Device d)
    {
        Guid? anchor = null;
        await api.DbAsync(async db =>
            anchor = await db.Marks.Where(m => m.Status == MarkStatus.Public).Select(m => (Guid?)m.Id).FirstOrDefaultAsync());
        if (anchor is not null) return anchor.Value;
        var id = await d.PlaceOk("街の最初のもの");
        await api.ReviewAsync();
        return id;
    }

    public static async Task<HashSet<Guid>> CityIds(this ApiFactory.Device d) =>
        (await d.Ok(City)).GetProperty("city").GetProperty("marks").EnumerateArray()
            .Select(m => m.GetProperty("id").GetGuid()).ToHashSet();
}

/// <summary>空の街から始める必要があるので、データベースを分ける。</summary>
public class EmptyCityTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task PlacingRevealsMarksOnlyAfterReviewAndAlwaysNextToSomething()
    {
        var alice = api.As("alice");
        var bob = api.As("bob");
        await alice.Register("アリス");

        // 名前を登録していない端末は置けない。
        Assert.Equal("REGISTRATION_REQUIRED", (await bob.Gql(Q.Place, Q.PlaceInput("こんにちは"))).ErrorCode);

        // 空の街では最初の一つだけ隣なしで置ける。
        var first = await alice.PlaceOk("最初のざる");
        Assert.Contains(first, await alice.CityIds());
        Assert.DoesNotContain(first, await bob.CityIds());

        await api.ReviewAsync();
        Assert.Contains(first, await bob.CityIds());
        Assert.Equal("PUBLIC", (await bob.GetMark(first)).GetProperty("status").GetString());

        // 公開されたマークがあれば、隣を選ばずには置けない。
        Assert.Equal("NEIGHBOR_REQUIRED", (await alice.Gql(Q.Place, Q.PlaceInput("二つ目"))).ErrorCode);

        var second = await alice.PlaceOk("二つ目のざる", first);
        await api.ReviewAsync();
        var mark = await bob.GetMark(second);
        Assert.Contains(first, mark.GetProperty("neighbors").EnumerateArray().Select(n => n.GetProperty("id").GetGuid()));

        // 新しい利用者は1日5個まで。信頼度を上げると続けて置ける。
        var aliceId = (await alice.Ok("{ me { userId } }")).GetProperty("me").GetProperty("userId").GetGuid();
        for (var i = 0; i < 3; i++) await alice.PlaceOk($"まだ置ける{i}", first);
        Assert.Equal("DAILY_LIMIT", (await alice.Gql(Q.Place, Q.PlaceInput("六つ目", first))).ErrorCode);
        await api.As("admin", admin: true).Ok("mutation($id: UUID!) { adminSetTrust(userId: $id, trust: TRUSTED) }", new { id = aliceId });

        // 何個置いても、隣の近くに落ち着き、どの種も重ならない。
        var prev = second;
        for (var i = 0; i < 12; i++) prev = await alice.PlaceOk($"続きの{i}", i % 3 == 0 ? first : prev);
        await api.DbAsync(async db =>
        {
            var marks = await db.Marks.ToListAsync();
            foreach (var m in marks)
            foreach (var o in marks.Where(o => o.Id != m.Id))
            {
                var d = Math.Sqrt(Math.Pow(m.SeedX - o.SeedX, 2) + Math.Pow(m.SeedY - o.SeedY, 2));
                Assert.True(d >= CityLayout.Radius(m.Weight) + CityLayout.Radius(o.Weight) - 0.5, $"overlap d={d}");
            }
            var adj = await db.Adjacencies.ToListAsync();
            foreach (var a in adj)
            {
                var ma = marks.First(m => m.Id == a.MarkAId);
                var mb = marks.First(m => m.Id == a.MarkBId);
                var d = Math.Sqrt(Math.Pow(ma.SeedX - mb.SeedX, 2) + Math.Pow(ma.SeedY - mb.SeedY, 2));
                Assert.True(d < (CityLayout.Radius(ma.Weight) + CityLayout.Radius(mb.Weight)) * 2.5, $"neighbours far apart d={d}");
            }
        });

        // 隣に置かれたことが、隣の重みの材料になっている。
        await api.DbAsync(async db =>
            Assert.True(await db.Engagements.AnyAsync(e => e.MarkId == first && e.Kind == EngagementKind.PlacedNext)));
    }
}

public class PlacementTests(ApiFactory api) : IClassFixture<ApiFactory>
{

    [Fact]
    public async Task UnknownDomainsStayPrivateUntilAdminApproves()
    {
        var carol = api.As("carol");
        var dave = api.As("dave");
        var admin = api.As("admin", admin: true);
        await carol.Register("キャロル");
        var anchor = await api.Anchor(carol);

        var mark = await carol.PlaceOk("個人ブログの良い記事", anchor, "https://someones-blog.example/post/1");
        await api.ReviewAsync();

        var mine = await carol.GetMark(mark);
        Assert.Equal("PENDING", mine.GetProperty("status").GetString());
        Assert.Equal("UNKNOWN_DOMAIN", mine.GetProperty("pendingReason").GetString());
        Assert.DoesNotContain(mark, await dave.CityIds());

        // 一般の端末は運営の操作ができない。
        Assert.Equal("FORBIDDEN", (await dave.Gql("mutation($id: UUID!) { adminApproveMark(markId: $id) }", new { id = mark })).ErrorCode);

        var queue = await admin.Ok("{ adminQueue { marks { id } } }");
        Assert.Contains(mark, queue.GetProperty("adminQueue").GetProperty("marks").EnumerateArray().Select(m => m.GetProperty("id").GetGuid()));
        await admin.Ok("mutation($id: UUID!) { adminApproveMark(markId: $id) }", new { id = mark });
        Assert.Contains(mark, await dave.CityIds());
    }

    [Fact]
    public async Task DeniedDomainsAndBadContentNeverReachTheCity()
    {
        var erin = api.As("erin");
        var admin = api.As("admin2", admin: true);
        await erin.Register("エリン");
        var anchor = await api.Anchor(erin);

        await admin.Ok("mutation { adminSetDomainRule(domain: \"evil.example\", kind: DENY) { domain } }");
        var denied = await erin.Gql(Q.Place, Q.PlaceInput("見て", anchor, "https://www.evil.example/x"));
        Assert.Equal("DOMAIN_DENIED", denied.ErrorCode);

        var bad = await erin.PlaceOk("R18の写真集", anchor);
        await api.ReviewAsync();
        await api.DbAsync(async db =>
            Assert.Equal(MarkStatus.Hidden, (await db.Marks.FirstAsync(m => m.Id == bad)).Status));
        Assert.DoesNotContain(bad, await erin.CityIds());
    }

    [Fact]
    public async Task AffiliateLinksInsideTheLinkedPageGetAPrLabel()
    {
        var fay = api.As("fay");
        await fay.Register("フェイ");
        var anchor = await api.Anchor(fay);
        const string url = "https://note.com/someone/n/n1";
        api.Previews.Pages[url] = FakePreviewFetcher.Page(url, "おすすめの靴", "良い靴でした", "https://amzn.to/abc");

        var mark = await fay.PlaceOk("靴の記事", anchor, url);
        await api.ReviewAsync();
        var view = await fay.GetMark(mark);
        Assert.Equal("PUBLIC", view.GetProperty("status").GetString());
        Assert.True(view.GetProperty("hasPr").GetBoolean());
    }

    [Fact]
    public async Task RecheckHidesALinkWhoseContentTurnedBad()
    {
        var gus = api.As("gus");
        await gus.Register("ガス");
        var anchor = await api.Anchor(gus);
        const string url = "https://gus.hatenablog.com/entry/1";
        api.Previews.Pages[url] = FakePreviewFetcher.Page(url, "キャンプ道具", "焚き火台の話");

        var mark = await gus.PlaceOk("焚き火台の記事", anchor, url);
        await api.ReviewAsync();
        Assert.Equal("PUBLIC", (await gus.GetMark(mark)).GetProperty("status").GetString());

        // ドメインが乗っ取られて、中身がすり替わった。
        api.Previews.Pages[url] = FakePreviewFetcher.Page(url, "激安アダルト", "18禁の広告");
        await api.WithScopeAsync(sp => sp.GetRequiredService<ReviewService>().RecheckMarkAsync(mark, default));
        await api.DbAsync(async db =>
            Assert.Equal(MarkStatus.Hidden, (await db.Marks.FirstAsync(m => m.Id == mark)).Status));
    }
}

public class SaveAndShopTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private const string Saves = "{ saves { id presence inMyShop snapshot { markId popText popAuthorName linkUrl neighbors { markId } } } }";

    [Fact]
    public async Task SavesWorkWithoutLoginSurviveTheMarkAndMoveOnRegistration()
    {
        var owner = api.As("owner");
        await owner.Register("置いた人");
        var a = await owner.PlaceOk("台所のざる", await api.Anchor(owner));
        await api.ReviewAsync();
        var b = await owner.PlaceOk("木のへら", a);
        await api.ReviewAsync();

        var visitor = api.As("visitor");
        var saved = await visitor.Ok("mutation($id: UUID!) { saveMark(markId: $id) { id snapshot { neighbors { markId } } } }", new { id = b });
        var snapNeighbors = saved.GetProperty("saveMark").GetProperty("snapshot").GetProperty("neighbors").EnumerateArray()
            .Select(n => n.GetProperty("markId").GetGuid());
        Assert.Contains(a, snapNeighbors);

        // 二度押しても一つだけ。
        await visitor.Ok("mutation($id: UUID!) { saveMark(markId: $id) { id } }", new { id = b });
        Assert.Equal(1, (await visitor.Ok(Saves)).GetProperty("saves").GetArrayLength());

        // 反応がないまま時間が経って、マークが街から消える。
        await api.DbAsync(async db =>
        {
            await db.Marks.Where(m => m.Id == b)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.LastEngagedAt, DateTimeOffset.UtcNow.AddDays(-100)));
        });
        await api.WithScopeAsync(sp => sp.GetRequiredService<MaintenanceService>().FadeAsync(default));
        Assert.DoesNotContain(b, await visitor.CityIds());

        var saves = (await visitor.Ok(Saves)).GetProperty("saves");
        Assert.Equal("GONE", saves[0].GetProperty("presence").GetString());
        Assert.Equal("木のへら", saves[0].GetProperty("snapshot").GetProperty("popText").GetString());

        // 登録すると、端末の保存がそのまま引き継がれる。
        await visitor.Register("眺める人");
        Assert.Equal(1, (await visitor.Ok("{ me { saveCount } }")).GetProperty("me").GetProperty("saveCount").GetInt32());
        await api.DbAsync(async db =>
            Assert.False(await db.Saves.AnyAsync(s => s.MarkId == b && s.DeviceKeyHash != null)));
    }

    [Fact]
    public async Task ShopShowsWhoFoundItAndBecomesAMarkNextToIt()
    {
        var finder = api.As("finder");
        await finder.Register("見つけた人");
        var found = await finder.PlaceOk("海辺で拾ったような器", await api.Anchor(finder));
        await api.ReviewAsync();

        var curator = api.As("curator");
        await curator.Register("並べる人");
        var saveId = (await curator.Ok("mutation($id: UUID!) { saveMark(markId: $id) { id } }", new { id = found }))
            .GetProperty("saveMark").GetProperty("id").GetGuid();

        await curator.Ok("mutation { openShop(name: \"気になるもの屋\") { id } }");
        Assert.Equal("SHOP_EMPTY", (await curator.Gql("mutation { setShopPublished(published: true) { id } }")).ErrorCode);

        var shop = (await curator.Ok("mutation($id: UUID!) { addShopItem(saveId: $id, stage: CURIOUS) { id items { originalAuthorName originalMarkId stage } } }", new { id = saveId }))
            .GetProperty("addShopItem");
        var item = shop.GetProperty("items")[0];
        Assert.Equal("見つけた人", item.GetProperty("originalAuthorName").GetString());
        Assert.Equal(found, item.GetProperty("originalMarkId").GetGuid());
        Assert.Equal("CURIOUS", item.GetProperty("stage").GetString());

        // 非公開の店は他人には見えない。
        var shopId = shop.GetProperty("id").GetGuid();
        Assert.Equal("NOT_FOUND", (await finder.Gql("query($id: UUID!) { shop(id: $id) { id } }", new { id = shopId })).ErrorCode);

        var published = (await curator.Ok("mutation { setShopPublished(published: true) { markId } }")).GetProperty("setShopPublished");
        var shopMark = published.GetProperty("markId").GetGuid();
        await api.ReviewAsync();

        var view = await finder.GetMark(shopMark);
        Assert.Contains(found, view.GetProperty("neighbors").EnumerateArray().Select(n => n.GetProperty("id").GetGuid()));
        await finder.Ok("query($id: UUID!) { shop(id: $id) { id } }", new { id = shopId });
    }

    [Fact]
    public async Task KidsModeHidesExternalLinksEverywhere()
    {
        var parent = api.As("parent");
        await parent.Register("親");
        var mark = await parent.PlaceOk("絵本の店", await api.Anchor(parent), "https://stores.jp/some-shop");
        await api.ReviewAsync();

        var kid = api.As("kid");
        await kid.Ok("mutation($id: UUID!) { saveMark(markId: $id) { id } }", new { id = mark });
        Assert.NotNull((await kid.GetMark(mark)).GetProperty("linkUrl").GetString());

        await kid.Ok("mutation { updateSettings(isKidsMode: true) { isKidsMode } }");
        var view = await kid.GetMark(mark);
        Assert.Equal(JsonValueKind.Null, view.GetProperty("linkUrl").ValueKind);
        Assert.True(view.GetProperty("hasLink").GetBoolean());
        var snap = (await kid.Ok(Saves)).GetProperty("saves")[0].GetProperty("snapshot");
        Assert.Equal(JsonValueKind.Null, snap.GetProperty("linkUrl").ValueKind);
    }
}

public class ReportTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task WeightedReportsHideAMarkAtTheThreshold()
    {
        var owner = api.As("reported-owner");
        await owner.Register("置いた人");
        var mark = await owner.PlaceOk("ふつうの紹介", await api.Anchor(owner));
        await api.ReviewAsync();

        const string report = "mutation($id: UUID!) { report(markId: $id, reason: \"不適切な内容\") { accepted } }";
        // 未登録の端末の通報は軽い(0.5)。同じ端末からの二度目は数えない。
        var anon = api.As("anon-reporter");
        await anon.Ok(report, new { id = mark });
        await anon.Ok(report, new { id = mark });
        Assert.Contains(mark, await anon.CityIds());

        // 閾値(3)に届くまで、登録済みの利用者(1)が通報する。
        for (var i = 0; i < 3; i++)
        {
            var r = api.As($"reporter-{i}");
            await r.Register($"通報{i}");
            await r.Ok(report, new { id = mark });
        }
        Assert.DoesNotContain(mark, await anon.CityIds());
        await api.DbAsync(async db =>
        {
            var m = await db.Marks.FirstAsync(x => x.Id == mark);
            Assert.Equal(MarkStatus.Hidden, m.Status);
            Assert.Equal(4, await db.Reports.CountAsync(x => x.MarkId == mark));
        });
    }
}
