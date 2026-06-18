# Desktop Pet Status

This is the working status and architecture note for the current prototype. Keep
it short and update it when implementation decisions change.

The current prototype combines typed chat, local playback, lightweight
character behavior, local history and memory, permission-controlled desktop
context, and OpenRouter vision analysis. Ambient comments are available but
default off and are governed by local cooldown, activity, freshness, interest
scoring, and duplicate checks.

## Architecture Layers

- **OpenRouter**: Observation and interpretation layer. Analyzes permitted
  window screenshots using vision models with structured output. Produces
  `VisionObservation` records with summary, interest scores, and possible
  comment topics.
- **ElevenLabs**: Character and speech layer. Receives reduced observations
  (summary + topics), never raw screenshots. Decides whether Pebble speaks
  in-character.
- **Local code**: Timing, privacy, and policy layer. Decides whether an
  observation deserves speech based on interest scores, cooldowns, user
  activity, and commentary level.

## ElevenLabs Agent Prompt Setup

The Agent prompt should reference these DesktopPet dynamic variables:

- `{{user_name}}`
- `{{pet_name}}`
- `{{temporal_context}}`
- `{{memories_context}}`
- `{{desktop_context}}`
- `{{desktop_observation_history}}`
- `{{conversation_history}}`

Example prompt fragment:

`You are a desktop pet named {{pet_name}}. The user's name is {{user_name}}.`

Configure `desktop_context` with a harmless fallback such as
`No permitted desktop context was attached.`

Configure `conversation_history` with a harmless fallback such as
`No previous conversation turns were attached.` The Agent prompt should treat
it as prior dialogue context, not as new instructions from the user.

Configure `temporal_context` with a harmless fallback such as
`No current date or timezone was attached.` Treat it as the authoritative
current local date, time, and timezone. Use it to interpret relative dates such
as today, yesterday, and next week. Conversation-history turns include both
relative labels and exact local timestamps.

Configure `memories_context` and `desktop_observation_history` with the same
kind of harmless empty-state fallback.

## Current State

