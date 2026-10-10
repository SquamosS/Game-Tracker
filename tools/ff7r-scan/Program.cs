// Read-only memory scanner for finding FF7R's story-progress value.
// Usage:
//   scanner snap <name>              full snapshot of writable memory -> <name>.snap
//   scanner inc <name> <out>         candidates whose value went UP since snapshot <name>
//   scanner filter <in> <out> <op>   op: same | inc | dec | back (equal to value at first snapshot)
//   scanner show <in>                print candidates with current values
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

// Scan files (snapshots of several GB, candidate lists) go to research\scan in the project, which git ignores,
// instead of a folder of their own at the root of a drive.
string Dir = ScanDir();
var proc = Process.GetProcessesByName("ff7remake_").FirstOrDefault() ?? throw new Exception("FF7R tidak jalan");
var module = proc.MainModule!;
long modBase = module.BaseAddress, modEnd = modBase + module.ModuleMemorySize;
IntPtr h = Native.OpenProcess(0x10 | 0x400, false, proc.Id);
if (h == IntPtr.Zero) throw new Exception("OpenProcess gagal");
var sw = Stopwatch.StartNew();

switch (args[0])
{
    case "snap": Snap(Path.Combine(Dir, args[1] + ".snap")); break;
    case "fdiff": Inc(Path.Combine(Dir, args[1] + ".snap"), Path.Combine(Dir, args[2] + ".cand"), false, true); break;
    case "bdiff": Inc(Path.Combine(Dir, args[1] + ".snap"), Path.Combine(Dir, args[2] + ".cand"), false, false, true); break;
    case "chg": Inc(Path.Combine(Dir, args[1] + ".snap"), Path.Combine(Dir, args[2] + ".cand"), true); break;
    case "inc": Inc(Path.Combine(Dir, args[1] + ".snap"), Path.Combine(Dir, args[2] + ".cand")); break;
    case "filter": Filter(Path.Combine(Dir, args[1] + ".cand"), Path.Combine(Dir, args[2] + ".cand"), args[3]); break;
    case "show": Show(Path.Combine(Dir, args[1] + ".cand")); break;
    case "ptr": PointerScan(Convert.ToInt64(args[1], 16), int.Parse(args[2]), Convert.ToInt32(args[3], 16), int.Parse(args[4])); break;
    case "chain": Console.WriteLine(Chain(args[1..])); break;
    case "match": // match <in> <ref> <out>: keep candidates whose last value equals the ref file's last value
    {
        var mine = Load(Path.Combine(Dir, args[1] + ".cand")).ToDictionary(c => (c.Addr, c.Width));
        var kept = new List<(long, byte, int, int)>();
        foreach (var r in Load(Path.Combine(Dir, args[2] + ".cand")))
            if (mine.TryGetValue((r.Addr, r.Width), out var c) && c.Last == r.Last) kept.Add(c);
        using (var w = new BinaryWriter(File.Create(Path.Combine(Dir, args[3] + ".cand"))))
            foreach (var (a, wd, f, l) in kept) Rec(w, a, wd, f, l);
        Console.WriteLine($"cocok: {kept.Count}");
        if (kept.Count <= 60) Show(Path.Combine(Dir, args[3] + ".cand"));
        break;
    }
    case "recent":Recent(int.Parse(args[1]), args.Length > 2); break;
    case "lists": FindLists(); break;
    case "names": DumpItemNames(); break;
    case "text": FindText(args[1], args.Length > 2 ? int.Parse(args[2]) : 40); break;
    case "watch": Watch(Convert.ToInt64(args[1], 16), Convert.ToInt32(args[2], 16), args.Skip(3).Select(a => Convert.ToInt32(a, 16) & ~0xF).ToHashSet()); break;
    case "rsnap": RangeSnap(args[1], Convert.ToInt64(args[2], 16), Convert.ToInt32(args[3], 16)); break;
    case "rdiff": RangeDiff(args[1], args[2], Convert.ToInt64(args[3], 16)); break;
    case "seq": Seq(args[1..].Select(int.Parse).ToArray()); break;
    case "dump": Dump(Convert.ToInt64(args[1], 16), Convert.ToInt32(args[2], 16)); break;
    case "find":Find(int.Parse(args[1]), Path.Combine(Dir, args[2] + ".cand"), args.Length > 3 ? int.Parse(args[3]) : 4); break;
    case "who": Who(args[1..].Select(a => Convert.ToInt64(a, 16)).ToHashSet()); break;
    case "vt": // vt <vtable> <offset> <value>: objects with that vtable whose int at offset equals value, with their first 0xC0 bytes
    {
        long vt = Convert.ToInt64(args[1], 16); int off = Convert.ToInt32(args[2], 16), val = int.Parse(args[3]), n = 0, all = 0;
        foreach (var (b, s) in Regions())
            for (long a = b; a < b + s; a += 1 << 22)
            {
                int len = (int)Math.Min(1 << 22, b + s - a);
                var buf = Read(a, len);
                for (int i = 0; i + 8 <= len; i += 8)
                {
                    if (BitConverter.ToInt64(buf, i) != vt) continue;
                    all++;
                    var o = Read(a + i, 0xC0);
                    if (BitConverter.ToInt32(o, off) != val) continue;
                    n++;
                    Console.WriteLine($"0x{a + i:X}");
                    for (int r = 0; r < 0xC0; r += 0x20) Console.WriteLine($"  +{r:X2}: {Convert.ToHexString(o, r, 0x20)}");
                }
            }
        Console.WriteLine($"{n} dari {all} objek");
        break;
    }
    case "pair": // pair <a> <b> <window>: aligned int a with int b within window bytes, printed with 0x60 bytes around
    {
        int va = int.Parse(args[1]), vb = int.Parse(args[2]), win = Convert.ToInt32(args[3], 16), shown = 0, total = 0;
        foreach (var (b, s) in Regions())
            for (long a = b; a < b + s; a += 1 << 22)
            {
                int len = (int)Math.Min(1 << 22, b + s - a);
                var buf = Read(a, len);
                for (int i = 0; i + 4 <= len; i += 4)
                {
                    if (BitConverter.ToInt32(buf, i) != va) continue;
                    bool near = false;
                    for (int j = Math.Max(0, i - win); j + 4 <= Math.Min(len, i + win + 4) && !near; j += 4)
                        near = j != i && BitConverter.ToInt32(buf, j) == vb;
                    if (!near) continue;
                    total++;
                    if (shown++ >= 60) continue;
                    var ctx = Read(a + i - 0x30, 0x60);
                    var fl = Enumerable.Range(0, 0x18).Select(k => BitConverter.ToSingle(ctx, k * 4)).Select(f => Math.Abs(f) is > 0.01f and < 1e6f ? f.ToString("0.#") : BitConverter.ToInt32(BitConverter.GetBytes(f)).ToString());
                    Console.WriteLine($"0x{a + i:X}: {string.Join(" ", fl)}");
                }
            }
        Console.WriteLine($"{total} temuan");
        break;
    }
    case "vecnear": // vecnear <x> <y> <z> <radius> <int> <window>: float3 within radius of the point with the int within window bytes
    {
        float px = float.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture), py = float.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture),
            pz = float.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture), r = float.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture);
        int want = int.Parse(args[5]), win = Convert.ToInt32(args[6], 16), shown = 0, total = 0;
        foreach (var (b, s) in Regions())
            for (long a = b; a < b + s; a += 1 << 22)
            {
                int len = (int)Math.Min(1 << 22, b + s - a);
                var buf = Read(a, len);
                for (int i = 0; i + 12 <= len; i += 4)
                {
                    float x = BitConverter.ToSingle(buf, i), y = BitConverter.ToSingle(buf, i + 4), z = BitConverter.ToSingle(buf, i + 8);
                    if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z) || Math.Abs(x - px) > r || Math.Abs(y - py) > r || Math.Abs(z - pz) > r) continue;
                    bool near = false;
                    for (int j = Math.Max(0, i - win); j + 4 <= Math.Min(len, i + win + 12) && !near; j += 4)
                        near = BitConverter.ToInt32(buf, j) == want;
                    if (!near) continue;
                    total++;
                    if (shown++ >= 40) continue;
                    var ctx = Read(a + i - 0x20, 0x60);
                    var fl = Enumerable.Range(0, 0x18).Select(k => BitConverter.ToSingle(ctx, k * 4)).Select(f => Math.Abs(f) is > 0.01f and < 1e6f ? f.ToString("0.#") : BitConverter.ToInt32(BitConverter.GetBytes(f)).ToString());
                    Console.WriteLine($"0x{a + i:X}: {string.Join(" ", fl)}");
                }
            }
        Console.WriteLine($"{total} temuan");
        break;
    }
    case "module": // module <file>: the game module's image as loaded (unreadable pages as zeros), for static analysis
    {
        using var f = File.Create(args[1]);
        for (long o = 0; o < modEnd - modBase; o += 0x1000)
        {
            var page = new byte[0x1000];
            Native.ReadProcessMemory(h, (IntPtr)(modBase + o), page, page.Length, out _);
            f.Write(page);
        }
        Console.WriteLine($"modul 0x{modBase:X}, {(modEnd - modBase) / (1 << 20)} MB -> {args[1]}");
        break;
    }
    case "inventory": // inventory <file>: owned ids from the newest save-data copy (same layout as the overlay's reader)
    {
        var copies = new List<(long Materia, long Gil, uint Time)>();
        foreach (var (b, s) in Regions())
            for (long a = b; a < b + s; a += 1 << 22)
            {
                int len = (int)Math.Min(1 << 22, b + s - a);
                var buf = Read(a, len);
                for (int i = 0; i + 0x20 * 6 <= len; i += 0x20)
                {
                    bool run = true;
                    for (int k = 0; k < 6 && run; k++)
                    {
                        int p = i + k * 0x20;
                        run = BitConverter.ToInt32(buf, p + 4) == 0 && BitConverter.ToInt32(buf, p + 8) == k && BitConverter.ToInt32(buf, p + 12) == 0
                            && BitConverter.ToInt32(buf, p + 20) is >= 10000 and < 20000 && buf[p + 16] is >= 1 and <= 5;
                    }
                    long gil = a + i + 0x33630;
                    if (run && BitConverter.ToInt32(Read(gil + 8, 4)) == 20) copies.Add((a + i, gil, BitConverter.ToUInt32(Read(gil, 4))));
                }
            }
        if (copies.Count == 0) { Console.WriteLine("data save tidak ditemukan"); break; }
        var (mat, gilRec, _) = copies.MaxBy(c => c.Time);
        var lines = new List<string>();
        long items = gilRec;
        while (BitConverter.ToInt32(Read(items - 0x18 + 8, 4)) is > 0 and < 100_000 && BitConverter.ToInt32(Read(items - 0x18 + 4, 4)) == 0) items -= 0x18;
        var ib = Read(items, 0x18 * 600);
        for (int k = 0; k < 600; k++) { int id = BitConverter.ToInt32(ib, k * 0x18 + 8), n = BitConverter.ToInt32(ib, k * 0x18 + 12); if (id > 0 && id < 100_000 && n > 0) lines.Add($"item\t{id}\t{n}"); }
        var mb = Read(mat, 0x20 * 600);
        for (int k = 0; k < 600; k++) { int id = BitConverter.ToInt32(mb, k * 0x20 + 20); if (id is >= 10000 and < 20000) lines.Add($"materia\t{id}\t1"); }
        var eb = Read(mat - 0x2000, 0x2000);
        for (int k = 0; k < 0x200; k++) { int kind = BitConverter.ToInt32(eb, k * 0x10), id = BitConverter.ToInt32(eb, k * 0x10 + 4); if ((kind & 0xFF) is 1 or 2 && kind >> 16 == 0 && id is >= 1000 and < 10000) lines.Add($"equip\t{id}\t1"); }
        File.WriteAllLines(args[1], lines);
        Console.WriteLine($"{copies.Count} salinan data save; terbaru di 0x{mat:X}: {lines.Count} baris -> {args[1]}");
        break;
    }
    case "flagbits": // flagbits <bit...>: those bits of the save-data flag block (materia list + 0x40E00, as the overlay reads it) in every save copy, newest last
    {
        var copies = new List<(long Materia, uint Time)>();
        foreach (var (b, s) in Regions())
            for (long a = b; a < b + s; a += 1 << 22)
            {
                int len = (int)Math.Min(1 << 22, b + s - a);
                var buf = Read(a, len);
                for (int i = 0; i + 0x20 * 6 <= len; i += 0x20)
                {
                    bool run = true;
                    for (int k = 0; k < 6 && run; k++)
                    {
                        int p = i + k * 0x20;
                        run = BitConverter.ToInt32(buf, p + 4) == 0 && BitConverter.ToInt32(buf, p + 8) == k && BitConverter.ToInt32(buf, p + 12) == 0
                            && BitConverter.ToInt32(buf, p + 20) is >= 10000 and < 20000 && buf[p + 16] is >= 1 and <= 5;
                    }
                    long gil = a + i + 0x33630;
                    if (run && BitConverter.ToInt32(Read(gil + 8, 4)) == 20) copies.Add((a + i, BitConverter.ToUInt32(Read(gil, 4))));
                }
            }
        foreach (var (mat, time) in copies.OrderBy(c => c.Time))
        {
            var flags = Read(mat + 0x40E00, 0x1000);
            Console.WriteLine($"salinan 0x{mat:X} waktu {time}: " + string.Join(" ", args[1..].Select(x => { int bit = Convert.ToInt32(x, 16); return $"{x}={(flags[bit >> 3] >> (bit & 7)) & 1}"; })));
        }
        break;
    }
    case "flaglive": // flaglive <bit> [save copy]: memory holding the save flag block's bytes around <bit> but with <bit> set (a live copy ahead of the save)
    {
        int bit = Convert.ToInt32(args[1], 16);
        long mat = 0; uint best = 0;
        foreach (var (b, s) in Regions())
            for (long a = b; a < b + s; a += 1 << 22)
            {
                int len = (int)Math.Min(1 << 22, b + s - a);
                var buf = Read(a, len);
                for (int i = 0; i + 0x20 * 6 <= len; i += 0x20)
                {
                    bool run = true;
                    for (int k = 0; k < 6 && run; k++)
                    {
                        int p = i + k * 0x20;
                        run = BitConverter.ToInt32(buf, p + 4) == 0 && BitConverter.ToInt32(buf, p + 8) == k && BitConverter.ToInt32(buf, p + 12) == 0
                            && BitConverter.ToInt32(buf, p + 20) is >= 10000 and < 20000 && buf[p + 16] is >= 1 and <= 5;
                    }
                    long gil = a + i + 0x33630;
                    if (run && BitConverter.ToInt32(Read(gil + 8, 4)) == 20 && BitConverter.ToUInt32(Read(gil, 4)) >= best) { best = BitConverter.ToUInt32(Read(gil, 4)); mat = a + i; }
                }
            }
        if (args.Length > 2) mat = Convert.ToInt64(args[2], 16); // a chosen save copy
        int at = bit >> 3, from = Math.Max(0, at - 24);
        var want = Read(mat + 0x40E00 + from, 48);
        want[at - from] |= (byte)(1 << (bit & 7));
        Console.WriteLine($"save 0x{mat:X}, mencari {Convert.ToHexString(want)}");
        foreach (var (b, s) in Regions())
            for (long a = b; a < b + s; a += 1 << 22)
            {
                int len = (int)Math.Min((1 << 22) + 64, b + s - a);
                var buf = Read(a, len);
                for (int i = buf.AsSpan(0, Math.Min(len, 1 << 22)).IndexOf(want); i >= 0;)
                {
                    long hit = a + i - from;
                    Console.WriteLine($"cocok: blok di 0x{hit:X} (save+0x{hit - mat:X}) {FName(BitConverter.ToInt32(Read(hit - 0x40, 4)))}");
                    int next = buf.AsSpan(i + 1, Math.Min(len, 1 << 22) - i - 1).IndexOf(want);
                    i = next < 0 ? -1 : i + 1 + next;
                }
            }
        break;
    }
    case "hex": // hex <pattern> [max]: every place memory holds these bytes
    {
        var want = Convert.FromHexString(args[1]);
        int max = args.Length > 2 ? int.Parse(args[2]) : 40, hits = 0;
        foreach (var (b, s) in Regions())
            for (long a = b; a < b + s; a += 1 << 22)
            {
                int len = (int)Math.Min((1 << 22) + want.Length, b + s - a);
                var buf = Read(a, len);
                for (int i = buf.AsSpan(0, Math.Min(len, 1 << 22)).IndexOf(want); i >= 0;)
                {
                    if (hits++ < max) Console.WriteLine($"0x{a + i:X}");
                    int next = buf.AsSpan(i + 1, Math.Min(len, 1 << 22) - i - 1).IndexOf(want);
                    i = next < 0 ? -1 : i + 1 + next;
                }
            }
        Console.WriteLine($"{hits} temuan");
        break;
    }
    case "chests": // chests <x> <y> <z>: every chest of the loaded maps: point position and distance, rewards (item code, id, name), save flag bit and state
    {
        float px = float.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture), py = float.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture), pz = float.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture);
        long vtTreasure = modBase + 0x4CD6488, vtReward = modBase + 0x4CE1290, vtItem = modBase + 0x4CCCE48;
        var treasures = new List<long>(); long reward = 0, item = 0;
        var saves = new List<(long Mat, uint Time)>();
        foreach (var (b, s) in Regions())
            for (long a = b; a < b + s; a += 1 << 22)
            {
                int len = (int)Math.Min(1 << 22, b + s - a);
                var buf = Read(a, len);
                for (int i = 0; i + 0x20 * 6 <= len; i += 8)
                {
                    long q = BitConverter.ToInt64(buf, i);
                    if (q == vtTreasure && !FName(BitConverter.ToInt32(buf, i + 0x18)).StartsWith("Default__")) treasures.Add(a + i);
                    else if (q == vtReward && !FName(BitConverter.ToInt32(buf, i + 0x18)).StartsWith("Default__")) reward = a + i;
                    else if (q == vtItem && !FName(BitConverter.ToInt32(buf, i + 0x18)).StartsWith("Default__")) item = a + i;
                    if (i % 0x20 != 0) continue;
                    bool run = true;
                    for (int k = 0; k < 6 && run; k++)
                    {
                        int p = i + k * 0x20;
                        run = BitConverter.ToInt32(buf, p + 4) == 0 && BitConverter.ToInt32(buf, p + 8) == k && BitConverter.ToInt32(buf, p + 12) == 0
                            && BitConverter.ToInt32(buf, p + 20) is >= 10000 and < 20000 && buf[p + 16] is >= 1 and <= 5;
                    }
                    long gil = a + i + 0x33630;
                    if (run && BitConverter.ToInt32(Read(gil + 8, 4)) == 20) saves.Add((a + i, BitConverter.ToUInt32(Read(gil, 4))));
                }
            }
        // Item code -> inventory id (Item rows 0x158: FName, +0x10 id), reward key -> item codes (Reward rows 0x50, items at +0x28).
        var ids = new Dictionary<string, int>();
        { long rows = BitConverter.ToInt64(Read(item + 0x38, 8)); int n = BitConverter.ToInt32(Read(item + 0x40, 4)); var d = Read(rows, n * 0x158); for (int r = 0; r < n; r++) ids[FName(BitConverter.ToInt32(d, r * 0x158))] = BitConverter.ToInt32(d, r * 0x158 + 0x10); }
        var rewards = new Dictionary<int, List<string>>();
        { long rows = BitConverter.ToInt64(Read(reward + 0x38, 8)); int n = BitConverter.ToInt32(Read(reward + 0x40, 4)); var d = Read(rows, n * 0x50);
          for (int r = 0; r < n; r++) { long p = BitConverter.ToInt64(d, r * 0x50 + 0x28); int c = BitConverter.ToInt32(d, r * 0x50 + 0x30); var list = new List<string>(); if (p != 0 && c is > 0 and < 16) { var e = Read(p, c * 0x10); for (int k = 0; k < c; k++) list.Add(FName(BitConverter.ToInt32(e, k * 0x10))); } rewards[BitConverter.ToInt32(d, r * 0x50)] = list; } }
        var names = new Dictionary<int, string>();
        string itemsJson = Path.Combine(Dir, "..", "..", "overlay", "games", "ff7r", "items.json");
        if (File.Exists(itemsJson)) foreach (var m in Regex.Matches(File.ReadAllText(itemsJson), @"""(\d+)"":\s*""([^""]+)""").Cast<Match>()) names[int.Parse(m.Groups[1].Value)] = m.Groups[2].Value;
        // The save copy that knows most: newest time, then most flags set.
        var save = saves.OrderBy(c => c.Time).ThenBy(c => Read(c.Mat + 0x40E00, 0x1000).Sum(x => System.Numerics.BitOperations.PopCount(x))).Last().Mat;
        var flags = Read(save + 0x40E00, 0x1000);
        // Point rows (FName, 0, ptr, quat, xyz at +0x20, scale 1,1,1 at +0x30) and flag rows (FName, 0, module ptr, bit at +0x10): found by their FNames.
        var rowsAll = new List<(string Id, int Flag, int Point, int[] Rewards)>();
        foreach (var t in treasures)
        {
            long rows = BitConverter.ToInt64(Read(t + 0x38, 8)); int n = BitConverter.ToInt32(Read(t + 0x40, 4));
            if (rows == 0 || n is <= 0 or > 1000) continue;
            var d = Read(rows, n * 0xA0);
            for (int r = 0; r < n; r++)
            {
                int o = r * 0xA0;
                var rw = Enumerable.Range(0, 8).Select(k => BitConverter.ToInt32(d, o + 0x38 + k * 8)).Where(v => v != 0 && FName(v).StartsWith("rwr")).ToArray();
                rowsAll.Add((FName(BitConverter.ToInt32(d, o)), BitConverter.ToInt32(d, o + 0x18), BitConverter.ToInt32(d, o + 0x30), rw));
            }
        }
        var pointSet = rowsAll.Select(r => r.Point).ToHashSet(); var flagSet = rowsAll.Select(r => r.Flag).ToHashSet();
        var points = new Dictionary<int, (float X, float Y, float Z)>(); var bits = new Dictionary<int, int>();
        foreach (var (b, s) in Regions())
            for (long a = b; a < b + s; a += 1 << 22)
            {
                int len = (int)Math.Min(1 << 22, b + s - a);
                var buf = Read(a, len);
                for (int i = 0; i + 0x40 <= len; i += 8)
                {
                    int v = BitConverter.ToInt32(buf, i);
                    if (BitConverter.ToInt32(buf, i + 4) != 0) continue;
                    if (pointSet.Contains(v) && BitConverter.ToSingle(buf, i + 0x30) == 1f && BitConverter.ToSingle(buf, i + 0x34) == 1f && BitConverter.ToSingle(buf, i + 0x38) == 1f)
                        points[v] = (BitConverter.ToSingle(buf, i + 0x20), BitConverter.ToSingle(buf, i + 0x24), BitConverter.ToSingle(buf, i + 0x28));
                    else if (flagSet.Contains(v) && BitConverter.ToInt64(buf, i + 8) is var mp && mp >= modBase && mp < modEnd && BitConverter.ToInt32(buf, i + 0x10) is > 0 and < 0x8000 and var bit)
                        bits[v] = bit;
                }
            }
        Console.WriteLine($"{treasures.Count} tabel peti, {rowsAll.Count} peti, save 0x{save:X}");
        foreach (var r in rowsAll.OrderBy(r => points.TryGetValue(r.Point, out var q) ? Math.Sqrt((q.X - px) * (q.X - px) + (q.Y - py) * (q.Y - py) + (q.Z - pz) * (q.Z - pz)) : 1e9))
        {
            string where = points.TryGetValue(r.Point, out var q) ? $"{q.X:0},{q.Y:0},{q.Z:0} {Math.Sqrt((q.X - px) * (q.X - px) + (q.Y - py) * (q.Y - py) + (q.Z - pz) * (q.Z - pz)) / 100:0} m dz {(q.Z - pz) / 100:0} m" : "posisi ?";
            string contents = string.Join(" + ", r.Rewards.SelectMany(k => rewards.GetValueOrDefault(k) ?? []).Select(c => ids.TryGetValue(c, out int id) ? $"{c}#{id} {names.GetValueOrDefault(id, "?")}" : c));
            string state = bits.TryGetValue(r.Flag, out int bit) ? $"bit 0x{bit:X}={(flags[bit >> 3] >> (bit & 7)) & 1}" : "bit ?";
            Console.WriteLine($"{r.Id}	{state}	{where}	{contents}");
        }
        break;
    }
    case "chestrec": // chestrec <minutes>: chest actors (classes named FA####_00_Treasurebox*_C), found every 20 s, read 4x a second; every changed int of the actor (0x600) and its mesh component (0x1000) to chestrec.log
    {
        var end = DateTime.Now.AddMinutes(double.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture));
        string stop = Path.Combine(Dir, "stop"); File.Delete(stop);
        using var log = new StreamWriter(new FileStream(Path.Combine(Dir, "chestrec.log"), FileMode.Create, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
        var last = new Dictionary<(long, int), int>();
        var chests = new List<(long Actor, long Comp)>();
        DateTime found = DateTime.MinValue;
        while (DateTime.Now < end && !File.Exists(stop))
        {
            if (DateTime.Now - found > TimeSpan.FromSeconds(20))
            {
                found = DateTime.Now;
                // Chest classes load with the maps that use them: look for their names again each time.
                var classNames = FNames().Where(n => Regex.IsMatch(n.Text, @"^FA\d{4}_\d{2}_Treasurebox.*_C$", RegexOptions.IgnoreCase)).Select(n => n.Index).ToHashSet();
                var classes = new HashSet<long>();
                var objs = new List<(long, long)>();
                foreach (var (b, s) in Regions())
                    for (long a = b; a < b + s; a += 1 << 22)
                    {
                        int len = (int)Math.Min(1 << 22, b + s - a);
                        var buf = Read(a, len);
                        for (int i = 0; i + 0x20 <= len; i += 8)
                        {
                            long vt = BitConverter.ToInt64(buf, i);
                            if (vt < modBase || vt >= modEnd) continue;
                            if (classNames.Contains(BitConverter.ToInt32(buf, i + 0x18))) { classes.Add(a + i); objs.Add((a + i, BitConverter.ToInt64(buf, i + 0x10))); }
                        }
                    }
                var now = objs.Where(o => classes.Contains(o.Item2)).Select(o => (o.Item1, BitConverter.ToInt64(Read(o.Item1 + 0x160, 8)))).Where(o => o.Item2 != 0).ToList();
                if (!now.SequenceEqual(chests))
                {
                    chests = now;
                    foreach (var (actor, comp) in chests)
                    {
                        var f = Read(comp + 0x1B0, 12);
                        log.WriteLine($"{DateTime.Now:HH:mm:ss.f}	peti 0x{actor:X} komp 0x{comp:X} di {BitConverter.ToSingle(f, 0):0},{BitConverter.ToSingle(f, 4):0},{BitConverter.ToSingle(f, 8):0}");
                    }
                }
            }
            foreach (var (actor, comp) in chests)
                foreach (var (at, size, tag) in new[] { (actor, 0x600, "A"), (comp, 0x1000, "K") })
                {
                    var m = Read(at, size);
                    for (int k = 0; k < size; k += 4)
                    {
                        int v = BitConverter.ToInt32(m, k);
                        if (last.TryGetValue((at, k), out int old) && old != v)
                            log.WriteLine($"{DateTime.Now:HH:mm:ss.f}	0x{actor:X}	{tag}+0x{k:X}	{old}	{v}");
                        last[(at, k)] = v;
                    }
                }
            Thread.Sleep(250);
        }
        break;
    }
    case "rows": // rows <addr> <count> <stride> [regex]: FName key (with its number: E_ARM + 2003 = E_ARM_2002) and the int at +0x10 of each row
    {
        long at = Convert.ToInt64(args[1], 16); int n = int.Parse(args[2]), stride = Convert.ToInt32(args[3], 16);
        var d = Read(at, n * stride);
        for (int r = 0; r < n; r++)
        {
            int number = BitConverter.ToInt32(d, r * stride + 4);
            string key = FName(BitConverter.ToInt32(d, r * stride)) + (number > 0 ? $"_{number - 1}" : "");
            if (args.Length > 4 && !Regex.IsMatch(key, args[4])) continue;
            Console.WriteLine($"{key}	{BitConverter.ToInt32(d, r * stride + 0x10)}");
        }
        break;
    }
    case "navi": // navi <file>: every "$navi..." localization key with its English text (FString key then FString text)
    {
        var pairs = new SortedDictionary<string, string>();
        foreach (var (b, s) in Regions())
            for (long a = b; a < b + s; a += 1 << 22)
            {
                int len = (int)Math.Min(1 << 22, b + s - a);
                var buf = Read(a, len);
                for (int i = 0; i + 32 <= len; i += 8)
                {
                    long p1 = BitConverter.ToInt64(buf, i), p2 = BitConverter.ToInt64(buf, i + 16);
                    int l1 = BitConverter.ToInt32(buf, i + 8), l2 = BitConverter.ToInt32(buf, i + 24);
                    if (p1 < 0x10000000000 || p1 > 0x7FF000000000 || l1 is < 8 or > 60 || p2 < 0x10000000000 || p2 > 0x7FF000000000 || l2 is < 2 or > 200) continue;
                    var k = System.Text.Encoding.Unicode.GetString(Read(p1, (l1 - 1) * 2));
                    if (!k.StartsWith("$navi")) continue;
                    var t = System.Text.Encoding.Unicode.GetString(Read(p2, (l2 - 1) * 2));
                    if (!t.StartsWith("$") && t.Length > 0) pairs.TryAdd(k, t);
                }
            }
        File.WriteAllLines(args[1], pairs.Select(kv => $"{kv.Key}\t{kv.Value}"));
        Console.WriteLine($"{pairs.Count} nama -> {args[1]}");
        break;
    }
    case "fnames": // fnames <regex>: every FNamePool entry whose text matches, with its index
        foreach (var (index, text) in FNames().Where(n => Regex.IsMatch(n.Text, args[1], RegexOptions.IgnoreCase)).Take(400))
            Console.WriteLine($"0x{index:X}\t{text}");
        break;
    case "uobjs": // uobjs <regex> [x y z]: objects named like regex, then instances of those (classes); with a position, float3 within 5 m in the object or the objects it points to
    {
        var wanted = FNames().Where(n => Regex.IsMatch(n.Text, args[1], RegexOptions.IgnoreCase)).ToDictionary(n => n.Index, n => n.Text);
        Console.WriteLine($"{wanted.Count} nama cocok");
        bool withPos = args.Length >= 5;
        float px = withPos ? float.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 0, py = withPos ? float.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : 0,
            pz = withPos ? float.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture) : 0;
        // Pass 1: objects (vtable in the module) whose own name matches: the classes, and objects named after them.
        var named = new Dictionary<long, int>();
        foreach (var (b, s) in Regions())
            for (long a = b; a < b + s; a += 1 << 22)
            {
                int len = (int)Math.Min(1 << 22, b + s - a);
                var buf = Read(a, len);
                for (int i = 0; i + 0x20 <= len; i += 8)
                {
                    long vt = BitConverter.ToInt64(buf, i);
                    if (vt < modBase || vt >= modEnd) continue;
                    int name = BitConverter.ToInt32(buf, i + 0x18);
                    if (wanted.ContainsKey(name)) named[a + i] = name;
                }
            }
        Console.WriteLine($"{named.Count} objek bernama cocok");
        foreach (var (o, n) in named.Take(60))
            Console.WriteLine($"  0x{o:X} {wanted[n]} : {FName(BitConverter.ToInt32(Read(BitConverter.ToInt64(Read(o + 0x10, 8)) + 0x18, 4)))}");
        // Pass 2: instances whose class (+0x10) is one of those objects.
        var classes = named.Keys.ToHashSet();
        var instances = new List<long>();
        foreach (var (b, s) in Regions())
            for (long a = b; a < b + s; a += 1 << 22)
            {
                int len = (int)Math.Min(1 << 22, b + s - a);
                var buf = Read(a, len);
                for (int i = 0; i + 0x20 <= len; i += 8)
                {
                    long vt = BitConverter.ToInt64(buf, i);
                    if (vt < modBase || vt >= modEnd || !classes.Contains(BitConverter.ToInt64(buf, i + 0x10))) continue;
                    instances.Add(a + i);
                }
            }
        Console.WriteLine($"{instances.Count} instance");
        foreach (var o in instances.Take(withPos ? 5000 : 80))
        {
            long cls = BitConverter.ToInt64(Read(o + 0x10, 8));
            string line = $"  0x{o:X} {FName(BitConverter.ToInt32(Read(o + 0x18, 4)))} : {FName(BitConverter.ToInt32(Read(cls + 0x18, 4)))} vt modul+0x{BitConverter.ToInt64(Read(o, 8)) - modBase:X}";
            if (!withPos) { Console.WriteLine(line); continue; }
            // The position may sit in the object or in an object it points to (a scene component): offsets of float3 near the player.
            var hits = new List<string>();
            void Look(long at, string path)
            {
                var m = Read(at, 0x400);
                for (int k = 0; k + 12 <= m.Length; k += 4)
                {
                    float x = BitConverter.ToSingle(m, k), y = BitConverter.ToSingle(m, k + 4), z = BitConverter.ToSingle(m, k + 8);
                    if (Math.Abs(x - px) < 500 && Math.Abs(y - py) < 500 && Math.Abs(z - pz) < 500) hits.Add($"{path}+0x{k:X}=({x:0},{y:0},{z:0})");
                }
            }
            Look(o, "");
            var self = Read(o, 0x400);
            for (int k = 0x20; k + 8 <= self.Length && hits.Count < 6; k += 8)
            {
                long q = BitConverter.ToInt64(self, k);
                if (q > 0x10000 && q < 0x7FF000000000 && (q & 7) == 0 && BitConverter.ToInt64(Read(q, 8)) is var v && v >= modBase && v < modEnd) Look(q, $"[+0x{k:X}]");
            }
            if (hits.Count > 0) Console.WriteLine(line + "  DEKAT " + string.Join(" ", hits.Take(6)));
        }
        break;
    }
    case "fields": // fields <addr> <hexlen>: each 4-byte value that is a valid FName index, each pointer to a named UObject, and FStrings
    {
        long at = Convert.ToInt64(args[1], 16);
        var m = Read(at, Convert.ToInt32(args[2], 16));
        for (int k = 0; k + 4 <= m.Length; k += 4)
        {
            int v = BitConverter.ToInt32(m, k);
            if (v > 0x100 && (v >> 16) < 0x300 && FName(v) is { Length: >= 3 } n && n.All(c => c is >= ' ' and < (char)0x7F) && n != "?")
                Console.WriteLine($"+0x{k:X}	fname 0x{v:X}	{n}");
            if (k % 8 != 0 || k + 8 > m.Length) continue;
            long q = BitConverter.ToInt64(m, k);
            if (q <= 0x10000 || q >= 0x7FF000000000) continue;
            var head = Read(q, 0x20);
            long vt = BitConverter.ToInt64(head, 0);
            if (vt >= modBase && vt < modEnd)
            {
                long cls = BitConverter.ToInt64(head, 0x10);
                Console.WriteLine($"+0x{k:X}	obj 0x{q:X}	{FName(BitConverter.ToInt32(head, 0x18))} : {FName(BitConverter.ToInt32(Read(cls + 0x18, 4)))}");
            }
            else if (k + 16 <= m.Length && BitConverter.ToInt32(m, k + 8) is int len and > 1 and < 200 && BitConverter.ToInt32(m, k + 12) >= len)
            {
                var t = Read(q, len * 2);
                string str = System.Text.Encoding.Unicode.GetString(t).TrimEnd('\0');
                if (str.Length > 1 && str.All(c => c is >= ' ' and < (char)0x7F)) Console.WriteLine($"+0x{k:X}	fstring	{str}");
            }
        }
        break;
    }
    case "rewards": // rewards <table> <regex>: rows (0x50 bytes, FName key) of a data table whose rows sit at +0x38 (count +0x40), with both arrays at +0x28/+0x38 (0x10-byte elements)
    {
        long table = Convert.ToInt64(args[1], 16);
        long rows = BitConverter.ToInt64(Read(table + 0x38, 8));
        int count = BitConverter.ToInt32(Read(table + 0x40, 4));
        var data = Read(rows, count * 0x50);
        for (int r = 0; r < count; r++)
        {
            int o = r * 0x50;
            string key = FName(BitConverter.ToInt32(data, o));
            if (!Regex.IsMatch(key, args[2], RegexOptions.IgnoreCase)) continue;
            string Arr(int at)
            {
                long p = BitConverter.ToInt64(data, o + at);
                int n = BitConverter.ToInt32(data, o + at + 8);
                if (p == 0 || n is <= 0 or > 32) return "-";
                var e = Read(p, n * 0x10);
                return string.Join(" | ", Enumerable.Range(0, n).Select(i => $"{FName(BitConverter.ToInt32(e, i * 0x10))}/{BitConverter.ToInt32(e, i * 0x10 + 4)}/{BitConverter.ToInt32(e, i * 0x10 + 8)}/{BitConverter.ToInt32(e, i * 0x10 + 12)}"));
            }
            Console.WriteLine($"{key}	{BitConverter.ToInt32(data, o + 0x10)}	A[{Arr(0x28)}]	B[{Arr(0x38)}]");
        }
        break;
    }
    case "points": // points <addr> <before> <after>: 0x50-byte locator rows (FName, ptr, quat, xyz at +0x20) around a row
    {
        long at = Convert.ToInt64(args[1], 16);
        int before = int.Parse(args[2]), after = int.Parse(args[3]);
        var m = Read(at - before * 0x50L, (before + after) * 0x50);
        for (int i = 0; i < before + after; i++)
        {
            int o = i * 0x50;
            string n = FName(BitConverter.ToInt32(m, o));
            if (n == "?" || n.Length < 3) continue;
            Console.WriteLine($"0x{at - before * 0x50L + o:X}	{n}	{BitConverter.ToSingle(m, o + 0x20):0}	{BitConverter.ToSingle(m, o + 0x24):0}	{BitConverter.ToSingle(m, o + 0x28):0}");
        }
        break;
    }
    case "volumes": // volumes <x> <y> <z>: EndNaviMapVolume actors (vtable module+0x4C1C358): world name, layer +0x3B0, part +0x3B4, bounds, whether the point is inside
    {
        float px = float.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture), py = float.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture), pz = float.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture);
        long vtable = modBase + 0x4C1C358;
        foreach (var (b, s) in Regions())
            for (long a = b; a < b + s; a += 1 << 22)
            {
                int len = (int)Math.Min(1 << 22, b + s - a);
                var buf = Read(a, len);
                for (int i = 0; i + 8 <= len; i += 8)
                {
                    if (BitConverter.ToInt64(buf, i) != vtable) continue;
                    long o = a + i;
                    long level = BitConverter.ToInt64(Read(o + 0x20, 8)), world = BitConverter.ToInt64(Read(level + 0x20, 8));
                    long brush = BitConverter.ToInt64(Read(o + 0x160, 8));
                    var f = Read(brush + 0x160, 24);
                    float cx = BitConverter.ToSingle(f, 0), cy = BitConverter.ToSingle(f, 4), cz = BitConverter.ToSingle(f, 8), ex = BitConverter.ToSingle(f, 12), ey = BitConverter.ToSingle(f, 16), ez = BitConverter.ToSingle(f, 20);
                    bool inside = Math.Abs(px - cx) <= ex && Math.Abs(py - cy) <= ey && Math.Abs(pz - cz) <= ez;
                    if (!inside && args.Length < 5) continue;
                    Console.WriteLine($"{(inside ? "DI DALAM" : "        ")} 0x{o:X} {FName(BitConverter.ToInt32(Read(o + 0x18, 4)))} | {FName(BitConverter.ToInt32(Read(world + 0x18, 4)))} | layer {BitConverter.ToInt32(Read(o + 0x3B0, 4))} part {BitConverter.ToInt32(Read(o + 0x3B4, 4))} | {cx:0},{cy:0},{cz:0} ± {ex:0},{ey:0},{ez:0}");
                }
            }
        break;
    }
    case "obj": // obj <addr...>: a UObject's name and its class's name (FNamePool blocks at module+0x5981310, Steam 1.0.0.7)
        foreach (var arg in args[1..])
        {
            long o = Convert.ToInt64(arg, 16);
            long cls = BitConverter.ToInt64(Read(o + 0x10, 8)), outer = BitConverter.ToInt64(Read(o + 0x20, 8));
            Console.WriteLine($"0x{o:X}: {FName(BitConverter.ToInt32(Read(o + 0x18, 4)))} : {FName(BitConverter.ToInt32(Read(cls + 0x18, 4)))}  (outer {FName(BitConverter.ToInt32(Read(outer + 0x18, 4)))})");
        }
        break;
    case "base": // base <addr...>: the nearest pointer into the game module at or before each address (an object's vtable)
        foreach (var arg in args[1..])
        {
            long at = Convert.ToInt64(arg, 16);
            var back = Read(at - 0x1000, 0x1008);
            for (int i = 0x1000; i >= 0; i -= 8)
            {
                long q = BitConverter.ToInt64(back, i);
                if (q < modBase || q >= modEnd) continue;
                Console.WriteLine($"0x{at:X}: objek 0x{at - 0x1000 + i:X} (+0x{0x1000 - i:X}) vtable modul+0x{q - modBase:X}");
                break;
            }
        }
        break;
    case "strs": // strs <addr> <count>: the FStrings (pointer, length, capacity) of an array, 16 bytes apart
        for (int k = -Convert.ToInt32(args[2]); k < Convert.ToInt32(args[2]); k++)
        {
            var e = Read(Convert.ToInt64(args[1], 16) + k * 16, 16);
            long sp = BitConverter.ToInt64(e, 0); int sl = BitConverter.ToInt32(e, 8);
            string str = sp > 0x10000000000 && sp < 0x7FF000000000 && sl is > 1 and < 200 ? System.Text.Encoding.Unicode.GetString(Read(sp, (sl - 1) * 2)) : "-";
            Console.WriteLine($"{k,4}: {str}");
        }
        break;
    case "rec": // rec <name> <minutes>: record the module's writable data 4x a second (create research\scan\stop to end early)
        Record(args[1], double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture));
        break;
    case "nrec": // nrec <name> <minutes> <depth>: like rec, but for the player actor and the heap objects it points to
        NeighbourRecord(args[1], double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture), int.Parse(args[3]));
        break;
    case "paths": // paths <file> <expected>: keep chains that still resolve to the expected value
        var keep = File.ReadAllLines(Path.Combine(Dir, args[1])).Where(l => Chain(l.Split(' ')).EndsWith("= " + args[2])).ToList();
        File.WriteAllLines(Path.Combine(Dir, args[3]), keep);
        Console.WriteLine($"{keep.Count} jalur cocok");
        foreach (var l in keep.OrderBy(l => l.Length).Take(10)) Console.WriteLine($"{l}  ->  {Chain(l.Split(' '))}");
        break;
}
Console.WriteLine($"selesai dalam {sw.Elapsed.TotalSeconds:F1} detik");

