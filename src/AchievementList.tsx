import { useMemo, useState } from "react";
import type { GameAchievements, Guide } from "./types";

interface Props {
  data: GameAchievements | null;
  guide?: Guide;
  loading: boolean;
}

export default function AchievementList({ data, guide, loading }: Props) {
  const [showDone, setShowDone] = useState(false);
  const [revealed, setRevealed] = useState<Set<string>>(new Set());

  const hints = useMemo(() => {
    const m = new Map<string, string>();
    guide?.sections.forEach((s) =>
      s.items.forEach((i) => i.achievement && i.hint && m.set(i.achievement.toLowerCase(), i.hint)),
    );
    return m;
  }, [guide]);

  if (!data) return <p className="muted">{loading ? "Memuat achievement…" : "Belum ada data."}</p>;

  // Locked first, easiest (highest global %) on top; unlocked ones newest first.
  const list = data.achievements
    .filter((a) => showDone || !a.achieved)
    .sort((a, b) =>
      a.achieved !== b.achieved
        ? Number(a.achieved) - Number(b.achieved)
        : a.achieved
          ? (b.unlockTime ?? 0) - (a.unlockTime ?? 0)
          : (b.globalPercent ?? 0) - (a.globalPercent ?? 0),
    );

  return (
    <>
      <label className="toggle">
        <input type="checkbox" checked={showDone} onChange={(e) => setShowDone(e.target.checked)} />
        Tampilkan yang sudah terbuka
      </label>
      {list.length === 0 && <p className="muted">Semua achievement sudah terbuka. 🎉</p>}
      <ul className="achievements">
        {list.map((a) => {
          const secret = a.hidden && !a.achieved && !revealed.has(a.apiName);
          const hint = hints.get(a.name.toLowerCase());
          return (
            <li key={a.apiName} className={a.achieved ? "done" : ""}>
              <img src={a.achieved ? a.icon : a.iconGray} alt="" />
              <div>
                <div className="name">
                  {a.name}
                  {a.globalPercent != null && <span className="pct">{a.globalPercent.toFixed(1)}%</span>}
                </div>
                {secret ? (
                  <button className="link" onClick={() => setRevealed(new Set(revealed).add(a.apiName))}>
                    Tersembunyi (spoiler), klik untuk lihat
                  </button>
                ) : (
                  <>
                    <div className="desc">{a.description}</div>
                    {hint && !a.achieved && <div className="hint">💡 {hint}</div>}
                  </>
                )}
              </div>
            </li>
          );
        })}
      </ul>
    </>
  );
}