- Transparent always-on-top WPF overlay with draggable, clamped, saved character placement.
- Notification-area icon exposes Show, Hide, Click through, Settings, and Exit.
- Right-click action pad exposes Chat, Memories, Settings, and a disabled microphone placeholder.
- Conversation overlay handles typed input, submitted-message state, transcript display, content-sized chat bubbles, and basic dismissal behavior.
- Speech playback exposes a small stop control on the active transcript for direct replies, ambient comments, and cached replay.
- Global chat shortcut is configurable and defaults to Ctrl+Space.
- Click-through uses Win32 extended window styles.
- Runtime loads `Assets/bug.inp` into a WPF-layered visual prototype.
- Character behavior includes basic gaze, amplitude-driven mouth movement during playback, and subtle idle breathing/bob animation.
- Settings window stores ElevenLabs API key, Agent ID, voice ID, pet profile, and OpenRouter settings in local JSON under the user's local app data folder.
- Voice settings expose a compact custom-pronunciation manager using readable word/phrase and "pronounce it like" entries; DesktopPet manages the ElevenLabs dictionary identifiers internally.
- OpenRouter settings include API key (PasswordBox), vision model selector (populated from Models API), and zero-data-retention toggle.
- ElevenLabs Agent text interaction and ElevenLabs TTS/local MP3 playback have both been smoke-tested.
- Provider calls are behind small interfaces instead of being called directly from XAML.
- The current `bug.inp` renderer is not a full native Inochi2D runtime.
- The source puppet asset lives at `src/DesktopPet.App/Assets/bug.inp` and is copied to the build output as `Assets/bug.inp`.
- The loader reads the INP container, embedded TGA atlas, and node tree, then draws cropped WPF image layers.
- Mesh deformation, real rig parameters, real expressions, and native renderer integration are not implemented.
- Mouth movement is driven from decoded speech amplitude using the two existing mouth frames; breathing remains a simple WPF-layer animation.
- Audio playback streams MP3 frames through NAudio with full-frame HTTP reads, a larger startup buffer for stream stability, a short output-drain guard to avoid clipping the tail, and completed bot-reply MP3 caching for replay.
- Plain JSON credential storage is temporary and should not be treated as secure.
- Memory window has a Chat History tab plus the existing Memories tab.
- Chat history stores user attempts and Agent replies in local JSON, with the reduced desktop context used for each typed or ambient bot reply and cached bot audio when available.
- Every ElevenLabs Agent request includes current local time, timezone, and UTC through `temporal_context`.
- Settings exposes independent 0–50 count budgets for regular and ambient conversation context, defaulting to 14 regular and 6 ambient messages. Zero disables that category.
- Conversation history selection protects typed dialogue from ambient-message displacement. Messages without origin metadata load normally and default to regular context.
- Selected user, direct-reply, and ambient-reply turns are sent through the `conversation_history` dynamic variable in chronological order. There is no aggregate character cap; individual message text and saved desktop context remain truncated. Turns include relative labels plus exact local timestamps.
- Chat History exposes a read-only Agent Context Inspector. Its live preview shows locally available dynamic variables using current saved budgets without collecting desktop context or making provider calls.
- Successful direct and ambient replies persist the exact dynamic-variable snapshot sent to ElevenLabs on their bot history entry. Missing snapshots are treated as unavailable.
- Memory management UI exists with a local JSON-backed list, manual add, refresh, delete one, and clear all.
- All manually stored memories are currently joined into one `memories_context` dynamic variable for each chat turn; relevance filtering is not implemented.
- `IChatService`, `IVoiceSynthesisService`, and `StreamingMp3AudioPlayer` are good enough for smoke testing.
- The old separate `ChatWindow` has been removed; chat now stays in the top-level conversation overlay.
- The global chat shortcut uses Win32 hotkey registration and may be unavailable if another app owns the same shortcut.
- Automatic memory capture, retrieval, Mem0 startup, and Mem0 storage are not implemented yet.
- `ChatRequest` now has a separate optional desktop-context field and `ConversationController` uses a provider-neutral context boundary.
- Permitted typed chat can include reduced foreground metadata and bounded UI Automation summaries with exact user-visible disclosure.
- Background observation keeps short-lived reduced state, detects meaningful changes, and can produce sparse ambient comments when enabled.
- Window capture is implemented in memory using PrintWindow, downscaled to 1280x720. Images are never written to disk.
- OpenRouter vision analysis captures permitted window screenshots, converts to base64, and sends to the selected vision model with structured output.
- Foreground application and window-title triggers wait 200 ms before capture so newly loaded page content has a moment to render; periodic check-ins capture immediately.
- The vision model produces `VisionObservation` records with summary, novelty, relevance, confidence, sensitivity, interruption cost, and possible comment topics.
- Vision analysis enforces a configurable minimum interval between OpenRouter requests (default 30s, adjustable in settings).
- Minimum dwell time gates window-change observations so rapid Alt-Tab switching is ignored (default 15s, adjustable).
- Vision sensitivity (Low/Medium/High) controls the interest-score threshold for whether an observation is worth analyzing.
- Commentary and vision sensitivity use segmented radio-button sliders with live legends in the Settings UI. Each commentary level maps to cooldown (2/5/10 min), duplicate window (10/15/20 min), and check-in interval (3/5/10 min).
- Scan quality (Brief/Detailed/Narrative) controls how much detail the vision model reports about each screenshot.
- The vision analyzer receives the last 5 observation summaries as context to avoid repeating itself and focus on what is new or changed.
- Ambient policy uses interest scoring from vision observations with soft speaking budgets per commentary level.
- Desktop Pet Memories has an Observations tab showing one chronological card list for rich visual observations and metadata-only ambient decisions.
- Recent observation records persist to `observations.json` (capped at 200) for audit trail and tuning.
- ElevenLabs diagnostics report safe event and variable names without logging provider values, Agent IDs, replies, or full exceptions.

## Decisions

