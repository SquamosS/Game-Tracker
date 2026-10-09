using System.Text.RegularExpressions;

namespace GameTracker;

/// <summary>
/// The area the game's map names for where you stand, read while the map screen is open.
///
/// The map's area label is a text widget (vtable module+0x4BEEA88: the class of every menu text, about 2,500 instances)
/// holding the area's localization key at +0x118, e.g. "$navi070_name_part007_600" (part 600 on layer 007 of map 070),
/// named in the localization table, with "$navi070_name_layer007" naming the floor. The label is made when the map opens
/// and destroyed when it closes, and no lasting object points at it, so it is looked for with a memory scan while the
/// game stands still (the map pauses it) and trusted only while its vtable is intact. Steam 1.0.0.7 (research\notes.md).
/// </summary>
public sealed partial class Ff7rChapterReader
{
    const long TextVtableRva = 0x4BEEA88;

    long TextVtable => Version == "Steam 1.0.0.7" ? (long)_moduleBase + TextVtableRva : 0;

    /// <summary>"$navi..." key -> English text, from the objective search's pass over the localization table.</summary>
    Dictionary<string, string> _naviTexts = new();
    long _mapLabel;
    Task<long>? _labelSearch;

    public record MapArea(string Key, string Area, string? Floor);

    /// <summary>The area the open map shows, or null while no label is known, the map is closed, or its name is unknown.</summary>
    public MapArea? ReadMapArea()
    {
        if (!Attach() || TextVtable == 0) return null;
        if (_labelSearch is { IsCompleted: true })
        {
            _mapLabel = _labelSearch.IsCompletedSuccessfully ? _labelSearch.Result : 0;
            _labelSearch = null;
        }
        if (_mapLabel == 0 || LabelKey(_mapLabel) is not { } key) return null;
        var texts = _naviTexts;
        if (texts.GetValueOrDefault(key) is not { } area) return null;
        int part = key.IndexOf("_name_part") + "_name_part".Length;
        return new MapArea(key, area, texts.GetValueOrDefault(key[..(part - 4)] + "layer" + key.Substring(part, 3)));
    }

    /// <summary>Starts looking for the map's area label (one full scan at low priority) unless one is running.</summary>
    public void LookForMapLabel()
    {
        if (Attach() && TextVtable != 0 && _labelSearch is null) _labelSearch = Scan(FindMapLabel);
    }

    long FindMapLabel(CancellationToken cancel)
    {
        long vtable = TextVtable;
        var found = new System.Collections.Concurrent.ConcurrentBag<long>();
        ForEachChunk(cancel, (a, buf, length) =>
        {
            for (int i = 0; i + 8 <= length; i += 8)
                if (BitConverter.ToInt64(buf, i) == vtable && LabelKey(a + i) is not null) found.Add(a + i);
        });
        // The map labels the area you are in; labels naming different areas would leave open which one that is.
        var labels = found.ToList();
        return labels.Select(LabelKey).Distinct().Count() == 1 ? labels[0] : 0;
    }

    static readonly Regex AreaKey = new(@"^\$navi\d{3}_name_part\d{3}_\d{3}$");

    /// <summary>The label's area key while it is a live text widget holding one; null once it was destroyed or reused.</summary>
    string? LabelKey(long label)
    {
        if (ReadInt64(label) != TextVtable) return null;
        long text = ReadInt64(label + 0x118);
        int length = ReadInt32(label + 0x120);
        if (text == 0 || length is < 20 or > 40) return null;
        string key = ReadUtf16(text, length);
        return AreaKey.IsMatch(key) ? key : null;
    }
}
