# M0 desktop shell findings

This throwaway proof adds a Tauri 2 Windows host and a small React/TypeScript UI. It contains no voice, AI, memory, or companion animation features.

## Build and run

Run these commands from `apps/desktop` in Windows PowerShell:

```powershell
npm ci
npm run build
npm run generate-icons
```

`generate-icons` uses the original `src-tauri/icons/tray.svg`; its generated PNG, ICO, and ICNS outputs are ignored by Git. To build the Windows host, open an x64 Visual Studio Developer PowerShell and run:

```powershell
Set-Location src-tauri
cargo build --target x86_64-pc-windows-msvc --jobs 2
```

For the live development proof, start Vite in one Windows PowerShell:

```powershell
npm run dev -- --host 127.0.0.1
```

Then launch `apps/desktop/src-tauri/target/x86_64-pc-windows-msvc/debug/toby-desktop-shell-spike.exe` in another. The debug host uses `http://127.0.0.1:1420`. Running `npm run tauri -- dev` in this WSL-backed Windows checkout hit a Windows file watcher `EBUSY` error on a DLL under `src-tauri/target`; launching the built host separately with Vite running worked.

## Recorded results

| Proof | Result |
| --- | --- |
| Original system tray icon | The Windows notification area exposed a `Toby Desktop Shell Spike` tray button with a 40×40 logical UI Automation bound. The icon is authored for this spike; no inherited assets are used. |
| Right-click menu and Quit | The host builds with a tray menu containing Settings and Quit. UI Automation still does not expose the context menu, but the Quit command was invoked through Win32 `WM_COMMAND` on the tray HWND and the process exited with code 0. See [M0 tray Quit verification](M0_TRAY_QUIT_VERIFICATION.md). |
| Transparent top-right overlay | Verified on a 1920×1080 monitor at 100% scaling: UI Automation reported `(1604, 16, 300, 80)`. The window rendered the overlay proof text and is configured transparent, undecorated, always on top, and excluded from the taskbar. |
| Settings route, close, and position | Opened Settings from the tray icon. After moving it to `(420, 280)`, a fresh process restored the Settings outer window at `(420, 280, 536, 339)`. Invoking Close hid the window (`visible: true → false`). |
| Ctrl+Shift+S stop path | Verified on the rebuilt host: the overlay visibility changed from `true` to `false`. |
| Clean exit | Verified via the native tray menu command path: posting `WM_COMMAND` for Quit to the `tray_icon_app` HWND terminated the debug process with exit code `0`. A physical right-click and selection has not been manually observed. See [M0 tray Quit verification](M0_TRAY_QUIT_VERIFICATION.md). |

Build evidence collected on Windows:

- `npm.cmd run build` completed TypeScript checking and Vite production bundling successfully (33 modules transformed).
- `cargo build --target x86_64-pc-windows-msvc --jobs 2` completed successfully with the MSVC toolchain.
- The live debug host remained running while the overlay, Settings, saved position, close action, and stop shortcut were checked.

CodeRabbit's local review prompted corrections to Vite's literal `TAURI_ENV_` prefix, transparent overlay page background, and PowerShell working directories. It also noted that a shortcut registration conflict aborts setup. That fail-fast behavior is retained so the app does not start without the required local stop shortcut. A saved position is now checked against connected monitor bounds and falls back to the primary monitor when unreachable. Review findings about the npm lockfile and tray SVG are resolved by including both source files in the commits.

## DPI and placement notes

The overlay size and 16-pixel edge margin are expressed in logical pixels. Placement reads the current monitor’s physical origin, dimensions, and scale factor, then computes the physical top-right position. At 100% scaling this produced the expected 300×80 window and 16-pixel margins. Scaling above 100%, negative-coordinate monitors, and the saved-position fallback after a monitor disconnect were not exercised; check those on a Windows machine with a multi-monitor DPI setup before relying on placement outside this proof.

Settings position is persisted in the app data directory as `settings-position.json` and restored before showing the window. Windows may report `(-32000, -32000)` for a hidden window; that sentinel is ignored during save and restore so it does not replace a real position.

## Follow-up

- Manually open the tray context menu, select Quit, and observe icon removal if physical-menu confirmation is required.
- Exercise the overlay and Settings at non-100% DPI and across monitors with negative virtual-screen coordinates.
- Run the Tauri CLI dev workflow outside a WSL-backed Windows file tree, or determine whether a narrower watcher configuration avoids the observed `EBUSY` failure.

This document records a foundation spike, not release readiness. The clean-shutdown process-exit proof passes through the native Quit command path; observing a physical menu selection and tray-icon removal remains optional corroboration outside this automated check.
