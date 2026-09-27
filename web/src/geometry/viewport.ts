// 街ビューの視点。画面座標 = 世界座標 × k + (tx, ty)。

export type View = { tx: number; ty: number; k: number };
export type Size = { width: number; height: number };

/** ズームの段階。重み1のマークの半径が画面で何pxに見えるかで決める。 */
export type ZoomLevel = "far" | "mid" | "near";

export const LEVEL_MID_PX = 22;
export const LEVEL_NEAR_PX = 55;
export const MAX_BASE_PX = 180;

export function zoomLevel(k: number, baseRadius: number): ZoomLevel {
  const px = k * baseRadius;
  if (px < LEVEL_MID_PX) return "far";
  if (px < LEVEL_NEAR_PX) return "mid";
  return "near";
}

/** 段階ごとの代表的な倍率(ボタンでのズームや、マークへ寄るときに使う)。 */
export function levelScale(level: ZoomLevel, baseRadius: number): number {
  const px = level === "far" ? LEVEL_MID_PX * 0.7 : level === "mid" ? (LEVEL_MID_PX + LEVEL_NEAR_PX) / 2 : LEVEL_NEAR_PX * 1.6;
  return px / baseRadius;
}

export function bounds(points: readonly { x: number; y: number; r: number }[]) {
  if (points.length === 0) return { minX: -50, minY: -50, maxX: 50, maxY: 50 };
  let minX = Infinity;
  let minY = Infinity;
  let maxX = -Infinity;
  let maxY = -Infinity;
  for (const p of points) {
    minX = Math.min(minX, p.x - p.r);
    minY = Math.min(minY, p.y - p.r);
    maxX = Math.max(maxX, p.x + p.r);
    maxY = Math.max(maxY, p.y + p.r);
  }
  return { minX, minY, maxX, maxY };
}

/** 街全体が収まる視点。 */
export function fitView(b: ReturnType<typeof bounds>, size: Size, padding = 24): View {
  const w = Math.max(1, b.maxX - b.minX);
  const h = Math.max(1, b.maxY - b.minY);
  const k = Math.min((size.width - padding * 2) / w, (size.height - padding * 2) / h);
  return {
    k,
    tx: size.width / 2 - ((b.minX + b.maxX) / 2) * k,
    ty: size.height / 2 - ((b.minY + b.maxY) / 2) * k,
  };
}

export function clampScale(k: number, minK: number, baseRadius: number) {
  return Math.min(Math.max(k, minK), MAX_BASE_PX / baseRadius);
}

/** 画面上の点 (sx, sy) を動かさずに倍率を変える。 */
export function zoomAround(v: View, sx: number, sy: number, nextK: number): View {
  const wx = (sx - v.tx) / v.k;
  const wy = (sy - v.ty) / v.k;
  return { k: nextK, tx: sx - wx * nextK, ty: sy - wy * nextK };
}

/** 世界座標の点を画面の中央に持ってくる視点。 */
export function centerOn(x: number, y: number, k: number, size: Size): View {
  return { k, tx: size.width / 2 - x * k, ty: size.height / 2 - y * k };
}

export function toWorld(v: View, sx: number, sy: number) {
  return { x: (sx - v.tx) / v.k, y: (sy - v.ty) / v.k };
}

export function lerpView(a: View, b: View, t: number): View {
  // 倍率は対数で補間すると、寄り引きが自然に見える。
  const k = Math.exp(Math.log(a.k) + (Math.log(b.k) - Math.log(a.k)) * t);
  // 画面中央に見える世界の点を直線で動かす。
  return { k, tx: a.tx + (b.tx - a.tx) * t, ty: a.ty + (b.ty - a.ty) * t };
}
