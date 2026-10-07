import { useCallback, useEffect, useMemo, useState } from "react";
import { invoke } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";
import { getCurrentWindow } from "@tauri-apps/api/window";
import type { GameAchievements, Settings } from "./types";
import { guideFor, guides } from "./guides";
import AchievementList from "./AchievementList";
import GuideView from "./GuideView";
import SettingsView from "./SettingsView";

type Tab = "achievements" | "guide" | "settings";

const REFRESH_MS = 60_000;
const DETECT_MS = 5_000;

export default function App() {
  const [tab, setTab] = useState<Tab>("achievements");
  const [settings, setSettings] = useState<Settings | null>(null);
  const [runningId, setRunningId] = useState<number | null>(null);
  const [pickedId, setPickedId] = useState<number>(guides[0]?.appId ?? 0);
  const [data, setData] = useState<GameAchievements | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const appId = runningId ?? pickedId;
  const guide = guideFor(appId);

  useEffect(() => {
    invoke<Settings>("load_settings").then((s) => {
      setSettings(s);
      if (!s.apiKey || !s.steamId) setTab("settings");
    });
  }, []);

  // Follow whichever Steam game is running.
  useEffect(() => {
    const detect = () => invoke<number | null>("running_app_id").then(setRunningId).catch(() => {});
    detect();
    const t = setInterval(detect, DETECT_MS);
    return () => clearInterval(t);
  }, []);

  const refresh = useCallback(async () => {
    if (!appId || !settings?.apiKey || !settings.steamId) return;
    setLoading(true);
    try {
      setData(await invoke<GameAchievements>("achievements", { appId }));
      setError(null);
    } catch (e) {
      setError(String(e));
    } finally {
      setLoading(false);
    }
  }, [appId, settings]);

  useEffect(() => {
    setData(null);
    refresh();
    const t = setInterval(refresh, REFRESH_MS);
    const un = listen("overlay-shown", refresh);
    return () => {
      clearInterval(t);
      un.then((f) => f());
    };
  }, [refresh]);

  const unlocked = useMemo(() => {
    const names = new Set<string>();
    data?.achievements.forEach((a) => a.achieved && names.add(a.name.toLowerCase()));
    return names;
  }, [data]);

  const total = data?.achievements.length ?? 0;
  const done = data?.achievements.filter((a) => a.achieved).length ?? 0;
  const title = data?.gameName || guide?.title || (appId ? `App ${appId}` : "Tidak ada game");

  return (
    <div className="panel">
      <header data-tauri-drag-region>
        <div data-tauri-drag-region className="title">
          <span data-tauri-drag-region className="game">{title}</span>
          <span data-tauri-drag-region className="sub">
            {runningId ? "Sedang dimainkan" : "Tidak ada game Steam berjalan"} · Ctrl+Shift+G
          </span>
        </div>
        <button className="icon" title="Muat ulang" onClick={refresh} disabled={loading}>
          ⟳
        </button>
        <button className="icon" title="Sembunyikan (Ctrl+Shift+G)" onClick={() => getCurrentWindow().hide()}>
          ✕
        </button>
      </header>

      {!runningId && guides.length > 0 && (
        <select className="picker" value={pickedId} onChange={(e) => setPickedId(Number(e.target.value))}>
          {guides.map((g) => (
            <option key={g.appId} value={g.appId}>
              {g.title}
            </option>
          ))}
        </select>
      )}

      {total > 0 && (
        <div className="progress">
          <div className="bar">
            <div style={{ width: `${(done / total) * 100}%` }} />
          </div>
          <span>
            {done}/{total} achievement · {Math.floor((done / total) * 100)}%
          </span>
        </div>
      )}

      <nav>
        <button className={tab === "achievements" ? "on" : ""} onClick={() => setTab("achievements")}>
          Achievement
        </button>
        <button className={tab === "guide" ? "on" : ""} onClick={() => setTab("guide")}>
          Panduan 100%
        </button>
        <button className={tab === "settings" ? "on" : ""} onClick={() => setTab("settings")}>
          Pengaturan
        </button>
      </nav>

      <main>
        {error && tab !== "settings" && <p className="error">{error}</p>}
        {tab === "achievements" && <AchievementList data={data} guide={guide} loading={loading} />}
        {tab === "guide" &&
          (guide ? (
            <GuideView guide={guide} unlocked={unlocked} />
          ) : (
            <p className="muted">Belum ada panduan untuk game ini.</p>
          ))}
        {tab === "settings" && settings && (
          <SettingsView
            settings={settings}
            onSaved={(s) => {
              setSettings(s);
              setTab("achievements");
            }}
          />
        )}
      </main>
    </div>
  );
}
