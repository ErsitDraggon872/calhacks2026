"""Acceptance check for the /interpret backend (docs/handoff-interpret-backend.md).

Sends every PNG in a folder to POST /interpret the way Unity will, checks each reply against the contract
(docs/interpret-contract.md) and the demo expectations, and prints the latency. Standard library only.

Name each photo after what's drawn on it; the stem before any "-" or "_" decides what's expected:
    apple.png, banana-2.png, sandwich.png   -> edible (directly or via material "plant")
    bridge.png, ladder_1.png                -> not edible
    scribble.png                            -> name "scribble"
    empty.png                               -> status "rejected"
    anything else                           -> only checked against the schema

    python tools/interpret_check.py --url http://localhost:8001 --dir path/to/photos
    python tools/interpret_check.py --url http://localhost:8001 --dir photos --socket gap
"""

import argparse
import base64
import json
import statistics
import struct
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

GOALS = {"gap": "get B across the gap", "gatekeeper": "something the gatekeeper will accept", "free": ""}
EDIBLE = {"apple", "banana", "sandwich", "pizza", "carrot", "cake", "burger", "fish"}
NOT_EDIBLE = {"bridge", "ladder", "plank", "rock", "rocket"}
# The eight fields every component carries; emit_kind / range_m are optional (JsonUtility defaults them).
COMPONENT_FIELDS = {"type": str, "activation": str, "anchor_x": (int, float), "anchor_y": (int, float),
                    "launch_speed": (int, float), "angle_deg": (int, float), "acceleration": (int, float),
                    "duration_s": (int, float)}
TOP_FIELDS = {"status": str, "capture_id": str, "run_id": int, "name": str, "message": str, "adjectives": list,
              "material": str, "traits": list, "width_m": (int, float), "ink_cost": int, "components": list}
TIMEOUT_S = 15


def png_size(data):
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError("not a PNG")
    return struct.unpack(">II", data[16:24])


def post(url, payload):
    body = json.dumps(payload).encode()
    request = urllib.request.Request(url + "/interpret", body, {"Content-Type": "application/json"})
    start = time.perf_counter()
    try:
        with urllib.request.urlopen(request, timeout=TIMEOUT_S) as response:
            return response.status, json.loads(response.read() or b"{}"), time.perf_counter() - start
    except urllib.error.HTTPError as e:
        text = e.read()
        try:
            return e.code, json.loads(text or b"{}"), time.perf_counter() - start
        except json.JSONDecodeError:
            return e.code, {"_raw": text.decode(errors="replace")}, time.perf_counter() - start


def schema_problems(reply, capture_id, run_id):
    problems = []
    for field, kind in TOP_FIELDS.items():
        if field not in reply:
            problems.append(f"missing {field}")
        elif not isinstance(reply[field], kind) or isinstance(reply[field], bool):
            problems.append(f"{field} has the wrong type ({type(reply[field]).__name__})")
    if reply.get("capture_id") != capture_id or reply.get("run_id") != run_id:
        problems.append("capture_id / run_id not echoed")
    if reply.get("status") not in ("ok", "rejected", "error"):
        problems.append(f"status {reply.get('status')!r}")
    if reply.get("status") == "rejected" and not reply.get("message"):
        problems.append("rejected without a message")
    components = reply.get("components") or []
    if reply.get("status") == "ok" and not components:
        problems.append("no components (send at least one support)")
    for i, component in enumerate(components):
        if not isinstance(component, dict):
            problems.append(f"components[{i}] is not an object")
            continue
        for field, kind in COMPONENT_FIELDS.items():
            if field not in component:
                problems.append(f"components[{i}] missing {field} (unused fields are 0)")
            elif not isinstance(component[field], kind) or component[field] is None:
                problems.append(f"components[{i}].{field} has the wrong type")
    return problems


def is_edible(reply):
    traits = {str(t).lower() for t in reply.get("traits") or []}
    return "edible" in traits or str(reply.get("material", "")).lower() == "plant"


def expectation_problem(stem, reply):
    if stem == "empty":
        return None if reply.get("status") == "rejected" else "expected status rejected"
    if reply.get("status") != "ok":
        return f"expected status ok, got {reply.get('status')}"
    if stem in EDIBLE and not is_edible(reply):
        return "expected edible"
    if stem in NOT_EDIBLE and is_edible(reply):
        return "should not be edible"
    if stem == "scribble" and reply.get("name") != "scribble":
        return "expected name scribble"
    return None


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--url", default="http://localhost:8001")
    parser.add_argument("--dir", required=True, type=Path)
    parser.add_argument("--socket", default="gatekeeper", choices=sorted(GOALS))
    args = parser.parse_args()
    url = args.url.rstrip("/")

    photos = sorted(args.dir.glob("*.png"))
    if not photos:
        sys.exit(f"No .png files in {args.dir}")

    failures, latencies = 0, []
    for run_id, photo in enumerate(photos, start=1):
        data = photo.read_bytes()
        width, height = png_size(data)
        capture_id = f"check-{run_id}"
        encoded = base64.b64encode(data).decode("ascii")
        payload = {
            "capture_id": capture_id, "run_id": run_id, "socket_id": args.socket, "goal": GOALS[args.socket],
            "board_png_base64": encoded, "board_width": width, "board_height": height,
            "cutout_png_base64": encoded, "cutout_x": 0, "cutout_y": 0, "cutout_width": width,
            "cutout_height": height, "ink_px": 0,
        }
        try:
            code, reply, seconds = post(url, payload)
        except Exception as e:  # unreachable, timeout
            failures += 1
            print(f"FAIL {photo.name:<24} {e}")
            continue
        latencies.append(seconds)
        stem = photo.stem.lower().replace("_", "-").split("-")[0]
        problems = schema_problems(reply, capture_id, run_id) if code == 200 else [f"HTTP {code}: {reply}"]
        expected = expectation_problem(stem, reply) if code == 200 else None
        if expected:
            problems.append(expected)
        verdict = "ok  " if not problems else "FAIL"
        failures += bool(problems)
        print(f"{verdict} {photo.name:<24} {seconds:5.2f}s  {reply.get('status')}  {reply.get('name')!r}  "
              f"material={reply.get('material')} traits={reply.get('traits')}")
        for problem in problems:
            print(f"       - {problem}")

    code, reply, _ = post(url, {"capture_id": "garbage", "run_id": 0})
    error_ok = code >= 400 and ("error" in reply or "detail" in reply)
    failures += not error_ok
    print(f"{'ok  ' if error_ok else 'FAIL'} garbage request -> HTTP {code} {reply}")

    if latencies:
        print(f"\nlatency: median {statistics.median(latencies):.2f}s, max {max(latencies):.2f}s "
              f"(target median < 4s, max < {TIMEOUT_S}s)")
    print(f"{failures} failure(s)")
    sys.exit(1 if failures else 0)


if __name__ == "__main__":
    main()
