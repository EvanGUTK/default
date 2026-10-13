# M0 dependency review

Authority: Milestone 0, "Dependency review" (docs/ROADMAP.md). Owner: Voice specialist. Branch: `spike/m0-speech-deps`. Reviewed 2026-10-13.

## Method and limits

Licenses below were read from primary sources during this spike, not recalled: the vendored `LICENSE` and `Cargo.toml` files in the downloaded crates, the crates.io and npm registry metadata, the GitHub license API, and Hugging Face model-card metadata. Versions are those resolved on the review date. `cargo metadata` supplied the license of every Rust crate in the speech spike's dependency tree.

This is an engineering review, not legal advice. Toby's own license is unselected (docs/DECISIONS.md), so "compatible" here means compatible with a permissively licensed or proprietary application, and a GPL component is called out wherever it would change that. Items I could not verify in this spike are marked **unverified**.

## Corrections to the work packet's assumptions

| Packet assumption | Finding |
| --- | --- |
| whisper-rs is MIT | **No.** `whisper-rs` 0.16.0 and `whisper-rs-sys` 0.15.0 are published under **Unlicense** (public-domain dedication). The whisper.cpp they bundle is MIT. Both are permissive, so there is no practical obstacle, but the notices should say Unlicense. |
| TypeScript is MIT | **No.** TypeScript (7.0.2 on npm) is **Apache-2.0**. React and react-dom (19.3.0) are MIT. |
| Tauri 2 and windows-rs are MIT/Apache | Confirmed as **MIT OR Apache-2.0** from crates.io (`tauri` 2.12.3, `windows` 0.62.2). GitHub's repository-level API shows only Apache-2.0 for both, so treat the crate metadata as authoritative. |

## Component table

| Component | License | Distribution notes | Constraint or risk |
| --- | --- | --- | --- |
| whisper.cpp (1.8.3, bundled inside `whisper-rs-sys` 0.15.0) | MIT (vendored `LICENSE`; upstream repository is MIT) | Compiled from source by `whisper-rs-sys` and statically linked into the Rust binary. Ship the MIT notice in third-party notices. Built CPU-only in this spike. | Build needs cmake, a C++ toolchain and libclang (bindgen). None were installed on this machine; the spike used a standalone cmake and the PyPI `libclang` wheel. A Windows build (MSVC plus LLVM) is **unverified**: this spike ran under WSL2 Linux. Upstream moves daily, so pin the version. |
| whisper-rs 0.16.0 / whisper-rs-sys 0.15.0 | **Unlicense** | Ordinary crates.io dependencies; nothing beyond the compiled library. | Hosted on Codeberg, small maintainer base, lags upstream whisper.cpp. Keep the option to drop to a whisper.cpp sidecar process, which docs/STACK.md already proposes. Both crates are `unsafe` FFI; keep them behind one adapter in `crates/speech/`. |
| Whisper model weights: `ggml-base.en.bin` (147,964,211 B), `ggml-small.en.bin` (487,614,201 B) | MIT (OpenAI Whisper repository and the `ggerganov/whisper.cpp` Hugging Face card) | **Never committed.** Downloaded at runtime from Hugging Face with a pinned SHA-256 (`crates/speech/scripts/fetch-assets.sh`). Product needs user-visible consent, size disclosure, resume, checksum verification and a removal control (docs/STACK.md policy). | The spike script fetches `resolve/main`; the checksum makes a changed file fail closed, but production should pin a commit revision or mirror the files. The download host can be unreachable or rate-limited. |
| Silero VAD, ggml build `ggml-silero-v5.1.2.bin` (885,098 B) | MIT (`snakers4/silero-vad` repository; `ggml-org/whisper-vad` card) | Runtime download like the Whisper weights, same checksum rule. Runs inside whisper.cpp, so no extra runtime. | Thresholds need tuning per environment; see docs/M0_SPEECH_FINDINGS.md. |
| **Recommended TTS: Kokoro-82M** (weights `hexgrad/Kokoro-82M`; ONNX export `onnx-community/Kokoro-82M-v1.0-ONNX`) | Apache-2.0 (both Hugging Face cards) | ONNX variants range from 86 MB (`model_q8f16`) to 326 MB (`model.onnx`); voice style files are separate small downloads. Run through ONNX Runtime (MIT) from the Rust host, for example via the `ort` crate (MIT OR Apache-2.0, currently `2.0.0-rc.14`, a release candidate). Download at runtime like the other weights. | **Not measured in this spike.** TTS latency and quality on this machine are unproven; see "TTS decision". The model card lists some CC BY 3.0 and CC BY 4.0 training audio; the weights are Apache-2.0, but have counsel confirm no attribution duty passes to the weights. English phonemization uses `misaki` (Apache-2.0), whose fallback is **espeak-ng (GPL-3.0)**. Do not link espeak-ng into Toby. |
| TTS runner-up: Piper (`OHF-Voice/piper1-gpl`) | **GPL-3.0** (GitHub license API); embeds espeak-ng (GPL-3.0). The earlier MIT repository `rhasspy/piper` was archived 2025-08-26. | The `rhasspy/piper-voices` card says MIT, but each voice has its own `MODEL_CARD` pointing at the dataset license. The `en_US-lessac-medium` card links the 2013 Blizzard license, a separate set of terms. A medium voice file is about 63 MB. | Bundling or linking Piper raises the question of whether the application becomes a GPL-3.0 derivative. Usable only as a separate, user-installed process, with each voice's license reviewed individually. docs/STACK.md already warns about this. |
| TTS rejected: Coqui TTS | Code MPL-2.0 (`coqui-ai/TTS`, last push 2024-08-16; community fork `idiap/coqui-ai-TTS` is active). **XTTS-v2 weights: Coqui Public Model License (non-commercial); Hugging Face license `other`.** | Python and PyTorch stack. | Non-commercial weights block distribution, and the original vendor repository is not maintained. Heavy runtime for a Tauri app. |
| Windows SAPI voices (Microsoft David and Zira) | Operating-system component | Used in this spike only to generate synthetic test phrases locally; outputs are not committed. Possible zero-dependency fallback TTS on Windows. | Dated voice quality, and the voices are not redistributable. Not a candidate for the primary voice. |
| Tauri 2 (`tauri` 2.12.3, `tauri-build` 2.7.2) | MIT OR Apache-2.0 | Rust crates linked into the desktop host. The WebView2 runtime and installer toolchain are separate components with their own terms; **unverified in this spike**. | The desktop-shell spike owns tray, overlay and packaging evidence. Not exercised here. |
| windows-rs (`windows` 0.62.2) | MIT OR Apache-2.0 | Rust crate, compiled in. Windows only. | The 0.x API changes between releases; pin the version and wrap it in `crates/platform-windows/`. Not exercised here. |
| React and react-dom 19.3.0 | MIT | Bundled into the front-end build. Keep the notice in third-party notices. | None for licensing. |
| TypeScript 7.0.2 | **Apache-2.0** | Build-time compiler; not shipped inside the app bundle. | Registry `latest` is a new major version. Compatibility with the planned toolchain is **unverified**. |
| Rust crates added by this spike: `anyhow` 1.0.104, `clap` 4.6.7 (with derive), `hound` 3.5.1, plus `whisper-rs` | `anyhow` and `clap`: MIT OR Apache-2.0. `hound`: Apache-2.0 only. | 25 crates in the runtime tree (counting `whisper-rs`): 19 are MIT OR Apache-2.0, 2 Unlicense, 1 MIT, 1 Apache-2.0 OR MIT, 1 Apache-2.0, 1 `(MIT OR Apache-2.0) AND Unicode-3.0` (`unicode-ident`). No copyleft. | `hound` was last released in 2023-09 but is small and stable. `clap` and `anyhow` are spike conveniences; the production adapter may not need `clap`. |
| Build-only crates: `bindgen` 0.72.1, `libloading` 0.8.9, `cmake` and 25 others (28 in total) | `bindgen`: BSD-3-Clause. `libloading`: ISC. The rest are MIT, Apache-2.0 or dual MIT/Apache-2.0 (two are `Unlicense OR MIT`). | Not shipped in the binary. | Need libclang at build time. Pin the toolchain in the Windows build instructions. |
| Build tools: cmake 4.4.4, libclang 18.1.1 (PyPI wheel) | cmake: BSD-3-Clause (**unverified**, from general knowledge). libclang: LLVM license, Apache-2.0 with LLVM exception (**unverified**). | Downloaded to a scratch directory; never installed system-wide or committed. | None for distribution. |
| Test audio: `jfk.wav` (`ggml-org/whisper.cpp` samples) | U.S. government speech excerpt distributed in the MIT whisper.cpp repository; copyright status **unverified** | Downloaded at runtime for a sanity check only; not committed. | Not treated as a redistributable fixture; keep it out of Git. |

