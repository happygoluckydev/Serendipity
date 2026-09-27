import type { Genre, Shape, Stage } from "./api/types";

/**
 * ジャンル(色)。設計書の未決事項「6〜8色に何を割り当てるか」の暫定案。
 * 色だけに意味を頼らないよう、どこでもラベルと一緒に出す。
 */
export const GENRES: { id: Genre; label: string; short: string }[] = [
  { id: "FASHION", label: "ファッション", short: "服" },
  { id: "LIVING", label: "暮らし・雑貨", short: "暮" },
  { id: "FOOD", label: "食", short: "食" },
  { id: "BOOKS", label: "本・学び", short: "本" },
  { id: "HOBBY", label: "趣味・遊び", short: "遊" },
  { id: "TECH", label: "テック・道具", short: "道" },
  { id: "BEAUTY", label: "美容・健康", short: "美" },
  { id: "PLACE", label: "場所・体験", short: "場" },
];

export const genreLabel = (g: Genre) => GENRES.find((x) => x.id === g)?.label ?? g;
/** CSS 変数の名前。線や文字は --g-xxx、塗りは --g-xxx-tint。 */
export const genreVar = (g: Genre) => `--g-${g.toLowerCase()}`;
export const genreColor = (g: Genre) => `var(${genreVar(g)})`;
export const genreTint = (g: Genre) => `var(${genreVar(g)}-tint)`;

export const SHAPES: { id: Shape; label: string }[] = [
  { id: "PRODUCT", label: "既製品" },
  { id: "VINTAGE", label: "古着" },
  { id: "GOODS", label: "雑貨" },
  { id: "STORE", label: "お店" },
  { id: "ARTICLE", label: "記事" },
];

export const shapeLabel = (s: Shape) => SHAPES.find((x) => x.id === s)?.label ?? s;

/** 24×24 の線画。stroke で描く。 */
export const SHAPE_PATHS: Record<Shape, string> = {
  PRODUCT: "M4 8l8-4 8 4v8l-8 4-8-4z M4 8l8 4 8-4 M12 12v8",
  VINTAGE: "M9 4l-5 3 2 3.5 2-1V20h8V9.5l2 1L20 7l-5-3c-.4 1.3-1.6 2.2-3 2.2S9.4 5.3 9 4z",
  GOODS: "M5 8h11v7a4 4 0 0 1-4 4H9a4 4 0 0 1-4-4z M16 10h1.5a2 2 0 0 1 0 4H16 M8.5 3.5v2 M12 3.5v2",
  STORE: "M4.5 10.5V20h15v-9.5 M3 10.5L5 4h14l2 6.5z M10 20v-5h4v5 M3 10.5c1.5 1.5 4.5 1.5 6 0 1.5 1.5 4.5 1.5 6 0 1.5 1.5 4.5 1.5 6 0",
  ARTICLE: "M6 3h9l4 4v14H6z M15 3v4h4 M9 11h7 M9 14.5h7 M9 18h4",
};

export const STAGES: { id: Stage; label: string; mark: string; hint: string }[] = [
  { id: "USED", label: "使って良かった", mark: "◎", hint: "実際に使って良かったもの" },
  { id: "CURIOUS", label: "気になる", mark: "？", hint: "まだ使っていないけれど気になるもの" },
  { id: "WANT", label: "欲しい", mark: "☆", hint: "いつか手に入れたいもの" },
];

export const stageInfo = (s: Stage) => STAGES.find((x) => x.id === s)!;

export function ShapeIcon({ shape, size = 24, color = "currentColor" }: { shape: Shape; size?: number; color?: string }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" aria-hidden="true" focusable="false">
      <path d={SHAPE_PATHS[shape]} fill="none" stroke={color} strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}

export function StageBadge({ stage }: { stage: Stage }) {
  const s = stageInfo(stage);
  return (
    <span className={`stage-badge stage-${stage.toLowerCase()}`}>
      <span aria-hidden="true">{s.mark}</span>
      {s.label}
    </span>
  );
}

export function GenreChip({ genre }: { genre: Genre }) {
  return (
    <span className="genre-chip" style={{ ["--chip" as string]: genreColor(genre), ["--chip-tint" as string]: genreTint(genre) }}>
      <span className="genre-dot" aria-hidden="true" />
      {genreLabel(genre)}
    </span>
  );
}

export function PrBadge() {
  return (
    <span className="pr-badge" title="収益目的のリンクを含みます">
      PR
    </span>
  );
}

export function formatDate(iso: string) {
  const d = new Date(iso);
  return `${d.getFullYear()}年${d.getMonth() + 1}月${d.getDate()}日`;
}
