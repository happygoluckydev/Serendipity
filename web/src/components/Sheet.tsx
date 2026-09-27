import { useEffect, useRef, type ReactNode } from "react";

type Props = {
  title: string;
  onClose: () => void;
  children: ReactNode;
  /** 見出しを画面に出さず、読み上げだけにする。 */
  hideTitle?: boolean;
  tall?: boolean;
};

/** 画面の下から出る一枚。閉じるボタンは大きく、Esc でも閉じる。 */
export function Sheet({ title, onClose, children, hideTitle, tall }: Props) {
  const panel = useRef<HTMLDivElement>(null);
  // 指で開いた直後、ブラウザが後から送る click が背景に当たって閉じてしまうのを防ぐ。
  const openedAt = useRef(performance.now());

  useEffect(() => {
    const prev = document.activeElement as HTMLElement | null;
    panel.current?.focus();
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    window.addEventListener("keydown", onKey);
    return () => {
      window.removeEventListener("keydown", onKey);
      prev?.focus?.();
    };
  }, [onClose]);

  return (
    <div className="sheet-layer">
      <div
        className="sheet-backdrop"
        onClick={() => {
          if (performance.now() - openedAt.current > 450) onClose();
        }}
        aria-hidden="true"
      />
      <div
        ref={panel}
        className={`sheet ${tall ? "sheet-tall" : ""}`}
        role="dialog"
        aria-modal="true"
        aria-label={title}
        tabIndex={-1}
      >
        <div className="sheet-head">
          {hideTitle ? <span /> : <h2 className="sheet-title">{title}</h2>}
          <button type="button" className="icon-btn" onClick={onClose} aria-label="閉じる">
            ✕
          </button>
        </div>
        <div className="sheet-body">{children}</div>
      </div>
    </div>
  );
}
