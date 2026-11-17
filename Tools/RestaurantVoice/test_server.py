import io
import json
import unittest
import wave
from unittest.mock import patch
import server


class Contracts(unittest.TestCase):
    def test_transcription_upload(self):
        audio = io.BytesIO()
        with wave.open(audio, "wb") as wav:
            wav.setnchannels(1); wav.setsampwidth(2); wav.setframerate(16000)
            wav.writeframes(b"\0\0" * 1600)
        with patch.object(server, "provider", return_value=b'{"text":"Bonjour merci"}') as api:
            kind, body = server.dispatch("/transcribe", audio.getvalue())
            self.assertEqual(json.loads(body)["text"], "Bonjour merci")
            self.assertIn(audio.getvalue(), api.call_args.args[1])
            self.assertIn("multipart/form-data", api.call_args.args[2])

    def test_speech_contract(self):
        with patch.object(server, "provider", return_value=b"RIFF audio") as api:
            kind, _ = server.dispatch("/speak", b'{"text":"Bonjour"}')
            self.assertEqual(kind, "audio/wav")
            self.assertEqual(json.loads(api.call_args.args[1])["response_format"], "wav")

    def test_conversation_keeps_server_role(self):
        request = {"dish": "salade", "drink": "eau", "history": [{"role": "user", "content": "Merci"}]}
        result = {"output": [{"content": [{"type": "output_text", "text": "Avec plaisir !"}]}]}
        with patch.object(server, "json_call", return_value=result) as api:
            _, body = server.dispatch("/chat", json.dumps(request).encode())
            self.assertEqual(json.loads(body)["reply"], "Avec plaisir !")
            self.assertFalse(api.call_args.args[1]["store"])
            self.assertIn("A1/A2", api.call_args.args[1]["instructions"])
        request["history"][0]["role"] = "system"
        with self.assertRaises(ValueError):
            server.dispatch("/chat", json.dumps(request).encode())

    def test_bad_audio_and_unbounded_input_rejected(self):
        with self.assertRaises((wave.Error, EOFError)):
            server.dispatch("/transcribe", b"not audio")
        with self.assertRaises(ValueError):
            server.dispatch("/speak", json.dumps({"text": "x" * 1501}).encode())
        with self.assertRaises(ValueError):
            server.dispatch("/chat", b'{"history":[]}')


if __name__ == "__main__":
    unittest.main()