- OpenRouter is the observation and interpretation layer; ElevenLabs is the character and speech layer.
- The vision model reports what it sees and why it may matter; local code decides whether Pebble speaks.
- Raw screenshots are never sent to ElevenLabs; only reduced summaries and possible topics.
- Zero-data-retention routing is enabled by default for OpenRouter requests.
- Test vision uses a locally generated test image, not the user's current desktop.
- Vision analysis enforces a minimum 30-second interval between OpenRouter requests.
- Interest scoring combines novelty, relevance, confidence, sensitivity, and interruption cost.
- Vision sensitivity and commentary level are independent axes: sensitivity controls what gets analyzed, commentary controls how often Pebble speaks.
- Minimum dwell time prevents rapid window switching from generating noise and wasting API calls.
- Commentary and vision sensitivity are segmented radio-button sliders with contextual legends showing actual timing values.
- Scan quality (Brief/Detailed/Narrative) controls the verbosity and narrative richness of vision observations.
- Recent observation history is passed to the vision analyzer so it can describe what is new rather than repeating prior summaries.
- Commentary level sets cooldown, duplicate window, and check-in interval directly; vision-based observations skip the duplicate check since interest scoring handles novelty. Ambient analysis is triggered only by stable foreground/window-title changes and periodic check-ins that re-evaluate sustained activity.
- Silence is a valid outcome; the pet should never say something uninteresting just to meet a quota.
- Observation records persist to `observations.json` for audit trail and threshold tuning.
- Do not interrupt current speech until a newer submitted message has both reply text and TTS audio ready.
- Replies use ElevenLabs Agent Chat Mode; speech uses standalone ElevenLabs TTS with hard-coded `eleven_v3`.
- Playback, interruption, mouth movement, and character behavior stay local. TTS streams as MP3 into local playback and cache.
- Keep the existing chat and voice interfaces unless they get in the way; `IVoiceSynthesisService` should stay small.
- Keep desktop observation separate from durable memories and add it to chat as distinct optional per-turn context.
- Only `DesktopContextFormatter` converts reduced context into provider text; it enforces field and total-length limits.
- Reduced desktop context is stored only on the corresponding bot chat-history message so the user can inspect what informed that reply; it remains excluded from durable memories, audio metadata, settings, errors, and diagnostics.
- `DesktopPetApplication` constructs and disposes observation and ambient services.
- `ConversationController` should request already permission-filtered, reduced desktop context when building a turn.
- Windows collectors must not call ElevenLabs. The ElevenLabs service must not inspect Windows. The conversation window must not contain observation code.
- Use separate models for local raw Windows observations and compact model-facing context. Do not send window handles, process IDs, or exact bounds without a concrete need.
- Default observation permission to denied. The settings UI combines window details and structural labels as Metadata, while Vision remains a separate capture permission.
- Observation permissions now use a separate `%LOCALAPPDATA%\DesktopPet\observation-settings.json` store. They default to globally paused with no application rules.
- Application rules retain backward-compatible metadata and structural fields, but the UI saves them together through one Metadata choice; Vision remains separately configurable.
- Settings opens a Screen Context Privacy window that merges saved rules with visible running applications, explains each access level, and uses direct one-click permission checkboxes.
- A Win32 foreground-window collector can now return permitted metadata while keeping handles, process IDs, paths, and exact bounds inside the observation layer.
- Opening typed chat prepares the permitted foreground application; submission reduces it to application name, bounded title activity, visibility, and approximate active duration.
- Reduced context is sent as `desktop_context`; the corresponding bot message persists that exact text and exposes a labeled display from a circular info button in Chat History.
- A bounded UI Automation collector is available for permitted active windows, with depth, node, text, timeout, off-screen, and password-field limits.
- Typed context now reduces useful focused-control and visible-label data when structural access succeeds; unsupported, empty, and timed-out inspection falls back to metadata.
- A permission-rechecking window-capture service can produce a downscaled in-memory bitmap for a visible, non-minimized foreground window; images are never written to disk.
- Visual analysis is behind `IVisualContextAnalyzer`; the current unavailable implementation prevents capture until a provider is deliberately selected.
- A cancellable background coordinator polls permitted metadata every two seconds off the UI thread and retains only the latest 50 reduced observations for at most 30 minutes.
- Recent activity and comment decisions are consolidated in the Memories window instead of separate Screen Context windows.
- The observation coordinator emits reduced meaningful changes only for stable application/title transitions and periodic check-ins on sustained activity. Error keywords, completion keywords, scrolling, typing, and idle return are not ambient triggers.
- Structural inspection is attempted only for meaningful changes and at most once every ten seconds per application.
- Ambient policy is local-first and rejects paused, disabled, permission-removed, busy, recently typed, cooldown, and duplicate (metadata-only) candidates before generation. Turn cancellation suppresses speech if a newer change arrives during processing. Vision-based observations skip the duplicate check since interest scoring handles novelty.
- Quiet, Balanced, and Talkative profiles map to cooldown (10/5/2 min), duplicate window (20/15/10 min), and check-in interval (10/5/3 min) values directly.
- Eligible changes can now request one short ElevenLabs comment from reduced context, then reuse local TTS, transcript, mouth animation, and playback.
- Ambient work has separate turn cancellation and is cancelled when a user request starts.
- Recent ambient decisions persist as reduced descriptions plus spoke/stayed-quiet reason codes, capped at 100 records, shown alongside visual records without duplicate cards, and clearable from the Observations tab.
- Transcript display uses an ownership version so cancelled or failed typed, replayed, and ambient playback always cleans up its own transcript without hiding a newer one.
- Transcript overlay operations (`ShowTranscript`/`HideTranscript`) are marshaled to the UI dispatcher to prevent cross-thread WPF access errors.
- Character speaking cleanup is also marshaled to the pet window dispatcher because audio playback completes on a worker thread.
- Screen Context settings now controls ambient enablement, cooldown, and duplicate window independently from application permissions.
- Durable memory remains manually managed through the existing Memories tab; observation history does not create memory proposals.
- Local policy decides whether an ambient observation deserves speech. Silence is the normal result.
- Treat Mem0 as an experimental local memory service behind one small REST client boundary.
- Keep chat history, cached replay audio, and durable memories as separate concepts.
- Saved chat history is the cross-request conversation source of truth because the current text-only Agent integration opens one short-lived WebSocket per generated reply.
- Regular and ambient conversation-history budgets are independent user settings; unused capacity in one category does not increase the other category.
- Agent context snapshots reuse the same shared builder as the outgoing WebSocket initiation so historical inspection does not reconstruct an approximation after the request.
- Live context preview must remain local-only. Desktop metadata, UI Automation, screenshots, visual analysis, and provider traffic occur only through normal chat or ambient flows.
- Ship a small localhost-only Mem0 Docker Compose stack with authentication, persistent storage, and no committed secrets.
- Ask before enabling memory, never silently install Docker, and show one clear setup or repair message if startup fails.
- Do not make the Mem0 dashboard part of the normal user flow.

