namespace Serendipity.Api.City;

public sealed class LayoutNode(Guid id, double x, double y, double r)
{
    public Guid Id { get; } = id;
    public double X { get; set; } = x;
    public double Y { get; set; } = y;
    public double R { get; set; } = r;
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
        foreach (var stretch in new[] { 1.0, 1.4, 2.0, 3.0 })
        {
            var dist = (anchor.R + r) * stretch;
            var bestScore = double.NegativeInfinity;
            (double X, double Y) best = default;
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
            // 少しの重なりは Relax がほどく。大きく重なるときだけ一段遠くを試す。
            if (bestScore >= -r * 0.25) return best;
            if (stretch >= 3.0) return best;
        }
        return (anchor.X + anchor.R + r, anchor.Y);
    }

    /// <summary>ばねと反発で種の位置をならす。edges は nodes の添字の組。</summary>
    public static void Relax(IList<LayoutNode> nodes, IReadOnlyList<(int A, int B)> edges, int iterations)
    {
        var n = nodes.Count;
        if (n < 2) return;
        var dx = new double[n];
        var dy = new double[n];
        var maxR = 0.0;
        foreach (var node in nodes) maxR = Math.Max(maxR, node.R);
        var cell = maxR * 2.4;
        var grid = new Dictionary<(long, long), List<int>>();

        for (var it = 0; it < iterations; it++)
        {
            Array.Clear(dx);
            Array.Clear(dy);
            grid.Clear();
            for (var i = 0; i < n; i++)
            {
                var key = ((long)Math.Floor(nodes[i].X / cell), (long)Math.Floor(nodes[i].Y / cell));
                if (!grid.TryGetValue(key, out var list)) grid[key] = list = [];
                list.Add(i);
            }

            // 重なりの反発。近いセルだけを見る。
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
                        var push = (min - d) * 0.5 / d;
                        dx[i] -= vx * push; dy[i] -= vy * push;
                        dx[j] += vx * push; dy[j] += vy * push;
                    }
                }
            }

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
                nodes[i].X += dx[i];
                nodes[i].Y += dy[i];
            }
        }
    }

    private static double Sq(double v) => v * v;
}
