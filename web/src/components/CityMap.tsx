import { useCallback, useEffect, useMemo, useRef, useState, type PointerEvent as ReactPointerEvent } from "react";
import type { CityMark, Genre } from "../api/types";
import { GENRES, SHAPE_PATHS, genreColor, genreLabel, genreTint, stageInfo } from "../domain";
import { computeDistricts } from "../geometry/districts";
import { CLIP_FACTOR, SiteGrid, computePowerDiagram, hitTest, polygonPath, type Site } from "../geometry/powerDiagram";
import {
  bounds,
  centerOn,
  clampScale,
  fitView,
  lerpView,
  levelScale,
  toWorld,
  zoomAround,
  zoomLevel,
  type View,
} from "../geometry/viewport";

const GENRE_ORDER = GENRES.map((g) => g.id);
/** これより小さく見える区画は、タップするとまず寄る(押し間違えを防ぐ)。 */
const MIN_TAP_RADIUS_PX = 18;
const TAP_SLOP_PX = 10;

export type FlyTarget = { x: number; y: number; seq: number };

type Props = {
  marks: CityMark[];
  baseRadius: number;
  fontScale: number;
  selectedId?: string | null;
  highlightId?: string | null;
  pickMode?: boolean;
  flyTo?: FlyTarget | null;
  onTapMark: (mark: CityMark) => void;
  onViewed?: (ids: string[]) => void;
};

function prefersReducedMotion() {
  return typeof matchMedia !== "undefined" && matchMedia("(prefers-reduced-motion: reduce)").matches;
}

