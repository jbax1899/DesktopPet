# Desktop Pet Status

This is the working status and architecture note for the current prototype. Keep
it short and update it when implementation decisions change.

The current prototype focuses on a typed-chat loop with local playback,
lightweight character behavior, and a future local memory path. The WPF overlay
and service interfaces are usable enough for smoke testing, but the character
renderer, audio pipeline, credential storage, and memory integration are still
prototype-grade.

## Current State

- Transparent always-on-top WPF overlay with draggable, clamped, saved character placement.
- Notification-area icon exposes Show, Hide, Click through, Settings, and Exit.
- Right-click action pad exposes Chat, Speak, and Settings. Speak is still a microphone-input stub.
- Conversation overlay handles typed input, submitted-message state, transcript display, and basic dismissal behavior.
- Global chat shortcut is configurable and defaults to Ctrl+Space.
- Click-through uses Win32 extended window styles.
- Runtime loads `Assets/bug.inp` into a WPF-layered visual prototype.
- Character behavior includes basic gaze, mouth movement during playback, and subtle idle breathing/bob animation.
- Settings window stores ElevenLabs API key, Agent ID, and voice ID in local JSON under the user's local app data folder.
- ElevenLabs Agent text interaction and ElevenLabs TTS/local MP3 playback have both been smoke-tested.
- Provider calls are behind small interfaces instead of being called directly from XAML.
- The current `bug.inp` renderer is not a full native Inochi2D runtime.
- The source puppet asset lives at `src/DesktopPet.App/Assets/bug.inp` and is copied to the build output as `Assets/bug.inp`.
- The loader reads the INP container, embedded TGA atlas, and node tree, then draws cropped WPF image layers.
- Mesh deformation, real rig parameters, real expressions, and native renderer integration are not implemented.
- Mouth movement and breathing are simple WPF-layer animations, not rig-, amplitude-, phoneme-, or viseme-driven behavior.
- Audio playback uses temporary MP3 files and WPF `MediaPlayer`.
- Plain JSON credential storage is temporary and should not be treated as secure.
- `IPetChatService`, `IVoiceSynthesisService`, and `TempFileAudioPlayer` are good enough for smoke testing.
- The old separate `ChatWindow` is deprecated and no longer used by the normal runtime path.
- The global chat shortcut uses Win32 hotkey registration and may be unavailable if another app owns the same shortcut.
- Long-term memory, Mem0 startup, memory storage, and the memory UI are not implemented yet.

## Current Decisions

- Keep the next loop typed-chat first. Screen-aware commenting and microphone input are later.
- Keep chat in the top-level conversation overlay, with input usable while previous replies display or speak.
- Do not interrupt current speech until a newer submitted message has both reply text and TTS audio ready.
- Replies use ElevenLabs Agent Chat Mode; speech uses standalone ElevenLabs TTS with hard-coded `eleven_v3`.
- Playback, interruption, mouth movement, and character behavior stay local. Stream TTS into local playback when practical.
- Keep the existing chat and voice interfaces unless they get in the way; `IVoiceSynthesisService` should stay small.
- Treat Mem0 as an experimental local memory service behind one small REST client boundary.
- Store completed text exchanges in memory, not generated audio, and keep memory behavior independent from TTS behavior.
- Ship a small localhost-only Mem0 Docker Compose stack with authentication, persistent storage, and no committed secrets.
- Ask before enabling memory, never silently install Docker, and show one clear setup or repair message if startup fails.
- Do not make the Mem0 dashboard part of the normal user flow.

## Memory Privacy Rules

- Do not store raw screenshots.
- Do not store full UI Automation trees.
- Do not store credentials.
- Show users what memories exist.
- Allow deleting one memory and clearing all memories.

## Reference Checks

Context7 docs checked:

- ElevenLabs Chat Mode supports text-only Agent responses, and ElevenLabs TTS can stream audio from text.
- ElevenLabs TTS accepts a `model_id`; this prototype should hard-code `eleven_v3` in the ElevenLabs implementation.
- Mem0 exposes memory operations such as add, search, list/get, update, and delete through SDKs and a REST API server.

## Next Prototype Target

Build the smaller conversation-and-memory loop:

1. Send typed messages with a few relevant retrieved memories.
2. Get ElevenLabs Agent text replies.
3. Generate accepted speech with standalone ElevenLabs TTS using `eleven_v3`.
4. Play speech locally while showing the transcript and simple character behavior.
5. Store completed exchanges in Mem0 and expose memory management UI.

## Near-Term Work

- Replace temp-file-only playback with streaming playback.
- Keep a simple stop/interruption path so speech can be cancelled.
- Add a more polished transcript timing path if full-text-at-once feels too abrupt.
- Drive mouth movement from audio timing or amplitude if practical; otherwise keep the current simple mouth frames until playback is stable.
- Keep the TTS request path small and fixed to `eleven_v3`.
- Add local Mem0 Compose files with pinned published images once the exact image and routes are tested.
- Add the one-time memory enable prompt.
- Start the local Mem0 stack from the app after memory is enabled.
- Automatically add completed user and Agent chat exchanges.
- Retrieve only a few relevant memories before sending a new typed message.
- Add a memory screen with list, refresh, delete one, and clear all.
- Add a small pronunciation dictionary screen with list, add, edit, preview, and delete entries.
- Let each pronunciation entry choose either alias text or a supported phoneme pronunciation.

The pronunciation dictionary assumes `eleven_v3`. Advanced PLS editing, model
compatibility UI, and import/export can wait.

## Later Work

- Window permissions.
- Foreground-window tracking.
- UI Automation summaries.
- Screenshots of permitted windows.
- Screen-aware comments from the pet.
- Scoring whether a desktop observation deserves a comment.
- Detailed records of why the pet spoke or stayed quiet.
- Desktop information in memory, but only after it has been reduced and approved.
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

## Out of Scope For Now

- Alternate TTS models, model selection, fallback logic, or provider framework work.
- Live Agent audio sessions, microphone-first interaction, request stitching, or stitching IDs.
- Advanced memory infrastructure such as vector tuning, graph memory, local model hosting, migrations, background summarization, dashboards, repair tooling, or custom supervisors.
- General plugin system work.
- Advanced pronunciation dictionary import/export or PLS editing.

## Open Questions

- Is the current WPF-layered puppet good enough for the memory/chat prototype, or should the renderer boundary be cleaned up first?
- What is the simplest streaming playback option that works well in WPF?
- Which Mem0 image, REST routes, and local configuration shape should the Compose stack use?
- What shape should the pronunciation dictionary use in settings?
