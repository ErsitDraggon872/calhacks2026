# `/interpret` contract (Unity ↔ backend)

**Status:** wired end to end; real-model accuracy is still to be checked with a Bitdeer key.
- **Backend:** `tools/thinkink_backend.py` (Andy's, on `http://127.0.0.1:8000`; setup in `docs/DEERAI-SETUP.md`). It returns this contract's identity fields (`name`, `material`, `traits`, `adjectives`) next to Andy's mechanics. That means components with the eight fields `type` … `duration_s` (no `emit_kind`/`range_m`; JsonUtility defaults them), extra types `lift` `glide` `roll` `attach` `hinge` `push_pull`, and a numeric `attributes` object. Unity ignores what it doesn't build. There is no `tether`: the grappling rope is cut.
- **Unity:** `HttpInterpreter` (`Assets/Scripts/Vision/HttpInterpreter.cs`) posts every Webcam and Replay capture to `GameConfig.interpretUrl` (`-interpret=<url>`, or `-interpret=none` for no AI).
  - An `error` reply, a timeout or an unreachable server is a failure, never "unknown". At the gatekeeper it says "Couldn't read that drawing. Press BUILD to try again."
  - `-capture=replay` feeds saved board photos (`GameConfig.replayFolder`, default `StreamingAssets/Replay`) through the same path.
  - Before the backend existed, labels became attributes through `InventionSpec.FromLabel`; that table is still the fallback for the mock and service capture sources.
- **What Unity does with the reply:** it already parses the whole response (`Assets/Scripts/Inventions/InventionSpec.cs`). It applies `material`, `traits` (`edible`, `hostile`, `glows`, `slippery`, `bouncy`, `sticky`) and puzzle checks. `bounce`, `thrust`, `emit` (other than light), `explode` and `body: dynamic` are parsed but built as a plain solid.

## Demo-critical subset (get these right first)
The demo route is gap → gatekeeper → flag, and what decides it is the gatekeeper accepting `edible`. In priority order:
1. `status` (`ok` / `rejected` / `error`), with `capture_id` and `run_id` echoed back.
2. `name`: the specific everyday noun ("apple", "banana", "rubber duck"), never a category or a puzzle answer.
3. `traits`, above all `edible` and `hostile` (then `glows`).
4. `material`: Unity adds implied traits from it (`plant` implies `edible`), so an apple with `material: plant` passes even without the trait listed.
5. `message`: one short player-facing line, required for `rejected`.

Everything else can be a safe default for now: `components` = a single `support`, `weight` `medium`, `body` `static`, `width_m` 0, `ink_cost` 5, `adjectives` empty.

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
- **`goal`**: the puzzle framing. Use it only to break a tie between two readings of the strokes (see `interpret-prompt.md`); it must never turn a duck into a bridge.
- **`socket_id`** and the `goal` Unity sends with it:

  | `socket_id` | `goal` |
  |---|---|
  | `gap` | `get B across the gap` |
  | `gatekeeper` | `something the gatekeeper will accept` |
  | `free` | `""` (a free build anywhere else) |

  `cave` and `villager` exist only in the Sandbox scene.

## Response
An object is a **combination of attributes**, like Scribblenauts: one value per property, any number of traits, and a few active components. Puzzles and the world react to the attributes, never to the name, so a torch, a firefly and a lightbulb all light the cave because they all `glow`.
```json
{
  "status": "ok",
  "capture_id": "cam-3",
  "run_id": 7,
  "name": "flaming rubber skateboard",
  "message": "A bouncy skateboard on fire, with a booster on the back. Press E to fire.",
  "adjectives": ["flaming", "rubber"],
  "material": "rubber",
  "weight": "light",
  "body": "static",
  "traits": ["hot", "glows", "rideable"],
  "width_m": 2.4,
  "ink_cost": 45,
  "components": [
    { "type": "support", "activation": "always", "anchor_x": 0.5, "anchor_y": 0.3,
      "launch_speed": 0, "angle_deg": 0, "acceleration": 0, "duration_s": 0, "emit_kind": "", "range_m": 0 },
    { "type": "thrust", "activation": "button", "anchor_x": 0.1, "anchor_y": 0.4,
      "launch_speed": 0, "angle_deg": 0, "acceleration": 12, "duration_s": 2.5, "emit_kind": "", "range_m": 0 }
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
- **`adjectives`**: the words the model used, for the "AI understood: …" card only. Fold their meaning into the other fields ("flaming" → `hot` + `glows`, "giant" → a bigger `width_m`, "rubber" → `material`).
- **`anchor_x/y`**: where the part is on the cutout, normalized 0–1 with the origin at the bottom-left. For example, the booster is drawn at the back. This is the "point to it" guardrail: every part has to be located on the drawing.

### Properties: one value each
| Field | Values | What Unity does |
|---|---|---|
| `material` | `wood` `stone` `metal` `rubber` `cloth` `paper` `glass` `ice` `jelly` `plant` `flesh` `cloud` (default `wood`) | Adds the traits it implies (below), and its surface feel for B |
| `weight` | `feather` `light` `medium` `heavy` (default `medium`) | Stored; for weight puzzles and, later, dynamic bodies |
| `body` | `static` `dynamic` (default `static`) | Everything is `static` for now; `dynamic` is reserved |

Unity adds a material's implied traits itself, so the model can't forget them:
| `material` | Implies |
|---|---|
| `wood`, `paper` | `flammable`, `floats` |
| `metal` | `conductive`, `magnetic` |
| `rubber` | `bouncy` |
| `ice` | `cold`, `slippery`, `fragile` |
| `glass` | `fragile` |
| `jelly` | `bouncy`, `sticky` |
| `cloud` | `lighter_than_air`, `soft` |
| `cloth` | `flammable`, `soft` |
| `plant` | `flammable`, `edible` |
| `flesh` | `alive` |
| `stone` | — |

### Traits: any number, at most 8
This is the combinatorial layer: every trait is a tag a puzzle or another object can check.
| Group | Traits |
|---|---|
| Elements | `glows` `hot` `cold` `wet` `flammable` `fireproof` `waterproof` |
| Physics | `floats` `lighter_than_air` `bouncy` `slippery` `sticky` `soft` `fragile` `sharp` `conductive` `magnetic` |
| Living | `alive` `edible` `hostile` `friendly` `noisy` `smelly` |
| Use | `climbable` `rideable` `holdable` `container` |

Exclusions (the backend avoids them; Unity resolves them anyway): `hot` and `cold` can't both be set (Unity keeps `hot`). `floats` and `lighter_than_air` can't go with `weight: heavy` (Unity drops them). Something on fire is `hot` (+ `glows` if it gives light); if it also burns away, add `flammable`.

What uses them today: `glows` gives off light and lights the cave; `edible` feeds the villager and `hostile` scares them off; `slippery`, `bouncy` and `sticky` change how B moves on it. Planned: `hot` + `flammable` burns away, `wet` puts out `hot`, `floats` crosses water, `climbable` lets B climb, `soft` breaks a fall, `sharp` cuts rope, `heavy` presses plates, `conductive` carries power.

### Components and the limits the backend clamps to
Unity clamps to the same limits again, defensively. Balloons, propellers, wheels and moving platforms are `thrust` pointed the right way, not separate types.

| `type` | Effect | `activation` | Fields used |
|---|---|---|---|
| `support` | Collidable platform in the drawn shape | `always` | — |
| `bounce` | Launch impulse on contact | `contact` or `button` | `launch_speed` 3–14 m/s; `angle_deg` −60…60 (0 = straight up, + = toward +x) |
| `thrust` | Limited propulsion | `button` or `contact` | `acceleration` 2–25 m/s²; `duration_s` 0.5–3 (fuel); `angle_deg` is the push direction relative to the object (0 = toward the object's +x, 90 = up) |
| `emit` | Sprays something from the anchor | `always`, `contact` or `button` | `emit_kind` `light`/`fire`/`water`/`wind`; `range_m` 0.5–8; `angle_deg` direction (0 = +x) |
| `explode` | Radial blast, breaks `fragile` level pieces, then the object is gone | `contact` or `button` | `range_m` 0.5–8; `launch_speed` 3–14 m/s (push) |

- Every object has at least one component. If nothing is recognised, return a single `support`: the drawn shape is still solid.
- Components never change the collider's shape: Unity always builds it from the cutout's silhouette, and components only add behaviour.
- At most one of each `type` per object.
- `width_m` is 0.5–6; `ink_cost` is 5–100. Each trait and each non-support component should add cost, more for strong values.
- Unknown `material`, `weight`, `body`, trait or component strings are ignored by Unity (logged), never an error. Matching is case-insensitive.

### Unity has the last word
The backend judges the **object** (what it is, valid combinations, clamps, `rejected` with a reason). It never sees the level, so Unity judges the **placement** before building a free build: no room means no build and no Ink spent. A component Unity can't run yet is dropped and the rest is built (a flamethrower is still a solid flamethrower). The tutorial gap always builds.

## Open (team decision)
- **Activation as a field.** The pitch lists "Activation" as a fourth component. Here it's a field on each active part, because a bare activation component means nothing without a part to trigger.
- **Cooldown and the approval pause.** Proposal: the BUILD cooldown starts when A **places** the object, so the approval pause never eats into it. In the current slice (no approval step yet), it starts once the cutout appears.
