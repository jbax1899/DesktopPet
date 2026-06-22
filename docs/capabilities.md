# Current Capabilities

## Pet and conversation

- Transparent always-on-top pet overlay with draggable, clamped, and saved placement, tray controls, click-through, and configurable global chat (Shift+Space) and push-to-talk (Ctrl+Space) shortcuts.
- Action pad opens Chat, Memories, and Settings. Settings uses a three-tab layout: General, Cloud Providers, and Data Capture.
- Push-to-talk records while Ctrl+Space is held; on release, audio is transcribed via OpenRouter STT, sent as a chat message, and triggers a full AI response with screenshots when enabled.
- Typed chat stays in the conversation overlay with content-sized bubbles and safe interruption behavior.
- ElevenLabs replies use short-lived Agent WebSocket sessions. Standalone TTS streams MP3 through NAudio, drives mouth movement, and caches completed reply audio for replay.
- The WPF `bug.inp` prototype supports gaze, two-frame amplitude-driven mouth movement, and simple breathing/bob animation.

## History and memory

- Chat history, replay audio, and manually managed memories are separate local concepts sharing a single SQLite database at `%LOCALAPPDATA%\DesktopPet\memory.db`.
- Saved chat history is the cross-request conversation source of truth. Regular and ambient message budgets are independently configurable.
- The Memories window has three tabs: Chat History, Observations, and Memories. Chat History uses Chat and Details columns with a compact preview pane.
- Manual memories are joined into `memories_context`. Automatic capture and relevance retrieval are not implemented.

## Desktop context and ambient comments

- Screen observation and ambient comments default to off, with per-application Metadata and Vision permissions.
- Typed chat can include reduced foreground metadata, bounded UI Automation labels, and optionally a vision-analyzed screenshot of the active window.
- `DesktopEnvironmentCaptureCoordinator` polls the foreground window and fires `ChangeDetected` events on stable application/title changes.
- `ImageCaptureCoordinator` captures screenshots, runs vision analysis, and stores the latest `VisionObservation` and thumbnail.
- `CommentaryCoordinator` reacts to environment changes, evaluates local policy, and optionally requests a short ambient ElevenLabs comment with speech synthesis and playback.
- Screenshots are downscaled to 1280x720 and analyzed by the configured OpenRouter vision model. Two 10-tick sliders control Detail and Verbosity.
- Local policy evaluates freshness, user activity, cooldown, duplicate topics, and vision interest before requesting ambient comments. Commentary presets lock cooldown, duplicate-suppression, and check-in values; Custom unlocks them.
- The Memories window combines visual observations and ambient decisions in an auditable Observations tab with configurable retention limits.

## Ambient audio capture

- Ambient audio capture is separately opt-in and supports the default microphone, default system-output loopback, and per-application capture via the Windows process loopback API. Per-app captures are restored from saved settings on startup.
- Per-application audio capture uses `ActivateAudioInterfaceAsync` with `AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK` (Windows 10 19041+), wrapped by the `DesktopPet.Audio.ProcessLoopback.Native` project.
- NAudio capture is reduced to a mono in-memory analysis stream with local activity gating, pre-roll, silence detection, and a 20-second max segment.
- Shared speech playback suppresses capture processing while the pet speaks and for 500 milliseconds afterward.
- Audio analysis is disabled by default. When enabled, completed segments enter a bounded sequential queue (1 active, 2 waiting) with OpenRouter STT.
- Full transcripts exist only in a memory-only working buffer with 300-second retention. Transcript observations are stored locally and appear in the Observations tab with source, confidence, and optional excerpt.
- Microphone transcript excerpts default off; system-audio excerpts default on. Audio observations are available to typed replies and ambient generation but are not promoted to durable memory.
