import { useState } from "react";
import { ApiError, api } from "../api/client";
import type { CityMark, Genre, Shape, Stage } from "../api/types";
import { GENRES, SHAPES, ShapeIcon, genreColor, genreTint } from "../domain";
import { POP_MAX, PhotoPicker, PopTextField, StagePicker, charCount } from "./PopFields";
import { Sheet } from "./Sheet";

type Props = {
  neighbor: CityMark | null;
  onCancel: () => void;
  onChangeNeighbor: () => void;
  onPlaced: (markId: string, message: string) => void;
};

/**
 * 置く。座標を選ぶのではなく「このマークの隣に置く」。
 * 何の隣を選ぶかが置く人の感性の表明になり、街区が自然に育つ。
 */
export function PlaceSheet({ neighbor, onCancel, onChangeNeighbor, onPlaced }: Props) {
  const [url, setUrl] = useState("");
  const [shape, setShape] = useState<Shape | null>(null);
  const [genre, setGenre] = useState<Genre | null>(neighbor?.genre ?? null);
  const [stage, setStage] = useState<Stage | null>(null);
  const [text, setText] = useState("");
  const [photo, setPhoto] = useState<{ key: string; url: string } | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const n = charCount(text.trim());
  const ready = shape && genre && stage && n > 0 && n <= POP_MAX;

  const submit = async () => {
    if (!shape || !genre || !stage) return;
    setBusy(true);
    setError(null);
    try {
      const result = await api.placeMark({
        nextToMarkId: neighbor?.id ?? null,
        url: url.trim() || null,
        shape,
        genre,
        stage,
        text,
        photoKey: photo?.key ?? null,
      });
      onPlaced(result.markId, result.message);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "置けませんでした。");
      setBusy(false);
    }
  };

  return (
    <Sheet title="街に置く" onClose={onCancel} tall>
      <div className="field">
        <span className="field-label">隣にするもの</span>
        {neighbor ? (
          <div className="neighbor-pick">
            <span className="neighbor" style={{ borderColor: genreColor(neighbor.genre) }}>
              <ShapeIcon shape={neighbor.shape} size={20} color={genreColor(neighbor.genre)} />
              <span>{neighbor.popText}</span>
            </span>
            <button type="button" className="btn btn-quiet" onClick={onChangeNeighbor}>
              選び直す
            </button>
          </div>
        ) : (
          <p className="note">街の最初の一つとして、まん中に置きます。</p>
        )}
      </div>

      <label className="field">
        <span className="field-label">リンク(なくてもよい)</span>
        <input
          className="input"
          type="url"
          inputMode="url"
          autoComplete="off"
          placeholder="https://"
          value={url}
          onChange={(e) => setUrl(e.target.value)}
        />
        <span className="field-help">初めて見るサイトは、運営が確認してから街に並びます。</span>
      </label>

      <fieldset className="field">
        <legend className="field-label">形</legend>
        <div className="choice-grid">
          {SHAPES.map((s) => (
            <button key={s.id} type="button" className={`choice ${shape === s.id ? "is-on" : ""}`} aria-pressed={shape === s.id} onClick={() => setShape(s.id)}>
              <ShapeIcon shape={s.id} size={26} />
              {s.label}
            </button>
          ))}
        </div>
      </fieldset>

      <fieldset className="field">
        <legend className="field-label">ジャンル(色)</legend>
        <div className="choice-grid">
          {GENRES.map((g) => (
            <button
              key={g.id}
              type="button"
              className={`choice genre-choice ${genre === g.id ? "is-on" : ""}`}
              aria-pressed={genre === g.id}
              style={{ ["--chip" as string]: genreColor(g.id), ["--chip-tint" as string]: genreTint(g.id) }}
              onClick={() => setGenre(g.id)}
            >
              <span className="genre-dot" aria-hidden="true" />
              {g.label}
            </button>
          ))}
        </div>
      </fieldset>

      <StagePicker value={stage} onChange={setStage} />
      <PopTextField value={text} onChange={setText} />
      <PhotoPicker value={photo} onChange={setPhoto} />

      <p className="field-help">成人向けのもの、危ないもの、人を傷つけるものは置けません。</p>
      {error && <p className="error">{error}</p>}
      <button type="button" className="btn btn-primary btn-big" disabled={!ready || busy} onClick={() => void submit()}>
        {busy ? "置いています…" : "置く"}
      </button>
    </Sheet>
  );
}