## TTS decision

**Recommendation: Kokoro-82M through ONNX Runtime, with Windows SAPI as an emergency fallback.**

| Criterion | Kokoro | Piper | Coqui |
| --- | --- | --- | --- |
| Engine and weight license | Apache-2.0 | GPL-3.0 engine; per-voice dataset licenses | MPL-2.0 code; XTTS weights non-commercial |
| Fits a closed or permissive Toby license | Yes | No, unless run as a separate process | Not for XTTS |
| Runtime weight | 86 to 326 MB ONNX, single runtime | About 63 MB per medium voice | PyTorch, much heavier |
| Maintenance signal | `hexgrad/kokoro` last pushed 2025-08-06; `kokoro-onnx` pushed 2026-09-01 | Active (`piper1-gpl` pushed 2026-10-11) | Original stalled; fork active |
| Evidence gathered here | Licenses and file sizes only | Licenses and file sizes only | Licenses only |

Why Kokoro: it is the only candidate whose engine and weights are both permissively licensed, which keeps Toby's own license choice open. Driven through ONNX Runtime from Rust, it also avoids a Python runtime.

Not shown, and required before this is more than a recommendation:

- First-audio latency and real-time factor on this machine (CPU only), for both the q8 and fp32 ONNX files.
- Whether the English path can phonemize technical vocabulary ("kubectl", "Tauri") without espeak-ng.
- Interruptibility: whether synthesis can be cancelled mid-utterance within the 250 ms stop target in docs/ROADMAP.md.
- A listening comparison against Piper's best medium voice.

Fallback if Kokoro fails those checks: Piper as a separately installed process (not bundled; the GPL obligations stay with that package), or a cloud voice if the owner accepts it. That choice belongs to the owner.

## Distribution plan

1. Application code is Rust and TypeScript, with its license unselected. A third-party notices file must list at least MIT, Apache-2.0, BSD-3-Clause, ISC and Unlicense components.
2. Models never enter Git or the installer by default. A first-run downloader fetches each model from a pinned URL, verifies SHA-256, shows the size, and offers removal.
3. No GPL component is linked or bundled. espeak-ng and Piper stay out of the dependency tree unless the owner decides otherwise.
4. Re-run this review at release (Milestone 7): versions, licenses and download URLs change.
