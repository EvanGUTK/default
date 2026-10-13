//! WAV I/O, resampling and synthetic fixture generation.

use std::f32::consts::TAU;
use std::path::Path;

use anyhow::{Context, Result, bail};

/// whisper.cpp consumes 16 kHz mono f32 audio.
pub const SAMPLE_RATE: u32 = 16_000;

const TONE_AMPLITUDE: f32 = 0.3;
const FIXTURE_SEGMENT_SECONDS: usize = 1;
/// Alternating silence and tone segments make up the 5 s synthetic fixture.
const FIXTURE_TONE_HZ: [Option<f32>; 5] = [None, Some(440.0), None, Some(880.0), None];

/// Reads a WAV file and converts it to 16 kHz mono f32.
pub fn load_wav(path: &Path) -> Result<Vec<f32>> {
    let mut reader =
        hound::WavReader::open(path).with_context(|| format!("opening {}", path.display()))?;
    let spec = reader.spec();
    let interleaved = read_interleaved_f32(&mut reader)?;
    Ok(to_mono_16k(&interleaved, spec.channels, spec.sample_rate))
}

fn read_interleaved_f32<R: std::io::Read>(reader: &mut hound::WavReader<R>) -> Result<Vec<f32>> {
    let spec = reader.spec();
    match (spec.sample_format, spec.bits_per_sample) {
        (hound::SampleFormat::Float, 32) => reader
            .samples::<f32>()
            .map(|s| s.context("reading float sample"))
            .collect(),
        (hound::SampleFormat::Int, bits @ (8 | 16 | 24 | 32)) => {
            let full_scale = (1i64 << (bits - 1)) as f32;
            reader
                .samples::<i32>()
                .map(|s| {
                    s.map(|v| v as f32 / full_scale)
                        .context("reading int sample")
                })
                .collect()
        }
        (format, bits) => bail!("unsupported WAV sample format {format:?} at {bits} bits"),
    }
}

/// Downmixes to mono and linearly resamples to [`SAMPLE_RATE`].
///
/// Linear interpolation without a low-pass filter is adequate for a measurement
/// harness fed 16 kHz audio; a production path needs a proper resampler.
pub fn to_mono_16k(interleaved: &[f32], channels: u16, sample_rate: u32) -> Vec<f32> {
    let channels = usize::from(channels.max(1));
    let mono: Vec<f32> = interleaved
        .chunks_exact(channels)
        .map(|frame| frame.iter().sum::<f32>() / channels as f32)
        .collect();
    resample_linear(&mono, sample_rate, SAMPLE_RATE)
}

fn resample_linear(input: &[f32], from_hz: u32, to_hz: u32) -> Vec<f32> {
    if from_hz == to_hz || input.is_empty() {
        return input.to_vec();
    }
    let ratio = f64::from(from_hz) / f64::from(to_hz);
    let out_len = (input.len() as f64 / ratio).floor() as usize;
    (0..out_len)
        .map(|i| {
            let pos = i as f64 * ratio;
            let idx = pos as usize;
            let frac = (pos - idx as f64) as f32;
            let a = input[idx];
            let b = input.get(idx + 1).copied().unwrap_or(a);
            a + (b - a) * frac
        })
        .collect()
}

/// Writes 16 kHz mono 16-bit PCM.
pub fn write_wav(path: &Path, samples: &[f32]) -> Result<()> {
    let spec = hound::WavSpec {
        channels: 1,
        sample_rate: SAMPLE_RATE,
        bits_per_sample: 16,
        sample_format: hound::SampleFormat::Int,
    };
    let mut writer = hound::WavWriter::create(path, spec)
        .with_context(|| format!("creating {}", path.display()))?;
    for sample in samples {
        writer.write_sample((sample.clamp(-1.0, 1.0) * f32::from(i16::MAX)) as i16)?;
    }
    writer.finalize().context("finalizing WAV")
}

