import { useEffect, useState } from "react";
import type { Guide } from "./types";
import { loadChecked, saveChecked } from "./storage";

interface Props {
  guide: Guide;
  /** Lower-cased names of unlocked achievements. */
  unlocked: Set<string>;
}

export default function GuideView({ guide, unlocked }: Props) {
  const [checked, setChecked] = useState(() => loadChecked(guide.appId));
  const [query, setQuery] = useState("");
  const [spoilers, setSpoilers] = useState(false);

  useEffect(() => setChecked(loadChecked(guide.appId)), [guide.appId]);

  const isDone = (id: string, achievement?: string) =>
    checked.has(id) || (!!achievement && unlocked.has(achievement.toLowerCase()));

  const toggle = (id: string) => {
    const next = new Set(checked);
    next.has(id) ? next.delete(id) : next.add(id);
    setChecked(next);
    saveChecked(guide.appId, next);
  };

  const q = query.trim().toLowerCase();
  const allItems = guide.sections.flatMap((s) => s.items);
  const doneCount = allItems.filter((i) => isDone(i.id, i.achievement)).length;

  return (
    <div className="guide">
      {guide.status === "draft" && <p className="draft">Panduan ini masih draft dan perlu dicek.</p>}
      <details className="notes">
        <summary>Catatan penting</summary>
        <ul>
          {guide.notes.map((n) => (
            <li key={n}>{n}</li>
          ))}
        </ul>
      </details>
      <div className="row">
        <input placeholder="Cari…" value={query} onChange={(e) => setQuery(e.target.value)} />
        <label className="toggle">
          <input type="checkbox" checked={spoilers} onChange={(e) => setSpoilers(e.target.checked)} />
          Tampilkan petunjuk
        </label>
      </div>
      <p className="muted">
        Checklist: {doneCount}/{allItems.length}
      </p>
      {guide.sections.map((section) => {
        const items = section.items.filter(
          (i) => !q || i.title.toLowerCase().includes(q) || i.hint?.toLowerCase().includes(q),
        );
        if (items.length === 0) return null;
        const sectionDone = section.items.filter((i) => isDone(i.id, i.achievement)).length;
        return (
          <section key={section.name}>
            <h3>
              {section.name}
              <span>
                {sectionDone}/{section.items.length}
              </span>
            </h3>
            <ul>
              {items.map((item) => {
                const auto = !!item.achievement && unlocked.has(item.achievement.toLowerCase());
                const done = isDone(item.id, item.achievement);
                return (
                  <li key={item.id} className={done ? "done" : ""}>
                    <label>
                      <input type="checkbox" checked={done} disabled={auto} onChange={() => toggle(item.id)} />
                      <span>
                        {item.missable && <b className="missable">MISSABLE </b>}
                        {item.title}
                      </span>
                    </label>
                    {item.hint && !done && (spoilers || item.missable) && <div className="hint">{item.hint}</div>}
                  </li>
                );
              })}
            </ul>
          </section>
        );
      })}
    </div>
  );
}
