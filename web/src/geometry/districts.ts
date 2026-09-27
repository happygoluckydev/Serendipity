// 引きで見たときの街区。格子ごとに一番多いジャンルの色で塗る。
// 色の平均は使わない(赤と緑の平均は茶色になり、どの区画も濁る)。

export type DistrictInput<G extends string> = { x: number; y: number; genre: G };

export type District<G extends string> = {
  key: string;
  genre: G;
  count: number;
  cx: number;
  cy: number;
};

export function computeDistricts<G extends string>(
  marks: readonly DistrictInput<G>[],
  size: number,
  genreOrder: readonly G[],
): { districts: District<G>[]; genreOf: G[] } {
  const buckets = new Map<string, { idx: number[]; counts: Map<G, number>; sx: number; sy: number }>();
  marks.forEach((m, i) => {
    const key = `${Math.floor(m.x / size)},${Math.floor(m.y / size)}`;
    let b = buckets.get(key);
    if (!b) buckets.set(key, (b = { idx: [], counts: new Map(), sx: 0, sy: 0 }));
    b.idx.push(i);
    b.counts.set(m.genre, (b.counts.get(m.genre) ?? 0) + 1);
    b.sx += m.x;
    b.sy += m.y;
  });

  const genreOf: G[] = marks.map((m) => m.genre);
  const districts: District<G>[] = [];
  for (const [key, b] of buckets) {
    let best: G = marks[b.idx[0]!]!.genre;
    let bestCount = -1;
    for (const g of genreOrder) {
      const c = b.counts.get(g) ?? 0;
      if (c > bestCount) {
        best = g;
        bestCount = c;
      }
    }
    for (const i of b.idx) genreOf[i] = best;
    districts.push({ key, genre: best, count: b.idx.length, cx: b.sx / b.idx.length, cy: b.sy / b.idx.length });
  }
  return { districts, genreOf };
}
