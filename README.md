# Desktop Pet

A native Windows desktop companion prototype. It provides a draggable pet,
typed chat with spoken replies, local history and memories, and optional
permission-based awareness of the active application.

## Features

- Draggable, always-on-top pet overlay with tray controls
- Typed chat from the overlay or a configurable global shortcut
- ElevenLabs Agent replies with streamed speech, replay, and custom pronunciations
- Local chat history, conversation continuity, and manually managed memories
- Optional screen context using per-application permissions
- Optional OpenRouter vision analysis and ambient comments

Screen observation and ambient comments are disabled by default.

## Stack

- .NET 10 / C#
- WPF for the desktop UI
- ElevenLabs for Agent chat and text-to-speech
- OpenRouter for optional screenshot analysis

## Requirements

- Windows
- .NET 10 SDK
- An ElevenLabs API key, Agent ID, and voice ID
- An OpenRouter API key only if vision analysis is enabled

## Setup

1. Run the app.
2. Open **Settings** from the tray icon or the pet's right-click menu.
3. Enter the ElevenLabs API key, Agent ID, and voice ID.
4. Optionally enter an OpenRouter API key and select a vision model.
5. Open **Screen Context Privacy** to enable observation and grant access to
   individual applications.

The ElevenLabs Agent prompt can use these dynamic variables:

- `user_name`
- `pet_name`
- `temporal_context`
- `memories_context`
- `conversation_history`
- `desktop_context`
- `desktop_observation_history`

Each variable should have a harmless fallback in the Agent configuration.
The Agent prompt should treat `temporal_context` as the authoritative current
date, time, and timezone when interpreting relative dates in conversation
history.

## Run

```powershell
dotnet run --project src/DesktopPet.App
```

## Local Data

Settings, history, memories, observations, and cached reply audio are stored
under `%LOCALAPPDATA%\DesktopPet`.