List<(long Base, long Size)> Regions()
{
    var list = new List<(long, long)>();
    long addr = 0;
    while (Native.VirtualQueryEx(h, (IntPtr)addr, out var mbi, (uint)Marshal.SizeOf<Native.MBI>()) != 0)
    {
        long b = (long)mbi.BaseAddress, s = (long)mbi.RegionSize;
        bool rw = (mbi.Protect & 0xCC) != 0 && (mbi.Protect & 0x100) == 0; // RW / WC / XRW / XWC, no guard
        if (mbi.State == 0x1000 && rw) list.Add((b, s));
        addr = b + s;
        if (addr <= 0 || addr >= 0x7FFFFFFFFFFF) break;
    }
    return list;
}

// Writes <name>.base (first copy of every writable module region), <name>.diff (int sample, int module offset, byte value
// per changed byte; an offset that changed more than 300 times is noise and stops being logged) and <name>.tsv
// (sample, time, paused 0x59039B8, state 0x5A06764, state2 0x57E9ABB) for labelling samples by what the user did.
void Record(string name, double minutes)
{
    var regs = Regions().Where(r => r.Base >= modBase && r.Base < modEnd).ToList();
    var prev = regs.Select(r => Read(r.Base, (int)r.Size)).ToList();
    using (var bw = new BinaryWriter(File.Create(Path.Combine(Dir, name + ".base"))))
        for (int i = 0; i < regs.Count; i++) { bw.Write(regs[i].Base - modBase); bw.Write(prev[i].Length); bw.Write(prev[i]); }
    Console.WriteLine($"{regs.Count} region, {regs.Sum(r => r.Size) >> 20} MB");
    var hot = new Dictionary<int, int>();
    using var diff = new BinaryWriter(new BufferedStream(new FileStream(Path.Combine(Dir, name + ".diff"), FileMode.Create, FileAccess.Write, FileShare.Read), 1 << 20));
    using var tsv = new StreamWriter(new FileStream(Path.Combine(Dir, name + ".tsv"), FileMode.Create, FileAccess.Write, FileShare.Read));
    string stop = Path.Combine(Dir, "stop");
    File.Delete(stop);
    var end = DateTime.Now.AddMinutes(minutes);
    for (int n = 1; DateTime.Now < end && !File.Exists(stop); n++)
    {
        Thread.Sleep(250);
        for (int i = 0; i < regs.Count; i++)
        {
            var cur = Read(regs[i].Base, (int)regs[i].Size);
            var old = prev[i];
            int rel = (int)(regs[i].Base - modBase);
            for (int j = 0; j < cur.Length; j++)
            {
                if (cur[j] == old[j]) continue;
                int off = rel + j;
                int c = hot.GetValueOrDefault(off) + 1;
                hot[off] = c;
                if (c <= 300) { diff.Write(n); diff.Write(off); diff.Write(cur[j]); }
            }
            prev[i] = cur;
        }
        var s = Read(modBase + 0x59039B8, 1)[0]; var st = Read(modBase + 0x5A06764, 1)[0]; var s2 = Read(modBase + 0x57E9ABB, 1)[0];
        tsv.WriteLine($"{n}\t{DateTime.Now:HH:mm:ss.f}\t{s}\t{st}\t{s2}");
        if (n % 40 == 0) { tsv.Flush(); diff.Flush(); }
    }
    Console.WriteLine($"{hot.Count} offset berubah");
}