/// A 5 s tone-and-silence clip. It contains no speech, so it exercises the
/// pipeline, VAD rejection and hallucination behavior without any recorded audio.
pub fn synthetic_fixture() -> Vec<f32> {
    let segment_len = FIXTURE_SEGMENT_SECONDS * SAMPLE_RATE as usize;
    FIXTURE_TONE_HZ
        .iter()
        .flat_map(|tone| {
            (0..segment_len).map(move |n| match tone {
                Some(hz) => TONE_AMPLITUDE * (TAU * hz * n as f32 / SAMPLE_RATE as f32).sin(),
                None => 0.0,
            })
        })
        .collect()
}

/// Adds deterministic white noise at the requested signal-to-noise ratio.
pub fn mix_noise(samples: &[f32], snr_db: f32, seed: u64) -> Vec<f32> {
    if samples.is_empty() {
        return Vec::new();
    }
    let signal_power = samples.iter().map(|s| s * s).sum::<f32>() / samples.len() as f32;
    let noise_amplitude = (signal_power / 10f32.powf(snr_db / 10.0)).sqrt() * 3f32.sqrt();
    let mut rng = XorShift64(seed.max(1));
    samples
        .iter()
        .map(|s| s + noise_amplitude * (rng.next_unit() * 2.0 - 1.0))
        .collect()
}

/// Tiny deterministic generator so noise runs are reproducible without a `rand` dependency.
struct XorShift64(u64);

impl XorShift64 {
    fn next_unit(&mut self) -> f32 {
        self.0 ^= self.0 << 13;
        self.0 ^= self.0 >> 7;
        self.0 ^= self.0 << 17;
        (self.0 >> 40) as f32 / (1u64 << 24) as f32
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn fixture_is_five_seconds_with_silent_edges() {
        let clip = synthetic_fixture();
        assert_eq!(clip.len(), 5 * SAMPLE_RATE as usize);
        assert!(clip[..SAMPLE_RATE as usize].iter().all(|s| *s == 0.0));
        assert!(
            clip[SAMPLE_RATE as usize..2 * SAMPLE_RATE as usize]
                .iter()
                .any(|s| s.abs() > 0.1)
        );
    }

    #[test]
    fn wav_round_trip_preserves_length_and_shape() {
        let dir = std::env::temp_dir().join(format!("speech-spike-{}", std::process::id()));
        std::fs::create_dir_all(&dir).unwrap();
        let path = dir.join("fixture.wav");
        let clip = synthetic_fixture();
        write_wav(&path, &clip).unwrap();
        let loaded = load_wav(&path).unwrap();
        std::fs::remove_dir_all(&dir).unwrap();
        assert_eq!(loaded.len(), clip.len());
        let max_err = clip
            .iter()
            .zip(&loaded)
            .map(|(a, b)| (a - b).abs())
            .fold(0.0, f32::max);
        assert!(max_err < 1e-3, "quantization error too large: {max_err}");
    }

    #[test]
    fn stereo_is_downmixed_by_averaging() {
        let stereo = [1.0, 0.0, 0.5, 0.5];
        assert_eq!(to_mono_16k(&stereo, 2, SAMPLE_RATE), vec![0.5, 0.5]);
    }

    #[test]
    fn resampling_scales_length_by_rate_ratio() {
        let input = vec![0.25; 48_000];
        let out = to_mono_16k(&input, 1, 48_000);
        assert_eq!(out.len(), 16_000);
        assert!(out.iter().all(|s| (s - 0.25).abs() < 1e-6));
    }

    #[test]
    fn noise_hits_requested_snr_and_is_deterministic() {
        let clip: Vec<f32> = (0..16_000)
            .map(|n| 0.3 * (TAU * 440.0 * n as f32 / 16_000.0).sin())
            .collect();
        let noisy = mix_noise(&clip, 10.0, 7);
        assert_eq!(noisy, mix_noise(&clip, 10.0, 7));
        let signal: f32 = clip.iter().map(|s| s * s).sum();
        let noise: f32 = clip.iter().zip(&noisy).map(|(c, n)| (n - c).powi(2)).sum();
        let measured_db = 10.0 * (signal / noise).log10();
        assert!(
            (measured_db - 10.0).abs() < 1.0,
            "measured SNR {measured_db} dB"
        );
    }
}
