# Desktop Pet Status

Working status and architecture note for the current prototype. Keep this file
limited to current capabilities, durable decisions, and active next steps.
Setup instructions and ElevenLabs dynamic variables live in `README.md`.

## References

- [Architecture](architecture.md) — project layout and key patterns
- [Capabilities](capabilities.md) — current features and subsystems

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
- Keep process loopback P/Invoke isolated in `DesktopPet.Audio.ProcessLoopback.Native`
  until NAudio adds native support. Replace with NAudio's built-in API when available.
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
- Replace `DesktopPet.Audio.ProcessLoopback.Native` interop with NAudio's
  built-in process loopback once NAudio 3.x ships support (PR #1225).

## Later Work

- Better credential storage, output-device selection, and richer subtitle or
  speech-bubble presentation.
- Blinking, more natural idle motion, and a cleaner renderer/performance
  boundary.
- Native Inochi2D experimentation, rig parameters, expressions, and mesh
  deformation.
- Secondary vision analysis for uncertain observations and a user-initiated
  "what do you see?" mode.
