import { useCallback, useEffect, useState } from "react";

/** 端末ごとの見やすさの設定。保存できない環境でも、この画面の間は効く。 */
export type FontSize = "normal" | "large" | "xlarge";
export const FONT_SCALE: Record<FontSize, number> = { normal: 1, large: 1.2, xlarge: 1.45 };

function read<T>(key: string, fallback: T): T {
  try {
    const raw = localStorage.getItem(key);
    return raw === null ? fallback : (JSON.parse(raw) as T);
  } catch {
    return fallback;
  }
}

function write(key: string, value: unknown) {
  try {
    localStorage.setItem(key, JSON.stringify(value));
  } catch {
    // 保存できなくても動く。
  }
}

export function useStoredState<T>(key: string, fallback: T): [T, (v: T) => void] {
  const [value, setValue] = useState<T>(() => read(key, fallback));
  const set = useCallback(
    (v: T) => {
      setValue(v);
      write(key, v);
    },
    [key],
  );
  return [value, set];
}

export function useToast(): [string | null, (message: string) => void] {
  const [message, setMessage] = useState<string | null>(null);
  useEffect(() => {
    if (!message) return;
    const t = setTimeout(() => setMessage(null), 3800);
    return () => clearTimeout(t);
  }, [message]);
  return [message, setMessage];
}