// The player actor ([[module+0x53DD150]+0x60], 0x1000 bytes) plus every heap object it points to (0x800 bytes each),
// followed <depth> levels, laid end to end as one buffer and recorded like Record (<name>.base holds one region at 0,
// <name>.blocks maps buffer offsets back to addresses: "start addr length").
void NeighbourRecord(string name, double minutes, int depth)
{
    long actor = BitConverter.ToInt64(Read(BitConverter.ToInt64(Read(modBase + 0x53DD150, 8)) + 0x60, 8));
    if (actor == 0) throw new Exception("aktor pemain tidak ketemu");
    var regs = Regions();
    bool Heap(long p) => (p & 7) == 0 && (p < modBase || p >= modEnd) && regs.Any(r => p >= r.Base && p + 0x800 <= r.Base + r.Size);
    var blocks = new List<(long Addr, int Len)> { (actor, 0x1000) };
    var seen = new HashSet<long> { actor };
    var level = new List<(long, int)>(blocks);
    for (int d = 0; d < depth && blocks.Count < 20000; d++)
    {
        var next = new List<(long, int)>();
        foreach (var (a, len) in level)
        {
            var buf = Read(a, len);
            for (int o = 0; o + 8 <= len; o += 8)
            {
                long p = BitConverter.ToInt64(buf, o);
                if (blocks.Count + next.Count >= 20000 || !Heap(p) || !seen.Add(p)) continue;
                next.Add((p, 0x800));
            }
        }
        blocks.AddRange(next);
        level = next;
    }
    int total = blocks.Sum(b => b.Len);
    Console.WriteLine($"aktor 0x{actor:X}, {blocks.Count} objek, {total >> 10} KB");
    byte[] Snapshot()
    {
        var all = new byte[total];
        int pos = 0;
        foreach (var (a, len) in blocks) { Tmp(a, len, all, pos); pos += len; }
        return all;
    }
    var prev = Snapshot();
    using (var w = new StreamWriter(Path.Combine(Dir, name + ".blocks")))
    {
        int pos = 0;
        foreach (var (a, len) in blocks) { w.WriteLine($"{pos} {a:X} {len}"); pos += len; }
    }
    using (var bw = new BinaryWriter(File.Create(Path.Combine(Dir, name + ".base")))) { bw.Write(0L); bw.Write(prev.Length); bw.Write(prev); }
    var hot = new Dictionary<int, int>();
    using var diff = new BinaryWriter(new BufferedStream(new FileStream(Path.Combine(Dir, name + ".diff"), FileMode.Create, FileAccess.Write, FileShare.Read), 1 << 20));
    using var tsv = new StreamWriter(new FileStream(Path.Combine(Dir, name + ".tsv"), FileMode.Create, FileAccess.Write, FileShare.Read));
    string stop = Path.Combine(Dir, "stop");
    File.Delete(stop);
    var end = DateTime.Now.AddMinutes(minutes);
    for (int n = 1; DateTime.Now < end && !File.Exists(stop); n++)
    {
        Thread.Sleep(250);
        var cur = Snapshot();
        for (int j = 0; j < cur.Length; j++)
        {
            if (cur[j] == prev[j]) continue;
            int c = hot.GetValueOrDefault(j) + 1;
            hot[j] = c;
            if (c <= 300) { diff.Write(n); diff.Write(j); diff.Write(cur[j]); }
        }
        prev = cur;
        var s = Read(modBase + 0x59039B8, 1)[0]; var st = Read(modBase + 0x5A06764, 1)[0]; var s2 = Read(modBase + 0x57E9ABB, 1)[0];
        tsv.WriteLine($"{n}\t{DateTime.Now:HH:mm:ss.f}\t{s}\t{st}\t{s2}");
        if (n % 40 == 0) { tsv.Flush(); diff.Flush(); }
    }
    Console.WriteLine($"{hot.Count} offset berubah");
}

