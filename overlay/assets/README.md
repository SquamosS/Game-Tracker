# Game pictures

One folder per game, named after the game's id in `GameModule.cs` (`ff7r`, ...):

```
assets/games/<id>/
  background.jpg   1920x1080 (16:9)   dashboard background; main character on the right half
  cover.jpg        600x900   (2:3)    library card cover
  icon.png         256x256            tray / taskbar icon
  source/                             the original files these were made from
```

All three are optional (`.jpg`, `.jpeg` or `.png`). The dashboard reads them when it opens, so a picture
can be swapped without rebuilding; the tray icon changes after a restart.
