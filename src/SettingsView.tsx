import { useState } from "react";
import { invoke } from "@tauri-apps/api/core";
import type { Settings } from "./types";

interface Props {
  settings: Settings;
  onSaved: (s: Settings) => void;
}

export default function SettingsView({ settings, onSaved }: Props) {
  const [form, setForm] = useState(settings);
  const [error, setError] = useState<string | null>(null);

  const save = async () => {
    const s = { ...form, apiKey: form.apiKey.trim(), steamId: form.steamId.trim() };
    try {
      await invoke("save_settings", { settings: s });
      onSaved(s);
    } catch (e) {
      setError(String(e));
    }
  };

  return (
    <div className="settings">
      <label>
        Steam Web API key
        <input
          type="password"
          value={form.apiKey}
          onChange={(e) => setForm({ ...form, apiKey: e.target.value })}
        />
        <small>Buat gratis di steamcommunity.com/dev/apikey (domain boleh diisi "localhost").</small>
      </label>
      <label>
        SteamID64
        <input value={form.steamId} onChange={(e) => setForm({ ...form, steamId: e.target.value })} />
        <small>Terisi otomatis dari Steam yang sedang login. Profil "Game details" harus Public.</small>
      </label>
      <label>
        Bahasa nama achievement
        <select value={form.language} onChange={(e) => setForm({ ...form, language: e.target.value })}>
          <option value="english">English</option>
          <option value="indonesian">Bahasa Indonesia (jika tersedia)</option>
        </select>
      </label>
      {error && <p className="error">{error}</p>}
      <div className="row">
        <button className="primary" onClick={save}>
          Simpan
        </button>
        <button onClick={() => invoke("quit")}>Keluar aplikasi</button>
      </div>
    </div>
  );
}
