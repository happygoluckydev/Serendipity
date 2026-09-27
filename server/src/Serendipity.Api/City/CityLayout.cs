namespace Serendipity.Api.City;

public sealed class LayoutNode(Guid id, double x, double y, double r)
{
    public Guid Id { get; } = id;
    public double X { get; set; } = x;
    public double Y { get; set; } = y;
    public double R { get; set; } = r;
    /// <summary>動かさない種(部分的にならすときの外周など)。</summary>
    public bool Pinned { get; set; }
}

/// <summary>
/// 種の配置。正は隣接関係で、座標はそこから導く派生値。
/// 隣同士はばねで引き寄せ、重なりは押し返す。温めた状態から少しずつ動かすので、
/// 再計算しても街の形は急に変わらない。
/// </summary>
public static class CityLayout
{
    /// <summary>重み1のマークの半径(世界座標)。クライアントも同じ値を使う。</summary>
    public const double BaseRadius = 10;

    public static double Radius(double weight) => BaseRadius * Math.Sqrt(Math.Max(1, weight));

    /// <summary>
    /// anchor の隣に半径 r の新しいマークを置く位置を選ぶ。
    /// anchor の周囲を一周して、他のマークと最も重ならない向きを取る。
    /// </summary>
    public static (double X, double Y) PlaceNear(LayoutNode anchor, double r, IReadOnlyList<LayoutNode> others, int seed)
    {
        const int steps = 24;
        var start = (seed & 0x7fffffff) % steps * (2 * Math.PI / steps);
        // 近い順に一周ずつ試し、誰とも重ならない向きが見つかるまで外へ広げる。
        // 街の外側まで出れば必ず空きがある。遠くに置いても、あとでばねが隣へ引き寄せる。
        var stretch = 1.0;
        (double X, double Y) best = (anchor.X + anchor.R + r, anchor.Y);
        for (var round = 0; round < 40; round++, stretch *= 1.35)
        {
            var dist = (anchor.R + r) * stretch;
            var bestScore = double.NegativeInfinity;
            for (var i = 0; i < steps; i++)
            {
                var a = start + i * (2 * Math.PI / steps);
                var cx = anchor.X + Math.Cos(a) * dist;
                var cy = anchor.Y + Math.Sin(a) * dist;
                var score = double.PositiveInfinity;
                foreach (var o in others)
                {
                    if (o.Id == anchor.Id) continue;
                    var clearance = Math.Sqrt(Sq(o.X - cx) + Sq(o.Y - cy)) - o.R - r;
                    if (clearance < score) score = clearance;
                }
                if (score > bestScore)
                {
                    bestScore = score;
                    best = (cx, cy);
                }
            }
            // 誰とも重ならなければ、パワー図で区画が必ず残る
            // (中心間が ri + rj 以上なら、境界は新しい種から ri 以上離れる)。
            if (bestScore >= 0) return best;
        }
        return best;
    }

    /// <summary>
    /// ばねと重なりの解消で種の位置をならす。edges は nodes の添字の組。
    /// ばね(隣同士を寄せる)と中心への弱い引力は柔らかい力として動かし、
    /// 重ならないことは毎回そのあとに固い制約として解く。ばねが第三のマークを押しつぶしても重なりが残らない。
    /// </summary>
    public static void Relax(IList<LayoutNode> nodes, IReadOnlyList<(int A, int B)> edges, int iterations)
    {
        var n = nodes.Count;
        if (n < 2) return;
        var dx = new double[n];
        var dy = new double[n];
        var maxR = 0.0;
        foreach (var node in nodes) maxR = Math.Max(maxR, node.R);
        var cell = maxR * 2.4;

        for (var it = 0; it < iterations; it++)
        {
            Array.Clear(dx);
            Array.Clear(dy);

            // 隣同士のばね。離れすぎたときだけ引き寄せる。
            foreach (var (ia, ib) in edges)
            {
                var a = nodes[ia];
                var b = nodes[ib];
                var vx = b.X - a.X;
                var vy = b.Y - a.Y;
                var d = Math.Sqrt(vx * vx + vy * vy);
                var rest = (a.R + b.R) * 1.05;
                if (d <= rest || d < 1e-6) continue;
                var pull = (d - rest) * 0.15 / d;
                dx[ia] += vx * pull; dy[ia] += vy * pull;
                dx[ib] -= vx * pull; dy[ib] -= vy * pull;
            }

            // 街がばらけないよう、ごく弱く中心へ寄せる。
            double cx = 0, cy = 0;
            foreach (var node in nodes) { cx += node.X; cy += node.Y; }
            cx /= n; cy /= n;

            var maxStep = BaseRadius * 0.5;
            for (var i = 0; i < n; i++)
            {
                dx[i] += (cx - nodes[i].X) * 0.002;
                dy[i] += (cy - nodes[i].Y) * 0.002;
                var len = Math.Sqrt(dx[i] * dx[i] + dy[i] * dy[i]);
                if (len > maxStep)
                {
                    dx[i] *= maxStep / len;
                    dy[i] *= maxStep / len;
                }
                if (nodes[i].Pinned) continue;
                nodes[i].X += dx[i];
                nodes[i].Y += dy[i];
            }

            ResolveOverlaps(nodes, cell, passes: 2);
        }
        ResolveOverlaps(nodes, cell, passes: 12);
    }

    /// <summary>重なっている組を、互いに半分ずつ押し戻す(その場で順に解く)。</summary>
    private static void ResolveOverlaps(IList<LayoutNode> nodes, double cell, int passes)
    {
        var n = nodes.Count;
        var grid = new Dictionary<(long, long), List<int>>();
        for (var pass = 0; pass < passes; pass++)
        {
            grid.Clear();
            for (var i = 0; i < n; i++)
            {
                var key = ((long)Math.Floor(nodes[i].X / cell), (long)Math.Floor(nodes[i].Y / cell));
                if (!grid.TryGetValue(key, out var list)) grid[key] = list = [];
                list.Add(i);
            }

            var moved = false;
            for (var i = 0; i < n; i++)
            {
                var a = nodes[i];
                var gx = (long)Math.Floor(a.X / cell);
                var gy = (long)Math.Floor(a.Y / cell);
                for (var ox = -1; ox <= 1; ox++)
                for (var oy = -1; oy <= 1; oy++)
                {
                    if (!grid.TryGetValue((gx + ox, gy + oy), out var list)) continue;
                    foreach (var j in list)
                    {
                        if (j <= i) continue;
                        var b = nodes[j];
                        var vx = b.X - a.X;
                        var vy = b.Y - a.Y;
                        var d = Math.Sqrt(vx * vx + vy * vy);
                        var min = a.R + b.R;
                        if (d >= min) continue;
                        if (d < 1e-6)
                        {
                            // 完全に重なった点は添字から決まる向きに離す(決定的)。
                            var ang = (i * 7919 + j * 104729) % 360 * Math.PI / 180;
                            vx = Math.Cos(ang);
                            vy = Math.Sin(ang);
                            d = 1;
                        }
                        if (a.Pinned && b.Pinned) continue;
                        // 片方が動かせないときは、もう片方が全部動く。
                        var share = a.Pinned || b.Pinned ? 1.0 : 0.5;
                        var push = (min - d) * share / d;
                        if (!a.Pinned) { a.X -= vx * push; a.Y -= vy * push; }
                        if (!b.Pinned) { b.X += vx * push; b.Y += vy * push; }
                        moved = true;
                    }
                }
            }
            if (!moved) return;
        }
    }

    private static double Sq(double v) => v * v;
}
