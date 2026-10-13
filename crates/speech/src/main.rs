//! CLI for the M0 speech spike.

use std::path::PathBuf;
use std::time::Duration;

use anyhow::{Context, Result, bail};
use clap::{Parser, Subcommand};
use toby_speech_spike::audio::{self, SAMPLE_RATE};
use toby_speech_spike::metrics::word_error_rate;
use toby_speech_spike::recognizer::{Options, Recognizer};
use toby_speech_spike::vad;

const DEFAULT_RUNS: usize = 3;
const NOISE_SEED: u64 = 0x5EED;

#[derive(Parser)]
#[command(about = "M0 local speech spike (whisper.cpp via whisper-rs)")]
struct Cli {
    #[command(subcommand)]
    command: Command,
}

#[derive(Subcommand)]
enum Command {
    /// Write the synthetic 5 s tone-and-silence fixture (no speech, no recorded audio).
    Fixture { out: PathBuf },
    /// Transcribe a WAV file and report audio-end to text latency.
    Transcribe {
        #[arg(long)]
        model: PathBuf,
        #[arg(long)]
        wav: PathBuf,
        /// Expected text; enables word error rate.
        #[arg(long)]
        reference: Option<String>,
        /// Timed runs on a warm model; the first run is reported separately.
        #[arg(long, default_value_t = DEFAULT_RUNS)]
        runs: usize,
        #[arg(long, default_value_t = default_threads())]
        threads: i32,
        #[arg(long)]
        beam_search: bool,
        /// Vocabulary hint passed to the decoder as the initial prompt.
        #[arg(long)]
        prompt: Option<String>,
        /// Mix deterministic white noise at this SNR before decoding.
        #[arg(long)]
        noise_snr_db: Option<f32>,
        /// Silero VAD model; only detected speech segments are sent to the recognizer.
        #[arg(long)]
        vad_model: Option<PathBuf>,
        /// Shrink the encoder window to the clip length (experimental in whisper.cpp).
        #[arg(long)]
        shrink_encoder: bool,
    },
    /// Print speech segments found by the Silero VAD model.
    Vad {
        #[arg(long)]
        vad_model: PathBuf,
        #[arg(long)]
        wav: PathBuf,
        #[arg(long, default_value_t = default_threads())]
        threads: i32,
    },
}

fn default_threads() -> i32 {
    std::thread::available_parallelism().map_or(4, |n| n.get().min(8) as i32)
}

fn millis(d: Duration) -> f64 {
    d.as_secs_f64() * 1000.0
}

fn main() -> Result<()> {
    match Cli::parse().command {
        Command::Fixture { out } => {
            audio::write_wav(&out, &audio::synthetic_fixture())?;
            println!("wrote {}", out.display());
            Ok(())
        }
        Command::Transcribe {
            model,
            wav,
            reference,
            runs,
            threads,
            beam_search,
            prompt,
            noise_snr_db,
            vad_model,
            shrink_encoder,
        } => {
            if runs == 0 {
                bail!("--runs must be at least 1");
            }
            let mut samples = audio::load_wav(&wav)?;
            if let Some(snr) = noise_snr_db {
                samples = audio::mix_noise(&samples, snr, NOISE_SEED);
            }
            let options = Options {
                threads,
                beam_search,
                initial_prompt: prompt.as_deref(),
                shrink_encoder,
            };
            if let Some(vad_model) = &vad_model {
                samples = apply_vad(vad_model, &samples, threads)?;
            }
            transcribe(&model, &samples, &options, runs, reference.as_deref())
        }
        Command::Vad {
            vad_model,
            wav,
            threads,
        } => {
            let samples = audio::load_wav(&wav)?;
            let report = vad::detect(&vad_model, &samples, threads)?;
            println!(
                "audio_s={:.2} vad_load_ms={:.1} vad_detect_ms={:.1}",
                report.audio_seconds,
                millis(report.model_load),
                millis(report.detect)
            );
            for s in &report.segments {
                println!("speech {:.2}s..{:.2}s", s.start_seconds, s.end_seconds);
            }
            println!("segments={}", report.segments.len());
            Ok(())
        }
    }
}

/// Returns only the speech portion of `samples`, printing what the VAD removed.
fn apply_vad(vad_model: &std::path::Path, samples: &[f32], threads: i32) -> Result<Vec<f32>> {
    let report = vad::detect(vad_model, samples, threads)?;
    let kept = vad::speech_only(samples, &report.segments);
    println!(
        "vad: detect_ms={:.0} segments={} kept_s={:.2} of {:.2}",
        millis(report.detect),
        report.segments.len(),
        kept.len() as f64 / f64::from(SAMPLE_RATE),
        report.audio_seconds
    );
    Ok(kept)
}

fn transcribe(
    model: &std::path::Path,
    samples: &[f32],
    options: &Options<'_>,
    runs: usize,
    reference: Option<&str>,
) -> Result<()> {
    if samples.is_empty() {
        println!("no speech detected; recognizer not invoked");
        println!("text=\"\"");
        return Ok(());
    }
    let mut recognizer = Recognizer::load(model)?;
    let audio_seconds = samples.len() as f64 / f64::from(SAMPLE_RATE);

    // Cold run first: it includes one-time allocation and cache warm-up costs.
    let cold = recognizer.transcribe(samples, options)?;
    let mut warm = Vec::with_capacity(runs);
    let mut last_text = cold.text.clone();
    for _ in 0..runs {
        let t = recognizer.transcribe(samples, options)?;
        warm.push(t.latency);
        last_text = t.text;
    }
    let warm_mean = warm.iter().map(|d| millis(*d)).sum::<f64>() / runs as f64;
    let warm_min = warm.iter().map(|d| millis(*d)).fold(f64::MAX, f64::min);
    let warm_max = warm.iter().map(|d| millis(*d)).fold(0.0, f64::max);

    println!("model={}", model.display());
    println!(
        "threads={} beam_search={} shrink_encoder={}",
        options.threads, options.beam_search, options.shrink_encoder
    );
    println!("audio_s={audio_seconds:.2}");
    println!("model_load_ms={:.0}", millis(recognizer.load_time));
    println!("cold_latency_ms={:.0}", millis(cold.latency));
    println!("warm_latency_ms mean={warm_mean:.0} min={warm_min:.0} max={warm_max:.0} (n={runs})");
    println!("real_time_factor={:.3}", warm_mean / 1000.0 / audio_seconds);
    println!("text={last_text:?}");
    if let Some(reference) = reference {
        let wer = word_error_rate(reference, &last_text).context("reference has no words")?;
        println!("reference={reference:?}");
        println!("wer={wer:.3}");
    }
    Ok(())
}
