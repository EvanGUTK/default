#![cfg_attr(not(target_os = "windows"), allow(dead_code))]

#[cfg(not(target_os = "windows"))]
compile_error!("This spike must run as a native Windows executable.");

use std::{
    path::PathBuf,
    sync::{Arc, Mutex},
    thread,
    time::Duration,
};

use windows::{
    Win32::{
        Foundation::{HWND, LPARAM, POINT, RECT},
        Graphics::Gdi::ClientToScreen,
        System::{
            Com::{
                CLSCTX_INPROC_SERVER, COINIT_APARTMENTTHREADED, CoCreateInstance, CoInitializeEx,
                CoUninitialize,
            },
            Threading::{
                OpenProcess, PROCESS_QUERY_LIMITED_INFORMATION, QueryFullProcessImageNameW,
            },
        },
        UI::{
            Accessibility::{CUIAutomation, IUIAutomation},
            HiDpi::{
                DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2, GetDpiForWindow,
                SetProcessDpiAwarenessContext,
            },
            Input::KeyboardAndMouse::{
                INPUT, INPUT_0, INPUT_KEYBOARD, INPUT_MOUSE, KEYBD_EVENT_FLAGS, KEYBDINPUT,
                KEYEVENTF_KEYUP, KEYEVENTF_UNICODE, MOUSEEVENTF_ABSOLUTE, MOUSEEVENTF_LEFTDOWN,
                MOUSEEVENTF_LEFTUP, MOUSEEVENTF_MOVE, MOUSEEVENTF_VIRTUALDESK, MOUSEINPUT,
                SendInput, VIRTUAL_KEY,
            },
            WindowsAndMessaging::{
                EnumWindows, GetClientRect, GetForegroundWindow, GetSystemMetrics, GetWindowRect,
                GetWindowTextLengthW, GetWindowTextW, GetWindowThreadProcessId, IsWindowVisible,
                SM_CXVIRTUALSCREEN, SM_CYVIRTUALSCREEN, SM_XVIRTUALSCREEN, SM_YVIRTUALSCREEN,
                SW_RESTORE, SW_SHOWNOACTIVATE, SetForegroundWindow, ShowWindow,
            },
        },
    },
    core::{BOOL, PWSTR},
};
use windows_capture::{
    capture::{Context, GraphicsCaptureApiHandler},
    encoder::ImageFormat,
    frame::Frame,
    graphics_capture_api::InternalCaptureControl,
    settings::{
        ColorFormat, CursorCaptureSettings, DirtyRegionSettings, DrawBorderSettings,
        MinimumUpdateIntervalSettings, SecondaryWindowSettings, Settings,
    },
    window::Window as CaptureWindow,
};

#[derive(Clone)]
struct WindowInfo {
    hwnd: HWND,
    title: String,
    rect: RECT,
    dpi: u32,
}

struct CaptureFrame {
    output: PathBuf,
    completed: Arc<Mutex<bool>>,
}

impl GraphicsCaptureApiHandler for CaptureFrame {
    type Flags = (PathBuf, Arc<Mutex<bool>>);
    type Error = Box<dyn std::error::Error + Send + Sync>;

    fn new(ctx: Context<Self::Flags>) -> Result<Self, Self::Error> {
        Ok(Self {
            output: ctx.flags.0,
            completed: ctx.flags.1,
        })
    }

    fn on_frame_arrived(
        &mut self,
        frame: &mut Frame<'_>,
        capture_control: InternalCaptureControl,
    ) -> Result<(), Self::Error> {
        frame.save_as_image(&self.output, ImageFormat::Png)?;
        println!(
            "WGC frame saved: {} ({}x{})",
            self.output.display(),
            frame.width(),
            frame.height()
        );
        *self
            .completed
            .lock()
            .expect("capture completion mutex poisoned") = true;
        capture_control.stop();
        Ok(())
    }
}

fn main() -> Result<(), Box<dyn std::error::Error>> {
    let dpi_aware = unsafe {
        SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2).is_ok()
    };
    println!("Per-monitor-v2 DPI awareness enabled: {dpi_aware}");
    run()
}

