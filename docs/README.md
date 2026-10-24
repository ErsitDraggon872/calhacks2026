# Docs: start here

## The game in one paragraph
Paper Mario-style **side view**. Player B walks left and right, steps toward or away from the camera between terraces, and **jumps with Space**. Player A (at the demo, a judge) draws in Henry's browser pad and presses BUILD. SpacetimeDB syncs the strokes to Unity, which cuts the ink out. A real whiteboard and webcam remain an alternative input. The drawing then appears in the world as a flat paper cutout you can stand on. **It is solid only where there's ink: no hidden colliders.** At the same time the board image goes to the backend's `POST /interpret`, which says *what* the drawing is: its name, material and traits. Puzzles react to those traits, never to the name. A torch, a firefly and a lightbulb all light a cave because they all `glow`.

**The one rule we say out loud:** *your lines decide its shape; AI decides what it is.*

**The pitch the demo has to earn:** *Draw on a real whiteboard. Your drawing becomes a solid object, and AI gives it properties that help your partner solve the game.*

## The demo route (`Assets/Scenes/Level.unity`)
1. **The gap** (shape). Any drawing is stretched across it as a bridge. Shape alone solves it; the AI's reading is shown but doesn't gate it.
2. **The gatekeeper** (meaning). Only something the AI calls `edible` opens the gate; something `hostile` scares the gatekeeper off. We draw a bridge first so it is visibly refused, then an apple so it is accepted. A different food after F5 proves it's the property, not the picture.
3. **Stairs → island → flag.**

The high-ledge build slot is still in the level, but it's off the rehearsed route.

## What is in and out for the demo
| In | Deferred (not on the demo route) |
|---|---|
| Ink-only bridge at the gap | Rope/tether, hinges and push/pull |
| `/interpret` → name + traits (`edible`, `glows`, `hostile`) | Ink economy and pickups |
| Gatekeeper decides on `edible` | Burning, extinguishing, floating, cutting, conductivity |
| "Apple → food → lets you through" card | Approve screen with ghost placement |
| Bounce, thrust and lift at tested strengths, plus surface traits | Glide, roll, attach (backend demo mode drops them); `emit` (non-light) and `explode` |
| | Grappling rope (`tether`): cut |
| Browser whiteboard + main free-invention physics | Andy's in-Unity drawing pad; Henry's SpacetimeDB browser whiteboard supplies drawing input |

The backend runs in demo mode by default: every answer is cut to support plus at most two of bounce, thrust and lift, with power snapped to three tested strengths, and a flyer's carrying power comes from its drawn width (a tiny bird can't lift the player; a giant one can). `THINKINK_DEMO=0` restores the full set: Unity's free inventions also run glide, roll and attach (E activates/rides, Space jumps off). A ride that falls off the level returns to where it was built. Unity still parses other deferred parts. Anything it cannot run yet keeps the drawn solid shape without the unsupported power. Recognized support-only vehicles gain a basic drive ability; puzzle solutions still depend on traits.

## Running it
- **The backend:** `BITDEER_API_KEY=... python tools/thinkink_backend.py` serves `/interpret` on `http://127.0.0.1:8000`. Without a key, `THINKINK_FAKE=1 THINKINK_FAKE_SEQUENCE=bridge,bridge,apple python tools/thinkink_backend.py` answers each BUILD in turn.
- **Unity:** defaults to Henry's browser whiteboard (`DigitalBoard`), opened from F4 > **Create room + open drawing pad**. It syncs drawings through a local SpacetimeDB server, started automatically with Play mode; one Unity Editor is sufficient and no ThinkInk key is needed. See [`coop-spacetimedb.md`](coop-spacetimedb.md). The Webcam capture source calls the backend at `GameConfig.interpretUrl`. `-capture=replay` uses saved board photos instead of the camera (`GameConfig.replayFolder`, default `StreamingAssets/Replay`).
- **Digital board instead of the whiteboard:** `-capture=digital` (`CaptureSource.DigitalBoard`). The judge draws in Henry's browser whiteboard (`tools/whiteboard/index.html`) and taps BUILD there; Unity joins its SpacetimeDB room and runs the same BUILD on the drawing. The default same-PC mode needs no ThinkInk key or cloud database. The AI backend still needs internet for real recognition. See [`coop-spacetimedb.md`](coop-spacetimedb.md#the-digital-board-one-game-machine).
- **Checking the backend:** `python tools/interpret_check.py --dir <photos>` checks a running backend against the demo expectations.

## Current docs
| Doc | What it's for |
|---|---|
| [`superpowers/specs/2026-10-23-draw-to-physics-design.md`](superpowers/specs/2026-10-23-draw-to-physics-design.md) | Full design: attributes, shape rules, world and camera, build order. Read it next to this page's in/out table: some of its later steps are deferred |
| [`interpret-contract.md`](interpret-contract.md) | The `POST /interpret` request and response between Unity and the backend |
| [`interpret-prompt.md`](interpret-prompt.md) | The system prompt the backend sends to the model, and why models over-answer "bridge" |
| [`coop-spacetimedb.md`](coop-spacetimedb.md) | Henry's SpacetimeDB co-op: the browser whiteboard as the digital board, the two-machine Creator/Explorer mode, the sync key and the server |
| [`handoff-digital-board.md`](handoff-digital-board.md) | State of the browser board, the local room server and Andy's invention physics: how to run them, what's verified, what's left |
| [`handoff-interpret-backend.md`](handoff-interpret-backend.md) | **Andy:** what to build for `/interpret`, in priority order, with acceptance tests |
| [`DEERAI-SETUP.md`](DEERAI-SETUP.md) | Running the `/interpret` backend (`tools/thinkink_backend.py`): key, fake mode, health check. Its "apply patch" step and DRAW mode come from Andy's branch and don't apply on `main` |
| [`unity-implementation-notes.md`](unity-implementation-notes.md) | How the Unity half is put together: station keys, free builds, the demo level |

## Finished handoffs (history; the work has landed)
| Doc | What it was |
|---|---|
| [`handoff-side-view-world.md`](handoff-side-view-world.md) | Verify the step 2 side-view world (PR #1) |
| [`handoff-build-placement.md`](handoff-build-placement.md) | Redesign free-build placement and the build ghost |
| [`handoff-gap-bridge-verify.md`](handoff-gap-bridge-verify.md) | Verify the ink-only gap bridge (PR #2) |

## Superseded
| Doc | Why |
|---|---|
| [`unity-agent-handoff.md`](unity-agent-handoff.md) | The original spec. It describes an **isometric camera, no jump, and hidden pre-placed solution colliders**, which is the opposite of the current design. Its Python vision service API (`/capture`, `/label`) is used only by the mock/service capture sources |
