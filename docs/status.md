# Work Status

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
