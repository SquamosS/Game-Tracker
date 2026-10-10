import json, re, sys
navi = dict(l.rstrip('\n').split('\t', 1) for l in open('research/scan/navi.txt', encoding='utf-8') if '\t' in l)
items = {int(k): v for k, v in json.load(open('overlay/games/ff7r/items.json', encoding='utf-8'))['items'].items()}
guide = json.load(open('overlay/games/ff7r/guide.json', encoding='utf-8'))
ITEM = {"materia", "aksesori", "armor", "senjata", "summon", "music disc", "manuskrip"}
area_re = re.compile(r'^([^:(]{3,60}?)\s*(?:\(([^)]*)\))?\s*:')
steps = []
for c in guide['chapters']:
    for o in c['objectives']:
        if o['type'] in ITEM:
            m = area_re.match(o['where'])
            steps.append((c['number'], o, m.group(1).strip() if m else None))
def same(step, name):
    return step['name'].lower() == name.lower() or (name.endswith(' Materia') and step['name'].lower() == name[:-8].lower())
chests = []
for l in open('research/scan/chests.tsv', encoding='utf-8'):
    cid, x, y, z, key, codes = l.rstrip('\n').split('\t')
    ids = [int(c) for c in codes.split(',') if c.isdigit()]
    chests.append(dict(id=cid, pos=(x, y, z), area=navi.get(key, key or '?'), ids=ids, raw=codes))
print('== PETI dengan isi penting (equipment/materia/kunci) ==')
matched = set()
for ch in sorted(chests, key=lambda c: c['id']):
    names = [items.get(i, f'#{i}') for i in ch['ids']]
    important = [i for i in ch['ids'] if i >= 100 and i != 118]  # 118 Moogle Medal: everywhere
    hits = [(n, s, a) for n, s, a in steps for nm in names if same(s, nm)]
    for _, s, _ in hits: matched.add(s['id'])
    if not important and not hits: continue
    status = []
    for n, s, a in hits:
        ok = a is not None and a.lower() == ch['area'].lower()
        status.append(f"Ch{n} {s['id']} guide '{a}' {'OK' if ok else 'BEDA'}")
    print(f"{ch['id']}\t{ch['area']}\t{' + '.join(names)}\t" + ('; '.join(status) if status else 'TIDAK ADA DI GUIDE'))
print('\n== Langkah guide Ch8/Ch14 tanpa peti cocok (bisa NPC/hadiah/toko atau peta lain) ==')
for n, s, a in steps:
    if n in (8, 14) and s['id'] not in matched:
        print(f"Ch{n} {s['id']}\t{s['type']}\t{s['name']}\t'{a}'")