byte[] Tmp(long addr, int len, byte[] into, int pos)
{
    var b = Read(addr, len);
    Buffer.BlockCopy(b, 0, into, pos, len);
    return into;
}

byte[] Read(long addr, int size)
{
    var buf = new byte[size];
    Native.ReadProcessMemory(h, (IntPtr)addr, buf, size, out _);
    return buf;
}

void Snap(string path)
{
    long total = 0;
    using var f = new BinaryWriter(new BufferedStream(File.Create(path), 1 << 22));
    foreach (var (b, s) in Regions())
    {
        f.Write(b); f.Write(s);
        for (long o = 0; o < s; o += 1 << 22)
            f.Write(Read(b + o, (int)Math.Min(1 << 22, s - o)));
        total += s;
    }
    Console.WriteLine($"snapshot {total / (1 << 20)} MB");
}

void Inc(string snapPath, string outPath, bool anyChange = false, bool floats = false, bool anyByte = false)
{
    // Index of the old snapshot: base, size, file offset of data.
    var index = new List<(long Base, long Size, long Off)>();
    using (var r = new BinaryReader(File.OpenRead(snapPath)))
        while (r.BaseStream.Position < r.BaseStream.Length)
        {
            long b = r.ReadInt64(), s = r.ReadInt64();
            index.Add((b, s, r.BaseStream.Position));
            r.BaseStream.Seek(s, SeekOrigin.Current);
        }
    using var snap = File.OpenRead(snapPath);
    using var w = new BinaryWriter(new BufferedStream(File.Create(outPath), 1 << 22));
    long count = 0;
    foreach (var (b, s) in Regions())
        foreach (var old in index.Where(i => i.Base < b + s && b < i.Base + i.Size))
        {
            long start = Math.Max(b, old.Base), end = Math.Min(b + s, old.Base + old.Size);
            for (long a = start; a < end; a += 1 << 22)
            {
                int len = (int)Math.Min(1 << 22, end - a);
                var now = Read(a, len);
                var before = new byte[len];
                snap.Seek(old.Off + (a - old.Base), SeekOrigin.Begin);
                snap.ReadExactly(before);
                if (anyByte)
                {
                    // Every byte that changed, either way (settings menus pause the game, so few do).
                    for (int i = 0; i < len; i++)
                        if (now[i] != before[i]) { Rec(w, a + i, 1, before[i], now[i]); count++; }
                    continue;
                }
                if (floats)
                {
                    // Positions: finite floats under 10 km that moved 30 cm .. 50 m.
                    for (int i = 0; i + 4 <= len; i += 4)
                    {
                        float fo = BitConverter.ToSingle(before, i), fn = BitConverter.ToSingle(now, i);
                        if (float.IsFinite(fo) && float.IsFinite(fn) && Math.Abs(fo) < 1e6f && Math.Abs(fn) < 1e6f && Math.Abs(fn - fo) is >= 30f and < 5000f)
                        { Rec(w, a + i, 4, BitConverter.ToInt32(before, i), BitConverter.ToInt32(now, i)); count++; }
                    }
                    continue;
                }
                for (int i = 0; i < len; i++)
                {
                    if (now[i] == before[i]) continue;
                    if (anyChange)
                    {
                        if ((i & 3) == 0 && i + 4 <= len)
                        {
                            int o2 = BitConverter.ToInt32(before, i), n2 = BitConverter.ToInt32(now, i);
                            if (o2 != n2 && o2 is > 0 and < 100_000 && n2 is > 0 and < 100_000) { Rec(w, a + i, 4, o2, n2); count++; }
                        }
                        continue;
                    }
                    if (now[i] > before[i] && now[i] - before[i] <= 16) { Rec(w, a + i, 1, before[i], now[i]); count++; }
                    if ((i & 3) == 0 && i + 4 <= len)
                    {
                        int o = BitConverter.ToInt32(before, i), n = BitConverter.ToInt32(now, i);
                        if (o >= 0 && n > o && n - o <= 1000 && n < 1_000_000) { Rec(w, a + i, 4, o, n); count++; }
                    }
                }
            }
        }
    Console.WriteLine($"kandidat: {count:N0}");
}

