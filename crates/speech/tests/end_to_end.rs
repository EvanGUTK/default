//! End-to-end recognition against a real model. Ignored by default because the
//! model weights are downloaded at runtime and never committed:
//!
//! ```text
//! scripts/fetch-assets.sh
//! WHISPER_MODEL=models/ggml-base.en.bin JFK_WAV=models/jfk.wav cargo test --release -- --ignored
//! ```

use std::path::PathBuf;

use toby_speech_spike::audio::load_wav;
use toby_speech_spike::metrics::word_error_rate;
use toby_speech_spike::recognizer::{Options, Recognizer};

const JFK_REFERENCE: &str = "And so my fellow Americans, ask not what your country can do for you, ask what you can do for your country.";
const MAX_ACCEPTABLE_WER: f32 = 0.1;

fn env_path(name: &str) -> PathBuf {
    PathBuf::from(std::env::var(name).unwrap_or_else(|_| panic!("set {name} (see file header)")))
}

fn options(shrink_encoder: bool) -> Options<'static> {
    Options {
        threads: 4,
        beam_search: false,
        initial_prompt: None,
        shrink_encoder,
    }
}

#[test]
#[ignore = "requires WHISPER_MODEL and JFK_WAV; run scripts/fetch-assets.sh"]
fn transcribes_public_domain_sample_accurately() {
    let samples = load_wav(&env_path("JFK_WAV")).unwrap();
    let mut recognizer = Recognizer::load(&env_path("WHISPER_MODEL")).unwrap();
    let transcript = recognizer.transcribe(&samples, &options(false)).unwrap();
    let wer = word_error_rate(JFK_REFERENCE, &transcript.text).unwrap();
    assert!(
        wer <= MAX_ACCEPTABLE_WER,
        "WER {wer} for {:?}",
        transcript.text
    );
}

#[test]
#[ignore = "requires WHISPER_MODEL and JFK_WAV; run scripts/fetch-assets.sh"]
fn shrunk_encoder_window_still_recognizes_sample() {
    let samples = load_wav(&env_path("JFK_WAV")).unwrap();
    let mut recognizer = Recognizer::load(&env_path("WHISPER_MODEL")).unwrap();
    let transcript = recognizer.transcribe(&samples, &options(true)).unwrap();
    let wer = word_error_rate(JFK_REFERENCE, &transcript.text).unwrap();
    assert!(
        wer <= MAX_ACCEPTABLE_WER,
        "WER {wer} for {:?}",
        transcript.text
    );
}
