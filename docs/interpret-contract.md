# `/interpret` contract (Unity ↔ backend)

**Status:** integrated for DRAW/WEBCAM free builds by `tools/thinkink_backend.py` + `InterpretHttpClient`. Socket puzzles still use legacy labeling. Unity currently uses `NoInterpreter`, so every drawing is `unknown`. The new service validates model outputs and provides metadata; Unity posts to `/interpret` directly for AI free builds. See `docs/DEERAI-SETUP.md` for backend startup and limitations.

Unity owns the webcam and the ink cut-out. The backend never sees the camera; it only receives the two images below.

## Conventions
- **Board space.** The upright camera frame (rotation and mirroring undone) cropped to `GameConfig.boardCrop`. Pixel origin is the **bottom-left**, as Unity's `Texture2D` stores it. The preview outline, the capture and these coordinates all use it.
- **Units.** Metres, seconds and degrees. Gameplay is a side view in the paper plane: +x is right, +y is up.
- **JSON.** Unity parses with `JsonUtility`, so:
  - field names are exact and types matter (`int` vs `float`)
  - no dictionaries and no polymorphism: every component uses the same flat object, and fields that don't apply are `0` / `""`
  - an error body is `{"error": "..."}`, or FastAPI's `{"detail": "..."}`

## Request: `POST /interpret`
```json
{
  "capture_id": "cam-3",
  "run_id": 7,
  "socket_id": "gap",
  "goal": "get across the gap",
  "board_png_base64": "...",
  "board_width": 1280,
  "board_height": 720,
  "cutout_png_base64": "...",
  "cutout_x": 412, "cutout_y": 160, "cutout_width": 394, "cutout_height": 334,
  "ink_px": 19248
}
```
- **`board_png_base64`**: the untouched board crop (RGB PNG). **Recognise from this one.** Ink extraction can drop faint strokes or keep a smudge, and that mustn't spoil recognition.
- **`cutout_png_base64`**: ink on transparent (RGBA PNG). It's exactly what the player sees in the world. The `cutout_*` rect says where it sits in the board image, so the backend can map part locations onto it.
- **`goal`**: the puzzle framing ("draw something that gets B across the gap"). It helps the model resolve ambiguous drawings.

## Response
```json
{
  "status": "ok",
  "capture_id": "cam-3",
  "run_id": 7,
  "name": "rocket skateboard",
  "message": "A skateboard with a booster on the back. Press E to fire.",
  "width_m": 2.4,
  "ink_cost": 35,
  "components": [
    { "type": "support", "activation": "always", "anchor_x": 0.5, "anchor_y": 0.3,
      "launch_speed": 0, "angle_deg": 0, "acceleration": 0, "duration_s": 0 },
    { "type": "thrust", "activation": "button", "anchor_x": 0.1, "anchor_y": 0.4,
      "launch_speed": 0, "angle_deg": 0, "acceleration": 12, "duration_s": 2.5 }
  ],
  "provider": "bitdeer",
  "latency_ms": 2140
}
```
- **`status`**:
  - `ok`
  - `rejected`: an unsupported combination or nonsense. `message` says why, in player-facing words. No Ink is spent.
  - `error`: the backend failed. Unity shows a retry message.
- **`run_id` / `capture_id`**: echoed back. Unity drops replies from an older run, which is what happens after F5.
- **`width_m`**: the object's width in the world. The height follows the cutout's aspect ratio.
- **`ink_cost`**: computed by the backend from the components, their strengths and the size. Unity charges it when A approves.
- **`anchor_x/y`**: where the part is on the cutout, normalized 0–1 with the origin at the bottom-left. For example, the booster is drawn at the back. This is the "point to it" guardrail: every part has to be located on the drawing.

### Components and the limits the backend clamps to
Unity clamps to the same limits again, defensively.

| `type` | Effect | `activation` | Fields used |
|---|---|---|---|
| `support` | Collidable platform in the drawn shape | `always` | — |
| `bounce` | Launch impulse on contact | `contact` or `button` | `launch_speed` 3–14 m/s; `angle_deg` −60…60 (0 = straight up, + = toward +x) |
| `thrust` | Limited propulsion | `button` or `contact` | `acceleration` 2–25 m/s²; `duration_s` 0.5–3 (fuel); `angle_deg` is the push direction relative to the object (0 = toward the object's +x) |

- Every object has at least one component. If nothing is recognised, return a single `support`: the drawn shape is still solid.
- At most one `thrust` and one `bounce` per object.
- `width_m` is 0.5–6; `ink_cost` is 5–100.

## Open (team decision)
- **Activation as a field.** The pitch lists "Activation" as a fourth component. Here it's a field on each active part, because a bare activation component means nothing without a part to trigger.
- **Cooldown and the approval pause.** Proposal: the BUILD cooldown starts when A **places** the object, so the approval pause never eats into it. In the current slice (no approval step yet), it starts once the cutout appears.
