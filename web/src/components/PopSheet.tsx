import { useEffect, useState } from "react";
import { ApiError, api } from "../api/client";
import type { MarkDetail, Me, Stage } from "../api/types";
import { GenreChip, PrBadge, ShapeIcon, StageBadge, formatDate, genreColor, shapeLabel } from "../domain";
import { POP_MAX, PhotoPicker, PopTextField, StagePicker, charCount } from "./PopFields";
import { Sheet } from "./Sheet";

type Props = {
  markId: string;
  me: Me | null;
  fontScale: number;
  /** チュートリアル中。保存を一度だけ促す。 */
  tutorial: boolean;
  onClose: () => void;
  onSaved: (message: string) => void;
  onChanged: () => void;
  onPlaceNext: (markId: string) => void;
  onGoToMark: (markId: string) => void;
  onOpenShop: (shopId: string) => void;
  onNeedRegister: (then: () => void) => void;
  toast: (message: string) => void;
};

type Mode = "view" | "link" | "report" | "addPop" | "takedown";

const PENDING_TEXT: Record<string, string> = {
  AUTO_REVIEW: "確認しています。少し待つと街に並びます。いまはあなたにだけ見えています。",
  UNKNOWN_DOMAIN: "初めてのサイトなので、運営が確認してから街に並びます。いまはあなたにだけ見えています。",
  AI_FLAGGED: "念のため運営が確認しています。いまはあなたにだけ見えています。",
};

const REPORT_REASONS = ["不適切な内容", "成人向けの内容", "危険なサイト・詐欺", "しつこい宣伝", "その他"];

