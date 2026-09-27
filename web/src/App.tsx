import { useCallback, useEffect, useMemo, useState } from "react";
import { api } from "./api/client";
import type { CityMark, CityView, Me, SaveView, Shop } from "./api/types";
import { AdminView } from "./components/AdminView";
import { CityMap, type FlyTarget } from "./components/CityMap";
import { PlaceSheet } from "./components/PlaceSheet";
import { PopSheet } from "./components/PopSheet";
import { RegisterSheet } from "./components/RegisterSheet";
import { SavesView } from "./components/SavesView";
import { SettingsView } from "./components/SettingsView";
import { MyShopView, ShopSheet } from "./components/ShopViews";
import { FONT_SCALE, useStoredState, useToast, type FontSize } from "./state";

type Tab = "city" | "saves" | "shop" | "settings";
type PlaceState = { step: "pick" | "form"; neighbor: CityMark | null } | null;

const TABS: { id: Tab; label: string; icon: string }[] = [
  { id: "city", label: "街", icon: "◎" },
  { id: "saves", label: "保存", icon: "♡" },
  { id: "shop", label: "自分の店", icon: "⌂" },
  { id: "settings", label: "せってい", icon: "⚙" },
];

function isAdminRoute() {
  return typeof location !== "undefined" && location.hash === "#admin";
}

