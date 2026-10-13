//! Thin wrapper over whisper-rs that times audio-end to text-out.

use std::path::Path;
use std::time::{Duration, Instant};

use anyhow::{Context, Result};
use whisper_rs::{
    FullParams, SamplingStrategy, WhisperContext, WhisperContextParameters, WhisperState,
};

use crate::audio::SAMPLE_RATE;

const BEAM_SIZE: i32 = 5;
/// Whisper's encoder context is 1500 frames for its fixed 30 s window (50 frames per second).
const ENCODER_FRAMES_PER_SECOND: usize = 50;
const FULL_ENCODER_CONTEXT: usize = 1500;
/// Extra frames beyond the clip length so trailing words are not truncated.
const ENCODER_CONTEXT_PADDING: usize = 100;

pub struct Recognizer {
    // Declared before `_context` so the state drops first.
    state: WhisperState,
    _context: WhisperContext,
    pub load_time: Duration,
}

/// Decoding options for one transcription call.
#[derive(Clone, Debug)]
pub struct Options<'a> {
    pub threads: i32,
    pub beam_search: bool,
    pub initial_prompt: Option<&'a str>,
    /// Shrink the encoder window to the clip length instead of the fixed 30 s.
    /// Experimental in whisper.cpp: faster for short clips, with an accuracy risk.
    pub shrink_encoder: bool,
}

pub struct Transcript {
    pub text: String,
    /// Wall time from handing over the finished audio buffer until the text is read out.
    pub latency: Duration,
}

impl Recognizer {
    /// Loads the model on CPU and creates a reusable decoding state.
    pub fn load(model_path: &Path) -> Result<Self> {
        let started = Instant::now();
        let mut params = WhisperContextParameters::default();
        params.use_gpu(false);
        let model = model_path
            .to_str()
            .context("model path is not valid UTF-8")?;
        let context = WhisperContext::new_with_params(model, params)
            .with_context(|| format!("loading model {}", model_path.display()))?;
        let state = context.create_state().context("creating whisper state")?;
        Ok(Self {
            state,
            _context: context,
            load_time: started.elapsed(),
        })
    }

    pub fn transcribe(&mut self, samples: &[f32], options: &Options<'_>) -> Result<Transcript> {
        let strategy = if options.beam_search {
            SamplingStrategy::BeamSearch {
                beam_size: BEAM_SIZE,
                patience: -1.0,
            }
        } else {
            SamplingStrategy::Greedy { best_of: 1 }
        };
        let mut params = FullParams::new(strategy);
        params.set_n_threads(options.threads);
        params.set_language(Some("en"));
        params.set_translate(false);
        params.set_no_context(true);
        params.set_print_special(false);
        params.set_print_progress(false);
        params.set_print_realtime(false);
        params.set_print_timestamps(false);
        if options.shrink_encoder {
            let frames = samples.len() / (SAMPLE_RATE as usize / ENCODER_FRAMES_PER_SECOND)
                + ENCODER_CONTEXT_PADDING;
            params.set_audio_ctx(frames.min(FULL_ENCODER_CONTEXT) as i32);
        }
        if let Some(prompt) = options.initial_prompt {
            params.set_initial_prompt(prompt);
        }

        let started = Instant::now();
        self.state
            .full(params, samples)
            .context("whisper inference failed")?;
        let text = self
            .state
            .as_iter()
            .map(|segment| segment.to_str_lossy().map(|t| t.into_owned()))
            .collect::<Result<Vec<_>, _>>()
            .context("reading segments")?
            .concat();
        let latency = started.elapsed();
        Ok(Transcript {
            text: text.trim().to_owned(),
            latency,
        })
    }
}
