#!/usr/bin/env bash
# Downloads model weights and a public-domain sample at runtime. Nothing fetched here is committed.
# Usage: scripts/fetch-assets.sh [dest-dir]   (default: crates/speech/models, git-ignored)
# Each file is verified against a pinned SHA-256 and removed if it does not match.
set -euo pipefail

dest="${1:-$(cd "$(dirname "$0")/.." && pwd)/models}"
mkdir -p "$dest"

HF=https://huggingface.co
# name|url|sha256
assets=(
  "ggml-base.en.bin|$HF/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin|a03779c86df3323075f5e796cb2ce5029f00ec8869eee3fdfb897afe36c6d002"
  "ggml-small.en.bin|$HF/ggerganov/whisper.cpp/resolve/main/ggml-small.en.bin|c6138d6d58ecc8322097e0f987c32f1be8bb0a18532a3f88f734d1bbf9c41e5d"
  "ggml-silero-v5.1.2.bin|$HF/ggml-org/whisper-vad/resolve/main/ggml-silero-v5.1.2.bin|29940d98d42b91fbd05ce489f3ecf7c72f0a42f027e4875919a28fb4c04ea2cf"
  "jfk.wav|https://github.com/ggml-org/whisper.cpp/raw/master/samples/jfk.wav|59dfb9a4acb36fe2a2affc14bacbee2920ff435cb13cc314a08c13f66ba7860e"
)

for entry in "${assets[@]}"; do
  IFS='|' read -r name url sha <<<"$entry"
  target="$dest/$name"
  if [[ -f "$target" ]] && echo "$sha  $target" | sha256sum --check --status; then
    echo "ok (cached) $name"
    continue
  fi
  echo "downloading $name"
  curl --fail --location --silent --show-error --output "$target.part" "$url"
  if ! echo "$sha  $target.part" | sha256sum --check --status; then
    rm -f "$target.part"
    echo "checksum mismatch for $name; removed" >&2
    exit 1
  fi
  mv "$target.part" "$target"
  echo "ok $name"
done
