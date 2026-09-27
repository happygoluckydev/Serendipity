using Microsoft.EntityFrameworkCore;
using Serendipity.Api.City;
using Serendipity.Api.Domain;
using Serendipity.Api.Safety;

namespace Serendipity.Api.Data;

public static class Seeder
{
    /// <summary>許可リストの初期案を入れる。運営が変えたルールは上書きしない。</summary>
    public static async Task SeedDomainRulesAsync(AppDbContext db, DateTimeOffset now, CancellationToken ct = default)
    {
        var existing = (await db.DomainRules.Select(r => r.Domain).ToListAsync(ct)).ToHashSet();
        foreach (var (domain, note) in LinkPolicy.InitialAllowList)
        {
            if (existing.Contains(domain)) continue;
            db.DomainRules.Add(new DomainRule { Domain = domain, Kind = DomainRuleKind.Allow, Note = note, UpdatedAt = now });
        }
        await db.SaveChangesAsync(ct);
    }

    private sealed record DemoMark(Genre Genre, MarkShape Shape, ExperienceStage Stage, string Text, string? Url = null);

    /// <summary>
    /// 試しに歩けるよう、空の街に見本のマークを置く(Seed:Demo=true のときだけ)。
    /// ジャンルごとに寄せて置くので、引きで見ると街区ができる。
    /// </summary>
    public static async Task SeedDemoCityAsync(AppDbContext db, DateTimeOffset now, CancellationToken ct = default)
    {
        if (await db.Marks.AnyAsync(ct)) return;

        var names = new[] { "みどり", "たける", "はる", "そら", "ゆい", "けんじ", "まち" };
        var users = names.Select(n => new User
        {
            Id = Guid.NewGuid(), DisplayName = n, Trust = TrustLevel.Normal, CreatedAt = now.AddDays(-30),
        }).ToList();
        db.Users.AddRange(users);

        DemoMark[][] districts =
        [
            [
                new(Genre.Fashion, MarkShape.Vintage, ExperienceStage.Used, "10年着ても形が崩れない古着のデニム"),
                new(Genre.Fashion, MarkShape.Product, ExperienceStage.Used, "雨の日も歩ける革靴。手入れが楽しい"),
                new(Genre.Fashion, MarkShape.Vintage, ExperienceStage.Curious, "70年代のスカーフ。柄が街で映えそう"),
                new(Genre.Fashion, MarkShape.Store, ExperienceStage.Used, "路地裏の古着屋。店主の話が面白い"),
                new(Genre.Fashion, MarkShape.Product, ExperienceStage.Want, "一生ものの帆布トート、いつか欲しい"),
                new(Genre.Fashion, MarkShape.Goods, ExperienceStage.Used, "真鍮のキーリング。育つ色がいい"),
            ],
            [
                new(Genre.Living, MarkShape.Goods, ExperienceStage.Used, "毎朝使う木のバターナイフ", "https://minne.com/"),
                new(Genre.Living, MarkShape.Product, ExperienceStage.Used, "音が静かな電気ケトル。夜中も安心"),
                new(Genre.Living, MarkShape.Goods, ExperienceStage.Curious, "手ぬぐいの柄を季節で替えたい"),
                new(Genre.Living, MarkShape.Goods, ExperienceStage.Used, "割れにくい子ども用の漆のお椀"),
                new(Genre.Living, MarkShape.Article, ExperienceStage.Used, "狭い部屋の収納、この記事で変わった", "https://note.com/"),
                new(Genre.Living, MarkShape.Product, ExperienceStage.Want, "干すだけで畳まなくていい洗濯ハンガー"),
            ],
            [
                new(Genre.Food, MarkShape.Store, ExperienceStage.Used, "商店街のパン屋。カレーパンが正解"),
                new(Genre.Food, MarkShape.Product, ExperienceStage.Used, "ご飯が3杯いける瓶詰めの海苔"),
                new(Genre.Food, MarkShape.Product, ExperienceStage.Curious, "北海道の小さな農家のじゃがいも"),
                new(Genre.Food, MarkShape.Article, ExperienceStage.Used, "味噌汁の出汁、これで迷わなくなった"),
                new(Genre.Food, MarkShape.Goods, ExperienceStage.Used, "焦げつかない鉄のフライパン"),
            ],
            [
                new(Genre.Books, MarkShape.Product, ExperienceStage.Used, "旅に出たくなる地図の本"),
                new(Genre.Books, MarkShape.Article, ExperienceStage.Used, "読書メモの取り方を変えた記事"),
                new(Genre.Books, MarkShape.Product, ExperienceStage.Curious, "表紙で選んだ詩集。まだ開いてない"),
                new(Genre.Books, MarkShape.Store, ExperienceStage.Used, "店主の手書きポップが楽しい古本屋"),
                new(Genre.Books, MarkShape.Product, ExperienceStage.Want, "80歳の祖母にも読める大きな字の図鑑"),
            ],
            [
                new(Genre.Hobby, MarkShape.Goods, ExperienceStage.Used, "子どもと遊べる木のパズル"),
                new(Genre.Hobby, MarkShape.Product, ExperienceStage.Used, "初心者でも続いたスケッチ用の色鉛筆"),
                new(Genre.Hobby, MarkShape.Vintage, ExperienceStage.Curious, "昔のフィルムカメラ。音が気持ちいい"),
                new(Genre.Hobby, MarkShape.Goods, ExperienceStage.Want, "ベランダで育てられる小さな盆栽"),
                new(Genre.Hobby, MarkShape.Article, ExperienceStage.Used, "週末のソロキャンプ、最初の一歩"),
            ],
            [
                new(Genre.Tech, MarkShape.Product, ExperienceStage.Used, "打鍵音が静かなキーボード"),
                new(Genre.Tech, MarkShape.Product, ExperienceStage.Used, "祖父でも使えたシンプルなラジオ"),
                new(Genre.Tech, MarkShape.Goods, ExperienceStage.Curious, "机が片付くケーブル留め"),
                new(Genre.Tech, MarkShape.Article, ExperienceStage.Used, "スマホの写真整理はこれで十分"),
            ],
            [
                new(Genre.Beauty, MarkShape.Product, ExperienceStage.Used, "香りが強すぎないハンドクリーム"),
                new(Genre.Beauty, MarkShape.Goods, ExperienceStage.Used, "よく眠れるようになった麻のまくら"),
                new(Genre.Beauty, MarkShape.Product, ExperienceStage.Want, "肌にやさしい固形石けん"),
                new(Genre.Beauty, MarkShape.Store, ExperienceStage.Used, "駅前の小さな銭湯。朝風呂が最高"),
            ],
            [
                new(Genre.Place, MarkShape.Store, ExperienceStage.Used, "海が見える喫茶店。窓際の席がいい"),
                new(Genre.Place, MarkShape.Store, ExperienceStage.Curious, "夜だけ開く植物屋さん"),
                new(Genre.Place, MarkShape.Article, ExperienceStage.Used, "雨の日に行ける美術館まとめ"),
                new(Genre.Place, MarkShape.Store, ExperienceStage.Used, "手ぶらで行ける陶芸教室"),
            ],
        ];

        var nodes = new List<LayoutNode>();
        var marks = new List<Mark>();
        var edges = new List<(int, int)>();
        var engagements = new List<Engagement>();
        var rng = new Random(42);
        Mark? previousDistrictHead = null;

        for (var d = 0; d < districts.Length; d++)
        {
            // 街区ごとに円周上へ散らしてから、隣同士をつなぐ。
            var angle = d * 2 * Math.PI / districts.Length;
            var cx = Math.Cos(angle) * 90;
            var cy = Math.Sin(angle) * 90;
            Mark? head = null;
            for (var i = 0; i < districts[d].Length; i++)
            {
                var spec = districts[d][i];
                var owner = users[rng.Next(users.Count)];
                var created = now.AddDays(-rng.Next(0, 20)).AddHours(-rng.Next(0, 24));
                var markId = Guid.NewGuid();
                // 重みは反応から計算されるので、見本の反応を入れておく。街区の最初のものは核テナントにする。
                var samples = new List<Engagement>();
                var saves = i == 0 ? rng.Next(3, 6) : rng.Next(0, 3);
                var taps = rng.Next(0, 6);
                for (var k = 0; k < saves + taps; k++)
                {
                    var at = now.AddHours(-rng.Next(1, 72));
                    if (at < created) at = created;
                    samples.Add(new Engagement
                    {
                        MarkId = markId,
                        Kind = k < saves ? EngagementKind.Save : EngagementKind.Tap,
                        ActorKey = $"demo-{k}",
                        CreatedAt = at,
                    });
                }
                engagements.AddRange(samples);
                var weight = Vitality.Weight(samples.Select(e => new EngagementSample(e.Kind, e.CreatedAt)), now, new CityOptions());
                var lastEngaged = samples.Count == 0 ? created : samples.Max(e => e.CreatedAt);
                var mark = new Mark
                {
                    Id = markId,
                    Shape = spec.Shape,
                    Genre = spec.Genre,
                    LinkUrl = spec.Url,
                    Domain = spec.Url is null ? null : LinkPolicy.HostKey(new Uri(spec.Url).Host),
                    OwnerUserId = owner.Id,
                    Status = MarkStatus.Public,
                    Weight = weight,
                    SeedX = cx + rng.NextDouble() * 20 - 10,
                    SeedY = cy + rng.NextDouble() * 20 - 10,
                    CreatedAt = created,
                    LastEngagedAt = lastEngaged,
                };
                mark.Pops.Add(new Pop
                {
                    Id = Guid.NewGuid(),
                    MarkId = mark.Id,
                    AuthorUserId = owner.Id,
                    Text = spec.Text,
                    Stage = spec.Stage,
                    Status = PopStatus.Visible,
                    CreatedAt = created,
                });
                marks.Add(mark);
                nodes.Add(new LayoutNode(mark.Id, mark.SeedX, mark.SeedY, CityLayout.Radius(weight)));
                var idx = marks.Count - 1;
                if (head is null) head = mark;
                else
                {
                    // 同じ街区の、少し前に置かれたものの隣に置いたことにする。
                    var back = rng.Next(1, Math.Min(i, 3) + 1);
                    edges.Add((idx, idx - back));
                }
            }
            if (previousDistrictHead is not null)
            {
                edges.Add((marks.IndexOf(head!), marks.IndexOf(previousDistrictHead)));
            }
            previousDistrictHead = head;
        }

        CityLayout.Relax(nodes, edges, 300);
        for (var i = 0; i < marks.Count; i++)
        {
            marks[i].SeedX = nodes[i].X;
            marks[i].SeedY = nodes[i].Y;
        }

        db.Marks.AddRange(marks);
        db.Engagements.AddRange(engagements);
        foreach (var (a, b) in edges)
        {
            db.Adjacencies.Add(new Adjacency { MarkAId = marks[a].Id, MarkBId = marks[b].Id, CreatedAt = now });
        }

        // 一つのマークに複数のポップ。
        var popular = marks[0];
        popular.Pops.Add(new Pop
        {
            Id = Guid.NewGuid(), MarkId = popular.Id, AuthorUserId = users.First(u => u.Id != popular.OwnerUserId).Id,
            Text = "わかる。膝のところの色落ちが最高", Stage = ExperienceStage.Used,
            Status = PopStatus.Visible, CreatedAt = now.AddDays(-1),
        });
        await db.SaveChangesAsync(ct);
    }
}
