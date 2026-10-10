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
- Names and objects (added 10 Oct): `fnames <regex>` (search the FNamePool; this build ends each name with a 0 byte and leaves gaps, so names are found at any even offset, not by a sequential walk), `uobjs <regex> [x y z]` (objects named like regex, then instances of those classes; with a position, float3 within 5 m in the object or objects it points to), `fields <addr> <hexlen>` (FName indexes, named object pointers and FStrings in a block), `rows <addr> <count> <stride> [regex]` (data-table rows: FName key with number + int at +0x10), `hex <bytes> [max]`.
- Game data tables (UEndDataObject*): rows TArray at +0x38, count +0x40; each row starts with an FName (index, then number: "E_ARM" number 2003 = "E_ARM_2002"). Item 0x158/id +0x10, Equipment 0x288, Materia 0x160, Reward 0x50 (items TArray +0x28), ObjectTreasure per map 0xA0 (id, flag name +0x18, point +0x30, rewards +0x38..). Vtables in `Ff7rTreasure.cs`.
- Chests and areas: `chests <x y z>` (every loaded chest with position, distance, contents, save-flag guess), `chestdump <file>` (TSV for the audit), `points <addr> <before> <after>` (locator rows: FName, ptr, quat, xyz +0x20, scale 1,1,1), `volumes <x y z> [all]` (area volumes holding a point), `rewards <table> <regex>`.
- Save data: `flagbits <bit...>` (bits of the flag block materia+0x40E00 in every save copy), `flaglive <bit> [copy]`, `savediff <old> <new> <from> <len>` (bytes changed between two save copies; the old copy survives in memory until the next load). `chestrec <min>` records chest actors 4x/s but does a full scan every 20 s at normal priority: only for short sessions.
- While a background scanner command runs, its exe is locked: build to another folder (`dotnet build -c Release -o ../../research/scan/alt`) and run `dotnet ../../research/scan/alt/scanner.dll <cmd>`.
- Python edit/analysis scripts: write them to the scratchpad with the Write tool and run `python -I <file>`; a bash heredoc holding `'''` breaks in this shell.

## Chest opened flag (found 10 Oct 2026)
- Bit = chest flag number (flag row int +0x10, `chests` prints it) + 0xA80 in the flag block (materia+0x40E00) of the LIVE save copy; other copies follow at the next save. Check with `flagbits <bit>`: the live copy is the one that differs. Overlay: `Ff7rTreasure.cs` ChestOpened, log `data/ff7r/logs/chest-flag.log`.
- Same snapshot method for other one-shot flags: snap before, act, snap after, `bdiff` + `same`, then keep changes within the save copies (`flagbits` lists them) before looking anywhere else.

## Into the overlay
- Overlay = canvas for many games (CLAUDE.md "Arsitektur"): every address, table name, item id, unit and game rule goes in
  `overlay/games/ff7r/reader/`; shared code (`overlay/*.cs`, `overlay/Modules/`) only gets game-neutral concepts (quest entry,
  chest, position, area) and talks to the game only through `IGameReader` (`overlay/GameReader.cs`).
- A new kind of value for the overlay: add a member to `IGameReader` with a default "not known" body (and a `virtual` one in
  `GameReaderBase`), implement it `public` with the exact type in `Ff7rChapterReader` (else the default silently wins: add a check in
  `tools/tracker-tests`), and use it from a module in `overlay/Modules/<feature>/`, not from MainWindow.
- New partial file `overlay/games/ff7r/reader/Ff7r<Thing>.cs` on `Ff7rChapterReader`, gated on `Version == "Steam 1.0.0.7"` (return null otherwise), doc comment stating offsets and how they were found.
- Heap objects: collect them by vtable in the objective search's second `ForEachChunk` pass (`Ff7rObjective.cs`), swap lists whole (the UI thread reads them), reset in `Detach()`. No extra full scans on a timer; if one is needed, rate-limit it (>= 30 s).
- Static values: read `_moduleBase + rva` each poll with the `out` overloads (no array per read).
- Keep the 1 s poll light (see CLAUDE.md "Aturan kode & performa"): big reads go through `Scan(...)` on a worker and merge on the UI thread; cache what rarely changes.
- Log new signals to `data/ff7r/logs/<name>.log` (`DataPaths.GameLogs("ff7r")`) first when their meaning is not certain, then act on them.
- Record the result (and failures) in `research/notes.md`, update `docs/HANDOFF-AI.md` section 0 for lasting features, restart with /restart-overlay, commit and push.