fn run() -> Result<(), Box<dyn std::error::Error>> {
    let windows = visible_windows()?;
    println!("Visible top-level windows (HWND, title, rect, DPI scale):");
    for w in &windows {
        println!(
            "  0x{:X} | {:?} | ({}, {}, {}x{}) | {:.2}x ({} DPI)",
            w.hwnd.0 as usize,
            w.title,
            w.rect.left,
            w.rect.top,
            w.rect.right - w.rect.left,
            w.rect.bottom - w.rect.top,
            f64::from(w.dpi) / 96.0,
            w.dpi,
        );
    }
    if windows.is_empty() {
        return Err("Windows returned no visible top-level windows".into());
    }

    let target = select_window(&windows)?;
    unsafe {
        let _ = ShowWindow(target.hwnd, SW_SHOWNOACTIVATE);
    }
    thread::sleep(Duration::from_millis(250));
    println!(
        "Chosen capture target: 0x{:X} {:?}",
        target.hwnd.0 as usize, target.title
    );
    let accessible_name = accessible_name(target.hwnd)?;
    println!("UI Automation accessible name: {:?}", accessible_name);

    let output = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .join("spike_output")
        .join("frame.png");
    std::fs::create_dir_all(output.parent().expect("frame output has parent"))?;
    let capture_window = CaptureWindow::from_raw_hwnd(target.hwnd.0);
    let size = (
        target.rect.right - target.rect.left,
        target.rect.bottom - target.rect.top,
    );
    if size.0 <= 0 || size.1 <= 0 {
        return Err(format!("Chosen window has invalid capture dimensions: {size:?}").into());
    }
    let completed = Arc::new(Mutex::new(false));
    let settings = Settings::new(
        capture_window,
        CursorCaptureSettings::WithoutCursor,
        DrawBorderSettings::WithoutBorder,
        SecondaryWindowSettings::Default,
        MinimumUpdateIntervalSettings::Default,
        DirtyRegionSettings::Default,
        ColorFormat::Rgba8,
        (output.clone(), completed.clone()),
    );
    CaptureFrame::start(settings)?;
    if !*completed.lock().expect("capture completion mutex poisoned") {
        return Err("WGC ended without delivering a frame".into());
    }

    synthesize_notepad_input()?;
    Ok(())
}

unsafe extern "system" fn enum_windows_callback(hwnd: HWND, context: LPARAM) -> BOOL {
    let rows = unsafe { &mut *(context.0 as *mut Vec<WindowInfo>) };
    if !unsafe { IsWindowVisible(hwnd) }.as_bool() {
        return BOOL(1);
    }
    let title_len = unsafe { GetWindowTextLengthW(hwnd) };
    let mut title_buf = vec![0u16; title_len.max(0) as usize + 1];
    let copied = unsafe { GetWindowTextW(hwnd, &mut title_buf) }.max(0) as usize;
    let title = String::from_utf16_lossy(&title_buf[..copied]);
    let mut rect = RECT::default();
    if unsafe { GetWindowRect(hwnd, &mut rect) }.is_err() {
        return BOOL(1);
    }
    let dpi = unsafe { GetDpiForWindow(hwnd) };
    rows.push(WindowInfo {
        hwnd,
        title,
        rect,
        dpi,
    });
    BOOL(1)
}

fn visible_windows() -> Result<Vec<WindowInfo>, Box<dyn std::error::Error>> {
    let mut windows = Vec::new();
    unsafe {
        EnumWindows(
            Some(enum_windows_callback),
            LPARAM(&mut windows as *mut _ as isize),
        )?
    };
    Ok(windows)
}

fn select_window(windows: &[WindowInfo]) -> Result<WindowInfo, Box<dyn std::error::Error>> {
    if let Some(title) = std::env::args().nth(1) {
        return windows
            .iter()
            .find(|w| w.title.eq_ignore_ascii_case(&title))
            .cloned()
            .ok_or_else(|| format!("No visible window has exact title {title:?}").into());
    }
    windows
        .iter()
        .find(|w| w.title.contains("M0 Windows Capture Spike"))
        .or_else(|| {
            windows
                .iter()
                .find(|w| w.title.to_lowercase().contains("notepad"))
        })
        .or_else(|| windows.iter().find(|w| !w.title.trim().is_empty()))
        .cloned()
        .ok_or_else(|| "No titled visible top-level window found".into())
}

fn accessible_name(hwnd: HWND) -> Result<String, Box<dyn std::error::Error>> {
    with_com_sta(|| {
        let automation: IUIAutomation =
            unsafe { CoCreateInstance(&CUIAutomation, None, CLSCTX_INPROC_SERVER)? };
        let element = unsafe { automation.ElementFromHandle(hwnd)? };
        let name = unsafe { element.CurrentName()? };
        Ok(name.to_string())
    })
}

fn with_com_sta<T>(
    f: impl FnOnce() -> Result<T, Box<dyn std::error::Error>>,
) -> Result<T, Box<dyn std::error::Error>> {
    unsafe { CoInitializeEx(None, COINIT_APARTMENTTHREADED).ok()? };
    let result = f();
    unsafe { CoUninitialize() };
    result
}

