# Desktop Pet Status

Working status and architecture note for the current prototype. Keep this file
limited to current capabilities, durable decisions, and active next steps.
Setup instructions and ElevenLabs dynamic variables live in `README.md`.

## Architecture

- **WPF/local code** owns the overlay, playback, character behavior, Windows
  observation, permissions, privacy reduction, timing, and ambient policy.
- **ElevenLabs** provides Agent text replies and `eleven_v3` speech. It receives
  profile, memory, conversation, temporal, and reduced desktop context through
  dynamic variables.
- **OpenRouter** optionally analyzes permitted screenshots into structured
  `VisionObservation` records and completed activity-gated audio segments into
  reduced `AudioObservation` records. Raw screenshots and audio are never sent
  to ElevenLabs.
- Provider calls stay behind small interfaces. Windows collectors do not call
  providers, and provider services do not inspect Windows directly.

## Current Capabilities

### Pet and conversation

- Transparent always-on-top pet overlay with draggable, clamped, saved
  placement, tray controls, click-through, a configurable global chat
  shortcut (Shift+Space), and a push-to-talk shortcut (Ctrl+Space).
- The action pad opens Chat, Memories, and Settings.
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

### History and memory

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

### Desktop context and ambient comments

- Screen observation and ambient comments default to off. Access is granted
  per application through separate Metadata and Vision permissions.
- Typed chat can include reduced foreground metadata and bounded UI Automation
  labels. An Advanced setting, enabled by default, also captures and analyzes
  the active window when Vision permission allows it. Unsupported, disabled,
  or timed-out inspection falls back to the available context.
- Permitted screenshots are captured in memory, downscaled to 1280x720, and
  analyzed by the configured OpenRouter vision model using structured output.
  Two 10-tick sliders control vision detail: Detail (summarized → verbatim)
  and Verbosity (succinct → verbose). These replace the former Brief/Detailed/
  Narrative radio buttons.
- Background observation reacts to stable application/title changes and
  periodic check-ins. Rapid switching is filtered by a minimum dwell time.
- Local policy evaluates freshness, user activity, cooldown, duplicate topics,
  and vision interest before requesting a short ambient ElevenLabs comment.
  Silence is the normal valid outcome.
- Settings expose commentary presets, an exact comment threshold, vision
  detail (two 10-tick sliders: Detail level and Verbosity), and a collapsed
  Advanced section for timing, interest-score weights, provider cost limits,
  context depth, and retention. Presets lock their exact cooldown,
  duplicate-suppression, and check-in values; Custom unlocks them.
  Configurable durations use seconds, except screenshot delay in milliseconds.
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

### Ambient audio capture prototype

- Ambient audio capture is separately opt-in and supports the default
  microphone and default system-output loopback device.
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

## Durable Decisions

- Keep OpenRouter as the observation/interpretation layer, ElevenLabs as the
  character/speech layer, and local code as the permission and policy layer.
- Keep observation separate from durable memory. Desktop context is optional
  per-turn context and must not silently become a memory.
- Default observation to globally paused with no application rules. Metadata
  and Vision remain separate permissions.
- Reduce Windows data before provider use. Do not send handles, process IDs,
  executable paths, exact bounds, full UI Automation trees, or other raw
  details without a concrete need.
- Only reduced summaries and possible topics may pass from OpenRouter analysis
  to ElevenLabs.
- Use `DesktopContextFormatter` as the bounded conversion point from reduced
  desktop models to provider text.
- Persist the exact reduced desktop context only on the corresponding bot
  history entry so the user can inspect what informed a reply.
- Keep live context preview local-only. It must not collect desktop context,
  capture screenshots, or call providers.
- Keep ambient policy local-first and cancellable. User requests take priority,
  and newer activity may cancel stale ambient work.
- Do not interrupt active speech until a newer submitted message has both reply
  text and TTS audio ready.
- Keep playback, interruption, transcript ownership, mouth movement, and
  character state local.
- Keep `IVoiceSynthesisService` small and the current provider boundaries unless
  they obstruct a concrete feature.
- Keep chat history and manually managed memories in one bundled SQLite
  database while preserving their separate selection rules and UI surfaces.

## Privacy and Local Data

- Provider API keys are encrypted with Windows DPAPI for the current user and
  stored separately from readable provider settings.
- Mutable JSON files use flushed temporary writes, atomic replacement, and one
  backup copy; malformed primaries are preserved for diagnosis.
- Observation settings default to paused and are stored separately in
  `%LOCALAPPDATA%\DesktopPet\observation-settings.json`.
- OpenRouter vision uses zero-data-retention routing by default. Test Vision
  uses a generated image rather than the user's desktop.
- Full captured screenshots are transient. Optional observation thumbnails,
  reduced observation records, chat history, memories, settings, and cached
  audio are stored under `%LOCALAPPDATA%\DesktopPet`.
- Raw microphone and system-loopback audio is never written to disk. Active
  segment buffers, queued PCM, temporary transcripts, and diagnostic metadata
  are cleared on disable or shutdown.
- Persisted audio observations contain reduced summaries, event labels, and
  optional bounded excerpts only. They do not contain full transcripts, PCM,
  sample arrays, device names, provider bodies, or transient policy scores.
- Raw audio is never sent to ElevenLabs. Only the bounded
  `audio_observation_history` text assembled from reduced observations and
  permitted transcript detail can enter Agent context.
- Do not persist full UI Automation trees, credentials, raw screenshots, or raw
  Windows identifiers in history, memory, diagnostics, or user-visible errors.
- Users can inspect and clear observations, inspect context used for replies,
  delete individual memories, and clear all memories.
- Diagnostics may report safe event and variable names, but not provider
  values, Agent IDs, signed URLs, replies, credentials, or full exceptions.

## Near-Term Work

- Smoke-test observation permissions, display scaling, multiple monitors, UI
  Automation timeouts, shutdown, and interruption by user chat.
- Tune the newly exposed observation defaults from real use.
- Add a visible vision-analysis error counter for debugging.
- Improve transcript timing if full-text-at-once remains too abrupt.
- Add automatic chat memory capture and retrieve only a few relevant memories
  per typed request.
- Decide whether audio events should independently trigger ambient-comment
  evaluation. Any trigger path must reuse the existing ambient policy and
  cooldown.

## Later Work

- Better credential storage, output-device selection, and richer subtitle or
  speech-bubble presentation.
- Blinking, more natural idle motion, and a cleaner renderer/performance
  boundary.
- Native Inochi2D experimentation, rig parameters, expressions, and mesh
  deformation.
- Secondary vision analysis for uncertain observations and a user-initiated
  "what do you see?" mode.
