// Manual checklist ticks live in the webview's localStorage, per game.
function key(appId: number) {
  return `checked:${appId}`;
}

export function loadChecked(appId: number): Set<string> {
  try {
    return new Set(JSON.parse(localStorage.getItem(key(appId)) ?? "[]"));
  } catch {
    return new Set();
  }
}

export function saveChecked(appId: number, ids: Set<string>) {
  try {
    localStorage.setItem(key(appId), JSON.stringify([...ids]));
  } catch {
    // Storage unavailable: ticks just won't persist.
  }
}
