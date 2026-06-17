# Desktop Pet Status

This is the working status and architecture note for the current prototype. Keep
it short and update it when implementation decisions change.

## Project Snapshot

Desktop Pet is a native Windows desktop pet built with .NET, C#, and WPF.

The current prototype focuses on a typed-chat loop with local playback,
lightweight character behavior, and a future local memory path. The WPF overlay
and service interfaces are usable enough for smoke testing, but the character
renderer, audio pipeline, credential storage, and memory integration are still
prototype-grade.

## Implemented

- Transparent always-on-top overlay window.
- Runtime loading of `Assets/bug.inp` into a WPF-layered visual prototype.
- Draggable character placement, clamped to the working area.
- Notification-area icon with Show, Hide, Click through, Settings, and Exit commands.
- Pet right-click action pad with Chat, Speak, and Settings square icon buttons.
- Action pad closes on away timeout, large cursor movement, outside left-click, or any right-click.
- Click-through toggle using Win32 extended window styles.
- Basic gaze behavior:
  - follows the mouse while idle;
  - looks toward an assumed viewer position while speaking.
- Basic mouth animation while speech playback is active.
- Subtle idle breathing/bob animation on the WPF-layered puppet root.
- Settings window for ElevenLabs API key, Agent ID, and voice ID.
- Local JSON settings storage under the user's local app data folder.
- Minimal bottom-center conversation overlay for typed chat.
- Configurable global chat shortcut, defaulting to Ctrl+Space.
- Chat action pad button and chat shortcut toggle the overlay text entry box.
- Escape closes the overlay text entry box.
- Conversation overlay displays the latest spoken transcript above the text entry location on a subtle gradient shadow.
- Submitted text stays grayed and italic in the entry area until typing resumes or the response arrives.
- Text entry box hides when a response is ready to be spoken.
- Speak action pad button is a visible microphone-input stub only.
- Smoke-test ElevenLabs Agent text interaction in text-only mode.
- Smoke-test ElevenLabs text-to-speech and local MP3 playback.
- Provider calls are behind small interfaces instead of being called directly from XAML.

## Prototype Constraints

- The current `bug.inp` renderer is not a full native Inochi2D runtime.
- The source puppet asset lives at `src/DesktopPet.App/Assets/bug.inp` and is copied to the build output as `Assets/bug.inp`.
- The loader reads the INP container, embedded TGA atlas, and node tree, then draws cropped WPF image layers.
- Mesh deformation, real rig parameters, real expressions, and native renderer integration are not implemented.
- Mouth movement currently alternates two visible parts. It is not audio-amplitude, phoneme, or viseme driven.
- Breathing is currently a root-level WPF transform, not real rig/chest deformation.
- Audio playback uses temporary MP3 files and WPF `MediaPlayer`.
- Plain JSON credential storage is temporary and should not be treated as secure.
- `IPetChatService`, `IVoiceSynthesisService`, and `TempFileAudioPlayer` are good enough for smoke testing.
- The old separate `ChatWindow` is deprecated and no longer used by the normal runtime path.
- Transcript display currently reveals text at a fixed character rate once TTS is ready and holds for three seconds after audio stops; it is not word-, phoneme-, or audio-synced.
- The global chat shortcut uses Win32 hotkey registration and may be unavailable if another app owns the same shortcut.
- Long-term memory, Mem0 startup, memory storage, and the memory UI are not implemented yet.

## Current Decisions

- Keep the next app loop typed-chat first. Screen-aware commenting is later.
- Keep chat entry in the top-level conversation overlay instead of a separate chat window.
- Keep the text input usable while a previous reply is displaying or speaking.
- Do not interrupt current speech until a newer submitted message has both reply text and TTS audio ready.
- Use ElevenLabs Agents in text-only Chat Mode for replies.
- Use standalone ElevenLabs Text to Speech for spoken replies.
- Use `eleven_v3` as the only TTS model for this prototype.
- Keep playback, interruption, mouth movement, and character behavior local.
- Stream TTS audio into local playback when possible.
- Streamed TTS does not mean a real-time voice conversation. There is no live microphone loop, turn detection, or continuously connected voice session in this prototype.
- Keep the existing chat and voice interfaces unless they get in the way.
- `IVoiceSynthesisService` can stay small. The ElevenLabs implementation can use `eleven_v3` internally.
- Do not add a TTS model selector, model fallback logic, model enums, capability checks, or a general provider framework.
- The completed audio stream may optionally be saved or cached after playback.
- Treat Mem0 as an experimental local memory service called from C# over REST.
- Use one small memory client boundary with add, search, list, delete, and clear behavior. Do not build a generic repository framework.
- Store completed text exchanges in memory, not generated audio.
- Do not let the TTS model affect memory behavior.
- Include a small Docker Compose stack for local Mem0 in the repository.
- On first memory use, ask the user to enable memory and explain that Docker is required.
- After that setup, the app may run `docker compose up -d`, reuse persistent data, and start Mem0 automatically when memory is needed.
- Do not silently install Docker.
- If Mem0 startup fails, show one clear setup or repair message.
- Keep Mem0 bound to localhost, keep authentication enabled, store data in persistent storage, and keep secrets out of Git.
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

1. User sends a typed message.
2. ElevenLabs Agent Chat Mode returns text.
3. Local code decides whether the response should be spoken.
4. Accepted text goes through standalone ElevenLabs TTS using `eleven_v3`.
5. Once TTS is ready, the entry box hides, any older speech is interrupted, and the new transcript starts revealing above the text entry location.
6. Audio is played locally.
7. The character looks toward the user, moves its mouth during playback, and returns to idle.
8. The completed text exchange is sent to Mem0.
9. A later message retrieves a few relevant memories before the Agent turn.
10. The user can view, delete, or clear those memories in the app.

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
- Saved overlay position.
- Blinking.
- More natural idle animation.
- Cleaner renderer/performance boundary.
- Native Inochi2D runtime experiment.
- Real rig parameters, expressions, and mesh deformation.
- Subtitles or a speech bubble.
- Output-device selection.
- Hosted Mem0, if local Mem0 works and there is a clear reason to add it.

## Explicitly Deferred

- Alternate TTS models.
- TTS model selector.
- TTS model fallback logic.
- Request stitching or stitching IDs.
- Agent audio session work.
- Vector-database tuning.
- Graph memory.
- Local model hosting.
- Migration tooling.
- Background summarization jobs.
- Windows service or custom Mem0 supervisor.
- Mem0 updater, log viewer, dashboard wrapper, or repair framework.
- General plugin system.
- Advanced pronunciation dictionary import/export.
- Advanced PLS editing.

## Open Questions

- Is the WPF-layered puppet good enough for the next prototype, or should the renderer boundary be cleaned up first?
- What is the simplest streaming playback option that works well in WPF?
- Should completed audio be cached, or is playback-only enough?
- What output format and voice settings should the `eleven_v3` request use?
- What shape should the pronunciation dictionary use in settings?
- Which published Mem0 container image should the local Compose stack use?
- What exact Mem0 REST routes should the C# client call?
- What provider credentials and model settings does local Mem0 need for this prototype?
- Where should generated Mem0 secrets and local configuration live on Windows?
- How should the app create a stable local user ID and character ID for memory?
- Should clear-all call a Mem0 delete-all route, or list and delete memories one by one?