export function PopSheet(props: Props) {
  const { markId, me, fontScale, tutorial, onClose } = props;
  const [mark, setMark] = useState<MarkDetail | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [mode, setMode] = useState<Mode>("view");
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    let alive = true;
    setMark(null);
    setMode("view");
    api
      .mark(markId)
      .then((m) => alive && setMark(m))
      .catch((e) => alive && setError(e instanceof Error ? e.message : "読み込めませんでした。"));
    return () => {
      alive = false;
    };
  }, [markId]);

  const original = mark?.pops.find((p) => p.isOriginal) ?? mark?.pops[0];
  const others = mark?.pops.filter((p) => p !== original) ?? [];

  const toggleSave = async () => {
    if (!mark) return;
    setBusy(true);
    try {
      if (mark.saveId) {
        await api.unsave(mark.saveId);
        setMark({ ...mark, saveId: null });
        props.toast("保存から外しました");
      } else {
        const saved = await api.saveMark(mark.id);
        setMark({ ...mark, saveId: saved.id });
        props.onSaved("保存しました。街から消えても手元に残ります。");
      }
      props.onChanged();
    } catch (e) {
      props.toast(e instanceof Error ? e.message : "保存できませんでした。");
    } finally {
      setBusy(false);
    }
  };

  const openLink = () => {
    if (!mark?.linkUrl) return;
    // 元のページに、この街から来たことや窓を渡さない。
    window.open(mark.linkUrl, "_blank", "noopener,noreferrer");
    setMode("view");
  };

  if (error) {
    return (
      <Sheet title="ポップ" onClose={onClose}>
        <p className="error">{error}</p>
      </Sheet>
    );
  }

  if (!mark || !original) {
    return (
      <Sheet title="ポップ" onClose={onClose} hideTitle>
        <p className="loading">開いています…</p>
      </Sheet>
    );
  }

  if (mode === "link") {
    return (
      <Sheet title="外のサイトへ移動します" onClose={() => setMode("view")}>
        <div className="confirm">
          <p className="confirm-lead">
            ここから先は <strong>{mark.domain}</strong> のページです。
            <br />
            この街の外なので、中身は街の運営では確認できません。
          </p>
          {mark.hasPr && (
            <p className="note">
              <PrBadge /> このリンクには、紹介した人に収益が入る仕組みが含まれています。
            </p>
          )}
          {mark.linkBroken && <p className="note">このリンクは最近開けないことがあります。</p>}
          <div className="btn-col">
            <button type="button" className="btn btn-primary btn-big" onClick={openLink}>
              移動する
            </button>
            <button type="button" className="btn btn-outline btn-big" onClick={() => setMode("view")}>
              やめる
            </button>
          </div>
        </div>
      </Sheet>
    );
  }

  if (mode === "report") {
    return <ReportForm mark={mark} onDone={(msg) => { props.toast(msg); setMode("view"); }} onCancel={() => setMode("view")} onTakedown={() => setMode("takedown")} />;
  }

  if (mode === "takedown") {
    return <TakedownForm mark={mark} onDone={(msg) => { props.toast(msg); setMode("view"); }} onCancel={() => setMode("view")} />;
  }

  if (mode === "addPop") {
    return (
      <AddPopForm
        mark={mark}
        onCancel={() => setMode("view")}
        onDone={(msg) => {
          props.toast(msg);
          setMode("view");
          void api.mark(mark.id).then(setMark);
        }}
      />
    );
  }

  const isShop = mark.shopId !== null;
  const bigText = { fontSize: `${26 * fontScale}px` };

  return (
    <Sheet title={`ポップ: ${original.text}`} onClose={onClose} hideTitle tall>
      <article className="pop" style={{ ["--accent-genre" as string]: genreColor(mark.genre) }}>
        <div className="pop-tags">
          <GenreChip genre={mark.genre} />
          <span className="shape-tag">
            <ShapeIcon shape={mark.shape} size={20} />
            {shapeLabel(mark.shape)}
          </span>
          <StageBadge stage={original.stage} />
          {mark.hasPr && <PrBadge />}
        </div>

        {mark.status === "PENDING" && mark.pendingReason && <p className="note">{PENDING_TEXT[mark.pendingReason]}</p>}

        {original.photoUrl && <img className="pop-photo" src={original.photoUrl} alt="" />}
        <p className="pop-text" style={bigText}>
          {original.text}
        </p>
        <p className="pop-by">
          {original.authorName}さんのポップ ・ {formatDate(original.createdAt)}
        </p>

        <div className="btn-col">
          <button
            type="button"
            className={`btn btn-big ${mark.saveId ? "btn-saved" : "btn-primary"} ${tutorial && !mark.saveId ? "is-beckoning" : ""}`}
            onClick={() => void toggleSave()}
            disabled={busy || mark.status !== "PUBLIC"}
            aria-pressed={mark.saveId !== null}
          >
            {mark.saveId ? "✓ 保存しています" : "♡ 保存する"}
          </button>
          {tutorial && !mark.saveId && <p className="hint">気になったら、ここを押すと手元に残ります</p>}

          {isShop && (
            <button type="button" className="btn btn-outline btn-big" onClick={() => props.onOpenShop(mark.shopId!)}>
              お店に入る
            </button>
          )}
          {mark.hasLink &&
            (mark.linkUrl ? (
              <button type="button" className="btn btn-outline btn-big" onClick={() => setMode("link")}>
                リンク先を見る({mark.domain})
              </button>
            ) : (
              <p className="note">リンクは、おうちの人と一緒に開いてね。</p>
            ))}
        </div>

        {mark.preview && (
          <div className="preview">
            {mark.preview.imageUrl && <img src={mark.preview.imageUrl} alt="" />}
            <div>
              <p className="preview-title">{mark.preview.title}</p>
              {mark.preview.siteName && <p className="preview-site">{mark.preview.siteName}</p>}
            </div>
          </div>
        )}

        {others.length > 0 && (
          <section className="pop-section">
            <h3>ほかの人のポップ</h3>
            <ul className="other-pops">
              {others.map((p) => (
                <li key={p.id} style={{ fontSize: `${17 * fontScale}px` }}>
                  <StageBadge stage={p.stage} /> {p.text}
                  {p.hasPr && <PrBadge />}
                  <span className="pop-by">
                    {" "}
                    — {p.authorName}さん{p.status === "PENDING" ? "(確認中)" : ""}
                  </span>
                </li>
              ))}
            </ul>
          </section>
        )}

        {mark.neighbors.length > 0 && (
          <section className="pop-section">
            <h3>隣にあるもの</h3>
            <div className="neighbor-row">
              {mark.neighbors.map((n) => (
                <button key={n.id} type="button" className="neighbor" onClick={() => props.onGoToMark(n.id)}>
                  <ShapeIcon shape={n.shape} size={20} color={genreColor(n.genre)} />
                  <span>{n.popText}</span>
                </button>
              ))}
            </div>
          </section>
        )}

        <section className="pop-section deeper">
          <h3>自分でも</h3>
          <div className="btn-col">
            {mark.status === "PUBLIC" && (
              <button
                type="button"
                className="btn btn-outline"
                onClick={() => (me?.isRegistered ? props.onPlaceNext(mark.id) : props.onNeedRegister(() => props.onPlaceNext(mark.id)))}
              >
                ＋ この隣に置く
              </button>
            )}
            {mark.canAddPop && (
              <button type="button" className="btn btn-outline" onClick={() => setMode("addPop")}>
                ✎ 私もポップを付ける
              </button>
            )}
            {!me?.isRegistered && mark.status === "PUBLIC" && !isShop && (
              <button type="button" className="btn btn-outline" onClick={() => props.onNeedRegister(() => setMode("addPop"))}>
                ✎ 私もポップを付ける
              </button>
            )}
          </div>
          {!mark.isMine && (
            <button type="button" className="link-btn" onClick={() => setMode("report")}>
              このマークを通報する
            </button>
          )}
        </section>
      </article>
    </Sheet>
  );
}

