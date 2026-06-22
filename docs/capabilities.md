# Current Capabilities

## Pet and conversation

- Transparent always-on-top pet overlay with draggable, clamped, saved
  placement, tray controls, click-through, a configurable global chat
  shortcut (Shift+Space), and a push-to-talk shortcut (Ctrl+Space).
- The action pad opens Chat, Memories, and Settings.   Settings uses a three-tab layout: General (profile and input), Cloud
  Providers (ElevenLabs and OpenRouter), and Data Capture (audio capture
  with a System row for system-wide audio, vision/screen context with
  commentary presets including Off, and per-window permissions).
- Push-to-talk records from the default microphone while Ctrl+Space is held.
  On release, the recording is transcribed via OpenRouter STT and sent as a
  chat message, triggering a full AI response. Screenshots are captured when
  enabled. A Listening mood shows while recording.
- Typed chat stays in the conversation overlay with content-sized bubbles,
  a solid light-gray transcript bubble with outlined green text, and safe
  interruption behavior.
- ElevenLabs replies use short-lived Agent WebSocket sessions. Standalone TTS
  streams MP3 through NAudio, drives mouth movement, and caches completed reply
  audio for replay.
- The WPF `bug.inp` prototype supports gaze, two-frame amplitude-driven mouth
  movement, and simple breathing/bob animation. It does not implement native
  Inochi2D rendering, mesh deformation, rig parameters, or expressions.

## History and memory

- Chat history, replay audio, and manually managed memories are separate local
  concepts.
- Chat history and memories share `%LOCALAPPDATA%\DesktopPet\memory.db` as their
  SQLite source of truth.
- Saved chat history is the cross-request conversation source of truth.
  Regular and ambient message budgets are independently configurable from
  0–50, defaulting to 14 and 6.
- Selected turns are sent chronologically through `conversation_history`.
  Typed dialogue is protected from displacement by ambient messages.
- Recent reduced audio observations can be sent through
  `audio_observation_history`. A 10-tick Transcript verbosity slider controls
  how much transcript text appears in Agent context (low = labels only, high =
  full transcript), and audio context depth is configurable from 0–20.
- Successful direct and ambient replies save the exact dynamic-variable
  snapshot sent to ElevenLabs. The Memories window orders its tabs as Chat
  History, Observations, and Memories. Each tab has a top action bar for
  selected-item deletion and clearing, while Memories also opens a small Add
  dialog.
- Chat History uses labeled Chat and Details columns. The compact local-only
  Details pane previews the next request with no selection; selecting a
  recorded reply switches it to that reply's saved snapshot.
- Manual memories are joined into `memories_context`. Automatic capture and
  relevance retrieval are not implemented, so every saved memory is currently
  included.

## Desktop context and ambient comments

- Screen observation and ambient comments default to off. Access is granted
  per application through separate Metadata and Vision permissions.
- Typed chat can include reduced foreground metadata and bounded UI Automation
  labels. An Advanced setting, enabled by default, also captures and analyzes
  the active window when Vision permission allows it. Unsupported, disabled,
  or timed-out inspection falls back to the available context.
- `DesktopEnvironmentCaptureCoordinator` polls the foreground window and
  optionally collects UI Automation structure. It fires `ChangeDetected` events
  on stable application/title changes and periodic check-ins.
- `ImageCaptureCoordinator` captures screenshots and runs vision analysis on
  change events, storing the latest `VisionObservation` and thumbnail for
  consumers via `IVisionObservationProvider`.
- `CommentaryCoordinator` is the commentary loop that reacts to environment
  changes and pulls from all capture systems. It optionally captures and
  analyzes screenshots inline, evaluates local policy, requests a short ambient
  ElevenLabs comment, and handles speech synthesis and playback. Silence is the
  normal valid outcome.
- Permitted screenshots are captured in memory, downscaled to 1280x720, and
  analyzed by the configured OpenRouter vision model using structured output.
  Two 10-tick sliders control vision detail: Detail (summarized → verbatim)
  and Verbosity (succinct → verbose).
