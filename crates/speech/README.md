# toby-speech-spike

M0 measurement harness for local speech recognition (whisper.cpp through `whisper-rs`). It is a spike, not the production speech adapter. Results: `docs/M0_SPEECH_FINDINGS.md`. Licenses: `docs/M0_DEPENDENCY_REVIEW.md`.

The package is standalone (it has its own `Cargo.lock`) because the repository has no root Cargo workspace yet.

## Prerequisites

- Rust 1.88 or newer (built with 1.99.0)
- cmake and a C++ compiler (whisper.cpp is built from source by `whisper-rs-sys`)
- libclang (bindgen); set `LIBCLANG_PATH` if it is not on the default search path

## Model and sample downloads

Model weights are never committed. Fetch them at runtime; each file is verified against a pinned SHA-256:

```text
scripts/fetch-assets.sh            # into ./models (git-ignored)
scripts/fetch-assets.sh /some/dir  # elsewhere
```

## Commands

```text
cargo build --release

# synthetic 5 s tone-and-silence clip (no speech, no recorded audio)
toby-speech-spike fixture out.wav

# latency and optional word error rate; first run is reported as cold
toby-speech-spike transcribe --model models/ggml-base.en.bin --wav clip.wav \
    --reference "run cargo build release" --runs 5 --shrink-encoder

# options: --threads N, --beam-search, --prompt "vocabulary hint",
#          --noise-snr-db=10 (deterministic white noise), --vad-model models/ggml-silero-v5.1.2.bin

# speech segments from the Silero VAD
toby-speech-spike vad --vad-model models/ggml-silero-v5.1.2.bin --wav clip.wav
```

## Tests

```text
cargo test --release                      # unit tests, no model needed
WHISPER_MODEL=models/ggml-base.en.bin JFK_WAV=models/jfk.wav \
    cargo test --release -- --ignored     # end-to-end, needs the downloaded assets
```

The end-to-end tests are `#[ignore]`d only because they need the runtime-downloaded assets.

## Known limits

- Audio resampling is linear without a low-pass filter; adequate for 16 kHz files, not for live capture.
- `--vad-model` filters in the harness because whisper-rs's `WhisperState::full` ignores whisper.cpp's own VAD parameter.
- `--shrink-encoder` uses whisper.cpp's experimental `audio_ctx` override.
