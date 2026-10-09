---
name: memory-research
description: Find a new value or structure in FINAL FANTASY VII REMAKE's memory (read-only) with the project's scanner, together with the user acting in game, then wire it into the overlay. Use when the user wants the overlay to know something new from the game (state, location, item, setting) or a research note says a value is still unknown.
---

# FF7R memory research

Read `research/notes.md` first: it lists every address found (and every dead end) on Steam 1.0.0.7. Memory is **read-only**; never write to the game.

## Working with the user
- One in-game action per message, in Indonesian, short: what to do, then "kabari saya". Never more than one step at a time.
- Prefer states the user can reproduce at will (open/close a menu, walk between two named areas, change an option). Menus pause the game, so memory is quiet there.
- Say plainly when a candidate turns out to be a coincidence and what the next approach is.
- Snapshots are ~8-9 GB on D: (`research/scan/`); delete them when the research ends.

## Scanner (`tools/ff7r-scan`, run from that folder)
`dotnet build -c Release -nologo -v q`, then `dotnet run -c Release --no-build -- <cmd>`. Files go to `research/scan/`.
- Known value: `find <v> <out> [4|2|1]` then `filter <in> <out> eq:<v>` after the user changes it.
- Unknown value: `snap <name>` (full memory) then `bdiff <snap> <out>` (every changed byte), `chg` (ints 0..100000), `fdiff` (floats moved 30 cm..50 m).
- Narrow: `filter <in> <out> same|chg|back|inc|dec|eq:N|fsame|fchg|fdir|fnear|fmoved`; `match <in> <ref> <out>` keeps candidates whose last value equals the ref file's last value (run `filter ... chg` first so "last" is current). `show <cand>`. Analyse pairs in Python (records: int64 addr, byte width, int32 first, int32 last; 17 bytes).
- Cycle pattern: state A snapshot -> B `bdiff` + `same` -> A `back` + `same` -> B `chg` + `match` + `same` -> repeat with a different variant of the state (e.g. main menu vs map) and finally a check in normal play.
- Prefer candidates in the module's static data (`modul+0x...`): stable across restarts. Module base this session: 0x7FF72A2C0000 (same after a game restart).
- Structures: `who <addr...>` (who points at an address, with nearby strings), `base <addr...>` (owning object and vtable), `obj <addr...>` (UObject name and class via FNamePool at module+0x5981310), `vt <vtable> <off> <val>` (instances of a class with a value), `strs`, `dump <addr> <hexlen>`, `text <string>`, `navi <file>` (all area names), `inventory <file>`, `module <file>` (module image for string/class-name search), `pair`, `vecnear`, `ptr <addr> <depth> <maxoff> <cap>` + `chain` (static pointer paths; verify across a battle, a room change and a game restart before trusting).
- Finding a class: its name string lives in the module (UTF-16); its FName index is block<<16 | offset/2 in the FNamePool; the UClass has that index at +0x18; instances have the UClass at +0x10 and share its vtable.

## Into the overlay
- New partial file `overlay/games/ff7r/reader/Ff7r<Thing>.cs` on `Ff7rChapterReader`, gated on `Version == "Steam 1.0.0.7"` (return null otherwise), doc comment stating offsets and how they were found.
- Heap objects: collect them by vtable in the objective search's second `ForEachChunk` pass (`Ff7rObjective.cs`), swap lists whole (the UI thread reads them), reset in `Detach()`. No extra full scans on a timer; if one is needed, rate-limit it (>= 30 s).
- Static values: read `_moduleBase + rva` each poll.
- Log new signals to `data/logs/<name>.log` first when their meaning is not certain, then act on them.
- Record the result (and failures) in `research/notes.md`, update `docs/HANDOFF-AI.md` section 0 for lasting features, restart with /restart-overlay, commit and push.
