use serde::{Deserialize, Serialize};
use std::{fs, path::PathBuf};
use tauri::{
    image::Image,
    menu::{Menu, MenuItem},
    tray::{MouseButton, MouseButtonState, TrayIconBuilder, TrayIconEvent},
    Manager, PhysicalPosition, Position, Size, WindowEvent,
};
use tauri_plugin_global_shortcut::{Code, GlobalShortcutExt, Modifiers, Shortcut};

#[derive(Serialize, Deserialize)]
struct SavedPosition {
    x: i32,
    y: i32,
}

fn settings_position_path(app: &tauri::AppHandle) -> Option<PathBuf> {
    let mut path = app.path().app_data_dir().ok()?;
    path.push("settings-position.json");
    Some(path)
}

fn restore_settings_position(app: &tauri::AppHandle, window: &tauri::WebviewWindow) {
    let Some(path) = settings_position_path(app) else {
        return;
    };
    let Ok(json) = fs::read_to_string(path) else {
        return;
    };
    let Ok(position) = serde_json::from_str::<SavedPosition>(&json) else {
        return;
    };
    if position.x <= -30_000 && position.y <= -30_000 {
        return;
    }
    if settings_position_is_reachable(window, position.x, position.y) {
        let _ = window.set_position(Position::Physical(PhysicalPosition::new(
            position.x, position.y,
        )));
    } else {
        center_settings_on_available_monitor(window);
    }
}

fn settings_position_is_reachable(window: &tauri::WebviewWindow, x: i32, y: i32) -> bool {
    let (Ok(size), Ok(monitors)) = (window.outer_size(), window.available_monitors()) else {
        return false;
    };
    let (x, y) = (i64::from(x), i64::from(y));

    monitors.iter().any(|monitor| {
        let origin = monitor.position();
        let monitor_size = monitor.size();
        let left = i64::from(origin.x);
        let top = i64::from(origin.y);
        let right = left + i64::from(monitor_size.width);
        let bottom = top + i64::from(monitor_size.height);
        let visible_width = (x + i64::from(size.width)).min(right) - x.max(left);
        let visible_height = (y + i64::from(size.height)).min(bottom) - y.max(top);
        visible_width >= 64 && visible_height >= 48
    })
}

fn center_settings_on_available_monitor(window: &tauri::WebviewWindow) {
    let (Ok(size), Ok(monitors)) = (window.outer_size(), window.available_monitors()) else {
        let _ = window.center();
        return;
    };
    let primary = window.primary_monitor().ok().flatten();
    let Some(monitor) = primary.as_ref().or_else(|| monitors.first()) else {
        let _ = window.center();
        return;
    };
    let origin = monitor.position();
    let monitor_size = monitor.size();
    let x = i64::from(origin.x) + (i64::from(monitor_size.width) - i64::from(size.width)) / 2;
    let y = i64::from(origin.y) + (i64::from(monitor_size.height) - i64::from(size.height)) / 2;
    let _ = window.set_position(Position::Physical(PhysicalPosition::new(
        x as i32, y as i32,
    )));
}

fn persist_settings_position(app: &tauri::AppHandle, x: i32, y: i32) {
    // Windows can report the hidden-window sentinel while the settings window is not shown.
    if x <= -30_000 && y <= -30_000 {
        return;
    }
    let Some(path) = settings_position_path(app) else {
        return;
    };
    let Some(parent) = path.parent() else { return };
    if fs::create_dir_all(parent).is_err() {
        return;
    }
    if let Ok(json) = serde_json::to_vec(&SavedPosition { x, y }) {
        let _ = fs::write(path, json);
    }
}

fn show_settings(app: &tauri::AppHandle) {
    if let Some(window) = app.get_webview_window("settings") {
        restore_settings_position(app, &window);
        let _ = window.show();
        let _ = window.set_focus();
    }
}

