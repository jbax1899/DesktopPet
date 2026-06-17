# Desktop Pet Status

This is the working status and architecture note for the current prototype. Keep
it short and update it when implementation decisions change.

The current prototype combines typed chat, local playback, lightweight
character behavior, local history and memory, and permission-controlled desktop
context. Ambient comments are available but default off and are governed by
local cooldown, activity, freshness, and duplicate checks.

## ElevenLabs Agent Prompt Setup

The Agent prompt should reference these DesktopPet dynamic variables:

- `{{user_name}}`
- `{{pet_name}}`
- `{{desktop_context}}`

Example prompt fragment:

`You are a desktop pet named {{pet_name}}. The user's name is {{user_name}}.`

Configure `desktop_context` with a harmless fallback such as
`No permitted desktop context was attached.`

## Current State

- Transparent always-on-top WPF overlay with draggable, clamped, saved character placement.
- Notification-area icon exposes Show, Hide, Click through, Settings, and Exit.
- Right-click action pad exposes Chat, Memories, Settings, and a disabled microphone placeholder.
- Conversation overlay handles typed input, submitted-message state, transcript display, content-sized chat bubbles, and basic dismissal behavior.
- Global chat shortcut is configurable and defaults to Ctrl+Space.
- Click-through uses Win32 extended window styles.
- Runtime loads `Assets/bug.inp` into a WPF-layered visual prototype.
- Character behavior includes basic gaze, amplitude-driven mouth movement during playback, and subtle idle breathing/bob animation.
- Settings window stores ElevenLabs API key, Agent ID, voice ID, and pet profile fields in local JSON under the user's local app data folder.
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
- Chat history stores user attempts and Agent replies in local JSON, with bot audio cached as local MP3 files when playback completes.
- Memory management UI exists with a local JSON-backed list, manual add, refresh, delete one, and clear all.
- All manually stored memories are currently joined into one `memories_context` dynamic variable for each chat turn; relevance filtering is not implemented.
- `IChatService`, `IVoiceSynthesisService`, and `StreamingMp3AudioPlayer` are good enough for smoke testing.
- The old separate `ChatWindow` has been removed; chat now stays in the top-level conversation overlay.
- The global chat shortcut uses Win32 hotkey registration and may be unavailable if another app owns the same shortcut.
- Automatic memory capture, retrieval, Mem0 startup, and Mem0 storage are not implemented yet.
- `ChatRequest` now has a separate optional desktop-context field and `ConversationController` uses a provider-neutral context boundary.
- Permitted typed chat can include reduced foreground metadata and bounded UI Automation summaries with exact user-visible disclosure.
- Background observation keeps short-lived reduced state, detects meaningful changes, and can produce sparse ambient comments when enabled.
- Window capture is implemented in memory, but the unavailable visual analyzer prevents capture until a provider is selected.
- Desktop Pet Memories has an Observations tab for recent reduced activity and persisted ambient-comment decisions.
- ElevenLabs diagnostics report safe event and variable names without logging provider values, Agent IDs, replies, or full exceptions.

## Decisions

- Do not interrupt current speech until a newer submitted message has both reply text and TTS audio ready.
- Replies use ElevenLabs Agent Chat Mode; speech uses standalone ElevenLabs TTS with hard-coded `eleven_v3`.
- Playback, interruption, mouth movement, and character behavior stay local. TTS streams as MP3 into local playback and cache.
- Keep the existing chat and voice interfaces unless they get in the way; `IVoiceSynthesisService` should stay small.
- Keep desktop observation separate from durable memories and add it to chat as distinct optional per-turn context.
- Only `DesktopContextFormatter` converts reduced context into provider text; it enforces field and total-length limits.
- Desktop context must not be added to chat-history, memory, audio-cache, settings, error, or diagnostic models.
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
- Reduced context is sent as `desktop_context` and the conversation overlay exposes the exact same text through a temporary clickable disclosure.
- A bounded UI Automation collector is available for permitted active windows, with depth, node, text, timeout, off-screen, and password-field limits.
- Typed context now reduces useful focused-control and visible-label data when structural access succeeds; unsupported, empty, and timed-out inspection falls back to metadata.
- A permission-rechecking window-capture service can produce a downscaled in-memory bitmap for a visible, non-minimized foreground window; images are never written to disk.
- Visual analysis is behind `IVisualContextAnalyzer`; the current unavailable implementation prevents capture until a provider is deliberately selected.
- A cancellable background coordinator polls permitted metadata every two seconds off the UI thread and retains only the latest 50 reduced observations for at most 30 minutes.
- Recent activity and comment decisions are consolidated in the Memories window instead of separate Screen Context windows.
- The observation coordinator emits reduced meaningful changes for application/title transitions, attention states, completion states, idle return, and long-running activity.
- Structural inspection is attempted only for meaningful changes and at most once every ten seconds per application.
- Ambient policy is local-first and rejects paused, disabled, stale, changed, busy, recently typed, full-screen, do-not-disturb, cooldown, hourly-limit, and duplicate candidates before generation.
- Quiet, Balanced, and Talkative profiles centralize initial cooldown and hourly limits.
- Eligible changes can now request one short ElevenLabs comment from reduced context, then reuse local TTS, transcript, mouth animation, and playback.
- Ambient work has separate turn cancellation, is checked again before speech, and is cancelled when a user request starts.
- Recent ambient decisions persist as reduced descriptions plus spoke/stayed-quiet reason codes, capped at 100 records and clearable from the Observations tab.
- Screen Context settings now controls ambient enablement, do-not-disturb, and Quiet/Balanced/Talkative behavior independently from application permissions.
- Durable memory remains manually managed through the existing Memories tab; observation history does not create memory proposals.
- Local policy decides whether an ambient observation deserves speech. Silence is the normal result.
- Treat Mem0 as an experimental local memory service behind one small REST client boundary.
- Keep chat history, cached replay audio, and durable memories as separate concepts.
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
- Do not write desktop context to chat history, memories, audio metadata, debug logs, or user-visible exception messages.
- Show the user what desktop context was used for a turn.
- Persist global controls and explicit application allow or deny rules in the separate observation settings store.

## Observation Follow-up

- Manually smoke-test permissions, scaling, multiple monitors, UI Automation timeouts, shutdown, and user-chat interruption.
- Select a vision provider before enabling visual capture or analysis.
- Tune ambient cooldowns and change heuristics from real use while keeping silence as the normal outcome.
- Keep auditing logs and local JSON files whenever observation models change.

## Near-Term Work

- Keep a simple stop/interruption path so speech can be cancelled.
- Add a more polished transcript timing path if full-text-at-once feels too abrupt.
- Keep the TTS request path small and fixed to `eleven_v3`.
- Add local Mem0 Compose files with pinned published images once the exact image and routes are tested.
- Add the one-time memory enable prompt.
- Start the local Mem0 stack from the app after memory is enabled.
- Automatically add completed user and Agent chat exchanges.
- Retrieve only a few relevant memories before sending a new typed message.
- Connect the memory screen to automatic chat memories and later Mem0 storage.
- Add a small pronunciation dictionary screen with list, add, edit, preview, and delete entries.
- Let each pronunciation entry choose either alias text or a supported phoneme pronunciation.

## Later Work

- A selected vision provider for permitted-window analysis.
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
