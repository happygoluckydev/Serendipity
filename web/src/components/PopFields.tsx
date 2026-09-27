import { useRef, useState } from "react";
import { api } from "../api/client";
import type { Stage } from "../api/types";
import { STAGES } from "../domain";

export const POP_MAX = 60;

export function charCount(s: string) {
  // 絵文字などを1文字と数える(サーバーと同じ数え方)。
  if (typeof Intl !== "undefined" && "Segmenter" in Intl) {
    return [...new Intl.Segmenter("ja", { granularity: "grapheme" }).segment(s)].length;
  }
  return [...s].length;
}

export function PopTextField({ value, onChange }: { value: string; onChange: (v: string) => void }) {
  const n = charCount(value.trim());
  return (
    <label className="field">
      <span className="field-label">ポップ(一言)</span>
      <textarea
        className="input pop-input"
        rows={2}
        value={value}
        placeholder="例: 10年使っても飽きない、台所の主役"
        onChange={(e) => onChange(e.target.value)}
      />
      <span className={`counter ${n > POP_MAX ? "over" : ""}`}>
        {n} / {POP_MAX}
      </span>
    </label>
  );
}

export function StagePicker({ value, onChange }: { value: Stage | null; onChange: (s: Stage) => void }) {
  return (
    <fieldset className="field">
      <legend className="field-label">あなたとの関係</legend>
      <div className="choice-row">
        {STAGES.map((s) => (
          <button
            key={s.id}
            type="button"
            className={`choice stage-choice stage-${s.id.toLowerCase()} ${value === s.id ? "is-on" : ""}`}
            aria-pressed={value === s.id}
            onClick={() => onChange(s.id)}
          >
            <span className="choice-mark" aria-hidden="true">
              {s.mark}
            </span>
            {s.label}
          </button>
        ))}
      </div>
    </fieldset>
  );
}

export function PhotoPicker({ value, onChange }: { value: { key: string; url: string } | null; onChange: (v: { key: string; url: string } | null) => void }) {
  const input = useRef<HTMLInputElement>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const pick = async (file: File | undefined) => {
    if (!file) return;
    setBusy(true);
    setError(null);
    try {
      onChange(await api.uploadPhoto(file));
    } catch (e) {
      setError(e instanceof Error ? e.message : "写真を送れませんでした。");
    } finally {
      setBusy(false);
      if (input.current) input.current.value = "";
    }
  };

  return (
    <div className="field">
      <span className="field-label">写真(1枚・なくてもよい)</span>
      {value ? (
        <div className="photo-preview">
          <img src={value.url} alt="選んだ写真" />
          <button type="button" className="btn btn-quiet" onClick={() => onChange(null)}>
            写真を外す
          </button>
        </div>
      ) : (
        <button type="button" className="btn btn-outline" disabled={busy} onClick={() => input.current?.click()}>
          {busy ? "送っています…" : "写真を選ぶ"}
        </button>
      )}
      <input
        ref={input}
        type="file"
        accept="image/jpeg,image/png,image/webp,image/gif"
        hidden
        onChange={(e) => void pick(e.target.files?.[0])}
      />
      {error && <p className="error">{error}</p>}
    </div>
  );
}