export function App() {
  const [admin, setAdmin] = useState(isAdminRoute);
  const [tab, setTab] = useState<Tab>("city");
  const [me, setMe] = useState<Me | null>(null);
  const [city, setCity] = useState<CityView | null>(null);
  const [cityError, setCityError] = useState<string | null>(null);
  const [saves, setSaves] = useState<SaveView[] | null>(null);
  const [myShop, setMyShop] = useState<Shop | null | undefined>(undefined);
  const [openMarkId, setOpenMarkId] = useState<string | null>(null);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [shopSheetId, setShopSheetId] = useState<string | null>(null);
  const [place, setPlace] = useState<PlaceState>(null);
  const [registerThen, setRegisterThen] = useState<{ then: () => void } | null>(null);
  const [flyTo, setFlyTo] = useState<FlyTarget | null>(null);
  const [fontSize, setFontSize] = useStoredState<FontSize>("machiaruki.fontSize", "normal");
  const [tutorialDone, setTutorialDone] = useStoredState<boolean>("machiaruki.tutorialDone", false);
  const [toast, showToast] = useToast();
  const fontScale = FONT_SCALE[fontSize];

  useEffect(() => {
    const onHash = () => setAdmin(isAdminRoute());
    window.addEventListener("hashchange", onHash);
    return () => window.removeEventListener("hashchange", onHash);
  }, []);

  const loadCity = useCallback(async () => {
    try {
      setCity(await api.city());
      setCityError(null);
    } catch (e) {
      setCityError(e instanceof Error ? e.message : "街を読み込めませんでした。");
    }
  }, []);
  const loadMe = useCallback(() => api.me().then(setMe).catch(() => undefined), []);
  const loadSaves = useCallback(() => api.saves().then(setSaves).catch(() => undefined), []);
  const loadShop = useCallback(() => api.myShop().then(setMyShop).catch(() => undefined), []);

  useEffect(() => {
    void loadCity();
    void loadMe();
  }, [loadCity, loadMe]);

  // 街は人の関心に応じて変わり続ける。眺めている間もときどき読み直す。
  useEffect(() => {
    if (tab !== "city" || openMarkId || place) return;
    const t = setInterval(() => void loadCity(), 60_000);
    return () => clearInterval(t);
  }, [tab, openMarkId, place, loadCity]);

  useEffect(() => {
    if (tab === "saves") void loadSaves();
    if (tab === "shop" && me?.isRegistered) void loadShop();
  }, [tab, me?.isRegistered, loadSaves, loadShop]);

  const marks = city?.marks ?? [];
  const markById = useMemo(() => new Map(marks.map((m) => [m.id, m])), [marks]);

  // チュートリアル: 街の中の一つのマークだけがゆっくり光る。触るとポップが開き、保存を一度だけ促す。
  const tutorialMarkId = useMemo(() => {
    if (tutorialDone || marks.length === 0) return null;
    const pub = marks.filter((m) => m.status === "PUBLIC" && !m.isShop);
    return [...pub].sort((a, b) => b.weight - a.weight)[0]?.id ?? null;
  }, [tutorialDone, marks]);
  const tutorialActive = tutorialMarkId !== null;

  const requireRegistered = (then: () => void) => {
    if (me?.isRegistered) then();
    else setRegisterThen({ then });
  };

  const goToMark = (markId: string, open = false) => {
    const m = markById.get(markId);
    setTab("city");
    setOpenMarkId(null);
    setShopSheetId(null);
    if (!m) {
      showToast("このマークは、いまは街に見当たりません");
      return;
    }
    setSelectedId(m.id);
    setFlyTo({ x: m.x, y: m.y, seq: Date.now() });
    if (open) setTimeout(() => setOpenMarkId(m.id), 650);
  };

  const onTapMark = (m: CityMark) => {
    if (place?.step === "pick") {
      setPlace({ step: "form", neighbor: m });
      return;
    }
    setSelectedId(m.id);
    setOpenMarkId(m.id);
    if (m.status === "PUBLIC") void api.recordEngagements([m.id], "TAP").catch(() => undefined);
  };

  const onViewed = useCallback((ids: string[]) => {
    for (let i = 0; i < ids.length; i += 200) {
      void api.recordEngagements(ids.slice(i, i + 200), "VIEW").catch(() => undefined);
    }
  }, []);

  const startPlace = (neighborId: string | null) =>
    requireRegistered(() => {
      setOpenMarkId(null);
      setTab("city");
      const neighbor = neighborId ? markById.get(neighborId) ?? null : null;
      const cityHasPublic = marks.some((m) => m.status === "PUBLIC");
      setPlace(neighbor || !cityHasPublic ? { step: "form", neighbor } : { step: "pick", neighbor: null });
    });

  if (admin) {
    return (
      <AdminView
        onExit={() => {
          history.replaceState(null, "", location.pathname);
          setAdmin(false);
        }}
      />
    );
  }

  return (
    <div className="app" style={{ ["--font-scale" as string]: fontScale }}>
      <main className="main">
        {tab === "city" && (
          <>
            {city ? (
              <CityMap
                marks={marks}
                baseRadius={city.baseRadius}
                fontScale={fontScale}
                selectedId={place?.neighbor?.id ?? selectedId}
                highlightId={tutorialMarkId}
                pickMode={place?.step === "pick"}
                flyTo={flyTo}
                onTapMark={onTapMark}
                onViewed={onViewed}
              />
            ) : (
              <div className="city-loading">{cityError ?? "街を開いています…"}</div>
            )}
            {city && marks.length === 0 && !place && (
              <div className="banner">
                まだ何もない街です。
                <button type="button" className="btn btn-primary" onClick={() => startPlace(null)}>
                  最初の一つを置く
                </button>
              </div>
            )}
            {tutorialActive && !openMarkId && (
              <div className="tutorial-hint" aria-live="polite">
                <span aria-hidden="true">👆</span> 光っているところを、さわってみて
              </div>
            )}
            {place?.step === "pick" && (
              <div className="banner banner-top" role="status">
                隣にしたいマークをさわってください
                <button type="button" className="btn btn-quiet" onClick={() => setPlace(null)}>
                  やめる
                </button>
              </div>
            )}
            {!place && !tutorialActive && marks.length > 0 && (
              <button type="button" className="fab" onClick={() => startPlace(null)}>
                ＋ 置く
              </button>
            )}
          </>
        )}
        {tab === "saves" && (
          <SavesView
            saves={saves}
            me={me}
            fontScale={fontScale}
            onGoTo={(s) => goToMark(s.snapshot.markId)}
            onChanged={() => {
              void loadSaves();
              void loadMe();
              void loadShop();
            }}
            onOpenShopTab={() => setTab("shop")}
            toast={showToast}
          />
        )}
        {tab === "shop" && (
          <MyShopView
            me={me}
            shop={myShop}
            fontScale={fontScale}
            onChanged={(s) => {
              setMyShop(s);
              void loadMe();
              void loadCity();
            }}
            onNeedRegister={() => requireRegistered(() => void loadShop())}
            onGoToMark={(id) => goToMark(id)}
            onOpenSaves={() => setTab("saves")}
            toast={showToast}
          />
        )}
        {tab === "settings" && (
          <SettingsView
            me={me}
            fontSize={fontSize}
            onFontSize={setFontSize}
            onKidsMode={(on) =>
              void api
                .updateSettings(on)
                .then(setMe)
                .catch((e: unknown) => showToast(e instanceof Error ? e.message : "変えられませんでした。"))
            }
            onReplayTutorial={() => {
              setTutorialDone(false);
              setTab("city");
            }}
            onNeedRegister={() => requireRegistered(() => undefined)}
          />
        )}
      </main>

      <nav className="tabbar" aria-label="画面の切り替え">
        {TABS.map((t) => (
          <button
            key={t.id}
            type="button"
            className={`tab ${tab === t.id ? "is-on" : ""}`}
            aria-current={tab === t.id ? "page" : undefined}
            onClick={() => {
              setPlace(null);
              setTab(t.id);
            }}
          >
            <span className="tab-icon" aria-hidden="true">
              {t.icon}
            </span>
            {t.label}
          </button>
        ))}
      </nav>

      {openMarkId && (
        <PopSheet
          markId={openMarkId}
          me={me}
          fontScale={fontScale}
          tutorial={tutorialActive}
          onClose={() => {
            setOpenMarkId(null);
            // 保存を促すのは一度だけ。閉じたらチュートリアルは終わり。
            if (tutorialActive) setTutorialDone(true);
          }}
          onSaved={(message) => {
            showToast(message);
            if (tutorialActive) setTutorialDone(true);
          }}
          onChanged={() => {
            void loadMe();
            if (tab === "saves") void loadSaves();
          }}
          onPlaceNext={(id) => startPlace(id)}
          onGoToMark={(id) => goToMark(id, true)}
          onOpenShop={(id) => {
            setOpenMarkId(null);
            setShopSheetId(id);
          }}
          onNeedRegister={requireRegistered}
          toast={showToast}
        />
      )}

      {shopSheetId && <ShopSheet shopId={shopSheetId} fontScale={fontScale} onClose={() => setShopSheetId(null)} onGoToMark={(id) => goToMark(id)} />}

      {place?.step === "form" && (
        <PlaceSheet
          neighbor={place.neighbor}
          onCancel={() => setPlace(null)}
          onChangeNeighbor={() => setPlace({ step: "pick", neighbor: null })}
          onPlaced={(markId, message) => {
            setPlace(null);
            showToast(message);
            void api
              .city()
              .then((c) => {
                setCity(c);
                const m = c.marks.find((x) => x.id === markId);
                if (m) {
                  setSelectedId(m.id);
                  setFlyTo({ x: m.x, y: m.y, seq: Date.now() });
                }
              })
              .catch(() => undefined);
          }}
        />
      )}

      {registerThen && (
        <RegisterSheet
          onCancel={() => setRegisterThen(null)}
          onDone={(m) => {
            setMe(m);
            const then = registerThen.then;
            setRegisterThen(null);
            showToast(`${m.displayName}さん、ようこそ`);
            then();
          }}
        />
      )}

      {toast && (
        <div className="toast" role="status" aria-live="polite">
          {toast}
        </div>
      )}
    </div>
  );
}