fn tray_rgba() -> Vec<u8> {
    const SIDE: usize = 32;
    let mut pixels = vec![0; SIDE * SIDE * 4];
    for y in 0..SIDE {
        for x in 0..SIDE {
            let dx = x as f32 - 16.0;
            let dy = y as f32 - 16.0;
            let rounded = dx.abs() <= 14.0 && dy.abs() <= 14.0
                || (dx.abs() - 14.0).hypot(dy.abs() - 14.0) <= 4.0;
            if rounded {
                set_pixel(&mut pixels, x, y, [99, 102, 241, 255]);
            }
            // Original arch mark and eyes, rasterized to keep the tray icon self-contained.
            let arch = (9..=23).contains(&x)
                && (9..=16).contains(&y)
                && ((x as i32 - 16).abs() + y as i32 <= 27);
            if arch && rounded {
                set_pixel(&mut pixels, x, y, [255, 255, 255, 255]);
            }
            if ((x as i32 - 12).pow(2) + (y as i32 - 19).pow(2) <= 4)
                || ((x as i32 - 20).pow(2) + (y as i32 - 19).pow(2) <= 4)
            {
                set_pixel(&mut pixels, x, y, [134, 239, 172, 255]);
            }
            if (y == 24 && (12..=20).contains(&x)) || (y == 25 && (14..=18).contains(&x)) {
                set_pixel(&mut pixels, x, y, [255, 255, 255, 255]);
            }
        }
    }
    pixels
}

fn set_pixel(pixels: &mut [u8], x: usize, y: usize, color: [u8; 4]) {
    let index = (y * 32 + x) * 4;
    pixels[index..index + 4].copy_from_slice(&color);
}

fn place_overlay_top_right(window: &tauri::WebviewWindow) -> tauri::Result<()> {
    if let Some(monitor) = window.current_monitor()? {
        let scale = monitor.scale_factor();
        let size = monitor.size();
        let origin = monitor.position();
        let width = (300.0 * scale).round() as u32;
        let margin = (16.0 * scale).round() as i32;
        let x = origin.x + size.width as i32 - width as i32 - margin;
        let y = origin.y + margin;
        window.set_size(Size::Logical(tauri::LogicalSize::new(300.0, 80.0)))?;
        window.set_position(Position::Physical(PhysicalPosition::new(x, y)))?;
    }
    Ok(())
}

pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_global_shortcut::Builder::new().build())
        .setup(|app| {
            let handle = app.handle().clone();
            let overlay = app
                .get_webview_window("overlay")
                .expect("overlay window configured");
            let settings = app
                .get_webview_window("settings")
                .expect("settings window configured");
            place_overlay_top_right(&overlay)?;
            restore_settings_position(&handle, &settings);

            let settings_handle = handle.clone();
            settings.on_window_event(move |event| match event {
                WindowEvent::Moved(position) => {
                    persist_settings_position(&settings_handle, position.x, position.y)
                }
                WindowEvent::CloseRequested { api, .. } => {
                    api.prevent_close();
                    if let Some(window) = settings_handle.get_webview_window("settings") {
                        let _ = window.hide();
                    }
                }
                _ => {}
            });

            let shortcut = Shortcut::new(Some(Modifiers::CONTROL | Modifiers::SHIFT), Code::KeyS);
            let shortcut_handle = handle.clone();
            app.global_shortcut()
                .on_shortcut(shortcut, move |_app, _shortcut, event| {
                    if event.state == tauri_plugin_global_shortcut::ShortcutState::Pressed {
                        if let Some(overlay) = shortcut_handle.get_webview_window("overlay") {
                            let _ = overlay.hide();
                        }
                    }
                })?;

            let settings_item = MenuItem::with_id(app, "settings", "Settings", true, None::<&str>)?;
            let quit_item = MenuItem::with_id(app, "quit", "Quit", true, None::<&str>)?;
            let menu = Menu::with_items(app, &[&settings_item, &quit_item])?;
            TrayIconBuilder::new()
                .icon(Image::new_owned(tray_rgba(), 32, 32))
                .tooltip("Toby Desktop Shell Spike")
                .menu(&menu)
                .show_menu_on_left_click(false)
                .on_menu_event(|app, event| match event.id().as_ref() {
                    "settings" => {
                        show_settings(app);
                    }
                    "quit" => app.exit(0),
                    _ => {}
                })
                .on_tray_icon_event(|tray, event| {
                    if let TrayIconEvent::Click {
                        button: MouseButton::Left,
                        button_state: MouseButtonState::Up,
                        ..
                    } = event
                    {
                        show_settings(tray.app_handle());
                    }
                })
                .build(app)?;
            Ok(())
        })
        .run(tauri::generate_context!())
        .expect("failed to run Tauri desktop shell");
}
