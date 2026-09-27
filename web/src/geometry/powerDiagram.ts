// 重み付きボロノイ図(パワー図)。
// 平面を隙間なく分割するので、マークは構造上重ならない。
// 各区画は種を中心とする円でも切り取り、街の外周が有機的な形になるようにする。

export type Point = { x: number; y: number };
export type Polygon = Point[];

export type Site = {
  x: number;
  y: number;
  /** 縄張りの半径(世界座標)。パワー図の重みは r² になる。 */
  r: number;
};

export type Cell = {
  index: number;
  polygon: Polygon;
  /** 区画の重心。アイコンや文字を置く位置。 */
  centroid: Point;
  /** 区画の中心から縁までのおおよその距離(内接円の半径の目安)。 */
  inradius: number;
  area: number;
};

/** 外周を切り取る円の大きさ(縄張りの半径に対する倍率)。 */
export const CLIP_FACTOR = 1.35;
const CIRCLE_STEPS = 28;

/** 点 p が、i と j のパワー図の境界で i 側にあるか(境界上を含む)。 */
function clipByBisector(poly: Polygon, a: number, b: number, c: number): Polygon {
  // 半平面 a*x + b*y <= c で多角形を切る(Sutherland–Hodgman)。
  const out: Polygon = [];
  const n = poly.length;
  for (let k = 0; k < n; k++) {
    const p = poly[k]!;
    const q = poly[(k + 1) % n]!;
    const fp = a * p.x + b * p.y - c;
    const fq = a * q.x + b * q.y - c;
    const pIn = fp <= 0;
    const qIn = fq <= 0;
    if (pIn) out.push(p);
    if (pIn !== qIn) {
      const t = fp / (fp - fq);
      out.push({ x: p.x + (q.x - p.x) * t, y: p.y + (q.y - p.y) * t });
    }
  }
  return out;
}

function circlePolygon(s: Site): Polygon {
  const R = s.r * CLIP_FACTOR;
  const poly: Polygon = [];
  for (let k = 0; k < CIRCLE_STEPS; k++) {
    const a = (k / CIRCLE_STEPS) * Math.PI * 2;
    poly.push({ x: s.x + Math.cos(a) * R, y: s.y + Math.sin(a) * R });
  }
  return poly;
}

function maxDistance(poly: Polygon, s: Site): number {
  let m = 0;
  for (const p of poly) m = Math.max(m, Math.hypot(p.x - s.x, p.y - s.y));
  return m;
}

export function polygonArea(poly: Polygon): number {
  let a = 0;
  for (let k = 0; k < poly.length; k++) {
    const p = poly[k]!;
    const q = poly[(k + 1) % poly.length]!;
    a += p.x * q.y - q.x * p.y;
  }
  return a / 2;
}

export function polygonCentroid(poly: Polygon): Point {
  const a = polygonArea(poly);
  if (Math.abs(a) < 1e-9) {
    const sx = poly.reduce((s, p) => s + p.x, 0);
    const sy = poly.reduce((s, p) => s + p.y, 0);
    return { x: sx / Math.max(1, poly.length), y: sy / Math.max(1, poly.length) };
  }
  let cx = 0;
  let cy = 0;
  for (let k = 0; k < poly.length; k++) {
    const p = poly[k]!;
    const q = poly[(k + 1) % poly.length]!;
    const f = p.x * q.y - q.x * p.y;
    cx += (p.x + q.x) * f;
    cy += (p.y + q.y) * f;
  }
  return { x: cx / (6 * a), y: cy / (6 * a) };
}

function distanceToSegment(p: Point, a: Point, b: Point): number {
  const dx = b.x - a.x;
  const dy = b.y - a.y;
  const len2 = dx * dx + dy * dy;
  const t = len2 === 0 ? 0 : Math.max(0, Math.min(1, ((p.x - a.x) * dx + (p.y - a.y) * dy) / len2));
  return Math.hypot(p.x - (a.x + dx * t), p.y - (a.y + dy * t));
}

function inradiusAt(poly: Polygon, c: Point): number {
  let m = Infinity;
  for (let k = 0; k < poly.length; k++) {
    m = Math.min(m, distanceToSegment(c, poly[k]!, poly[(k + 1) % poly.length]!));
  }
  return Number.isFinite(m) ? m : 0;
}

/** 近くの種を素早く探すための格子。 */
export class SiteGrid {
  readonly size: number;
  private readonly cells = new Map<string, number[]>();

  constructor(
    readonly sites: readonly Site[],
    size?: number,
  ) {
    const maxR = sites.reduce((m, s) => Math.max(m, s.r), 1);
    this.size = size ?? maxR * 2 * CLIP_FACTOR;
    sites.forEach((s, i) => {
      const key = this.key(Math.floor(s.x / this.size), Math.floor(s.y / this.size));
      const list = this.cells.get(key);
      if (list) list.push(i);
      else this.cells.set(key, [i]);
    });
  }

