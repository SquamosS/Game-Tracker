// Read-only memory scanner for finding FF7R's story-progress value.
// Usage:
//   scanner snap <name>              full snapshot of writable memory -> <name>.snap
//   scanner inc <name> <out>         candidates whose value went UP since snapshot <name>
//   scanner filter <in> <out> <op>   op: same | inc | dec | back (equal to value at first snapshot)
//   scanner show <in>                print candidates with current values
using System.Diagnostics;
using System.Runtime.InteropServices;

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

void Inc(string snapPath, string outPath, bool anyChange = false, bool floats = false)
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

