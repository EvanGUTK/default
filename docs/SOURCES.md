# Primary sources and verification notes

## Inspection date

2026-10-13. Links describe external capabilities and policies at inspection time; they do not establish that Toby implements them. Recheck current documentation and provider eligibility before selecting versions or releasing an integration.

## Provider and agent references

- [OpenAI Sign in with ChatGPT quickstart](https://developers.openai.com/siwc/quickstart): identity sign-in and eligible plan-backed requests are distinct.
- [Codex app-server plan integration](https://developers.openai.com/siwc/token-sharing-open-source/codex-app-server): official integration path for supported plan usage.
- [Codex app-server](https://developers.openai.com/codex/app-server): managed conversations, protocol, state, and approvals.
- [Codex as a platform](https://developers.openai.com/blog/codex-as-a-platform): explains app-server, SDK, and noninteractive routes.
- [Anthropic legal and compliance](https://code.claude.com/docs/en/legal-and-compliance): credential use and third-party integration restrictions.
- [Claude Agent SDK and Claude plans](https://support.claude.com/en/articles/15036540-use-the-claude-agent-sdk-with-your-claude-plan): support guidance that needs to be reconciled with the exact proposed SDK deployment and authentication.
- [OpenAI computer use](https://developers.openai.com/api/docs/guides/tools-computer-use): screenshot/action loop and client-side execution responsibility.

## Desktop and speech references

- [Tauri capability reference](https://v2.tauri.app/reference/acl/capability/): command/window capability boundaries.
- [Tauri configuration](https://v2.tauri.app/reference/config/): platform configuration and host settings.
- [Microsoft screen capture](https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture): native capture and consent behavior.
- [Microsoft UI Automation overview](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-overview): accessibility/control observation concepts.
- [Microsoft SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput): native input injection and limitations.
- [Whisper.cpp](https://github.com/ggml-org/whisper.cpp): local recognition candidate.
- [Piper](https://github.com/OHF-Voice/piper1-gpl): local speech playback candidate; license/distribution review required.
- [SQLite FTS5](https://www.sqlite.org/fts5.html): proposed bounded local retrieval foundation.

## Repository references

- [Original inspiration](https://github.com/rbrown101010/bluey-by-riley): broad interaction reference; source reuse excluded by owner preference.
- [Fork](https://github.com/EvanGUTK/default): repository inspected for baseline.
- [GitHub licensing guide](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/licensing-a-repository): distinguishes a repository without a license from explicit licensing.

The proposed architecture, staffing, schedules, acceptance thresholds, and visual direction are design judgments. They are not vendor guarantees or measured performance results.
