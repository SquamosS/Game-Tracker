use serde::Serialize;
use serde_json::Value;
use std::collections::HashMap;

const API: &str = "https://api.steampowered.com/ISteamUserStats";
/// Offset between a 32-bit Steam account id and its SteamID64.
const STEAM_ID64_BASE: u64 = 76561197960265728;

#[derive(Serialize, Clone)]
#[serde(rename_all = "camelCase")]
pub struct Achievement {
    pub api_name: String,
    pub name: String,
    pub description: String,
    pub hidden: bool,
    pub icon: String,
    pub icon_gray: String,
    pub achieved: bool,
    pub unlock_time: Option<u64>,
    pub global_percent: Option<f64>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct GameAchievements {
    pub app_id: u32,
    pub game_name: String,
    pub achievements: Vec<Achievement>,
}

/// The app id of the Steam game running right now, if any.
#[cfg(windows)]
pub fn running_app_id() -> Option<u32> {
    use winreg::{enums::HKEY_CURRENT_USER, RegKey};
    let key = RegKey::predef(HKEY_CURRENT_USER)
        .open_subkey("Software\\Valve\\Steam")
        .ok()?;
    let id: u32 = key.get_value("RunningAppID").ok()?;
    (id != 0).then_some(id)
}

#[cfg(not(windows))]
pub fn running_app_id() -> Option<u32> {
    None
}

/// SteamID64 of the user signed in to the Steam client, if any.
#[cfg(windows)]
pub fn active_steam_id() -> Option<u64> {
    use winreg::{enums::HKEY_CURRENT_USER, RegKey};
    let key = RegKey::predef(HKEY_CURRENT_USER)
        .open_subkey("Software\\Valve\\Steam\\ActiveProcess")
        .ok()?;
    let account: u32 = key.get_value("ActiveUser").ok()?;
    (account != 0).then_some(STEAM_ID64_BASE + account as u64)
}

#[cfg(not(windows))]
pub fn active_steam_id() -> Option<u64> {
    let _ = STEAM_ID64_BASE;
    None
}

async fn get_json(client: &reqwest::Client, url: &str) -> Result<(u16, Value), String> {
    let resp = client.get(url).send().await.map_err(|e| format!("Tidak bisa menghubungi Steam: {e}"))?;
    let status = resp.status().as_u16();
    let body = resp.json::<Value>().await.unwrap_or(Value::Null);
    Ok((status, body))
}

fn as_f64(v: &Value) -> Option<f64> {
    // Steam has returned the percentage both as a number and as a string.
    v.as_f64().or_else(|| v.as_str()?.parse().ok())
}

pub async fn fetch(
    client: &reqwest::Client,
    api_key: &str,
    steam_id: &str,
    language: &str,
    app_id: u32,
) -> Result<GameAchievements, String> {
    if api_key.is_empty() {
        return Err("Steam API key belum diisi. Buka tab Pengaturan.".into());
    }
    if steam_id.is_empty() {
        return Err("SteamID64 belum diisi. Buka tab Pengaturan.".into());
    }

    let schema_url = format!("{API}/GetSchemaForGame/v2/?key={api_key}&appid={app_id}&l={language}");
    let player_url = format!("{API}/GetPlayerAchievements/v1/?key={api_key}&steamid={steam_id}&appid={app_id}");
    let global_url = format!("{API}/GetGlobalAchievementPercentagesForApp/v2/?gameid={app_id}");

    let (schema_status, schema) = get_json(client, &schema_url).await?;
    let (_, player) = get_json(client, &player_url).await?;
    // Global percentages are a nice-to-have; the overlay works without them.
    let (_, global) = get_json(client, &global_url).await.unwrap_or((0, Value::Null));

    if schema_status == 403 || schema_status == 401 {
        return Err("Steam menolak API key. Cek lagi di tab Pengaturan.".into());
    }
    let stats = &player["playerstats"];
    if stats["success"].as_bool() != Some(true) {
        let msg = stats["error"].as_str().unwrap_or("tidak diketahui");
        return Err(if msg.contains("not public") {
            "Profil Steam kamu privat. Set \"Game details\" ke Public di pengaturan privasi Steam.".into()
        } else {
            format!("Steam tidak mengembalikan progres achievement: {msg}")
        });
    }

    let mut unlocked: HashMap<String, (bool, u64)> = HashMap::new();
    for a in stats["achievements"].as_array().into_iter().flatten() {
        if let Some(name) = a["apiname"].as_str() {
            let achieved = a["achieved"].as_u64() == Some(1);
            unlocked.insert(name.to_string(), (achieved, a["unlocktime"].as_u64().unwrap_or(0)));
        }
    }

    let mut percent: HashMap<String, f64> = HashMap::new();
    for a in global["achievementpercentages"]["achievements"].as_array().into_iter().flatten() {
        if let (Some(name), Some(p)) = (a["name"].as_str(), as_f64(&a["percent"])) {
            percent.insert(name.to_string(), p);
        }
    }

    let game = &schema["game"];
    let achievements = game["availableGameStats"]["achievements"]
        .as_array()
        .into_iter()
        .flatten()
        .filter_map(|a| {
            let api_name = a["name"].as_str()?.to_string();
            let (achieved, time) = unlocked.get(&api_name).copied().unwrap_or((false, 0));
            Some(Achievement {
                name: a["displayName"].as_str().unwrap_or(&api_name).to_string(),
                description: a["description"].as_str().unwrap_or_default().to_string(),
                hidden: a["hidden"].as_u64() == Some(1),
                icon: a["icon"].as_str().unwrap_or_default().to_string(),
                icon_gray: a["icongray"].as_str().unwrap_or_default().to_string(),
                achieved,
                unlock_time: (achieved && time > 0).then_some(time),
                global_percent: percent.get(&api_name).copied(),
                api_name,
            })
        })
        .collect();

    Ok(GameAchievements {
        app_id,
        game_name: stats["gameName"]
            .as_str()
            .or(game["gameName"].as_str())
            .unwrap_or_default()
            .to_string(),
        achievements,
    })
}
