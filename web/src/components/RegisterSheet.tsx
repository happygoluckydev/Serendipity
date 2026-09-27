import { useState } from "react";
import { api } from "../api/client";
import type { Me } from "../api/types";
import { Sheet } from "./Sheet";

/** 置く・店を開くときだけ名前を登録する。保存したものはそのまま引き継ぐ。 */
export function RegisterSheet({ onDone, onCancel }: { onDone: (me: Me) => void; onCancel: () => void }) {
  const [name, setName] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async () => {
    setBusy(true);
    setError(null);
    try {
      onDone(await api.register(name));
    } catch (e) {
      setError(e instanceof Error ? e.message : "登録できませんでした。");
      setBusy(false);
    }
  };

  return (
    <Sheet title="名前を決める" onClose={onCancel}>
      <p className="note">置いたものやポップには、この名前が出ます。これまでに保存したものは、そのまま残ります。</p>
      <label className="field">
        <span className="field-label">名前(20文字まで)</span>
        <input className="input" value={name} maxLength={40} autoComplete="nickname" onChange={(e) => setName(e.target.value)} />
      </label>
      {error && <p className="error">{error}</p>}
      <button type="button" className="btn btn-primary btn-big" disabled={busy || !name.trim()} onClick={() => void submit()}>
        決める
      </button>
    </Sheet>
  );
}
