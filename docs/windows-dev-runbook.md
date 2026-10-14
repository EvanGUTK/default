# Windows dev runbook

How to build and run Bluey Desktop from this WSL session, on the Windows host. Verified 2026-10-13 on `windows/scout-review` at `b408afa` (the scaffold, before the round-1 Windows feature commits). SDK `10.0.300`. Project `Windows/BlueyDesktop.App` (`net10.0-windows`, WPF, `OutputType` WinExe).

Use the worktree you were assigned. Do not build `/mnt/e/codex/default` while agents are working in worktrees.

| | |
| --- | --- |
| WSL | `/mnt/e/codex/worktrees/<name>/Windows` |
| PowerShell | `E:\codex\worktrees\<name>\Windows` |

`<name>` for this checkout is `scout-review`. The commands below use that path. Swap it for `forge-engine`, `nova-ui`, or `relay-shared` when verifying those trees.

## Build

```bash
powershell.exe -NoProfile -Command "Set-Location 'E:\codex\worktrees\scout-review\Windows'; dotnet build"
```

`dotnet build` with no project argument builds `BlueyDesktop.slnx`. A clean baseline looks like this (the `--nologo` on the recorded run only hides the SDK banner):

```text
  Determining projects to restore...
  Restored E:\codex\worktrees\scout-review\Windows\BlueyDesktop.App\BlueyDesktop.App.csproj (in 51 ms).
  BlueyDesktop.App -> E:\codex\worktrees\scout-review\Windows\BlueyDesktop.App\bin\Debug\net10.0-windows\BlueyDesktop.App.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:00.91
```

Baseline at `b408afa`: **0 warnings, 0 errors.** Compare later builds to that. New warnings are not "the scaffold".

## Run

`dotnet run` stays in the foreground until the window closes. Start it in the background from WSL:

```bash
powershell.exe -NoProfile -Command "Set-Location 'E:\codex\worktrees\scout-review\Windows'; dotnet run --project BlueyDesktop.App"
```

The WinExe writes nothing to stdout while it is up. An empty console is not proof that it launched, and it is not proof that it failed.

## On screen, or only a process

```bash
powershell.exe -NoProfile -Command "Get-CimInstance Win32_Process -Filter \"Name = 'BlueyDesktop.App.exe'\" | Select-Object ProcessId, ExecutablePath | Format-List; Get-Process -Name BlueyDesktop.App -ErrorAction SilentlyContinue | Select-Object Id, MainWindowTitle, MainWindowHandle, Responding | Format-List"
```

Read both:

- `ExecutablePath` must point at **this** worktree's `bin\Debug\net10.0-windows\BlueyDesktop.App.exe`. Another agent's copy uses the same process name.
- On screen: `MainWindowHandle` is not 0, `MainWindowTitle` is non-empty, `Responding` is True. The scaffold title is `MainWindow`.
- Process only: the exe is in the list and `MainWindowHandle` is 0. That is not a visible window.

Baseline launch on 2026-10-13 19:49: PID 28400, path under this worktree, title `MainWindow`, handle 462246, `Responding` True. `IsWindowVisible` was True and `IsIconic` was False (not minimized).

## Stop

```bash
powershell.exe -NoProfile -Command "Stop-Process -Name BlueyDesktop.App -Force; Start-Sleep -Seconds 1; if (Get-Process -Name BlueyDesktop.App -ErrorAction SilentlyContinue) { Write-Output 'STILL RUNNING' } else { Write-Output 'STOPPED' }"
```

Stop the app by name, not every `dotnet.exe`. A `dotnet run` host and an MSBuild node reuse process are also named `dotnet.exe`. Killing those can interrupt someone else's build.

After a forced stop, the `dotnet run` command exits **1**. That exit code means the window was killed. It does not mean the app failed to start. Confirm with the `Get-Process` check printing `STOPPED`.

Baseline stop: PID 28400 and its `dotnet run` parent (PID 31640) were both gone. No `BlueyDesktop.App.exe` remained.
