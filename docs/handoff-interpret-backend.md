# Handoff: finish the `/interpret` backend (Andy)

> **Update (2026-10-24, branch `wesley/interpret-backend`):** your `tools/thinkink_backend.py` is in, unchanged except for these additions:
> - identity (`name`, `material`, `traits`, `adjectives`) in the prompt, schema and validation
> - the image now goes before a tie-breaker-only goal
> - food, bridge and blank fixtures, plus `THINKINK_FAKE_SEQUENCE` for fake mode
> - `THINKINK_PORT`
> - the grappling rope (`tether`) is cut
>
> Your Unity changes stay on `feature/thinkink-full-update`. What's left for you:
> 1. Run it with the real key.
> 2. Run `tools/interpret_check.py` on real whiteboard photos for both sockets.
> 3. Tune `tools/thinkink_system_prompt.txt` until foods come back `edible` and the goal doesn't turn things into bridges.
>
> The checklist below still applies; read "your backend" as `tools/thinkink_backend.py`.

**`/interpret` is the most valuable missing piece of the demo.** Right now every drawing comes back as `unknown`. A judge can't see any AI: any scribble becomes a bridge, and nothing shows that the game understood what was drawn. Once your backend says "apple → `edible`", the gatekeeper accepts it, a "Apple → food → lets you through" card appears, and the pitch works.

**Please park the rocket until this ships.** Rockets and thrust are not on the demo route. Unity builds a `thrust` component as a plain rocket-shaped solid, so nothing breaks while the rocket is unfinished.

## What the demo needs from you
The route is **gap → gatekeeper → flag**:
- **The gap** turns any drawing into a bridge. Shape does that; your reading is only displayed.
- **The gatekeeper** decides on *meaning*. Something `edible` opens the gate, something `hostile` scares the gatekeeper off, and anything else is refused.

On stage we draw a bridge at the gatekeeper (refused), then an apple (accepted). After a reset, a judge draws a *different* food, like a banana or a sandwich, and it's accepted too. That's the moment that proves it's the AI's understanding, not the picture.

So, in priority order:
1. **`edible` must be right.** Foods are edible (directly in `traits`, or via `material: "plant"`, which Unity expands to `edible`). Bridges, ladders, planks and rocks are not.
2. **`name` is the specific noun** the judge drew: "apple", not "food", and never "bridge" because the goal mentioned a gap. `docs/interpret-prompt.md` explains why models do that and how the prompt avoids it.
3. **`status` and the echoes are always valid.** Echo `capture_id` and `run_id` back. Use `rejected` with a friendly `message` for a blank board or a non-object, and `error` (or an HTTP error) when Bitdeer fails.
4. **Latency:** median under 4 s; Unity gives up at 15 s.

Everything else can be a safe default for now: `components: [one support]`, `weight: "medium"`, `body: "static"`, `width_m: 0`, `ink_cost: 5`, `adjectives: []`. Unity parses `bounce`, `thrust`, `emit` and `explode` but doesn't run them yet.

## The contract
The full spec is [`docs/interpret-contract.md`](interpret-contract.md), including its "Demo-critical subset" section. The short version:

**Request** (`POST /interpret`, JSON):
```json
{
  "capture_id": "cam-3", "run_id": 7,
  "socket_id": "gatekeeper", "goal": "something the gatekeeper will accept",
  "board_png_base64": "...", "board_width": 1280, "board_height": 720,
  "cutout_png_base64": "...", "cutout_x": 412, "cutout_y": 160, "cutout_width": 394, "cutout_height": 334,
  "ink_px": 19248
}
```
- **Recognise from `board_png_base64`**, the untouched whiteboard crop (RGB). The cutout is the extracted ink on transparency, and it can lose faint strokes.
- `socket_id` is `gap`, `gatekeeper` or `free`. The `goal` is a **tie-breaker only**; see the prompt doc.