fn synthesize_notepad_input() -> Result<(), Box<dyn std::error::Error>> {
    let notepad = visible_windows()?
        .into_iter()
        .find(|w| {
            w.title.contains("M0 Windows Capture Spike")
                && process_image_name(w.hwnd)
                    .map(|name| name.eq_ignore_ascii_case("notepad.exe"))
                    .unwrap_or(false)
        })
        .ok_or(
            "Open the dedicated M0 Windows Capture Spike scratch file in Notepad before running",
        )?;

    println!(
        "SendInput target: Notepad HWND=0x{:X} title={:?}",
        notepad.hwnd.0 as usize, notepad.title
    );
    with_com_sta(|| {
        let automation: IUIAutomation =
            unsafe { CoCreateInstance(&CUIAutomation, None, CLSCTX_INPROC_SERVER)? };
        let element = unsafe { automation.ElementFromHandle(notepad.hwnd)? };
        unsafe { element.SetFocus()? };
        unsafe {
            let _ = ShowWindow(notepad.hwnd, SW_RESTORE);
            let _ = SetForegroundWindow(notepad.hwnd);
        }
        thread::sleep(Duration::from_millis(500));
        if unsafe { GetForegroundWindow() } != notepad.hwnd {
            return Err(format!(
                "Windows did not focus Notepad (foreground HWND=0x{:X}); no input was sent",
                unsafe { GetForegroundWindow() }.0 as usize
            )
            .into());
        }

        let mut client = RECT::default();
        unsafe { GetClientRect(notepad.hwnd, &mut client)? };
        let mut point = POINT {
            x: (client.right - client.left) / 2,
            y: (client.bottom - client.top) / 2,
        };
        if !unsafe { ClientToScreen(notepad.hwnd, &mut point) }.as_bool() {
            return Err("ClientToScreen failed for Notepad client center".into());
        }
        send_mouse_move_and_click(point.x, point.y)?;
        send_unicode_text("hello")?;
        thread::sleep(Duration::from_millis(300));
        if unsafe { GetForegroundWindow() } != notepad.hwnd {
            return Err("Foreground changed during input; Notepad receipt is uncertain".into());
        }
        println!("SendInput submitted mouse move + click and Unicode text \"hello\" to Notepad.");
        Ok(())
    })
}

fn process_image_name(hwnd: HWND) -> Result<String, Box<dyn std::error::Error>> {
    let mut pid = 0;
    unsafe { GetWindowThreadProcessId(hwnd, Some(&mut pid)) };
    if pid == 0 {
        return Err("GetWindowThreadProcessId returned PID 0".into());
    }
    let process = unsafe { OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid)? };
    let mut buffer = vec![0u16; 32768];
    let mut len = buffer.len() as u32;
    unsafe {
        QueryFullProcessImageNameW(
            process,
            windows::Win32::System::Threading::PROCESS_NAME_FORMAT(0),
            PWSTR(buffer.as_mut_ptr()),
            &mut len,
        )?
    };
    let path = String::from_utf16_lossy(&buffer[..len as usize]);
    Ok(path.rsplit(['\\', '/']).next().unwrap_or(&path).to_string())
}

fn send_mouse_move_and_click(x: i32, y: i32) -> Result<(), Box<dyn std::error::Error>> {
    let left = unsafe { GetSystemMetrics(SM_XVIRTUALSCREEN) };
    let top = unsafe { GetSystemMetrics(SM_YVIRTUALSCREEN) };
    let width = unsafe { GetSystemMetrics(SM_CXVIRTUALSCREEN) }.max(1);
    let height = unsafe { GetSystemMetrics(SM_CYVIRTUALSCREEN) }.max(1);
    let ax = (((x - left) as i64 * 65535) / i64::from((width - 1).max(1))).clamp(0, 65535) as i32;
    let ay = (((y - top) as i64 * 65535) / i64::from((height - 1).max(1))).clamp(0, 65535) as i32;
    let flags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK;
    let inputs = [
        mouse_input(ax, ay, flags),
        mouse_input(0, 0, MOUSEEVENTF_LEFTDOWN),
        mouse_input(0, 0, MOUSEEVENTF_LEFTUP),
    ];
    let sent = unsafe { SendInput(&inputs, std::mem::size_of::<INPUT>() as i32) };
    if sent != inputs.len() as u32 {
        return Err(format!("SendInput sent {sent}/{} mouse events", inputs.len()).into());
    }
    Ok(())
}

fn mouse_input(
    dx: i32,
    dy: i32,
    flags: windows::Win32::UI::Input::KeyboardAndMouse::MOUSE_EVENT_FLAGS,
) -> INPUT {
    INPUT {
        r#type: INPUT_MOUSE,
        Anonymous: INPUT_0 {
            mi: MOUSEINPUT {
                dx,
                dy,
                mouseData: 0,
                dwFlags: flags,
                time: 0,
                dwExtraInfo: 0,
            },
        },
    }
}

fn send_unicode_text(text: &str) -> Result<(), Box<dyn std::error::Error>> {
    let mut inputs = Vec::with_capacity(text.encode_utf16().count() * 2);
    for unit in text.encode_utf16() {
        inputs.push(keyboard_input(unit, KEYEVENTF_UNICODE));
        inputs.push(keyboard_input(unit, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP));
    }
    let sent = unsafe { SendInput(&inputs, std::mem::size_of::<INPUT>() as i32) };
    if sent != inputs.len() as u32 {
        return Err(format!("SendInput sent {sent}/{} keyboard events", inputs.len()).into());
    }
    Ok(())
}

fn keyboard_input(code: u16, flags: KEYBD_EVENT_FLAGS) -> INPUT {
    INPUT {
        r#type: INPUT_KEYBOARD,
        Anonymous: INPUT_0 {
            ki: KEYBDINPUT {
                wVk: VIRTUAL_KEY(0),
                wScan: code,
                dwFlags: flags,
                time: 0,
                dwExtraInfo: 0,
            },
        },
    }
}
