"""One-off: fills guide.json's explicit rule columns from the text rules the overlay used to read (stage 2 of the
multi-game canvas, docs/HANDOFF-AI.md section 0). Run from the project root:

    python -I tools/guide-columns.py overlay/games/ff7r/guide.json [--write]

Without --write it only reports what it would set. The columns mirror the old code exactly:
  rewardOf   "where" says "hadiah" and names a side quest/discovery of the same chapter (MainWindow.RewardOf)
  auto       "where" says "otomatis": "chapter" (akhir chapter), "boss" (boss/kalah/drop/fight) or "yes" (RewardTag);
             trophies: "chapter" for "otomatis saat chapter" / "Trofi otomatis" (ChapterEndTrophy)
  closes     the warning's sentence starting with "Setelah"; closesEn: warningEn's starting with "After"
  gameTitle  the game's own quest name: "Discovery: X" -> "X" (SameQuest)
"""
import json
import re
import sys

path = sys.argv[1]
write = "--write" in sys.argv
with open(path, encoding="utf-8") as f:
    text = f.read()
guide = json.loads(text)
assert json.dumps(guide, indent=2, ensure_ascii=False) + "\n" in (text, text.replace("\r\n", "\n")), "guide.json does not round-trip; refusing to rewrite it"


def low(s):
    return (s or "").lower()


def reward_of(o, objectives):
    if o["type"] in ("side quest", "kejadian", "cerita") or "hadiah" not in low(o["where"]):
        return None
    for q in objectives:
        if q["type"] in ("side quest", "kejadian") and q is not o and low(q["name"].replace("Discovery:", "").strip()) in low(o["where"]):
            return q["id"]
    return None


def auto(o):
    where = low(o["where"])
    if o["type"] == "trofi":
        return "chapter" if "otomatis saat chapter" in where or "trofi otomatis" in where else None
    if o["type"] == "cerita" or o.get("optional") or "otomatis" not in where:
        return None
    if "akhir chapter" in where:
        return "chapter"
    if any(w in where for w in ("boss", "kalah", "drop", "fight")):
        return "boss"
    return "yes"


def sentence(warning, start):
    if not warning:
        return None
    return next((s for s in re.split(r"(?<=\.)\s+", warning) if s.startswith(start)), None)


changes = 0
for chapter in guide["chapters"]:
    objectives = chapter["objectives"]
    normal = [o for o in objectives if not o.get("hard")]
    for i, o in enumerate(objectives):
        values = {
            "rewardOf": reward_of(o, objectives),
            "auto": auto(o),
            "gameTitle": o["name"].replace("Discovery:", "").strip() if "Discovery:" in o["name"] else None,
            "closes": sentence(o.get("warning"), "Setelah"),
            "closesEn": sentence(o.get("warningEn"), "After"),
        }
        # Normal mode left Hard-only steps out of the chapter: the first match must be the same either way.
        if not o.get("hard") and values["rewardOf"] != reward_of(o, normal):
            print(f"!! {o['id']}: rewardOf differs between Hard ({values['rewardOf']}) and Normal ({reward_of(o, normal)})")
        new = {}
        for key, value in o.items():
            if key in values:
                continue
            new[key] = value
            if key == ("whereEn" if "whereEn" in o else "where"):
                for k in ("gameTitle", "rewardOf", "auto"):
                    if values[k] is not None:
                        new[k] = values[k]
            if key == ("warningEn" if "warningEn" in o else "warning"):
                for k in ("closes", "closesEn"):
                    if values[k] is not None:
                        new[k] = values[k]
        for k, v in values.items():
            if v is not None and k not in new:
                new[k] = v
        for k, v in values.items():
            if o.get(k) != v:
                changes += 1
                print(f"{o['id']}: {k} = {v!r}")
        objectives[i] = new

print(f"{changes} values")
if write:
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(json.dumps(guide, indent=2, ensure_ascii=False) + "\n")
    print("written")
