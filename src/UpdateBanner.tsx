import { useEffect, useState } from "react";
import { invoke } from "@tauri-apps/api/core";
import { openUrl } from "@tauri-apps/plugin-opener";

interface UpdateInfo {
  version: string;
  url: string;
}

export default function UpdateBanner() {
  const [update, setUpdate] = useState<UpdateInfo | null>(null);

  useEffect(() => {
    invoke<UpdateInfo | null>("check_update").then(setUpdate).catch(() => {});
  }, []);

  if (!update) return null;
  return (
    <button className="update" onClick={() => openUrl(update.url)}>
      Versi baru {update.version} tersedia. Klik untuk mengunduh.
    </button>
  );
}
