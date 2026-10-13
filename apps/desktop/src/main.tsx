import { createRoot } from "react-dom/client";
import { getCurrentWindow } from "@tauri-apps/api/window";
import "./style.css";

function App() {
  const isOverlay = new URLSearchParams(window.location.search).get("window") === "overlay";

  if (isOverlay) {
    return (
      <main className="overlay" aria-label="Toby overlay shell proof">
        <span className="status-dot" />
        <div>
          <strong>Toby shell proof</strong>
          <p>Ctrl + Shift + S hides this overlay</p>
        </div>
      </main>
    );
  }

  return (
    <main className="settings">
      <header>
        <div>
          <p className="eyebrow">TOBY · M0 SHELL SPIKE</p>
          <h1>Settings window</h1>
        </div>
        <button className="close" onClick={() => void getCurrentWindow().hide()} aria-label="Close settings">
          ×
        </button>
      </header>
      <p className="description">This small window is opened from the system tray. Its last position is saved when it moves.</p>
      <div className="proof-list">
        <span>System tray</span><span className="proof-ok">Ready</span>
        <span>Overlay stop shortcut</span><kbd>Ctrl + Shift + S</kbd>
      </div>
    </main>
  );
}

createRoot(document.getElementById("root")!).render(<App />);