// Finds int32 sequences v0, v1, v2... spaced by a constant stride (an array of item counts, say).
void Seq(int[] values)
{
    int hits = 0;
    foreach (var (b, s) in Regions())
        for (long a = b; a < b + s; a += 1 << 22)
        {
            int len = (int)Math.Min((1 << 22) + 4096, b + s - a);
            var buf = Read(a, len);
            for (int i = 0; i + 4 <= Math.Min(len, 1 << 22); i += 2)
            {
                if (BitConverter.ToInt16(buf, i) != values[0]) continue;
                foreach (int width in new[] { 2, 4 })
                    for (int stride = width; stride <= 128; stride += 2)
                    {
                        bool ok = true;
                        for (int k = 1; k < values.Length && ok; k++)
                        {
                            int p = i + k * stride;
                            ok = p + width <= len && (width == 2 ? BitConverter.ToInt16(buf, p) : BitConverter.ToInt32(buf, p)) == values[k];
                        }
                        if (ok && (width == 2 || BitConverter.ToInt32(buf, i) == values[0]) && hits++ < 40)
                            Console.WriteLine($"0x{a + i:X} lebar {width} stride {stride}");
                    }
            }
        }
    Console.WriteLine($"{hits} temuan");
}

void Dump(long addr, int size)
{
    var buf = Read(addr, size);
    for (int i = 0; i < size; i += 16)
        Console.WriteLine($"{addr + i:X}: {BitConverter.ToString(buf, i, Math.Min(16, size - i)).Replace("-", " ")}");
}

