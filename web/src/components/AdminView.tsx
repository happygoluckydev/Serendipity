import { useCallback, useEffect, useState } from "react";
import { adminKey, api, setAdminKey } from "../api/client";
import type { AdminQueue, DomainRuleKind } from "../api/types";

const REASON: Record<string, string> = {
  UNKNOWN_DOMAIN: "許可リスト外",
  AI_FLAGGED: "AIが確認を要求",
  AUTO_REVIEW: "自動審査待ち",
};

/** 運営の確認画面。確認待ちのマーク・ポップ、通報、ドメインのルール。 */
export function AdminView({ onExit }: { onExit: () => void }) {
  const [key, setKey] = useState(adminKey() ?? "");
  const [queue, setQueue] = useState<AdminQueue | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [domain, setDomain] = useState("");
  const [kind, setKind] = useState<DomainRuleKind>("ALLOW");

  const load = useCallback(async () => {
    setError(null);
    try {
      setQueue(await api.adminQueue());
    } catch (e) {
      setQueue(null);
      setError(e instanceof Error ? e.message : "読み込めませんでした。");
    }
  }, []);

  useEffect(() => {
    if (adminKey()) void load();
  }, [load]);

  const act = async (f: () => Promise<unknown>) => {
    try {
      await f();
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : "できませんでした。");
    }
  };

  return (
    <div className="page admin">
      <div className="admin-head">
        <h1 className="page-title">運営</h1>
        <button type="button" className="btn btn-quiet" onClick={onExit}>
          街へ戻る
        </button>
      </div>
      <div className="admin-key">
        <input className="input" type="password" placeholder="運営の鍵" value={key} onChange={(e) => setKey(e.target.value)} />
        <button
          type="button"
          className="btn btn-outline"
          onClick={() => {
            setAdminKey(key || null);
            void load();
          }}
        >
          開く
        </button>
      </div>
      {error && <p className="error">{error}</p>}
      {queue && (
        <>
          <h2 className="section-title">確認待ち・通報のあるマーク({queue.marks.length})</h2>
          <ul className="admin-list">
            {queue.marks.map((m) => (
              <li key={m.id}>
                <p>
                  <strong>{m.popText}</strong> — {m.ownerName}
                </p>
                <p className="field-help">
                  {m.status} {m.pendingReason ? `・${REASON[m.pendingReason] ?? m.pendingReason}` : ""} {m.reportWeight > 0 ? `・通報の重み ${m.reportWeight}` : ""}
                </p>
                {m.reviewNote && <p className="field-help">{m.reviewNote}</p>}
                {m.linkUrl && (
                  <p className="field-help">
                    リンク: <code>{m.linkUrl}</code>
                  </p>
                )}
                <div className="card-actions">
                  <button type="button" className="btn btn-outline" onClick={() => void act(() => api.adminApproveMark(m.id))}>
                    公開する
                  </button>
                  <button type="button" className="btn btn-outline" onClick={() => void act(() => api.adminHideMark(m.id, null))}>
                    隠す
                  </button>
                  {m.domain && (
                    <button type="button" className="btn btn-quiet" onClick={() => void act(() => api.adminSetDomainRule(m.domain!, "ALLOW", "確認して許可"))}>
                      {m.domain} を許可リストへ
                    </button>
                  )}
                  <button type="button" className="btn btn-quiet" onClick={() => void act(() => api.adminBlockUser(m.ownerId))}>
                    投稿者をブロック
                  </button>
                </div>
              </li>
            ))}
          </ul>

          <h2 className="section-title">確認待ちのポップ({queue.pops.length})</h2>
          <ul className="admin-list">
            {queue.pops.map((p) => (
              <li key={p.id}>
                <p>
                  <strong>{p.text}</strong> — {p.authorName}
                </p>
                {p.reviewNote && <p className="field-help">{p.reviewNote}</p>}
                <div className="card-actions">
                  <button type="button" className="btn btn-outline" onClick={() => void act(() => api.adminApprovePop(p.id))}>
                    公開する
                  </button>
                  <button type="button" className="btn btn-outline" onClick={() => void act(() => api.adminHidePop(p.id))}>
                    隠す
                  </button>
                </div>
              </li>
            ))}
          </ul>

          <h2 className="section-title">通報・削除依頼({queue.reports.length})</h2>
          <ul className="admin-list">
            {queue.reports.map((r) => (
              <li key={r.id}>
                <p>
                  {r.kind === "TAKEDOWN" ? "削除依頼" : "通報"}: {r.reason}
                </p>
                <p className="field-help">
                  {r.contact ? `連絡先 ${r.contact}・` : ""}重み {r.weight}・対象 {r.markId ?? r.popId}
                </p>
                <div className="card-actions">
                  {r.markId && (
                    <button type="button" className="btn btn-outline" onClick={() => void act(() => api.adminHideMark(r.markId!, r.reason))}>
                      マークを隠す
                    </button>
                  )}
                  <button type="button" className="btn btn-quiet" onClick={() => void act(() => api.adminResolveReport(r.id, false))}>
                    対応済み
                  </button>
                  <button type="button" className="btn btn-quiet" onClick={() => void act(() => api.adminResolveReport(r.id, true))}>
                    問題なし
                  </button>
                </div>
              </li>
            ))}
          </ul>

          <h2 className="section-title">ドメインのルール</h2>
          <div className="admin-key">
            <input className="input" placeholder="example.com" value={domain} onChange={(e) => setDomain(e.target.value)} />
            <select className="input" value={kind} onChange={(e) => setKind(e.target.value as DomainRuleKind)}>
              <option value="ALLOW">許可</option>
              <option value="REVIEW">確認待ち</option>
              <option value="DENY">拒否</option>
            </select>
            <button type="button" className="btn btn-outline" disabled={!domain.trim()} onClick={() => void act(() => api.adminSetDomainRule(domain, kind, null))}>
              設定
            </button>
          </div>
          <ul className="domain-list">
            {queue.domains.map((d) => (
              <li key={d.domain}>
                <code>{d.domain}</code> {d.kind === "ALLOW" ? "許可" : d.kind === "DENY" ? "拒否" : "確認待ち"} {d.note ? `(${d.note})` : ""}
              </li>
            ))}
          </ul>
        </>
      )}
    </div>
  );
}
