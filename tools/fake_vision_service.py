"""Stand-in for the Python vision service, for testing Unity's HttpVisionClient without a camera.

Implements the API contract from docs/unity-agent-handoff.md on http://localhost:8000 using only the
standard library. Captures return one of the mock PNGs from Assets/Resources/MockDrawings, chosen to
match the socket's first accepted label (so a normal run solves every socket), or the --label override.

    python tools/fake_vision_service.py                   # calibrated, labels after 1.5 s
    python tools/fake_vision_service.py --uncalibrated    # /capture fails until POST /calibrate
    python tools/fake_vision_service.py --label cat       # every drawing is labelled "cat"
    python tools/fake_vision_service.py --empty           # every capture reports an empty board
"""

import argparse
import base64
import itertools
import json
import struct
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

DRAWINGS = Path(__file__).resolve().parent.parent / "Assets" / "Resources" / "MockDrawings"
LABEL_TO_DRAWING = {"light": "sun", "fish": "fish", "cat": "cat", "bridge": "bridge", "unknown": "scribble"}

args = None
state = {"calibrated": True}
captures = {}
ids = itertools.count(1)
lock = threading.Lock()


def png_info(name):
    data = (DRAWINGS / f"{name}.png").read_bytes()
    width, height = struct.unpack(">II", data[16:24])
    return base64.b64encode(data).decode("ascii"), width, height


class Handler(BaseHTTPRequestHandler):
    def send_json(self, code, payload):
        body = json.dumps(payload).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def read_json(self):
        length = int(self.headers.get("Content-Length") or 0)
        return json.loads(self.rfile.read(length) or b"{}")

    def do_GET(self):
        if self.path == "/health":
            return self.send_json(200, {"camera": True, "calibrated": state["calibrated"],
                                        "provider": "fake", "provider_reachable": True})
        if self.path.startswith("/label/"):
            capture_id = self.path[len("/label/"):]
            with lock:
                entry = captures.get(capture_id)
            if entry is None:
                return self.send_json(404, {"error": "unknown id"})
            started = time.time()
            time.sleep(args.label_delay)
            return self.send_json(200, {"status": "done", "socket_id": entry["socket_id"], "run_id": entry["run_id"],
                                        "label": entry["label"], "confidence": 0.87, "provider": "fake",
                                        "latency_ms": round((time.time() - started) * 1000)})
        self.send_json(404, {"error": "not found"})

    def do_POST(self):
        if self.path == "/calibrate":
            state["calibrated"] = True
            return self.send_json(200, {"markers_found": 4, "saved": True})
        if self.path == "/capture":
            req = self.read_json()
            if not state["calibrated"]:
                return self.send_json(409, {"error": "not calibrated"})
            capture_id = f"cap-{next(ids)}"
            base = {"id": capture_id, "socket_id": req.get("socket_id"), "run_id": req.get("run_id", 0)}
            if args.empty:
                return self.send_json(200, {**base, "empty_board": True, "ink_px": 0, "png_base64": "",
                                            "width": 0, "height": 0, "angle": 0.0, "outline": []})
            accepted = req.get("accepted_labels") or []
            label = args.label or (accepted[0] if accepted else "bridge")
            png, width, height = png_info(LABEL_TO_DRAWING.get(label, "scribble"))
            with lock:
                captures[capture_id] = {**base, "label": label}
            return self.send_json(200, {**base, "empty_board": False, "ink_px": 4200, "png_base64": png,
                                        "width": width, "height": height, "angle": 0.0,
                                        "outline": [0.0, 0.0, float(width), 0.0, float(width), float(height), 0.0, float(height)]})
        self.send_json(404, {"error": "not found"})

    def log_message(self, fmt, *a):
        print(f"[fake-vision] {self.command} {self.path} -> {fmt % a}")


def main():
    global args
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--port", type=int, default=8000)
    parser.add_argument("--label-delay", type=float, default=1.5)
    parser.add_argument("--label", help="force this label for every capture")
    parser.add_argument("--uncalibrated", action="store_true")
    parser.add_argument("--empty", action="store_true")
    args = parser.parse_args()
    state["calibrated"] = not args.uncalibrated
    server = ThreadingHTTPServer(("127.0.0.1", args.port), Handler)
    print(f"[fake-vision] listening on http://localhost:{args.port}")
    server.serve_forever()


if __name__ == "__main__":
    main()