- Rapid switching is filtered by a minimum dwell time.
- Local policy evaluates freshness, user activity, cooldown, duplicate topics,
  and vision interest before requesting a short ambient ElevenLabs comment.
- Settings expose commentary presets, an exact comment threshold, vision
  detail (two 10-tick sliders: Detail level and Verbosity), and a collapsed
  Advanced section for timing, interest-score weights, provider cost limits,
  context depth, and retention. Presets lock their exact cooldown,
  duplicate-suppression, and check-in values; Custom unlocks them.
  Configurable durations use seconds, except screenshot delay in milliseconds.
  Commentary is its own section under Data Capture, decoupled from Vision.
- The comment threshold is evaluated after vision analysis. Its weighted score
  combines novelty, relevance, privacy safety, and interruption cost.
- Duplicate-topic suppression applies to metadata-only and vision-backed
  comments. One observation-context depth is shared by vision analysis, typed
  replies, and ambient replies.
- The Memories window combines visual observations and ambient decisions in an
  auditable Observations tab. Their retention limits are configurable in
  Advanced settings and default to 200 observations and 100 decisions.
- Individual visual, ambient, and audio observation entries can be deleted.
  Deleting a visual record also removes its thumbnail and linked ambient
  decision; deleting an audio record also removes its temporary transcript
  chunk when it is still in memory.
- Clicking an already-selected chat, observation, or memory deselects it.
- Ambient visual observations can save local JPEG thumbnails for inspection.
  Clearing observations also removes those thumbnails.

## Ambient audio capture prototype

- Ambient audio capture is separately opt-in and supports the default
  microphone, default system-output loopback device, and per-application
  audio capture via the Windows process loopback API. Per-app captures are
  restored from saved settings on application startup.
- Per-application audio capture uses `ActivateAudioInterfaceAsync` with
  `AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK` (Windows 10 build 19041+).
  The `DesktopPet.Audio.ProcessLoopback.Native` project wraps the Win32
  interop, providing a clean `StartAsync`/`Stop` API that delivers raw PCM16
  frames via callback. Stop is synchronous to avoid races when switching
  per-app captures (the old capture must fully release the native audio client
  before a new one activates).
  // TODO: Replace with NAudio's built-in process loopback once NAudio 3.x ships
  // support (PR #1225 / WasapiCapture.CreateForProcessCaptureAsync).
- The application grid includes an Audio column with per-app toggle buttons.
  When system-wide audio is enabled, per-app audio buttons are grayed out
  because system loopback already captures all application audio.
- NAudio capture is reduced to a mono in-memory analysis stream. Local activity
  gating uses a short pre-roll, rejects brief spikes, closes segments after
  silence, and force-closes continuous segments at 20 seconds.
- Shared speech playback suppresses microphone and system-loopback processing
  while the pet speaks and for 500 milliseconds afterward. Capture devices stay
  active, while partial segments are reset at both suppression boundaries.
- Audio analysis is separately disabled by default. When enabled, completed
  segments enter a bounded sequential queue with one active OpenRouter request
  and at most two waiting segments. Capture continues if analysis is disabled,
  unavailable, rejected, or fails.
- The selected OpenRouter model must be an STT model (e.g. whisper-large-v3)
  listed via the `/api/v1/audio/transcriptions` endpoint. Mono samples are
  converted to PCM16 WAV in memory and sent as base64 with zero-data-retention
  routing when configured.
- Full transcripts exist only in a memory-only working buffer with 300-second
  default retention. Transcript observations are stored in
  `%LOCALAPPDATA%\DesktopPet\audio-observations.json`, default to 100 records,
  and appear in the Memories window's Observations tab with source, confidence,
  optional excerpt, and transcript-retention status.
- Microphone transcript excerpts default off; system-audio excerpts default
  on. Persisted excerpts are one line and capped at 160 characters. Failed or
  low-confidence transcription is diagnostics-only.
- Transcript observations are available to typed replies and ambient reply
  generation. Context depth controls how many recent observations are included.
  Audio observations are not promoted to durable memory.
- Clearing observations also clears reduced audio observations, queued audio
  analysis work, and the memory-only transcript buffer.
