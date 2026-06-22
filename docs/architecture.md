# Architecture

## Projects

The codebase is organized into three projects:

- **`DesktopPet.Core`** — WPF-free class library containing pure domain logic, storage, security, and provider integrations. No UI dependencies.
  - `Cloud/` — ElevenLabs and OpenRouter chat/voice services
  - `Memory/` — SQLite stores for chat history, memories, audio observations
  - `Audio/` — Pure audio types (`TranscriptBuffer`, `AudioSegmentBuffer`, `AnalysisCoordinator`)
  - `Storage/` — `JsonFileStore`, `CredentialStore`, `SecureJsonSerializer`
  - `Settings/` — Pure settings types (`ChatHistoryContextSettings`, `OverlayPosition`)
  - `Errors/` — Structured error types

- **`DesktopPet.App`** — WPF application with UI, Windows integrations, and composition root.
  - `Shell/` — `DesktopPetApplication` (~200 lines) as slim lifecycle orchestrator using 4 domain factories:
    - `MemoryServiceFactory` — Database, stores, ChatHistoryStore
    - `CloudServiceFactory` — ChatService, VoiceSynthesis, Models, Credits
    - `AudioServiceFactory` — TranscriptBuffer, AnalysisCoordinator, CaptureCoordinator
    - `ObservationServiceFactory` — VisualContext, ImageCapture, CommentaryCoordinator
  - `Settings/` — `SettingsHub` facade, `SettingsWindow`, `UiSettings`
  - `Audio/` — WPF-dependent coordinators, capture sources, playback
  - `Observation/` — Desktop context, screenshots, vision analysis
  - `Overlay/` — WPF pet window, conversation overlay

- **`DesktopPet.Audio.ProcessLoopback.Native`** — Standalone Win32 interop for process loopback capture.

## Key patterns

- **`SettingsHub`** unifies 6 settings stores behind a single facade with a `Saved` event
- **Interface segregation** — providers don't touch Windows; collectors don't call APIs
- **Injectable factories** — `AudioCaptureCoordinator` accepts a `Func<int, IAudioCaptureSource>?` for testability
- **`InternalsVisibleTo`** — Core exposes internals to App and Tests while keeping public API minimal

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