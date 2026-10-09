# Game images

One folder per game, named by the game's id:

```
assets/games/<game-id>/
  background.jpg   dashboard background, 1920x1080 (16:9), main character right of centre
  cover.jpg        card cover, 600x900 or larger at 2:3
  icon.png         optional, 256x256, tray and taskbar icon
```

The dashboard loads these by name; a missing file falls back to a plain colour.