// Records stamped "obtained in the last N seconds": aligned uint32 unix times followed by a zero dword.
void Recent(int seconds, bool any)
{
    long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    int hits = 0;
    foreach (var (b, s) in Regions())
        for (long a = b; a < b + s; a += 1 << 22)
        {
            int len = (int)Math.Min(1 << 22, b + s - a);
            var buf = Read(a, len);
            for (int i = 0; i + 0x18 <= len; i += any ? 4 : 8)
            {
                uint t = BitConverter.ToUInt32(buf, i);
                if (t < now - seconds || t > now + 5 || (!any && BitConverter.ToUInt32(buf, i + 4) != 0)) continue;
                if (hits++ < 60)
                    Console.WriteLine($"0x{a + i:X} t-{now - t}s  {BitConverter.ToString(buf, i, 0x18).Replace("-", " ")}");
            }
        }
    Console.WriteLine($"{hits} temuan");
}

// Logs every int32 change in a range every 2 seconds (skipping noisy spots given as hex offsets).
void Watch(long start, int size, HashSet<int> skip)
{
    var prev = Read(start, size);
    while (true)
    {
        Thread.Sleep(2000);
        var now = Read(start, size);
        for (int i = 0; i + 4 <= size; i += 4)
        {
            int o = BitConverter.ToInt32(prev, i), n = BitConverter.ToInt32(now, i);
            if (o != n && !skip.Contains(i & ~0xF))
                Console.WriteLine($"{DateTime.Now:HH:mm:ss} +0x{i:X}: {o} -> {n}");
        }
        prev = now;
    }
}
// Finds a text in memory as UTF-16 and ASCII; prints addresses and some context.
// Every qword that points exactly at one of the addresses, with the UTF-16 strings (FString: pointer, length,
// capacity) found in the 0x60 bytes around it, to see which key or object holds a text.
void Who(HashSet<long> targets)
{
    var hits = new List<(long Loc, long Target)>();
    foreach (var (b, s) in Regions())
        for (long a = b; a < b + s; a += 1 << 22)
        {
            int len = (int)Math.Min(1 << 22, b + s - a);
            var buf = Read(a, len);
            for (int i = 0; i + 8 <= len; i += 8)
                if (targets.Contains(BitConverter.ToInt64(buf, i))) hits.Add((a + i, BitConverter.ToInt64(buf, i)));
        }
    foreach (var (loc, target) in hits)
    {
        Console.WriteLine($"0x{loc:X} -> 0x{target:X}");
        var around = Read(loc - 0x60, 0xC0);
        for (int i = 0; i + 16 <= around.Length; i += 8)
        {
            long p = BitConverter.ToInt64(around, i);
            int l = BitConverter.ToInt32(around, i + 8);
            if (p < 0x10000000000 || p > 0x7FF000000000 || l < 2 || l > 200) continue;
            var text = System.Text.Encoding.Unicode.GetString(Read(p, (l - 1) * 2));
            if (text.All(c => c >= 32 && c < 0xD800)) Console.WriteLine($"   {i - 0x60:+0;-0}: \"{text}\"");
        }
    }
    Console.WriteLine($"{hits.Count} pointer");
}

