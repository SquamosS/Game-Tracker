"""Checks the guide's item steps against the chests the overlay logged (data/chests/*.tsv).

Run from the project root: python -I tools/ff7r-scan/audit-chests.py
For each chest holding equipment, materia or a key item: the guide steps of the same item and whether the guide's
area (the text before the first colon of "where") is the chest's area. Then the guide's item steps of the chapters
given (default: all) that no logged chest holds (NPC rewards, shops, or maps not logged yet).
"""
import csv, glob, json, re, sys

items = {int(k): v for k, v in json.load(open('overlay/games/ff7r/items.json', encoding='utf-8'))['items'].items()}
guide = json.load(open('overlay/games/ff7r/guide.json', encoding='utf-8'))
chapters = {int(a) for a in sys.argv[1:]} or None
ITEM = {"materia", "aksesori", "armor", "senjata", "summon", "music disc", "manuskrip"}
area_re = re.compile(r'^([^:(]{3,60}?)\s*(?:\(([^)]*)\))?\s*:')
steps = []
for c in guide['chapters']:
    for o in c['objectives']:
        if o['type'] in ITEM or '(chest)' in o['name']:
            m = area_re.match(o['where'])
            steps.append((c['number'], o, m.group(1).strip() if m else None))

def same(step, name):
    n = re.sub(r'\s*\(chest\)$', '', step['name']).lower()
    return n == name.lower() or (name.endswith(' Materia') and n == name[:-8].lower())

import os
opened = set(json.load(open('data/chests/opened.json', encoding='utf-8'))) if os.path.exists('data/chests/opened.json') else set()
chests = []
for f in sorted(glob.glob('data/chests/*.tsv')):
    for row in csv.DictReader(open(f, encoding='utf-8'), delimiter='\t'):
        row['ids'] = [int(i) for i in row['item ids'].split(',') if i.isdigit()]
        chests.append(row)

print(f'== {len(chests)} peti dari {len(glob.glob("data/chests/*.tsv"))} tabel ==')
matched = set()
for ch in chests:
    names = [items.get(i, f'#{i}') for i in ch['ids']]
    # Moogle Medals sit in many chests and the guide does not list them one by one: list the chest, match nothing.
    if ch['ids'] == [118]:
        print(f"{ch['id']}	{ch['area'] or '?'}	Moogle Medal	MOOGLE MEDAL{' (dibuka)' if ch['id'] in opened else ''}")
        continue
    hits = [(n, s, a) for n, s, a in steps for nm in names if same(s, nm)]
    for _, s, _ in hits: matched.add(s['id'])
    if not [i for i in ch['ids'] if i >= 100] and not hits: continue
    status = [f"Ch{n} {s['id']} '{a}' {'OK' if a and a.lower() == ch['area'].lower() else 'BEDA'}" for n, s, a in hits]
    print(f"{ch['id']}\t{ch['area'] or '?'}\t{' + '.join(names)}\t" + ('; '.join(status) or 'TIDAK ADA DI GUIDE'))

print('\n== Langkah item guide tanpa peti tercatat ==')
for n, s, a in steps:
    if (chapters is None or n in chapters) and s['id'] not in matched:
        print(f"Ch{n} {s['id']}\t{s['type']}\t{s['name']}\t'{a}'")
