# Local restaurant voice prototype

Python 3.10+; no packages required. The Unity scene works offline with HELP response cards. This optional server provides recorded-speech transcription, French synthesized dialogue and free conversation after assessment. Audio/transcripts are sent to OpenAI when enabled; the server does not write recordings or transcripts to disk. It is a local development bridge, not a public authenticated service.

1. Set `OPENAI_API_KEY` in the terminal environment using your own secret manager. Never put it in a Unity asset, source file, APK or chat.
2. From the project root run `python Tools/RestaurantVoice/server.py`.
3. Open Paris, select **Restaurant learning systems**, set **Restaurant Voice Bridge / Service Url** to `http://127.0.0.1:8787` and save the scene.
4. For Editor Play mode, permit microphone access in Windows. Press START SPEAKING, say the French answer, then FINISH SPEAKING. Recordings stop automatically before twelve seconds. Let the waiter finish speaking before recording.
5. For a USB-connected Quest development build: run `adb reverse tcp:8787 tcp:8787`. In Android Player Settings set **Allow downloads over HTTP** to **Development Only**, enable Development Build, and accept the headset microphone permission. Repeat `adb reverse` after reconnecting/rebooting. Do not use the PC's LAN address: this server intentionally listens only on loopback. These settings were not changed automatically.
6. Select KEEP PRACTICING after completing the assessment. Requests carry the recent conversation and selected order. REPLAY CONVERSATION clears the conversation history.

No API call was made during implementation because a provider key was not configured. Provider access, latency, microphone permission, French recognition and headset networking still need an end-to-end test. Charges depend on the provider account. To return to the offline demo, clear Service Url.

## Contracts

| Route | Request | Response |
| --- | --- | --- |
| POST `/transcribe` | mono PCM16 WAV, 16 kHz, up to 12 seconds | `{"text":"Bonjour merci"}` |
| POST `/speak` | `{"text":"Bonjour","language":"fr-FR"}` | WAV audio |
| POST `/chat` | `dish`, `drink`, `history` with user/assistant turns | `{"reply":"Avec plaisir !"}` |
| GET `/health` | none | server status; does not validate provider credentials |

Defaults: `gpt-transcribe`, `gpt-4o-mini-tts`, `gpt-4.1-mini`. Override via `RESTAURANT_STT_MODEL`, `RESTAURANT_TTS_MODEL`, `RESTAURANT_CHAT_MODEL` only with a model supporting the same endpoint contract.

**TTS migration needed:** OpenAI currently documents retirement of the Speech API models on January 6, 2027. This bounded-request adapter is temporary for the demo. Replace the server's `/speak` implementation with a supported Realtime audio workflow before then; changing only its model name is insufficient. Unity's WAV contract can remain unchanged. See [speech generation](https://developers.openai.com/api/docs/guides/text-to-speech), [current transcription](https://developers.openai.com/api/docs/guides/speech-to-text) and [Responses API](https://developers.openai.com/api/docs/guides/text).

Before public distribution, replace the local bridge with an authenticated HTTPS backend, account quotas and appropriate data retention controls. Do not expose this stdlib development server to the Internet.

Run contract tests: `python -m unittest discover -s Tools/RestaurantVoice -v`. These mock the provider and do not spend credits or validate model quality.
