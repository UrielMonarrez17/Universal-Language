"""Local prototype bridge. Python 3.10+, stdlib only; never put the API key in Unity."""
import io
import json
import os
import threading
import uuid
import wave
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.request import Request, urlopen
from urllib.error import HTTPError, URLError

MAX_BODY = 512_000
INSTRUCTIONS = (
    "You are the friendly waiter in a fictional Paris cafe, helping an adult A1/A2 French learner. "
    "Speak French, at most two short sentences, then one simple question. Stay in the restaurant role. "
    "Gently reformulate errors without scores or pronunciation claims. Never change evaluation progress. "
    "Menu: croque-monsieur (ham and cheese) 12 EUR, tomato and cheese salad 10 EUR, "
    "still or sparkling water 3 EUR, coffee 3 EUR. Do not invent allergy safety. "
    "If asked to repeat, simplify your last sentence. The assessment has already ended."
)


def provider(path, data, content_type="application/json"):
    key = os.environ.get("OPENAI_API_KEY")
    if not key:
        raise RuntimeError("Voice provider is not configured")
    request = Request("https://api.openai.com/v1/" + path, data=data,
                      headers={"Authorization": "Bearer " + key, "Content-Type": content_type})
    with urlopen(request, timeout=18) as response:
        return response.read()


def json_call(path, payload):
    return json.loads(provider(path, json.dumps(payload).encode()))


def dispatch(path, body):
    if path == "/transcribe":
        with wave.open(io.BytesIO(body), "rb") as wav:
            if wav.getsampwidth() != 2 or wav.getnchannels() != 1 or wav.getframerate() != 16000:
                raise ValueError("Expected mono 16 kHz PCM16 WAV")
            if not 0 < wav.getnframes() <= 192000:
                raise ValueError("Recording must be at most twelve seconds")
        boundary = uuid.uuid4().hex
        parts = []
        for name, value in {"model": os.getenv("RESTAURANT_STT_MODEL", "gpt-transcribe")}.items():
            parts.append(f'--{boundary}\r\nContent-Disposition: form-data; name="{name}"\r\n\r\n{value}\r\n'.encode())
        parts.append(f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="speech.wav"\r\nContent-Type: audio/wav\r\n\r\n'.encode() + body + b"\r\n")
        parts.append(f"--{boundary}--\r\n".encode())
        result = json.loads(provider("audio/transcriptions", b"".join(parts), "multipart/form-data; boundary=" + boundary))
        return "application/json", json.dumps({"text": result.get("text", "")}).encode()
    data = json.loads(body)
    if not isinstance(data, dict):
        raise ValueError("Expected a JSON object")
    if path == "/speak":
        text = data.get("text")
        if not isinstance(text, str) or not 1 <= len(text.strip()) <= 1500:
            raise ValueError("Expected 1–1500 text characters")
        # Temporary bounded-request TTS adapter; see README migration deadline.
        payload = {"model": os.getenv("RESTAURANT_TTS_MODEL", "gpt-4o-mini-tts"),
                   "voice": "coral", "input": text, "response_format": "wav",
                   "instructions": "Speak slowly and warmly in French for a beginner language learner."}
        return "audio/wav", provider("audio/speech", json.dumps(payload).encode())
    if path == "/chat":
        history = data.get("history")
        if not isinstance(history, list) or not 1 <= len(history) <= 12:
            raise ValueError("Expected a bounded conversation")
        turns = []
        for turn in history:
            if not isinstance(turn, dict) or turn.get("role") not in ("user", "assistant"):
                raise ValueError("Invalid conversation role")
            content = turn.get("content")
            if not isinstance(content, str) or not 1 <= len(content) <= 1500:
                raise ValueError("Invalid conversation turn")
            turns.append({"role": turn["role"], "content": content})
        order = {key: data.get(key) for key in ("dish", "drink")}
        if order["dish"] not in ("salade", "croque-monsieur") or order["drink"] not in ("eau", "café", "cafe"):
            raise ValueError("Select a valid order first")
        result = json_call("responses", {"model": os.getenv("RESTAURANT_CHAT_MODEL", "gpt-4.1-mini"),
            "instructions": INSTRUCTIONS + " Current order: " + json.dumps(order),
            "input": turns, "max_output_tokens": 180, "store": False})
        reply = " ".join(part.get("text", "") for item in result.get("output", [])
                         for part in item.get("content", []) if part.get("type") == "output_text")
        if not reply.strip():
            raise RuntimeError("No conversation reply")
        return "application/json", json.dumps({"reply": reply}).encode()
    raise ValueError("Unknown endpoint")


class Handler(BaseHTTPRequestHandler):
    gate = threading.BoundedSemaphore(2)

    def log_message(self, *_):
        pass  # Do not log learner audio, transcripts, or credentials.

    def respond(self, status, kind, body):
        self.send_response(status)
        self.send_header("Content-Type", kind)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        self.respond(200 if self.path == "/health" else 404, "application/json",
                     b'{"status":"local prototype"}' if self.path == "/health" else b'{}')

    def do_POST(self):
        self.connection.settimeout(20)
        if self.path not in ("/transcribe", "/speak", "/chat"):
            self.respond(404, "application/json", b'{}'); return
        # Native Unity client only; a web page must not spend local provider credits.
        if self.headers.get("Origin"):
            self.respond(403, "application/json", b'{}'); return
        if not self.gate.acquire(blocking=False):
            self.respond(429, "application/json", b'{"error":"Busy"}'); return
        try:
            size = int(self.headers.get("Content-Length", "0"))
            if not 0 < size <= MAX_BODY:
                raise ValueError("Invalid request size")
            kind, response = dispatch(self.path, self.rfile.read(size))
            self.respond(200, kind, response)
        except (ValueError, wave.Error, EOFError):
            self.respond(400, "application/json", b'{"error":"Invalid request"}')
        except (HTTPError, URLError, RuntimeError, TimeoutError):
            self.respond(503, "application/json", b'{"error":"Voice service unavailable"}')
        finally:
            self.gate.release()


if __name__ == "__main__":
    if not os.environ.get("OPENAI_API_KEY"):
        raise SystemExit("Set OPENAI_API_KEY in this terminal before starting. Never add it to Unity.")
    print("Restaurant voice: http://127.0.0.1:8787 (local prototype; Ctrl+C to stop)")
    ThreadingHTTPServer(("127.0.0.1", 8787), Handler).serve_forever()