  private key(gx: number, gy: number) {
    return `${gx},${gy}`;
  }

  /** (x, y) を含む格子から外側へ、一周ずつ種を返す。 */
  *rings(x: number, y: number, maxRing: number): Generator<{ ring: number; indices: number[] }> {
    const gx = Math.floor(x / this.size);
    const gy = Math.floor(y / this.size);
    for (let ring = 0; ring <= maxRing; ring++) {
      const indices: number[] = [];
      for (let ox = -ring; ox <= ring; ox++) {
        for (let oy = -ring; oy <= ring; oy++) {
          if (Math.max(Math.abs(ox), Math.abs(oy)) !== ring) continue;
          const list = this.cells.get(this.key(gx + ox, gy + oy));
          if (list) indices.push(...list);
        }
      }
      yield { ring, indices };
    }
  }
}

/**
 * パワー図を計算する。区画 i は |p - s_i|² - r_i² が最小になる点の集まりを、
 * 半径 r_i * CLIP_FACTOR の円で切り取ったもの。
 * 重みの差が大きいと区画が空になることがあり、その場合は polygon が空になる。
 */
export function computePowerDiagram(sites: readonly Site[]): Cell[] {
  if (sites.length === 0) return [];
  const grid = new SiteGrid(sites);
  const maxW = sites.reduce((m, s) => Math.max(m, s.r * s.r), 0);
  const maxRing = Math.ceil(
    (sites.reduce((m, s) => Math.max(m, s.r), 0) * CLIP_FACTOR * 2 + Math.sqrt(maxW)) / grid.size,
  ) + 2;

  return sites.map((si, i) => {
    let poly = circlePolygon(si);
    const wi = si.r * si.r;
    for (const { ring, indices } of grid.rings(si.x, si.y, maxRing)) {
      if (poly.length === 0) break;
      // この輪にある種は、少なくとも (ring - 1) * size だけ離れている。
      // 境界までの距離 (d² + wi - wj) / 2d は d について単調増加なので、
      // 多角形の一番遠い頂点より境界が遠ければ、これより外の種は区画に影響しない。
      const dMin = Math.max(0, (ring - 1) * grid.size);
      if (dMin > 0) {
        const reach = (dMin * dMin + wi - maxW) / (2 * dMin);
        if (reach > maxDistance(poly, si)) break;
      }
      for (const j of indices) {
        if (j === i) continue;
        const sj = sites[j]!;
        const dx = sj.x - si.x;
        const dy = sj.y - si.y;
        const wj = sj.r * sj.r;
        if (dx === 0 && dy === 0) {
          // 同じ位置の種は、重いほう(同じなら添字の小さいほう)が取る。
          if (wj > wi || (wj === wi && j < i)) {
            poly = [];
            break;
          }
          continue;
        }
        // |p-si|² - wi <= |p-sj|² - wj  ⇔  2p·(sj-si) <= |sj|²-|si|² - wj + wi
        const a = 2 * dx;
        const b = 2 * dy;
        const c = sj.x * sj.x + sj.y * sj.y - si.x * si.x - si.y * si.y - wj + wi;
        poly = clipByBisector(poly, a, b, c);
        if (poly.length === 0) break;
      }
    }
    const area = poly.length >= 3 ? Math.abs(polygonArea(poly)) : 0;
    const centroid = area > 0 ? polygonCentroid(poly) : { x: si.x, y: si.y };
    return { index: i, polygon: area > 0 ? poly : [], centroid, inradius: area > 0 ? inradiusAt(poly, centroid) : 0, area };
  });
}

/**
 * 点を含む区画の添字。どの区画にも入らなければ -1。
 * パワーが最小の種を選び、その種の切り取り円の内側かを確かめる。
 * 格子の一辺は最大半径の 2×CLIP_FACTOR 倍なので、隣り合う格子まで見れば足りる。
 */
export function hitTest(sites: readonly Site[], grid: SiteGrid, p: Point): number {
  let best = -1;
  let bestPower = Infinity;
  for (const { indices } of grid.rings(p.x, p.y, 1)) {
    for (const i of indices) {
      const s = sites[i]!;
      const power = (p.x - s.x) ** 2 + (p.y - s.y) ** 2 - s.r * s.r;
      if (power < bestPower || (power === bestPower && i < best)) {
        bestPower = power;
        best = i;
      }
    }
  }
  if (best < 0) return -1;
  const s = sites[best]!;
  const inside = (p.x - s.x) ** 2 + (p.y - s.y) ** 2 <= (s.r * CLIP_FACTOR) ** 2;
  return inside ? best : -1;
}

export function polygonPath(poly: Polygon): string {
  if (poly.length === 0) return "";
  let d = `M${poly[0]!.x.toFixed(2)},${poly[0]!.y.toFixed(2)}`;
  for (let k = 1; k < poly.length; k++) d += `L${poly[k]!.x.toFixed(2)},${poly[k]!.y.toFixed(2)}`;
  return d + "Z";
}
