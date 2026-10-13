# M0 Windows Capture/Input Spike Findings

## Environment and command

- Date: 2026-10-13
- OS: Windows 11 Pro, build 26200
- Workspace: WSL checkout at `/mnt/c/users/egoodman/desktop/herdr/wt-windows-capture`
- Native build/run: Windows `cargo.exe` 1.98.1 and `rustc.exe` 1.98.1, invoked through `powershell.exe` from the C: checkout path
- Command: `cargo run --manifest-path crates/platform-windows/Cargo.toml --bin windows-capture-spike`
- Target: `M0 Windows Capture Spike.txt - Notepad`, an empty scratch file under `%TEMP%`; an unrelated open `CLAUDE.md` Notepad window was left untouched

The WSL Rust installation only had `x86_64-unknown-linux-gnu`. The Windows installation had `x86_64-pc-windows-msvc` and its native linker toolchain, so compiling and running through PowerShell avoided cross-compilation. The WSL checkout was visible at `C:\Users\egoodman\desktop\herdr\wt-windows-capture`. No C++ build step was needed.

## Dependencies and APIs

- `windows` **0.62.2**, with direct features `Win32_Foundation`, `Win32_Graphics_Gdi`, `Win32_System_Com`, `Win32_System_Threading`, `Win32_UI_Accessibility`, `Win32_UI_HiDpi`, `Win32_UI_Input_KeyboardAndMouse`, and `Win32_UI_WindowsAndMessaging`.
- `windows-capture` **2.0.1** for Windows Graphics Capture (WGC), using its `windows` **0.62.2** dependency and frame-to-PNG support.
- `Cargo.lock` pins the resolved dependency versions for this spike.

The binary enumerates top-level windows with `EnumWindows`, reads visibility/title/rect/DPI through Win32, gets the accessible window name through `IUIAutomation`, captures a selected HWND via WGC, and uses `SendInput` for mouse and Unicode keyboard events. Process-image verification restricts input to the titled scratch Notepad window.

## Recorded run

The successful run reported per-monitor-v2 process awareness and listed all windows returned by `EnumWindows` for which `IsWindowVisible` was true. This excerpt keeps the real handles, bounds, DPI values, target name, and results while redacting unrelated application titles from the full console output.

```text
Per-monitor-v2 DPI awareness enabled: true
Visible top-level windows (HWND, title, rect, DPI scale):
  0x1306F0 | "" | (0, 0, 1x1) | 1.00x (96 DPI)
  0x1013E | "" | (0, 1032, 1920x48) | 1.00x (96 DPI)
  0x50320 | "" | (1920, 1035, 2560x48) | 1.00x (96 DPI)
  0x10162 | "" | (4480, 976, 1280x48) | 1.00x (96 DPI)
  0x1046C | [unrelated application title redacted] | (1913, -357, 1294x1399) | 1.00x (96 DPI)
  0x5E1154 | "*M0 Windows Capture Spike.txt - Notepad" | (2355, -115, 1920x1032) | 1.00x (96 DPI)
  ... other visible top-level HWND rows omitted; each reported 96 DPI / 1.00x ...
Chosen capture target: 0x5E1154 "*M0 Windows Capture Spike.txt - Notepad"
UI Automation accessible name: "*M0 Windows Capture Spike.txt - Notepad"
WGC frame saved: C:\Users\egoodman\desktop\herdr\wt-windows-capture\crates\platform-windows\spike_output\frame.png (1910x1027)
SendInput target: Notepad HWND=0x5E1154 title="*M0 Windows Capture Spike.txt - Notepad"
SendInput submitted mouse move + click and Unicode text "hello" to Notepad.
```

The run took two executions for visual confirmation. The first execution typed `hello` into the empty scratch document. The next execution captured the window before typing again; its PNG visibly showed the existing `hello` in Notepad. Both executions completed WGC and submitted the mouse and keyboard events. The second execution submitted another `hello` after the frame was captured, so the scratch buffer is dirty; its final text after that second action was not inspected. The scratch file was not saved. The generated PNG is intentionally ignored by Git.

## Findings

- **Window listing:** succeeded. Visible top-level HWNDs were printed with title, screen bounds, and `GetDpiForWindow` scale. The process enabled per-monitor-v2 awareness before enumerating.
- **DPI observations:** every enumerated window returned **96 DPI / 1.00x (100%)** on this machine. The observed virtual desktop bounds span x=0 to 5760; windows appeared across x=0..1920, x=1920..4480, and x=4480..5760, but all reported the same factor. This run does not prove mixed-DPI behavior because no window reported a different DPI.
- **UI Automation:** succeeded. `IUIAutomation::ElementFromHandle` returned the Notepad window name `*M0 Windows Capture Spike.txt - Notepad`.
- **WGC:** succeeded for the restored Notepad window. The PNG was 1910×1027. The target must be restored/visible; a first attempt against a minimized window produced no frame and waited until stopped. No capture permission prompt appeared on this desktop.
- **SendInput:** succeeded against the verified `notepad.exe` scratch window. The binary checked that this exact Notepad HWND was foreground before injection, submitted virtual-desktop mouse move plus left down/up, then Unicode `hello`. Notepad's title became dirty, and the following WGC frame visibly contained `hello`. An unrelated `CLAUDE.md` Notepad window was not targeted or modified.
- **Apartment constraint:** the first attempt initialized COM STA globally and then WGC failed to initialize WinRT's MTA apartment. Moving STA initialization into the individual UI Automation calls resolved the failure. Keep WGC's WinRT initialization out of an already initialized STA thread.
- **Mixed-DPI/permissions:** no mixed-DPI or permission-denial scenario was exercised. Test on monitors with different Windows scaling settings and on configurations where capture consent or policy restrictions apply before relying on those cases.

## Run it again

Create/open a blank `%TEMP%\M0 Windows Capture Spike.txt` in Notepad, then run the command above from Windows PowerShell in the repository. The binary refuses to type unless it finds that exact scratch-file title under `notepad.exe` and confirms it is foreground. Review the text in the window; close it without saving afterward. The frame appears at `crates/platform-windows/spike_output/frame.png` and is ignored by Git.
