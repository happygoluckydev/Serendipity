import { useEffect, useState } from "react";
import { api } from "../api/client";
import type { Me, Shop, ShopItem, Stage } from "../api/types";
import { GenreChip, PrBadge, STAGES, StageBadge } from "../domain";
import { Sheet } from "./Sheet";

type MyShopProps = {
  me: Me | null;
  shop: Shop | null | undefined;
  fontScale: number;
  onChanged: (shop: Shop | null) => void;
  onNeedRegister: () => void;
  onGoToMark: (markId: string) => void;
  onOpenSaves: () => void;
  toast: (m: string) => void;
};

/** 自分の店。保存したものから選んで並べ、公開すると街にお店のマークが立つ。 */
export function MyShopView({ me, shop, fontScale, onChanged, onNeedRegister, onGoToMark, onOpenSaves, toast }: MyShopProps) {
  const [name, setName] = useState("");
  const [busy, setBusy] = useState(false);

  if (!me?.isRegistered) {
    return (
      <div className="page">
        <h1 className="page-title">自分の店</h1>
        <p className="empty">保存したものから選んで、自分の店として街に並べられます。まず名前を決めてください。</p>
        <button type="button" className="btn btn-primary btn-big" onClick={onNeedRegister}>
          名前を決める
        </button>
      </div>
    );
  }
  if (shop === undefined) return <p className="loading page-pad">読み込んでいます…</p>;

  const run = async (f: () => Promise<Shop>, message?: string) => {
    setBusy(true);
    try {
      onChanged(await f());
      if (message) toast(message);
    } catch (e) {
      toast(e instanceof Error ? e.message : "できませんでした。");
    } finally {
      setBusy(false);
    }
  };

  if (shop === null) {
    return (
      <div className="page">
        <h1 className="page-title">自分の店を開く</h1>
        <p className="empty">保存は自分だけのもの。店に並べたものだけが、ほかの人に見えます。</p>
        <label className="field">
          <span className="field-label">店の名前(20文字まで)</span>
          <input className="input" value={name} maxLength={40} onChange={(e) => setName(e.target.value)} placeholder="例: 気になるもの屋" />
        </label>
        <button type="button" className="btn btn-primary btn-big" disabled={busy || !name.trim()} onClick={() => void run(() => api.openShop(name), "店を開きました")}>
          店を開く
        </button>
      </div>
    );
  }

  return (
    <div className="page">
      <h1 className="page-title">{shop.name}</h1>
      <p className="shop-state">
        {shop.isPublished ? (shop.markInCity ? "街に出ています" : "公開の準備中です(確認が済むと街に並びます)") : "まだ非公開です"}
      </p>
      <div className="btn-col">
        {shop.isPublished ? (
          <>
            {shop.markId && shop.markInCity && (
              <button type="button" className="btn btn-outline" onClick={() => onGoToMark(shop.markId!)}>
                街で店を見る →
              </button>
            )}
            <button type="button" className="btn btn-outline" disabled={busy} onClick={() => void run(() => api.setShopPublished(false, null), "店を閉じました")}>
              店を閉じる
            </button>
          </>
        ) : (
          <button
            type="button"
            className="btn btn-primary btn-big"
            disabled={busy || shop.items.length === 0}
            onClick={() => void run(() => api.setShopPublished(true, null), "街に店を出しました")}
          >
            街に店を出す
          </button>
        )}
      </div>

      <h2 className="section-title">並んでいるもの({shop.items.length})</h2>
      {shop.items.length === 0 ? (
        <p className="empty">
          まだありません。
          <button type="button" className="link-btn" onClick={onOpenSaves}>
            保存したものから並べる
          </button>
        </p>
      ) : (
        <ul className="card-list">
          {shop.items.map((item) => (
            <li key={item.id} className="card">
              <ItemBody item={item} fontScale={fontScale} onGoToMark={onGoToMark} />
              <div className="card-body">
                <div className="choice-row">
                  {STAGES.map((st) => (
                    <button
                      key={st.id}
                      type="button"
                      className={`choice ${item.stage === st.id ? "is-on" : ""}`}
                      aria-pressed={item.stage === st.id}
                      disabled={busy}
                      onClick={() => void run(() => api.updateShopItem(item.id, st.id as Stage))}
                    >
                      <span aria-hidden="true">{st.mark}</span>
                      {st.label}
                    </button>
                  ))}
                </div>
                <button type="button" className="btn btn-quiet" disabled={busy} onClick={() => void run(() => api.removeShopItem(item.id))}>
                  店から下げる
                </button>
              </div>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function ItemBody({ item, fontScale, onGoToMark }: { item: ShopItem; fontScale: number; onGoToMark: (id: string) => void }) {
  const s = item.snapshot;
  const image = s.popPhotoUrl ?? s.imageUrl;
  return (
    <>
      {image && <img className="card-photo" src={image} alt="" />}
      <div className="card-body">
        <div className="pop-tags">
          <StageBadge stage={item.stage} />
          <GenreChip genre={s.genre} />
          {s.hasPr && <PrBadge />}
        </div>
        <p className="card-text" style={{ fontSize: `${21 * fontScale}px` }}>
          {s.popText}
        </p>
        {/* 他人の発見の寄せ集めで、元の人が埋もれないようにする。 */}
        <p className="pop-by">元のポップ: {item.originalAuthorName}さん</p>
        {item.originalInCity ? (
          <button type="button" className="btn btn-quiet" onClick={() => onGoToMark(item.originalMarkId)}>
            元のマークへ →
          </button>
        ) : (
          <p className="gone-note">元のマークはもう街にはありません</p>
        )}
      </div>
    </>
  );
}

/** ほかの人の店。街のお店マークから入る。 */
export function ShopSheet({ shopId, fontScale, onClose, onGoToMark }: { shopId: string; fontScale: number; onClose: () => void; onGoToMark: (id: string) => void }) {
  const [shop, setShop] = useState<Shop | null>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    api.shop(shopId).then(setShop, (e) => setError(e instanceof Error ? e.message : "開けませんでした。"));
  }, [shopId]);

  return (
    <Sheet title={shop ? shop.name : "お店"} onClose={onClose} tall>
      {error && <p className="error">{error}</p>}
      {!shop && !error && <p className="loading">開いています…</p>}
      {shop && (
        <>
          <p className="pop-by">{shop.ownerName}さんの店</p>
          <ul className="card-list">
            {shop.items.map((item) => (
              <li key={item.id} className="card">
                <ItemBody item={item} fontScale={fontScale} onGoToMark={onGoToMark} />
              </li>
            ))}
          </ul>
        </>
      )}
    </Sheet>
  );
}
