mod settings;
mod steam;

use settings::Settings;
use tauri::{AppHandle, Emitter, Manager, PhysicalPosition, State, WebviewWindow};
use tauri_plugin_global_shortcut::ShortcutState;

/// Hotkey that shows or hides the overlay. Shift+Tab is avoided because Steam uses it.
const TOGGLE_SHORTCUT: &str = "ctrl+shift+g";

struct Http(reqwest::Client);

#[tauri::command]
fn running_app_id() -> Option<u32> {
    steam::running_app_id()
}

#[tauri::command]
fn load_settings(app: AppHandle) -> Settings {
    settings::load(&app)
}

#[tauri::command]
fn save_settings(app: AppHandle, settings: Settings) -> Result<(), String> {
    settings::save(&app, &settings)
}

#[tauri::command]
async fn achievements(
    app: AppHandle,
    http: State<'_, Http>,
    app_id: u32,
) -> Result<steam::GameAchievements, String> {
    let s = settings::load(&app);
    steam::fetch(&http.0, &s.api_key, &s.steam_id, &s.language, app_id).await
}

#[tauri::command]
fn quit(app: AppHandle) {
    app.exit(0);
}

fn toggle_overlay(app: &AppHandle) {
    let Some(window) = app.get_webview_window("main") else { return };
    if window.is_visible().unwrap_or(false) {
        let _ = window.hide();
    } else {
        let _ = window.show();
        let _ = window.set_focus();
        let _ = window.emit("overlay-shown", ());
    }
}

/// Docks the overlay to the right edge of the monitor it opens on.
fn dock_right(window: &WebviewWindow) {
    let (Ok(Some(monitor)), Ok(size)) = (window.current_monitor(), window.outer_size()) else { return };
    let area = monitor.size();
    let origin = monitor.position();
    let margin = (24.0 * monitor.scale_factor()) as i32;
    let x = origin.x + area.width as i32 - size.width as i32 - margin;
    let y = origin.y + margin;
    let _ = window.set_position(PhysicalPosition::new(x, y));
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(
            tauri_plugin_global_shortcut::Builder::new()
                .with_shortcuts([TOGGLE_SHORTCUT])
                .expect("valid shortcut")
                .with_handler(|app, _shortcut, event| {
                    if event.state == ShortcutState::Pressed {
                        toggle_overlay(app);
                    }
                })
                .build(),
        )
        .manage(Http(reqwest::Client::new()))
        .setup(|app| {
            if let Some(window) = app.get_webview_window("main") {
                dock_right(&window);
            }
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            running_app_id,
            load_settings,
            save_settings,
            achievements,
            quit
        ])
        .run(tauri::generate_context!())
        .expect("error while running Game Tracker");
}