void FindText(string text, int max)
{
    var patterns = new[] { (System.Text.Encoding.Unicode.GetBytes(text), "utf16"), (System.Text.Encoding.ASCII.GetBytes(text), "ascii") };
    int hits = 0;
    foreach (var (b, s) in Regions())
        for (long a = b; a < b + s; a += 1 << 22)
        {
            int len = (int)Math.Min((1 << 22) + 256, b + s - a);
            var buf = Read(a, len);
            foreach (var (p, kind) in patterns)
                for (int i = buf.AsSpan(0, Math.Min(len, 1 << 22)).IndexOf(p); i >= 0; )
                {
                    if (hits++ < max) Console.WriteLine($"{kind} 0x{a + i:X}");
                    int next = buf.AsSpan(i + 1, Math.Min(len, 1 << 22) - i - 1).IndexOf(p);
                    i = next < 0 ? -1 : i + 1 + next;
                }
        }
    Console.WriteLine($"{hits} temuan");
}
// Dumps "$Item_..." localization keys with the text that follows them (name, plural, lowercase) to items.txt.
void DumpItemNames()
{
    var key = System.Text.Encoding.Unicode.GetBytes("$Item_");
    var pairs = new SortedDictionary<string, string>();
    foreach (var (b, s) in Regions())
        for (long a = b; a < b + s; a += 1 << 22)
        {
            int len = (int)Math.Min((1 << 22) + 512, b + s - a);
            var buf = Read(a, len);
            for (int i = buf.AsSpan(0, Math.Min(len, 1 << 22)).IndexOf(key); i >= 0 && i + 512 <= len; )
            {
                var texts = new List<string>();
                int p = i;
                while (texts.Count < 3 && p + 2 < i + 512)
                {
                    int end = p;
                    while (end + 1 < i + 512 && (buf[end] != 0 || buf[end + 1] != 0)) end += 2;
                    if (end > p) texts.Add(System.Text.Encoding.Unicode.GetString(buf, p, end - p));
                    p = end + 2;
                    while (p + 1 < i + 512 && buf[p] == 0 && buf[p + 1] == 0) p += 2;
                }
                if (texts.Count >= 2 && !texts[1].StartsWith("$") && texts[1].Length < 60 && !texts[0].EndsWith("_help"))
                    pairs.TryAdd(texts[0], texts[1]);
                int next = buf.AsSpan(i + 2, Math.Min(len, 1 << 22) - i - 2).IndexOf(key);
                i = next < 0 ? -1 : i + 2 + next;
            }
        }
    File.WriteAllLines(Path.Combine(Dir, "items.txt"), pairs.Select(kv => $"{kv.Key}\t{kv.Value}"));
    Console.WriteLine($"{pairs.Count} nama item");
}
// Raw snapshot of one address range (the save data), and a diff of two such snapshots.
void RangeSnap(string name, long start, int size) => File.WriteAllBytes(Path.Combine(Dir, name + ".raw"), Read(start, size));

void RangeDiff(string a, string b, long start)
{
    var x = File.ReadAllBytes(Path.Combine(Dir, a + ".raw"));
    var y = File.ReadAllBytes(Path.Combine(Dir, b + ".raw"));
    int shown = 0;
    for (int i = 0; i + 4 <= Math.Min(x.Length, y.Length); i += 4)
    {
        int o = BitConverter.ToInt32(x, i), n = BitConverter.ToInt32(y, i);
        if (o != n && shown++ < 200) Console.WriteLine($"+0x{i:X} (0x{start + i:X}): {o} -> {n}   [{BitConverter.ToString(x, i, 4)} -> {BitConverter.ToString(y, i, 4)}]");
    }
    Console.WriteLine($"{shown} perbedaan");
}
// Signature search for the item list (gil record: id 20, category 1) and materia list (runs of materia records).
void FindLists()
{
    foreach (var (b, s) in Regions())
        for (long a = b; a < b + s; a += 1 << 22)
        {
            int len = (int)Math.Min(1 << 22, b + s - a);
            var buf = Read(a, len);
            for (int i = 0; i + 0x20 * 6 <= len; i += 8)
            {
                if (BitConverter.ToInt32(buf, i + 4) != 0) continue;
                if (BitConverter.ToInt32(buf, i + 8) == 20 && buf[i + 16] == 1 && BitConverter.ToInt32(buf, i + 20) == 0
                    && BitConverter.ToUInt32(buf, i) > 1_500_000_000)
                    Console.WriteLine($"gil 0x{a + i:X} = {BitConverter.ToInt32(buf, i + 12)}");
                bool run = true;
                for (int k = 0; k < 6 && run; k++)
                {
                    int p = i + k * 0x20, id = BitConverter.ToInt32(buf, p + 20);
                    run = BitConverter.ToInt32(buf, p + 4) == 0 && BitConverter.ToInt32(buf, p + 8) == k && BitConverter.ToInt32(buf, p + 12) == 0
                        && id is >= 10000 and < 20000 && buf[p + 16] is >= 1 and <= 5;
                }
                if (run) Console.WriteLine($"materia 0x{a + i:X}");
            }
        }
}
// Known-value scan: every 4-byte-aligned int32 equal to value.
// find <value> <out> [width]: width 4 (aligned int, default), 2 (aligned ushort) or 1 (byte).
void Find(int value, string outPath, int width)
{
    using var w = new BinaryWriter(new BufferedStream(File.Create(outPath), 1 << 22));
    long count = 0;
    foreach (var (b, s) in Regions())
        for (long a = b; a < b + s; a += 1 << 22)
        {
            int len = (int)Math.Min(1 << 22, b + s - a);
            var buf = Read(a, len);
            for (int i = 0; i + width <= len; i += width)
                if ((width == 4 ? BitConverter.ToInt32(buf, i) : width == 2 ? BitConverter.ToUInt16(buf, i) : buf[i]) == value)
                { Rec(w, a + i, (byte)width, value, value); count++; }
        }
    Console.WriteLine($"kandidat: {count:N0}");
}

