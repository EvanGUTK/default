//! Standalone Silero VAD pass (via whisper.cpp) for endpointing measurements.

use std::path::Path;
use std::time::{Duration, Instant};

use anyhow::{Context, Result};
use whisper_rs::{WhisperVadContext, WhisperVadContextParams, WhisperVadParams};

use crate::audio::SAMPLE_RATE;

/// whisper.cpp reports VAD timestamps in centiseconds.
const CENTISECONDS_PER_SECOND: f32 = 100.0;
/// Context kept around each speech segment so word onsets and tails are not clipped.
const SPEECH_PAD_SECONDS: f32 = 0.1;

#[derive(Clone, Copy, Debug, PartialEq)]
pub struct SpeechSegment {
    pub start_seconds: f32,
    pub end_seconds: f32,
}

pub struct VadReport {
    pub segments: Vec<SpeechSegment>,
    pub model_load: Duration,
    pub detect: Duration,
    pub audio_seconds: f32,
}

pub fn detect(model_path: &Path, samples: &[f32], threads: i32) -> Result<VadReport> {
    let load_started = Instant::now();
    let mut ctx_params = WhisperVadContextParams::new();
    ctx_params.set_n_threads(threads);
    ctx_params.set_use_gpu(false);
    let model = model_path
        .to_str()
        .context("VAD model path is not valid UTF-8")?;
    let mut context = WhisperVadContext::new(model, ctx_params).context("loading VAD model")?;
    let model_load = load_started.elapsed();

    let detect_started = Instant::now();
    let found = context
        .segments_from_samples(WhisperVadParams::new(), samples)
        .context("running VAD")?;
    let detect = detect_started.elapsed();

    let segments = (0..found.num_segments())
        .filter_map(|idx| found.get_segment(idx))
        .map(|s| SpeechSegment {
            start_seconds: s.start / CENTISECONDS_PER_SECOND,
            end_seconds: s.end / CENTISECONDS_PER_SECOND,
        })
        .collect();
    Ok(VadReport {
        segments,
        model_load,
        detect,
        audio_seconds: samples.len() as f32 / SAMPLE_RATE as f32,
    })
}

/// Concatenates the padded speech segments, dropping everything else.
///
/// whisper.cpp's own `vad` parameter is only honored by `whisper_full`, not by the
/// `whisper_full_with_state` call that `WhisperState::full` makes, so filtering is done here.
pub fn speech_only(samples: &[f32], segments: &[SpeechSegment]) -> Vec<f32> {
    let pad = (SPEECH_PAD_SECONDS * SAMPLE_RATE as f32) as usize;
    let mut kept = Vec::new();
    let mut cursor = 0usize;
    for segment in segments {
        let start = ((segment.start_seconds * SAMPLE_RATE as f32) as usize).saturating_sub(pad);
        let end = (((segment.end_seconds * SAMPLE_RATE as f32) as usize) + pad).min(samples.len());
        let start = start.max(cursor);
        if start < end {
            kept.extend_from_slice(&samples[start..end]);
            cursor = end;
        }
    }
    kept
}

#[cfg(test)]
mod tests {
    use super::*;

    fn seg(start_seconds: f32, end_seconds: f32) -> SpeechSegment {
        SpeechSegment {
            start_seconds,
            end_seconds,
        }
    }

    #[test]
    fn no_segments_keeps_nothing() {
        assert!(speech_only(&[0.5; 16_000], &[]).is_empty());
    }

    #[test]
    fn keeps_padded_segment_and_clamps_to_buffer() {
        let samples = vec![1.0; 16_000];
        let kept = speech_only(&samples, &[seg(0.0, 0.5)]);
        // 0.5 s of speech plus 0.1 s trailing pad; no leading pad before sample 0.
        assert_eq!(kept.len(), 9_600);
        assert_eq!(speech_only(&samples, &[seg(0.9, 1.0)]).len(), 3_200);
    }

    #[test]
    fn overlapping_padding_is_not_duplicated() {
        let samples = vec![1.0; 32_000];
        let kept = speech_only(&samples, &[seg(0.5, 0.6), seg(0.65, 0.8)]);
        // Union of [0.4, 0.7] and [0.55, 0.9] is [0.4, 0.9] = 0.5 s.
        assert_eq!(kept.len(), 8_000);
    }
}