export function CityMap({ marks, baseRadius, fontScale, selectedId, highlightId, pickMode, flyTo, onTapMark, onViewed }: Props) {
  const wrapRef = useRef<HTMLDivElement>(null);
  const svgRef = useRef<SVGSVGElement>(null);
  const [size, setSize] = useState({ width: 0, height: 0 });
  const [view, setView] = useState<View | null>(null);
  const viewRef = useRef(view);
  viewRef.current = view;

  const sites: Site[] = useMemo(
    () => marks.map((m) => ({ x: m.x, y: m.y, r: baseRadius * Math.sqrt(Math.max(1, m.weight)) })),
    [marks, baseRadius],
  );
  const cells = useMemo(() => computePowerDiagram(sites), [sites]);
  const grid = useMemo(() => new SiteGrid(sites), [sites]);
  const paths = useMemo(() => cells.map((c) => polygonPath(c.polygon)), [cells]);
  // 内側の縁取り。区画を重心へ少し縮めたもの(区画は凸なので、はみ出さない)。
  const innerPaths = useMemo(
    () =>
      cells.map((c) =>
        polygonPath(c.polygon.map((p) => ({ x: c.centroid.x + (p.x - c.centroid.x) * 0.84, y: c.centroid.y + (p.y - c.centroid.y) * 0.84 }))),
      ),
    [cells],
  );
  const districtInfo = useMemo(() => computeDistricts(marks, baseRadius * 7, GENRE_ORDER), [marks, baseRadius]);
  const cityBounds = useMemo(() => bounds(sites.map((s) => ({ ...s, r: s.r * CLIP_FACTOR }))), [sites]);
  const fitted = useMemo(() => (size.width > 0 ? fitView(cityBounds, size) : null), [cityBounds, size]);
  const minK = (fitted?.k ?? 1) * 0.6;

  // 画面の大きさを追う。
  useEffect(() => {
    const el = wrapRef.current;
    if (!el) return;
    const ro = new ResizeObserver(() => setSize({ width: el.clientWidth, height: el.clientHeight }));
    ro.observe(el);
    setSize({ width: el.clientWidth, height: el.clientHeight });
    return () => ro.disconnect();
  }, []);

  // 最初は街全体が見えるようにする。
  useEffect(() => {
    if (!view && fitted && marks.length > 0) setView(fitted);
  }, [view, fitted, marks.length]);

  const anim = useRef<number | null>(null);
  const animateTo = useCallback((target: View, ms = 420) => {
    if (anim.current) cancelAnimationFrame(anim.current);
    const from = viewRef.current;
    if (!from || prefersReducedMotion()) {
      setView(target);
      return;
    }
    const start = performance.now();
    const step = (now: number) => {
      const t = Math.min(1, (now - start) / ms);
      const e = t < 0.5 ? 4 * t * t * t : 1 - (-2 * t + 2) ** 3 / 2;
      setView(lerpView(from, target, e));
      if (t < 1) anim.current = requestAnimationFrame(step);
      else anim.current = null;
    };
    anim.current = requestAnimationFrame(step);
  }, []);

  useEffect(() => {
    if (!flyTo || size.width === 0) return;
    const current = viewRef.current;
    const k = Math.max(current?.k ?? 0, levelScale("near", baseRadius));
    animateTo(centerOn(flyTo.x, flyTo.y, k, size), 600);
    // seq が変わったときだけ飛ぶ。
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [flyTo?.seq]);

  const zoomBy = useCallback(
    (factor: number, sx?: number, sy?: number) => {
      const v = viewRef.current;
      if (!v) return;
      const k = clampScale(v.k * factor, minK, baseRadius);
      animateTo(zoomAround(v, sx ?? size.width / 2, sy ?? size.height / 2, k), 260);
    },
    [animateTo, baseRadius, minK, size],
  );

  // ---- タップ ----
  const handleTap = useCallback(
    (sx: number, sy: number) => {
      const v = viewRef.current;
      if (!v) return;
      const w = toWorld(v, sx, sy);
      let idx = hitTest(sites, grid, w);
      if (idx < 0) {
        // 区画が空になったマークは、種の位置に小さな点で出しているので、そこを探す。
        idx = cells.findIndex((c, i) => c.area === 0 && Math.hypot(w.x - sites[i]!.x, w.y - sites[i]!.y) <= sites[i]!.r * 0.35);
      }
      if (idx < 0) return;
      const cell = cells[idx]!;
      const radiusPx = (cell.area > 0 ? cell.inradius : sites[idx]!.r * 0.35) * v.k;
      if (radiusPx < MIN_TAP_RADIUS_PX && zoomLevel(v.k, baseRadius) !== "near") {
        const k = clampScale(Math.max(v.k * 2.2, levelScale("mid", baseRadius)), minK, baseRadius);
        animateTo(zoomAround(v, sx, sy, k), 320);
        return;
      }
      onTapMark(marks[idx]!);
    },
    [sites, grid, cells, marks, baseRadius, minK, animateTo, onTapMark],
  );

  // ---- 指とマウスの操作 ----
  const pointers = useRef(new Map<number, { x: number; y: number }>());
  const gesture = useRef<{
    startX: number;
    startY: number;
    startTime: number;
    moved: boolean;
    startView: View;
    pinch?: { dist: number; midX: number; midY: number };
  } | null>(null);

  const local = (e: { clientX: number; clientY: number }) => {
    const rect = svgRef.current!.getBoundingClientRect();
    return { x: e.clientX - rect.left, y: e.clientY - rect.top };
  };

  const beginGesture = (moved: boolean) => {
    const v = viewRef.current;
    if (!v) return;
    const pts = [...pointers.current.values()];
    if (pts.length === 1) {
      gesture.current = { startX: pts[0]!.x, startY: pts[0]!.y, startTime: performance.now(), moved, startView: v };
    } else if (pts.length >= 2) {
      const [a, b] = pts as [{ x: number; y: number }, { x: number; y: number }];
      gesture.current = {
        startX: (a.x + b.x) / 2,
        startY: (a.y + b.y) / 2,
        startTime: performance.now(),
        moved: true,
        startView: v,
        pinch: { dist: Math.hypot(a.x - b.x, a.y - b.y), midX: (a.x + b.x) / 2, midY: (a.y + b.y) / 2 },
      };
    }
  };

  const onPointerDown = (e: ReactPointerEvent<SVGSVGElement>) => {
    if (anim.current) {
      cancelAnimationFrame(anim.current);
      anim.current = null;
    }
    svgRef.current?.setPointerCapture(e.pointerId);
    pointers.current.set(e.pointerId, local(e));
    beginGesture(pointers.current.size > 1);
  };

  const onPointerMove = (e: ReactPointerEvent<SVGSVGElement>) => {
    if (!pointers.current.has(e.pointerId)) return;
    pointers.current.set(e.pointerId, local(e));
    const g = gesture.current;
    if (!g) return;
    const pts = [...pointers.current.values()];
    if (g.pinch && pts.length >= 2) {
      const [a, b] = pts as [{ x: number; y: number }, { x: number; y: number }];
      const dist = Math.hypot(a.x - b.x, a.y - b.y);
      const midX = (a.x + b.x) / 2;
      const midY = (a.y + b.y) / 2;
      const k = clampScale(g.startView.k * (dist / Math.max(1, g.pinch.dist)), minK, baseRadius);
      const zoomed = zoomAround(g.startView, g.pinch.midX, g.pinch.midY, k);
      setView({ k, tx: zoomed.tx + (midX - g.pinch.midX), ty: zoomed.ty + (midY - g.pinch.midY) });
      return;
    }
    const p = pts[0]!;
    const dx = p.x - g.startX;
    const dy = p.y - g.startY;
    if (!g.moved && Math.hypot(dx, dy) > TAP_SLOP_PX) g.moved = true;
    if (g.moved) setView({ ...g.startView, tx: g.startView.tx + dx, ty: g.startView.ty + dy });
  };

  const onPointerUp = (e: ReactPointerEvent<SVGSVGElement>) => {
    const g = gesture.current;
    const wasSingle = pointers.current.size === 1;
    pointers.current.delete(e.pointerId);
    if (wasSingle && g && !g.moved && performance.now() - g.startTime < 700) {
      handleTap(g.startX, g.startY);
    }
    // 二本指の片方を離したら、残った指で続けて動かせるようにする(タップにはしない)。
    if (pointers.current.size > 0) beginGesture(true);
    else gesture.current = null;
  };

  // ホイールでのズーム(パソコン)。
  useEffect(() => {
    const el = svgRef.current;
    if (!el) return;
    const onWheel = (e: WheelEvent) => {
      e.preventDefault();
      const v = viewRef.current;
      if (!v) return;
      const rect = el.getBoundingClientRect();
      const k = clampScale(v.k * Math.exp(-e.deltaY * 0.0015), minK, baseRadius);
      setView(zoomAround(v, e.clientX - rect.left, e.clientY - rect.top, k));
    };
    el.addEventListener("wheel", onWheel, { passive: false });
    return () => el.removeEventListener("wheel", onWheel);
  }, [minK, baseRadius]);

  const level = view ? zoomLevel(view.k, baseRadius) : "far";

  // 画面に入っているマークだけを描く。
  const visible = useMemo(() => {
    if (!view || size.width === 0) return [] as number[];
    const a = toWorld(view, 0, 0);
    const b = toWorld(view, size.width, size.height);
    const out: number[] = [];
    sites.forEach((s, i) => {
      const r = s.r * CLIP_FACTOR;
      if (s.x + r >= a.x && s.x - r <= b.x && s.y + r >= a.y && s.y - r <= b.y) out.push(i);
    });
    return out;
  }, [view, size, sites]);

  // 眺めたマークを知らせる(重みの材料)。引きの段階は数えない。
  const reported = useRef(new Set<string>());
  useEffect(() => {
    if (!onViewed || level === "far") return;
    const t = setTimeout(() => {
      const ids = visible.map((i) => marks[i]!.id).filter((id) => !reported.current.has(id));
      if (ids.length === 0) return;
      ids.forEach((id) => reported.current.add(id));
      onViewed(ids);
    }, 1500);
    return () => clearTimeout(t);
  }, [visible, level, marks, onViewed]);

  const hatchSize = view ? 7 / view.k : 1;

  return (
    <div ref={wrapRef} className={`city-wrap ${pickMode ? "is-picking" : ""}`}>
      <svg
        ref={svgRef}
        className={`city level-${level}`}
        width={size.width}
        height={size.height}
        role="img"
        aria-label="街の地図。ドラッグで移動、ピンチやボタンで拡大縮小、マークをタップするとポップが開きます。"
        onPointerDown={onPointerDown}
        onPointerMove={onPointerMove}
        onPointerUp={onPointerUp}
        onPointerCancel={onPointerUp}
      >
        <defs>
          {GENRES.map((g) => (
            <pattern
              key={g.id}
              id={`hatch-${g.id}`}
              patternUnits="userSpaceOnUse"
              width={hatchSize}
              height={hatchSize}
              patternTransform="rotate(45)"
            >
              <rect width={hatchSize} height={hatchSize} fill={genreTint(g.id)} />
              <rect width={hatchSize * 0.3} height={hatchSize} fill={genreColor(g.id)} opacity={0.3} />
            </pattern>
          ))}
        </defs>
        {view && (
          <g transform={`translate(${view.tx} ${view.ty}) scale(${view.k})`}>
            {visible.map((i) => {
              const m = marks[i]!;
              const cell = cells[i]!;
              const districtGenre: Genre = districtInfo.genreOf[i] ?? m.genre;
              const pending = m.status === "PENDING";
              const cls = [
                "cell",
                m.isNew ? "is-new" : "",
                m.id === selectedId ? "is-selected" : "",
                m.id === highlightId ? "is-highlight" : "",
                pending ? "is-pending" : "",
              ].join(" ");
              if (cell.area === 0) {
                return (
                  <circle
                    key={m.id}
                    className={cls}
                    cx={sites[i]!.x}
                    cy={sites[i]!.y}
                    r={sites[i]!.r * 0.3}
                    fill={genreTint(m.genre)}
                    stroke={genreColor(m.genre)}
                    strokeWidth={1.5}
                    vectorEffect="non-scaling-stroke"
                    opacity={m.opacity}
                  />
                );
              }
              if (level === "far") {
                return (
                  <path
                    key={m.id}
                    className={cls}
                    d={paths[i]}
                    fill={genreTint(districtGenre)}
                    stroke="var(--street)"
                    strokeWidth={0.8}
                    vectorEffect="non-scaling-stroke"
                    opacity={m.opacity}
                  />
                );
              }
              return (
                <g key={m.id} className={cls} opacity={m.opacity}>
                  <path
                    d={paths[i]}
                    className="cell-fill"
                    fill={m.stage === "WANT" ? `url(#hatch-${m.genre})` : genreTint(m.genre)}
                    stroke="var(--street)"
                    strokeWidth={level === "near" ? 4 : 2.5}
                    vectorEffect="non-scaling-stroke"
                  />
                  <path
                    d={innerPaths[i]}
                    className="cell-edge"
                    fill="none"
                    stroke={pending ? "var(--muted)" : genreColor(m.genre)}
                    strokeWidth={level === "near" ? 2 : 1.5}
                    strokeDasharray={pending ? "2 4" : m.stage === "CURIOUS" ? "7 5" : m.stage === "WANT" ? "1.5 4" : undefined}
                    strokeLinecap="round"
                    vectorEffect="non-scaling-stroke"
                  />
                </g>
              );
            })}
          </g>
        )}
        {view && level === "far" && (
          <g className="district-labels" aria-hidden="true">
            {placeDistrictLabels(districtInfo.districts, view).map((d) => (
              <text key={d.key} x={d.x} y={d.y} className="district-label">
                {genreLabel(d.genre)}
              </text>
            ))}
          </g>
        )}
        {view && level !== "far" && (
          <g className="mark-labels" aria-hidden="true">
            {visible.map((i) => {
              const m = marks[i]!;
              const cell = cells[i]!;
              if (cell.area === 0) return null;
              const r = cell.inradius * view.k;
              if (r < 9) return null;
              const cx = cell.centroid.x * view.k + view.tx;
              const cy = cell.centroid.y * view.k + view.ty;
              const showText = level === "near" && r >= 38;
              const icon = Math.min(showText ? 26 : 36, Math.max(14, r * 0.9));
              const iconY = showText ? cy - r * 0.55 : cy - icon / 2;
              return (
                <g key={m.id}>
                  <g transform={`translate(${cx - icon / 2} ${iconY}) scale(${icon / 24})`}>
                    <path
                      d={SHAPE_PATHS[m.shape]}
                      fill="none"
                      stroke={m.status === "PENDING" ? "var(--muted)" : genreColor(m.genre)}
                      strokeWidth={1.9}
                      strokeLinecap="round"
                      strokeLinejoin="round"
                    />
                  </g>
                  {m.status === "PENDING" && r >= 22 && !showText && (
                    <text x={cx} y={iconY + icon + 14} className="pending-label">
                      確認中
                    </text>
                  )}
                  {showText && (
                    <foreignObject x={cx - r * 0.82} y={iconY + icon + 2} width={r * 1.64} height={Math.max(24, r * 1.25)}>
                      <div className="cell-text" style={{ fontSize: `${Math.min(19, Math.max(12, r / 5.2)) * fontScale}px` }}>
                        {m.popPhotoUrl && r >= 72 && <img src={m.popPhotoUrl} alt="" className="cell-photo" draggable={false} />}
                        {m.status === "PENDING" && <span className="cell-pending">確認中 </span>}
                        <span className="cell-stage">{stageInfo(m.stage).mark}</span>
                        {m.popText}
                        {m.hasPr && <span className="cell-pr">PR</span>}
                      </div>
                    </foreignObject>
                  )}
                </g>
              );
            })}
          </g>
        )}
      </svg>

      {/* 画面の読み上げ・キーボード用に、見えているマークを一覧でも出す。 */}
      <ul className="sr-only">
        {level !== "far" &&
          visible.map((i) => (
            <li key={marks[i]!.id}>
              <button type="button" onClick={() => onTapMark(marks[i]!)}>
                {genreLabel(marks[i]!.genre)}・{stageInfo(marks[i]!.stage).label}: {marks[i]!.popText}
              </button>
            </li>
          ))}
      </ul>

      <div className="zoom-controls">
        <button type="button" className="zoom-btn" onClick={() => zoomBy(1.8)} aria-label="近づく">
          ＋
        </button>
        <button type="button" className="zoom-btn" onClick={() => zoomBy(1 / 1.8)} aria-label="離れる">
          −
        </button>
        <button type="button" className="zoom-btn zoom-fit" onClick={() => fitted && animateTo(fitted, 420)} aria-label="街全体を見る">
          全体
        </button>
      </div>
    </div>
  );
}

/**
 * 街区の名前を、重ならないように選んで置く。大きい街区から順に置き、
 * 既に置いた名前とぶつかるもの、同じ名前がすぐ近くにあるものは省く。
 */
function placeDistrictLabels(districts: { key: string; genre: Genre; count: number; cx: number; cy: number }[], view: View) {
  const placed: { key: string; genre: Genre; x: number; y: number; w: number }[] = [];
  const sorted = [...districts].filter((d) => d.count >= 2).sort((a, b) => b.count - a.count);
  for (const d of sorted) {
    const x = d.cx * view.k + view.tx;
    const y = d.cy * view.k + view.ty;
    const w = genreLabel(d.genre).length * 15 + 12;
    const clash = placed.some(
      (p) =>
        (Math.abs(p.x - x) < (p.w + w) / 2 && Math.abs(p.y - y) < 26) ||
        (p.genre === d.genre && Math.hypot(p.x - x, p.y - y) < 160),
    );
    if (!clash) placed.push({ key: d.key, genre: d.genre, x, y, w });
  }
  return placed;
}
