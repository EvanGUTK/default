param(
  [int]$ExitTimeoutSeconds = 5
)

$ErrorActionPreference = 'Stop'
$processName = 'toby-desktop-shell-spike'
$exePath = Join-Path $PSScriptRoot '..\src-tauri\target\x86_64-pc-windows-msvc\debug\toby-desktop-shell-spike.exe'
$exePath = [System.IO.Path]::GetFullPath($exePath)
if (-not (Test-Path $exePath)) {
  throw "Debug host not found: $exePath"
}
if (Get-Process -Name $processName -ErrorAction SilentlyContinue) {
  throw "Stop the existing $processName process before running this isolated check."
}
Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class TobyTrayQuitProbe {
  public delegate bool EnumThreadDelegate(IntPtr hwnd, IntPtr lParam);
  [DllImport("user32.dll")]
  public static extern bool EnumThreadWindows(uint threadId, EnumThreadDelegate callback, IntPtr lParam);
  [DllImport("user32.dll", SetLastError=true)]
  public static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)]
  public static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int max);
}
'@

$hostProcess = Start-Process -FilePath $exePath -PassThru
"launched_pid=$($hostProcess.Id)"
$trayWindow = $null
try {
  for ($attempt = 0; $attempt -lt 40 -and -not $trayWindow; $attempt++) {
    Start-Sleep -Milliseconds 250
    $hostProcess.Refresh()
    if ($hostProcess.HasExited) {
      throw "Host exited before the tray window appeared (exit code $($hostProcess.ExitCode))."
    }

    $windows = [System.Collections.Generic.List[object]]::new()
    $callback = [TobyTrayQuitProbe+EnumThreadDelegate] {
      param($hwnd, $unused)
      $className = [System.Text.StringBuilder]::new(256)
      [void][TobyTrayQuitProbe]::GetClassName($hwnd, $className, $className.Capacity)
      $windows.Add([pscustomobject]@{ Hwnd = $hwnd; Class = $className.ToString() })
      return $true
    }
    foreach ($thread in $hostProcess.Threads) {
      [void][TobyTrayQuitProbe]::EnumThreadWindows([uint32]$thread.Id, $callback, [IntPtr]::Zero)
    }
    $trayWindow = $windows | Where-Object Class -eq 'tray_icon_app' | Select-Object -First 1
  }

  if (-not $trayWindow) {
    throw 'Could not locate the tray_icon_app HWND within 10 seconds.'
  }
  "tray_hwnd=$($trayWindow.Hwnd) class=$($trayWindow.Class)"

  # muda's Windows backend starts command IDs at 1000. Settings is created first
  # and Quit second in src-tauri/src/lib.rs, so the current Quit command is 1001.
  $posted = [TobyTrayQuitProbe]::PostMessage(
    $trayWindow.Hwnd,
    0x0111, # WM_COMMAND
    [IntPtr]1001,
    [IntPtr]::Zero
  )
  if (-not $posted) {
    $errorCode = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
    throw "PostMessage(WM_COMMAND, 1001) failed; Win32 error $errorCode."
  }
  'post_wm_command_1001_accepted=True'

  if (-not $hostProcess.WaitForExit($ExitTimeoutSeconds * 1000)) {
    throw "Host did not exit within $ExitTimeoutSeconds seconds after the Quit command."
  }
  "exit_code=$($hostProcess.ExitCode)"
  if ($hostProcess.ExitCode -ne 0) {
    throw "Expected exit code 0, got $($hostProcess.ExitCode)."
  }
  'RESULT=clean_exit'
} finally {
  $hostProcess.Refresh()
  if (-not $hostProcess.HasExited) {
    Stop-Process -Id $hostProcess.Id -Force -ErrorAction SilentlyContinue
    Write-Warning "Stopped probe host PID $($hostProcess.Id) after an incomplete check."
  }
  $hostProcess.Dispose()
}
