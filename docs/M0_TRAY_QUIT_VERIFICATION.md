# M0 tray Quit verification

## Result

The tray Quit command exits the debug host with code `0` when Windows delivers
its native `WM_COMMAND` to the Tauri tray window. The UI Automation limitation
from the initial spike remains: this check dispatches the native Quit command
programmatically; it does not physically open and click the visible context
menu.

## Reproduce on Windows

Prerequisites:

- Build `apps/desktop/src-tauri/target/x86_64-pc-windows-msvc/debug/toby-desktop-shell-spike.exe`.
- Run the Vite development server at `http://127.0.0.1:1420` in a separate
  Windows PowerShell window (`npm.cmd run dev -- --host 127.0.0.1` from
  `apps/desktop`).
- Ensure no other `toby-desktop-shell-spike` process is running.

From the repository root, run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\apps\desktop\scripts\verify-tray-quit.ps1
```

The script starts the debug host, enumerates its thread windows until it finds
the `tray_icon_app` HWND, posts `WM_COMMAND` (`0x0111`) with command ID `1001`,
and waits for the process handle. In muda's current Windows backend, native menu
command IDs start at `1000`; Toby creates Settings first and Quit second, making
Quit ID `1001`. If menu creation order or muda's allocation changes, update the
probe ID and rerun the check.

## Recorded run

Environment: Windows x64, PowerShell, existing MSVC debug build, Vite at
`http://127.0.0.1:1420`.

Command:

```text
powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\users\egoodman\desktop\herdr\wt-desktop-shell\apps\desktop\scripts\verify-tray-quit.ps1
```

Output:

```text
launched_pid=27388
tray_hwnd=1577890 class=tray_icon_app
post_wm_command_1001_accepted=True
exit_code=0
RESULT=clean_exit
```

The HWND and PID vary each run. The meaningful checks are successful message
posting, process termination within the timeout, and exit code `0`.
