import type { Me } from "../api/types";
import type { FontSize } from "../state";

type Props = {
  me: Me | null;
  fontSize: FontSize;
  onFontSize: (f: FontSize) => void;
  onKidsMode: (on: boolean) => void;
  onReplayTutorial: () => void;
  onNeedRegister: () => void;
};

const SIZES: { id: FontSize; label: string }[] = [
  { id: "normal", label: "ふつう" },
  { id: "large", label: "大きく" },
  { id: "xlarge", label: "もっと大きく" },
];

export function SettingsView({ me, fontSize, onFontSize, onKidsMode, onReplayTutorial, onNeedRegister }: Props) {
  return (
    <div className="page">
      <h1 className="page-title">せってい</h1>

      <section className="settings-block">
        <h2 className="section-title">ポップの文字の大きさ</h2>
        <div className="choice-row">
          {SIZES.map((s) => (
            <button key={s.id} type="button" className={`choice ${fontSize === s.id ? "is-on" : ""}`} aria-pressed={fontSize === s.id} onClick={() => onFontSize(s.id)}>
              {s.label}
            </button>
          ))}
        </div>
      </section>

      <section className="settings-block">
        <h2 className="section-title">子ども向け</h2>
        <p className="field-help">街を眺めて保存はできますが、外のサイトへのリンクは開けなくなります。</p>
        <button
          type="button"
          role="switch"
          aria-checked={me?.isKidsMode ?? false}
          className={`switch ${me?.isKidsMode ? "is-on" : ""}`}
          onClick={() => onKidsMode(!(me?.isKidsMode ?? false))}
        >
          <span className="switch-knob" aria-hidden="true" />
          {me?.isKidsMode ? "子ども向けにしています" : "子ども向けにする"}
        </button>
      </section>

      <section className="settings-block">
        <h2 className="section-title">名前</h2>
        {me?.isRegistered ? (
          <p>{me.displayName}さん</p>
        ) : (
          <>
            <p className="field-help">眺める・保存するだけなら、名前はいりません。置く・店を開くときに決めます。</p>
            <button type="button" className="btn btn-outline" onClick={onNeedRegister}>
              名前を決める
            </button>
          </>
        )}
      </section>

      <section className="settings-block">
        <button type="button" className="btn btn-outline btn-big" onClick={onReplayTutorial}>
          使い方をもう一度
        </button>
      </section>

      <section className="settings-block rules">
        <h2 className="section-title">この街の決まり</h2>
        <ul>
          <li>ここに並ぶのは、人が「良かった」「気になる」と思ったものです。売上のためのおすすめは出しません。</li>
          <li>成人向けのものは置けません。</li>
          <li>紹介した人に収益が入るリンクには「PR」と出ます。</li>
          <li>反応のないマークは少しずつ薄れ、やがて街から消えます。保存したものは手元に残ります。</li>
          <li>リンク先の記事やお店の持ち主の方は、マークの「通報する」から削除を依頼できます。</li>
        </ul>
      </section>
    </div>
  );
}