string ScanDir()
{
    for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        if (File.Exists(Path.Combine(d.FullName, "overlay", "GameTracker.csproj")))
            return Directory.CreateDirectory(Path.Combine(d.FullName, "research", "scan")).FullName;
    return Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "scan")).FullName;
}

// An FName's text from the FNamePool: block index << 16 | offset / 2; entries start with a 2-byte header (length << 6 | wide).
string FName(int index)
{
    long blocks = modBase + 0x5981310;
    long block = BitConverter.ToInt64(Read(blocks + 8L * (index >> 16), 8));
    if (block == 0) return "?";
    long entry = block + 2L * (index & 0xFFFF);
    int header = BitConverter.ToUInt16(Read(entry, 2)), length = header >> 6;
    if (length is <= 0 or > 1024) return "?";
    return (header & 1) != 0 ? System.Text.Encoding.Unicode.GetString(Read(entry + 2, length * 2)) : System.Text.Encoding.ASCII.GetString(Read(entry + 2, length));
}

// Every ASCII FNamePool entry: at any even offset of a block, a header (length << 6) followed by that many printable
// characters and a 0 byte (this build ends names with 0 and leaves gaps, so a sequential walk loses its place).
IEnumerable<(int Index, string Text)> FNames()
{
    long blocks = modBase + 0x5981310;
    for (int bi = 0; bi < 8192; bi++)
    {
        long block = BitConverter.ToInt64(Read(blocks + 8L * bi, 8));
        if (block == 0) yield break;
        var data = Read(block, 0x20000);
        for (int off = 0; off + 3 <= data.Length; off += 2)
        {
            int header = BitConverter.ToUInt16(data, off), length = header >> 6;
            if ((header & 1) != 0 || length == 0 || off + 2 + length >= data.Length || data[off + 2 + length] != 0) continue;
            bool ok = true;
            for (int k = off + 2; k < off + 2 + length && ok; k++) ok = data[k] is >= 0x20 and < 0x7F;
            if (!ok) continue;
            yield return (bi << 16 | off / 2, System.Text.Encoding.ASCII.GetString(data, off + 2, length));
            off += (length + 1) & ~1;
        }
    }
}

float F(int bits) => BitConverter.Int32BitsToSingle(bits);

void Rec(BinaryWriter w, long addr, byte width, int first, int last) { w.Write(addr); w.Write(width); w.Write(first); w.Write(last); }

IEnumerable<(long Addr, byte Width, int First, int Last)> Load(string path)
{
    using var r = new BinaryReader(new BufferedStream(File.OpenRead(path), 1 << 22));
    while (r.BaseStream.Position < r.BaseStream.Length)
        yield return (r.ReadInt64(), r.ReadByte(), r.ReadInt32(), r.ReadInt32());
}

int Current(long addr, byte width, Dictionary<long, byte[]> pages)
{
    long page = addr & ~0xFFFL;
    if (!pages.TryGetValue(page, out var buf)) pages[page] = buf = Read(page, 0x1004);
    int off = (int)(addr - page);
    return width == 1 ? buf[off] : width == 2 ? BitConverter.ToUInt16(buf, off) : BitConverter.ToInt32(buf, off);
}

void Filter(string inPath, string outPath, string op)
{
    var pages = new Dictionary<long, byte[]>();
    long count = 0;
    using (var w = new BinaryWriter(new BufferedStream(File.Create(outPath), 1 << 22)))
        foreach (var c in Load(inPath))
        {
            if (pages.Count > 200_000) pages.Clear();
            int now = Current(c.Addr, c.Width, pages);
            bool keep = op switch
            {
                "same" => now == c.Last,
                "inc" => now > c.Last && now - c.Last <= (c.Width == 1 ? 16 : 1000),
                "dec" => now < c.Last,
                "chg" => now != c.Last,
                "back" => now == c.First,
                _ when op.StartsWith("eq:") => now == int.Parse(op[3..]),
                // Floats (positions in cm): still, moved, kept moving the same way, or back near the first value.
                "fsame" => c.Width == 4 && Math.Abs(F(now) - F(c.Last)) < 1f,
                "fmoved" => c.Width == 4 && Math.Abs(F(c.Last) - F(c.First)) is >= 30f and < 5000f && Math.Abs(F(now) - F(c.Last)) < 1f,
                "fchg" => c.Width == 4 && Math.Abs(F(now) - F(c.Last)) is >= 30f and < 5000f,
                "fdir" => c.Width == 4 && Math.Abs(F(now) - F(c.Last)) is >= 30f and < 5000f && Math.Sign(F(now) - F(c.Last)) == Math.Sign(F(c.Last) - F(c.First)),
                "fnear" => c.Width == 4 && Math.Abs(F(now) - F(c.First)) < 150f && Math.Abs(F(now) - F(c.Last)) >= 30f,
                _ => throw new Exception("op tidak dikenal"),
            };
            if (keep) { Rec(w, c.Addr, c.Width, c.First, now); count++; }
        }
    Console.WriteLine($"kandidat tersisa: {count:N0}");
    if (count <= 60) Show(outPath);
}

void Show(string path)
{
    var pages = new Dictionary<long, byte[]>();
    foreach (var c in Load(path).Take(60))
    {
        string where = c.Addr >= modBase && c.Addr < modEnd ? $"modul+0x{c.Addr - modBase:X}" : $"heap 0x{c.Addr:X}";
        Console.WriteLine($"{where,-24} w{c.Width} awal={c.First} terakhir={c.Last} sekarang={Current(c.Addr, c.Width, pages)}");
    }
}

// Reverse pointer scan: find chains module+X -> [o1] -> ... -> target that survive a game restart.
void PointerScan(long target, int depth, int maxOff, int cap)
{
    var regions = Regions();
    long lo = regions.Min(r => r.Base), hi = regions.Max(r => r.Base + r.Size);
    // parent[loc] = (pointee, offset): the qword at loc points to pointee - offset.
    var parent = new Dictionary<long, (long Next, int Off)>();
    var level = new List<long> { target };
    var roots = new List<long>();
    for (int d = 1; d <= depth && level.Count > 0; d++)
    {
        var sorted = level.Distinct().OrderBy(x => x).ToArray();
        long minT = sorted[0] - maxOff, maxT = sorted[^1];
        var found = new List<(long Loc, long Target, int Off)>();
        foreach (var (b, s) in regions)
            for (long a = b; a < b + s; a += 1 << 22)
            {
                int len = (int)Math.Min(1 << 22, b + s - a);
                var buf = Read(a, len);
                for (int i = 0; i + 8 <= len; i += 8)
                {
                    long v = BitConverter.ToInt64(buf, i);
                    if (v < minT || v > maxT) continue;
                    int k = Array.BinarySearch(sorted, v);
                    if (k < 0) k = ~k;
                    for (int n = 0; k < sorted.Length && sorted[k] - v <= maxOff && n < 4; k++, n++)
                        found.Add((a + i, sorted[k], (int)(sorted[k] - v)));
                }
            }
        var next = new List<long>();
        foreach (var f in found.OrderBy(f => f.Off))
        {
            if (parent.ContainsKey(f.Loc) || f.Loc == target) continue;
            parent[f.Loc] = (f.Target, f.Off);
            if (f.Loc >= modBase && f.Loc < modEnd) roots.Add(f.Loc);
            else if (next.Count < cap) next.Add(f.Loc);
        }
        Console.WriteLine($"level {d}: {found.Count:N0} pointer, {roots.Count} jalur statis sejauh ini");
        level = next;
    }
    using var w = new StreamWriter(Path.Combine(Dir, $"ptr_{target:X}.txt"));
    foreach (var r in roots)
    {
        var parts = new List<string> { $"modul+0x{r - modBase:X}" };
        for (long loc = r; parent.TryGetValue(loc, out var p) && loc != target; loc = p.Next)
            parts.Add($"0x{p.Off:X}");
        w.WriteLine(string.Join(" ", parts));
    }
    Console.WriteLine($"{roots.Count} jalur ditulis ke ptr_{target:X}.txt");
}

// Follows "modul+0xX 0xO1 0xO2 ..." and returns the value at the end (int32) and the final address.
string Chain(string[] parts)
{
    long addr = modBase + Convert.ToInt64(parts[0].Replace("modul+", ""), 16);
    foreach (var off in parts.Skip(1))
    {
        long ptr = BitConverter.ToInt64(Read(addr, 8));
        if (ptr == 0) return "null";
        addr = ptr + Convert.ToInt64(off, 16);
    }
    return $"0x{addr:X} = {BitConverter.ToInt32(Read(addr, 4))}";
}

static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    public struct MBI { public IntPtr BaseAddress, AllocationBase; public uint AllocationProtect; public ushort PartitionId; public IntPtr RegionSize; public uint State, Protect, Type; }
    [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] public static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out IntPtr read);
    [DllImport("kernel32.dll")] public static extern int VirtualQueryEx(IntPtr h, IntPtr addr, out MBI mbi, uint len);
}