**Minimal good replies** at the gatekeeper:
```json
{"status": "ok", "capture_id": "cam-3", "run_id": 7, "name": "apple", "message": "An apple! Someone hungry will love this.",
 "adjectives": [], "material": "plant", "weight": "light", "body": "static", "traits": ["edible"], "width_m": 0.5, "ink_cost": 10,
 "components": [{"type": "support", "activation": "always", "anchor_x": 0.5, "anchor_y": 0.5, "launch_speed": 0, "angle_deg": 0,
                 "acceleration": 0, "duration_s": 0, "emit_kind": "", "range_m": 0}],
 "provider": "bitdeer", "latency_ms": 2140}
```
```json
{"status": "ok", "capture_id": "cam-4", "run_id": 7, "name": "rope bridge", "message": "A rope bridge. Walk across!",
 "adjectives": [], "material": "wood", "weight": "medium", "body": "static", "traits": [], "width_m": 5, "ink_cost": 15,
 "components": [{"type": "support", "activation": "always", "anchor_x": 0.5, "anchor_y": 0.5, "launch_speed": 0, "angle_deg": 0,
                 "acceleration": 0, "duration_s": 0, "emit_kind": "", "range_m": 0}],
 "provider": "bitdeer", "latency_ms": 1980}
```
```json
{"status": "rejected", "capture_id": "cam-5", "run_id": 7, "name": "", "message": "The board looks blank. Draw something!",
 "adjectives": [], "material": "", "weight": "", "body": "", "traits": [], "width_m": 0, "ink_cost": 0, "components": [],
 "provider": "bitdeer", "latency_ms": 900}
```
Errors: return HTTP 4xx/5xx with `{"error": "..."}` (FastAPI's `{"detail": "..."}` is fine too).

## JSON rules Unity depends on (it parses with `JsonUtility`)
- **Field names exact, types exact.** `run_id` and `ink_cost` are integers; the rest of the numbers are floats.
- **No dictionaries or polymorphism.** Every component is the same flat object with all ten fields; unused fields are `0` or `""`. Never `null`.
- **Unknown strings are ignored, not errors.** Lower-case vocabulary only, exactly as spelled in the contract (`lighter_than_air`, not `lighter-than-air`).
- **Extra fields are ignored,** so passing the prompt's `observation` through is harmless. Logging it is very useful.

## The prompt
Start from [`docs/interpret-prompt.md`](interpret-prompt.md): system prompt, user-message layout, few-shot examples and settings (temperature ~0.4, validate, retry once, then fall back to a plain `support`). The things that matter most for this demo:
- Recognise first and only then read the goal. Put the goal *after* the image, on one line.
- Specific nouns, not categories.
- Keep the pizza example (edible via `plant`) in the few-shots. Don't let bridges dominate.

## How to check it: acceptance script
`tools/interpret_check.py` sends a folder of whiteboard photos to your endpoint exactly as Unity will. It checks every reply against the contract and the demo expectations, tries a garbage request, and prints latency. Name each photo after what's drawn on it:

| File | Expected |
|---|---|
| `apple.png`, `banana.png`, `sandwich.png` (and `pizza`, `carrot`, `cake`, `burger`, `fish`) | `status: ok` and edible |
| `bridge.png`, `ladder.png` | `status: ok` and **not** edible |
| `scribble.png` | `name: "scribble"` |
| `empty.png` (a blank board) | `status: rejected` |
| anything else (`rubber-duck.png`, `unfamiliar-*.png`) | schema only. Check the printed name by eye |

Variants like `apple-2.png` are expected to behave the same as `apple.png`. Photos must be PNG; take them of the real whiteboard with black marker, roughly like the C270 sees it.

```bash
python tools/interpret_check.py --url http://127.0.0.1:8000 --dir path/to/photos
```

To check that the goal doesn't leak (a duck at the gap must stay a duck):
```bash
python tools/interpret_check.py --url http://127.0.0.1:8000 --dir path/to/photos --socket gap
```

**Done when:**
- the script exits 0 for both `--socket gatekeeper` and `--socket gap` on at least 3 foods, 2 non-foods, a scribble and a blank board
- median latency is under 4 s

## Running it for the demo
- Serve on the demo laptop at **`http://127.0.0.1:8000`**. Unity defaults to that URL, and running locally avoids an extra network hop. Your server still needs internet for Bitdeer.
- Add `GET /health` returning `{"ok": true, "provider": "bitdeer"}`, so we can check it before going on stage.
- Read the Bitdeer key from an environment variable. Never commit it.

## What Unity does with your reply (so you know what judges see)
- **The card:** shows the name, a plain word for the key property ("food", "gives light", "scary") and the outcome. For example: "Apple → food → lets you through ✓".
- **When the request fails:** if you return `error`, time out, or can't be reached, Unity says "Couldn't read that drawing. Press BUILD to try again". It doesn't pretend the drawing was "unknown".
- **When a reply is stale:** a reply with an old `run_id` (after F5 reset) is dropped.
- **Unity's side is wired** (`HttpInterpreter`, photo replay). Run the game with `-capture=replay` (or the webcam) and it calls your backend at `GameConfig.interpretUrl`. Unity was checked against your backend in fake mode: bridge refused, apple and banana accepted, gate opens, flag reached.

## Hand back
Send Wesley:
- the URL and how to start the server (one command)
- the acceptance script output for both sockets
- the 8–10 photos you used, so they can go into Unity's replay folder
