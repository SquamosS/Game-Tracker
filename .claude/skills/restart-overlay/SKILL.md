---
name: restart-overlay
description: Build GameTracker and restart the FF7R overlay so a change takes effect (stop the running exe, build, launch with --game ff7r, check it runs and crash.log). Use after any overlay code change, or when the user asks to restart/apply.
---

# Restart overlay

The user allowed restarting the overlay for fixes at any time; say in one line that it was restarted.

1. Stop the running app (its exe locks the build output):
   `Stop-Process -Name GameTracker -Force -ErrorAction SilentlyContinue; Start-Sleep -Seconds 1`
2. Build (must be 0 warnings, 0 errors; the repo keeps it clean):
   `Set-Location "D:\Claude Project\GameTracker\overlay"; dotnet build -nologo -v q`
   A build that only says "Time Elapsed ~1s" may not have recompiled; if unsure, grep the DLL in `bin\Debug\net8.0-windows` for a new identifier.
3. Launch straight into the overlay (no dashboard):
   `Start-Process "D:\Claude Project\GameTracker\overlay\bin\Debug\net8.0-windows\GameTracker.exe" -ArgumentList "--game","ff7r"`
4. Check after ~5 s: `Get-Process GameTracker` (Responding), `data\logs\crash.log` (should not exist or have nothing new), and for reader changes the last line of `data\logs\objective-search.log`.
5. Guide-only changes (`overlay/games/ff7r/guide.json`) need no restart: copy the file to `overlay\bin\Debug\net8.0-windows\games\ff7r\` and the overlay reloads it (FileSystemWatcher).
6. Commit and push to `backup/local-2026-10-08` (auto-push is allowed).

Do not build to `bin` while the user plays with the app running; if a build must happen without stopping it, build to a scratch folder with `-o`.
