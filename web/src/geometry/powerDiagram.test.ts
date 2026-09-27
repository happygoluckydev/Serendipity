import { describe, expect, it } from "vitest";
import { CLIP_FACTOR, SiteGrid, computePowerDiagram, hitTest, polygonArea, type Site } from "./powerDiagram";

function randomSites(n: number, seed: number): Site[] {
  let s = seed;
  const rand = () => {
    s = (s * 1103515245 + 12345) & 0x7fffffff;
    return s / 0x7fffffff;
  };
  return Array.from({ length: n }, () => ({
    x: rand() * 400 - 200,
    y: rand() * 400 - 200,
    r: 10 * Math.sqrt(1 + rand() * 5),
  }));
}

/** 総当たりの答え: パワー最小の種で、その切り取り円の内側なら、その種。 */
function bruteOwner(sites: Site[], x: number, y: number): number {
  let best = -1;
  let bestPower = Infinity;
  sites.forEach((s, i) => {
    const p = (x - s.x) ** 2 + (y - s.y) ** 2 - s.r * s.r;
    if (p < bestPower) {
      bestPower = p;
      best = i;
    }
  });
  const s = sites[best]!;
  return (x - s.x) ** 2 + (y - s.y) ** 2 <= (s.r * CLIP_FACTOR) ** 2 ? best : -1;
}

function pointInPolygon(poly: { x: number; y: number }[], x: number, y: number) {
  let inside = false;
  for (let i = 0, j = poly.length - 1; i < poly.length; j = i++) {
    const a = poly[i]!;
    const b = poly[j]!;
    if (a.y > y !== b.y > y && x < ((b.x - a.x) * (y - a.y)) / (b.y - a.y) + a.x) inside = !inside;
  }
  return inside;
}

describe("computePowerDiagram", () => {
  it("returns a bounded circle-ish cell for a single site", () => {
    const [cell] = computePowerDiagram([{ x: 5, y: -3, r: 10 }]);
    expect(cell!.polygon.length).toBeGreaterThan(10);
    const expected = Math.PI * (10 * CLIP_FACTOR) ** 2;
    expect(cell!.area).toBeGreaterThan(expected * 0.95);
    expect(cell!.area).toBeLessThanOrEqual(expected);
    expect(cell!.centroid.x).toBeCloseTo(5, 5);
    expect(cell!.centroid.y).toBeCloseTo(-3, 5);
  });

  it("splits two equal sites along the perpendicular bisector", () => {
    const cells = computePowerDiagram([
      { x: -10, y: 0, r: 10 },
      { x: 10, y: 0, r: 10 },
    ]);
    for (const p of cells[0]!.polygon) expect(p.x).toBeLessThanOrEqual(1e-9);
    for (const p of cells[1]!.polygon) expect(p.x).toBeGreaterThanOrEqual(-1e-9);
  });

  it("gives heavier sites more territory", () => {
    const cells = computePowerDiagram([
      { x: -12, y: 0, r: 10 },
      { x: 12, y: 0, r: 20 },
    ]);
    // 境界は重いほうから遠ざかる: x = (wi - wj) / (2 * 24) = (100 - 400) / 48
    const maxX = Math.max(...cells[0]!.polygon.map((p) => p.x));
    expect(maxX).toBeCloseTo((100 - 400) / 48, 6);
  });

  it("never overlaps and matches brute-force ownership", () => {
    const sites = randomSites(250, 7);
    const cells = computePowerDiagram(sites);
    const grid = new SiteGrid(sites);
    let s = 99;
    const rand = () => {
      s = (s * 1103515245 + 12345) & 0x7fffffff;
      return s / 0x7fffffff;
    };
    for (let k = 0; k < 3000; k++) {
      const x = rand() * 480 - 240;
      const y = rand() * 480 - 240;
      const owner = bruteOwner(sites, x, y);
      const containing = cells.filter((c) => c.polygon.length > 0 && pointInPolygon(c.polygon, x, y));
      // 境界ぎりぎりの点では多角形の近似がずれるので、はっきり内側の点だけ比べる。
      if (containing.length === 1) expect(containing[0]!.index).toBe(owner);
      expect(containing.length).toBeLessThanOrEqual(1);
      expect(hitTest(sites, grid, { x, y })).toBe(owner);
    }
  });

  it("covers the area of the union with no gaps between neighbours", () => {
    // 詰めて並べた種では、区画の面積の合計が各切り取り円の和集合とほぼ一致する。
    const sites: Site[] = [];
    for (let i = 0; i < 6; i++) for (let j = 0; j < 6; j++) sites.push({ x: i * 18, y: j * 18, r: 10 });
    const cells = computePowerDiagram(sites);
    const total = cells.reduce((sum, c) => sum + c.area, 0);
    // 内側の格子は 18×18 の正方形がそのまま区画になる。
    const inner = cells.filter((_, i) => {
      const x = Math.floor(i / 6);
      const y = i % 6;
      return x > 0 && x < 5 && y > 0 && y < 5;
    });
    for (const c of inner) expect(c.area).toBeCloseTo(18 * 18, 3);
    expect(total).toBeGreaterThan(36 * 18 * 18 * 0.9);
  });

  it("handles coincident sites by giving the spot to one of them", () => {
    const cells = computePowerDiagram([
      { x: 0, y: 0, r: 10 },
      { x: 0, y: 0, r: 10 },
    ]);
    const nonEmpty = cells.filter((c) => c.polygon.length > 0);
    expect(nonEmpty).toHaveLength(1);
    expect(Math.abs(polygonArea(nonEmpty[0]!.polygon))).toBeGreaterThan(0);
  });
});
