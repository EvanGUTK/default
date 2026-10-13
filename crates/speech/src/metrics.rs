//! Word error rate over normalized text.

/// Lowercases, drops punctuation (keeping apostrophes inside words) and splits on whitespace.
pub fn normalize(text: &str) -> Vec<String> {
    text.to_lowercase()
        .split(|c: char| !(c.is_alphanumeric() || c == '\''))
        .filter(|w| !w.is_empty())
        .map(str::to_owned)
        .collect()
}

/// Word-level Levenshtein distance divided by reference length.
///
/// Returns `None` when the reference has no words.
pub fn word_error_rate(reference: &str, hypothesis: &str) -> Option<f32> {
    let reference = normalize(reference);
    let hypothesis = normalize(hypothesis);
    if reference.is_empty() {
        return None;
    }
    let mut previous: Vec<usize> = (0..=hypothesis.len()).collect();
    for (i, ref_word) in reference.iter().enumerate() {
        let mut current = vec![i + 1];
        for (j, hyp_word) in hypothesis.iter().enumerate() {
            let substitution = previous[j] + usize::from(ref_word != hyp_word);
            current.push(substitution.min(previous[j + 1] + 1).min(current[j] + 1));
        }
        previous = current;
    }
    Some(previous[hypothesis.len()] as f32 / reference.len() as f32)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn identical_text_ignoring_case_and_punctuation_has_zero_wer() {
        assert_eq!(
            word_error_rate("Run cargo build, release.", "run cargo build release"),
            Some(0.0)
        );
    }

    #[test]
    fn counts_substitution_insertion_and_deletion() {
        assert_eq!(word_error_rate("a b c d", "a x c"), Some(0.5));
        assert_eq!(word_error_rate("a b", "a b c d"), Some(1.0));
    }

    #[test]
    fn empty_reference_is_undefined() {
        assert_eq!(word_error_rate("...", "anything"), None);
    }

    #[test]
    fn empty_hypothesis_is_total_error() {
        assert_eq!(word_error_rate("a b c", ""), Some(1.0));
    }
}
