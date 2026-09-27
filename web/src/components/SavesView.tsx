import { useState } from "react";
import { api } from "../api/client";
import type { Me, SaveView, Stage } from "../api/types";
import { GenreChip, PrBadge, STAGES, StageBadge, formatDate } from "../domain";

type Props = {
  saves: SaveView[] | null;
  me: Me | null;
  fontScale: number;
  onGoTo: (save: SaveView) => void;
  onChanged: () => void;
  onOpenShopTab: () => void;
  toast: (m: string) => void;
};

/** 保存一覧。街にまだあるか、もうないかを示す。 */
export function SavesView({ saves, me, fontScale, onGoTo, onChanged, onOpenShopTab, toast }: Props) {
  if (saves === null) return <p className="loading page-pad">読み込んでいます…</p>;

  return (
    <div className="page">
      <h1 className="page-title">保存したもの</h1>
      {saves.length === 0 ? (
        <p className="empty">
          まだありません。街でマークをさわって「♡ 保存する」を押すと、ここに残ります。街から消えても、ここからは消えません。
        </p>
      ) : (
        <ul className="card-list">
          {saves.map((s) => (
            <SaveCard key={s.id} save={s} me={me} fontScale={fontScale} onGoTo={onGoTo} onChanged={onChanged} onOpenShopTab={onOpenShopTab} toast={toast} />
          ))}
        </ul>
      )}
    </div>
  );
}

function SaveCard({ save, me, fontScale, onGoTo, onChanged, onOpenShopTab, toast }: Omit<Props, "saves"> & { save: SaveView }) {
  const [stagePick, setStagePick] = useState(false);
  const [busy, setBusy] = useState(false);
  const snap = save.snapshot;
  const image = snap.popPhotoUrl ?? snap.imageUrl;

  const addToShop = async (stage: Stage) => {
    setBusy(true);
    try {
      await api.addShopItem(save.id, stage);
      toast("店に並べました");
      setStagePick(false);
      onChanged();
    } catch (e) {
      toast(e instanceof Error ? e.message : "並べられませんでした。");
    } finally {
      setBusy(false);
    }
  };

  const remove = async () => {
    setBusy(true);
    try {
      await api.unsave(save.id);
      onChanged();
    } catch (e) {
      toast(e instanceof Error ? e.message : "外せませんでした。");
      setBusy(false);
    }
  };

  return (
    <li className={`card ${save.presence === "GONE" ? "is-gone" : ""}`}>
      {image && <img className="card-photo" src={image} alt="" />}
      <div className="card-body">
        <div className="pop-tags">
          <GenreChip genre={snap.genre} />
          <StageBadge stage={snap.stage} />
          {snap.hasPr && <PrBadge />}
        </div>
        <p className="card-text" style={{ fontSize: `${21 * fontScale}px` }}>
          {snap.popText}
        </p>
        <p className="pop-by">
          {snap.popAuthorName}さんのポップ ・ {formatDate(save.savedAt)}に保存
        </p>
        {snap.neighbors.length > 0 && (
          <p className="card-neighbors">そのとき隣にあったもの: {snap.neighbors.map((n) => n.popText).join(" / ")}</p>
        )}
        {save.presence === "IN_CITY" ? (
          <button type="button" className="btn btn-outline" onClick={() => onGoTo(save)}>
            今はここ →
          </button>
        ) : (
          <p className="gone-note">もう街にはありません</p>
        )}
        <div className="card-actions">
          {me?.isRegistered && me.shopId && !save.inMyShop && !stagePick && (
            <button type="button" className="btn btn-quiet" onClick={() => setStagePick(true)}>
              店に並べる
            </button>
          )}
          {me?.isRegistered && me.shopId && save.inMyShop && <span className="in-shop">店に並んでいます</span>}
          {me?.isRegistered && !me.shopId && (
            <button type="button" className="btn btn-quiet" onClick={onOpenShopTab}>
              自分の店を開く
            </button>
          )}
          <button type="button" className="btn btn-quiet" disabled={busy} onClick={() => void remove()}>
            保存から外す
          </button>
        </div>
        {stagePick && (
          <div className="stage-pick">
            <p className="field-label">店ではどう並べますか</p>
            <div className="choice-row">
              {STAGES.map((st) => (
                <button key={st.id} type="button" className="choice" disabled={busy} onClick={() => void addToShop(st.id)}>
                  <span aria-hidden="true">{st.mark}</span>
                  {st.label}
                </button>
              ))}
            </div>
            <button type="button" className="btn btn-quiet" onClick={() => setStagePick(false)}>
              やめる
            </button>
          </div>
        )}
      </div>
    </li>
  );
}