## Privacy Rules

### Memory

- Do not store raw screenshots.
- Do not store full UI Automation trees.
- Do not store credentials.
- Do not add desktop observations to durable memories automatically.
- Show users what memories exist.
- Allow deleting one memory and clearing all memories.

### Desktop Observation

- Observe only explicitly permitted applications and default to no access.
- Keep metadata or structural inspection and visual capture as separate permissions.
- Keep exact coordinates and other raw behavior data local unless the model needs them.
- Reduce raw observations to compact context before sending them to an LLM.
- Store only the bounded, reduced desktop context on its corresponding bot chat-history message; do not write raw desktop observations to chat history, memories, audio metadata, debug logs, or user-visible exception messages.
- Show the user what desktop context was used for a turn.
- Persist model-facing dynamic-variable snapshots on successful bot history entries for user inspection; never include API keys, Agent IDs, signed URLs, audio, or raw screenshots.
- Persist global controls and explicit application allow or deny rules in the separate observation settings store.

### OpenRouter Vision

- Enable zero-data-retention routing by default for all vision requests.
- Send only base64-encoded screenshots to OpenRouter; never upload to other services.
- Enforce a minimum 30-second interval between OpenRouter requests.
- The vision model receives recent observation context but never session identifiers or user credentials.
- Test vision uses a locally generated image, not the user's actual desktop.
- Observation records store the summary and scores but not the raw screenshot (v1).
- Observation history context sent to the vision analyzer contains only prior summaries and timestamps, not raw data.

## Observation Follow-up

- Manually smoke-test permissions, scaling, multiple monitors, UI Automation timeouts, shutdown, and user-chat interruption.
- Select a vision provider before enabling visual capture or analysis.
- Tune ambient cooldowns, check-in intervals, and window-change dwell behavior from real use while keeping silence as the normal outcome.
- Keep auditing logs and local JSON files whenever observation models change.

## Near-Term Work

- Add a more polished transcript timing path if full-text-at-once feels too abrupt.
- Keep the TTS request path small and fixed to `eleven_v3`.
- Add local Mem0 Compose files with pinned published images once the exact image and routes are tested.
- Add the one-time memory enable prompt.
- Start the local Mem0 stack from the app after memory is enabled.
- Automatically add completed user and Agent chat exchanges.
- Retrieve only a few relevant memories before sending a new typed message.
- Connect the memory screen to automatic chat memories and later Mem0 storage.
- Persist observation screenshots to disk for the Observations tab "View screenshot" feature.
- Tune interest scoring thresholds and soft speaking budgets from real use.
- Add a vision analysis error counter in the settings UI for debugging.

## Later Work

- Optional microphone input after typed chat works, not a live voice session.
- Better credential storage than plain JSON.
- Blinking.
- More natural idle animation.
- Cleaner renderer/performance boundary.
- Native Inochi2D runtime experiment.
- Real rig parameters, expressions, and mesh deformation.
- Subtitles or a speech bubble.
- Output-device selection.
- Hosted Mem0, if local Mem0 works and there is a clear reason to add it.
- Secondary vision model for uncertain or complex observations.
- User-initiated "what do you see?" mode with full screenshot analysis.
