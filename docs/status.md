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
  `VisionObservation` records. Raw screenshots are never sent to ElevenLabs.
- Provider calls stay behind small interfaces. Windows collectors do not call
  providers, and provider services do not inspect Windows directly.

## Current Capabilities

### Pet and conversation

- Transparent always-on-top pet overlay with draggable, clamped, saved
  placement, tray controls, click-through, and a configurable global chat
  shortcut.
- The action pad opens Chat, Memories, and Settings. Microphone input remains a
  disabled placeholder.
- Typed chat stays in the conversation overlay with content-sized bubbles,
  transcript display, stop-playback control, and safe interruption behavior.
- ElevenLabs replies use short-lived Agent WebSocket sessions. Standalone TTS
  streams MP3 through NAudio, drives mouth movement, and caches completed reply
  audio for replay.
- The WPF `bug.inp` prototype supports gaze, two-frame amplitude-driven mouth
  movement, and simple breathing/bob animation. It does not implement native
  Inochi2D rendering, mesh deformation, rig parameters, or expressions.

### History and memory

- Chat history, replay audio, and manually managed memories are separate local
  concepts.
- Saved chat history is the cross-request conversation source of truth.
  Regular and ambient message budgets are independently configurable from
  0–50, defaulting to 14 and 6.
- Selected turns are sent chronologically through `conversation_history`.
  Typed dialogue is protected from displacement by ambient messages.
- Successful direct and ambient replies save the exact dynamic-variable
  snapshot sent to ElevenLabs. Chat History provides a local-only context
  inspector and live preview.
- Manual memories are stored in local JSON and joined into
  `memories_context`. Automatic capture, relevance retrieval, and Mem0 storage
  are not implemented.

### Desktop context and ambient comments

- Screen observation and ambient comments default to off. Access is granted
  per application through separate Metadata and Vision permissions.
- Typed chat can include reduced foreground metadata and bounded UI Automation
  labels. Unsupported or timed-out structural inspection falls back to
  metadata.
- Permitted screenshots are captured in memory, downscaled to 1280x720, and
  analyzed by the configured OpenRouter vision model using structured output.
- Background observation reacts to stable application/title changes and
  periodic check-ins. Rapid switching is filtered by a minimum dwell time.
- Local policy evaluates freshness, user activity, cooldown, duplicate topics,
  and vision interest before requesting a short ambient ElevenLabs comment.
  Silence is the normal valid outcome.
- Commentary level controls cooldown, duplicate suppression, and check-in
  timing. Vision sensitivity controls the analysis threshold; scan quality
  controls observation detail.
- The Memories window combines visual observations and ambient decisions in an
  auditable Observations tab. Observation records are capped at 200 and ambient
  decisions at 100.
- Ambient visual observations can save local JPEG thumbnails for inspection.
  Clearing observations also removes those thumbnails.

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
- Treat local Mem0 as experimental behind one small REST boundary. Ask before
  enabling it, never install Docker silently, commit no secrets, and keep its
  dashboard outside the normal user flow.

## Privacy and Local Data

- Credentials currently use plain local JSON and should not be considered
  securely stored.
- Observation settings default to paused and are stored separately in
  `%LOCALAPPDATA%\DesktopPet\observation-settings.json`.
- OpenRouter vision uses zero-data-retention routing by default. Test Vision
  uses a generated image rather than the user's desktop.
- Full captured screenshots are transient. Optional observation thumbnails,
  reduced observation records, chat history, memories, settings, and cached
  audio are stored under `%LOCALAPPDATA%\DesktopPet`.
- Do not persist full UI Automation trees, credentials, raw screenshots, or raw
  Windows identifiers in history, memory, diagnostics, or user-visible errors.
- Users can inspect and clear observations, inspect context used for replies,
  delete individual memories, and clear all memories.
- Diagnostics may report safe event and variable names, but not provider
  values, Agent IDs, signed URLs, replies, credentials, or full exceptions.

## Near-Term Work

- Smoke-test observation permissions, display scaling, multiple monitors, UI
  Automation timeouts, shutdown, and interruption by user chat.
- Tune interest thresholds, commentary timing, dwell behavior, and speaking
  budgets from real use.
- Add a visible vision-analysis error counter for debugging.
- Improve transcript timing if full-text-at-once remains too abrupt.
- Add a tested, pinned localhost-only Mem0 Compose stack and one-time enable
  flow with clear setup/repair errors.
- Add automatic chat memory capture and retrieve only a few relevant memories
  per typed request.

## Later Work

- Optional push-to-talk microphone input, not a live voice session.
- Better credential storage, output-device selection, and richer subtitle or
  speech-bubble presentation.
- Blinking, more natural idle motion, and a cleaner renderer/performance
  boundary.
- Native Inochi2D experimentation, rig parameters, expressions, and mesh
  deformation.
- Hosted Mem0 only if the local experiment proves useful.
- Secondary vision analysis for uncertain observations and a user-initiated
  "what do you see?" mode.
