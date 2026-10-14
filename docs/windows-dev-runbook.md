# Windows dev runbook

How to build and run Bluey Desktop from this WSL session, on the Windows host. Re-checked 2026-10-13 on `windows/scout-review` at `50a1a79` (merged main: Core, the tray package reference, the engine, and the animated character window). SDK `10.0.300`.

Use the worktree you were assigned. Do not build `/mnt/e/codex/default` while agents are working in worktrees.

| | |
| --- | --- |
| WSL | `/mnt/e/codex/worktrees/<name>/Windows` |
| PowerShell | `E:\codex\worktrees\<name>\Windows` |

`<name>` for this checkout is `scout-review`. Swap it for another agent's worktree when you are verifying their tree.

## What `dotnet build` covers

`BlueyDesktop.slnx` contains:

- `BlueyDesktop.App` — WPF (`net10.0-windows`)
- `BlueyDesktop.Core` — class library (`net10.0`)

`BlueyDesktop.Engine.SelfTest` is **not** in the solution. `dotnet build` does not compile it. Run that project on its own (below).

## Build

```bash
powershell.exe -NoProfile -Command "Set-Location 'E:\codex\worktrees\scout-review\Windows'; dotnet build"
```

Recorded at `50a1a79` (`--nologo` only hides the SDK banner):

```text
  Determining projects to restore...
  Restored ...\BlueyDesktop.Core\BlueyDesktop.Core.csproj (in 58 ms).
  Restored ...\BlueyDesktop.App\BlueyDesktop.App.csproj (in 140 ms).
  BlueyDesktop.Core -> ...\BlueyDesktop.Core\bin\Debug\net10.0\BlueyDesktop.Core.dll
  BlueyDesktop.App -> ...\BlueyDesktop.App\bin\Debug\net10.0-windows\BlueyDesktop.App.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:01.29
```

## Run

`dotnet run` stays in the foreground until the window closes. Start it in the background from WSL:

```bash
powershell.exe -NoProfile -Command "Set-Location 'E:\codex\worktrees\scout-review\Windows'; dotnet run --project BlueyDesktop.App"
```

The WinExe writes nothing to stdout while it is up. An empty console is not proof that it launched, and it is not proof that it failed.

## On screen, or only a process

`Get-Process` `MainWindowHandle` is **not** a reliable check anymore. The character window sets `ShowInTaskbar="False"` and `WindowStyle="None"`, and `Process.MainWindowHandle` stays 0 even while a visible 160×160 window titled `Bluey` is on screen. Confirmed on PID 7612: handle 0, and `EnumWindows` still found `hwnd` visible, title `Bluey`, rect `2376,1208`, size `160x160`.

Several worktrees can be running `BlueyDesktop.App.exe` at once. Match `ExecutablePath` to **this** worktree before you decide anything.

```bash
powershell.exe -NoProfile -Command "Get-CimInstance Win32_Process -Filter \"Name = 'BlueyDesktop.App.exe'\" | Select-Object ProcessId, ExecutablePath | Format-List"
```

Then enumerate top-level windows for that PID (a visible window titled `Bluey`, about 160×160, means it is on screen). A process with no such window is background-only.

## Stop

Stop **this worktree's PID**, not the process name. `Stop-Process -Name BlueyDesktop.App` kills every agent's copy.

```bash
powershell.exe -NoProfile -Command "Stop-Process -Id <pid> -Force"
```

The parent `dotnet run` exits **1** because the child was killed. That exit code does not mean startup failed. Confirm the PID is gone and that you did not stop a different worktree's exe.

Do not kill every `dotnet.exe`. MSBuild node-reuse processes share that name.

## Engine self-test

From the `Windows` directory:

```bash
powershell.exe -NoProfile -Command "Set-Location 'E:\codex\worktrees\scout-review\Windows'; dotnet run --project BlueyDesktop.Engine.SelfTest"
```

`Program.cs` prints one `PASS` or `FAIL` line per check, then `<n> passed, <n> failed`, and exits 0 only when the failed count is 0. Most checks use a fake input driver. The last section registers Ctrl+Alt+S, sends that chord once (the hotkey consumes it), and flashes a small window titled `Bluey self-test (closes by itself)` for about a second. There is a 7 second wait inside the stop-latch checks, so the run takes roughly 10–15 seconds.

A separate `dotnet build BlueyDesktop.Engine.SelfTest` at this commit was also **0 warnings, 0 errors**.

Recorded run at `50a1a79`: **80 passed, 0 failed**, exit 0. One line was a skip, not a failure: `SKIP: Windows would not give the test window keyboard focus`, so the two "focused PasswordBox / TextBox" checks did not run. The UI Automation tree check (PasswordBox Secure, TextBox Ordinary, no keyboard focus required) did pass.