function ReportForm({ mark, onDone, onCancel, onTakedown }: { mark: MarkDetail; onDone: (m: string) => void; onCancel: () => void; onTakedown: () => void }) {
  const [reason, setReason] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const send = async () => {
    if (!reason) return;
    setBusy(true);
    try {
      onDone((await api.report({ markId: mark.id }, reason)).message);
    } catch (e) {
      setError(e instanceof Error ? e.message : "送れませんでした。");
      setBusy(false);
    }
  };
  return (
    <Sheet title="通報する" onClose={onCancel}>
      <fieldset className="field">
        <legend className="field-label">どんな問題がありますか</legend>
        <div className="btn-col">
          {REPORT_REASONS.map((r) => (
            <button key={r} type="button" className={`choice ${reason === r ? "is-on" : ""}`} aria-pressed={reason === r} onClick={() => setReason(r)}>
              {r}
            </button>
          ))}
        </div>
      </fieldset>
      {error && <p className="error">{error}</p>}
      <div className="btn-col">
        <button type="button" className="btn btn-primary btn-big" disabled={!reason || busy} onClick={() => void send()}>
          送る
        </button>
        <button type="button" className="link-btn" onClick={onTakedown}>
          記事を書いた本人として削除を依頼する
        </button>
      </div>
    </Sheet>
  );
}

function TakedownForm({ mark, onDone, onCancel }: { mark: MarkDetail; onDone: (m: string) => void; onCancel: () => void }) {
  const [contact, setContact] = useState("");
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const send = async () => {
    setBusy(true);
    try {
      onDone((await api.requestTakedown(mark.id, contact, reason)).message);
    } catch (e) {
      setError(e instanceof Error ? e.message : "送れませんでした。");
      setBusy(false);
    }
  };
  return (
    <Sheet title="削除の依頼" onClose={onCancel}>
      <p className="note">リンク先の記事やお店の持ち主の方からの依頼を受け付けます。運営が確認して、連絡先にお返事します。</p>
      <label className="field">
        <span className="field-label">連絡先(メールアドレスなど)</span>
        <input className="input" value={contact} onChange={(e) => setContact(e.target.value)} maxLength={200} />
      </label>
      <label className="field">
        <span className="field-label">理由</span>
        <textarea className="input" rows={3} value={reason} onChange={(e) => setReason(e.target.value)} maxLength={500} />
      </label>
      {error && <p className="error">{error}</p>}
      <button type="button" className="btn btn-primary btn-big" disabled={busy || !contact.trim() || !reason.trim()} onClick={() => void send()}>
        依頼を送る
      </button>
    </Sheet>
  );
}

function AddPopForm({ mark, onDone, onCancel }: { mark: MarkDetail; onDone: (m: string) => void; onCancel: () => void }) {
  const [text, setText] = useState("");
  const [stage, setStage] = useState<Stage | null>(null);
  const [photo, setPhoto] = useState<{ key: string; url: string } | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const n = charCount(text.trim());
  const send = async () => {
    if (!stage) return;
    setBusy(true);
    setError(null);
    try {
      await api.addPop({ markId: mark.id, text, stage, photoKey: photo?.key ?? null });
      onDone("ポップを付けました。確認が済むと皆に見えます。");
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "付けられませんでした。");
      setBusy(false);
    }
  };
  return (
    <Sheet title="私もポップを付ける" onClose={onCancel}>
      <PopTextField value={text} onChange={setText} />
      <StagePicker value={stage} onChange={setStage} />
      <PhotoPicker value={photo} onChange={setPhoto} />
      {error && <p className="error">{error}</p>}
      <button type="button" className="btn btn-primary btn-big" disabled={busy || !stage || n === 0 || n > POP_MAX} onClick={() => void send()}>
        付ける
      </button>
    </Sheet>
  );
}
