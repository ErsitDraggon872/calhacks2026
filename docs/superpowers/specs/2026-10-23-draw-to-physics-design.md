# Draw-to-physics: design

> **Demo scope (2026-10-24):** the demo route is gap → gatekeeper → flag, built on `/interpret` traits. Ink, the Approve screen, rideable rockets and the planned interaction rules below are deferred. See the in/out table in [`docs/README.md`](../../README.md).

## Pitch
One player draws an invention on a physical whiteboard. AI translates it into working physics as a game object.
The other player controls, tests and uses that object to solve open-ended puzzles.
**Physical drawing → visible cutout → working object** is the memorable part.

- Player A invents the tools (whiteboard + Enter/Backspace + arrows/Q/E while placing).
- Player B operates them (WASD move, Space jump, E activate).

## Loop
1. **Discover**: B reaches an obstacle and asks A for help.
2. **Invent**: A draws on the whiteboard and presses Enter.
3. **Interpret**: the backend sends the sketch to Bitdeer, which maps it to supported physics components.
4. **Approve**: A sees the interpreted components, the Ink cost and a placement ghost. Enter places it (spends Ink); Backspace rejects it (no Ink spent).
5. **Operate**: B rides, triggers or bumps into the invention.
6. **Iterate**: if it fails, the team redraws and tries again. Physics that honestly does what A drew is the fun, not a bug.

## Attributes (how inventions combine)
Unlimited-feeling inventions come from *combining* a small set of tested attributes, not from generating new physics. That's how Scribblenauts got so many possibilities: every object is a bag of shared attributes, and puzzles and the world react to the attributes, never to the noun. A torch, a firefly and a lightbulb all solve the dark cave because they all `glow`.

Each drawing is interpreted into three layers (full vocabulary and limits in `docs/interpret-contract.md`):
| Layer | What | Examples |
|---|---|---|
| Properties (one each) | `material`, `weight`, `body` | rubber (bouncy), ice (slippery, cold, fragile), wood (flammable, floats) |
| Traits (any number) | tags the world checks | `glows`, `edible`, `hostile`, `hot`, `wet`, `floats`, `climbable`, `sharp` |
| Components (behaviour) | Support, Bounce, Thrust, Emit, Explode, each with an activation (always / contact / B's E) | mushroom = bounce on contact; balloon or wheel = thrust pointed up or along; torch = emit light |

A material implies its traits (wood is always flammable), so the model can't forget them. Anything the AI can't read still becomes a Support in the drawn shape: never a hard failure.

**Interaction rules make it multiply.** Today: `glows` lights the cave, `edible` feeds the villager and `hostile` scares them off, and `slippery` / `bouncy` / `sticky` change how B moves on it. Planned: `hot` + `flammable` burns away, `wet` puts out fire, `floats` crosses water, `climbable` is a ladder, `soft` breaks a fall, `sharp` cuts rope, `heavy` presses plates, `conductive` carries power.

**Sockets check traits, not labels.** A socket lists the traits that solve it (any of them); the old `acceptedLabels` remain only as a fallback against the name.

## Shape, behaviour and placement
Every invention, at a socket or free-built, follows the same four rules:
1. **Shape always comes from the cutout's silhouette.** What you see is what you stand on: a gap in the ink is a gap in the collider.
2. **Attributes add behaviour, never shape.** Materials, traits and components decide what the object does, not its colliders.
3. **Placement and size are decided by where it's built:** stretched across a socket (the gap), fitted into a build slot's box, or fitted beside B. From step 3, the backend's `width_m` takes over the size.
4. **Sockets decide placement and the win condition, never physics.** No hidden colliders stand in for the drawing.

## Backend's job (Andy)
- Turn the sketch into attributes: material, weight, traits and components. Check names and combinations; reject nonsense with a reason.
- Convert parameters to fixed, clamped, Unity-compatible values.
- Calculate the Ink cost from component, strength and size.
- Return an object spec Unity can instantiate safely. Unity still clamps defensively, and has the last word on placement: the backend never sees the level.

The request/response contract is in `docs/interpret-contract.md`: `POST /interpret` gets both the raw board crop and the cutout, and returns a flat, clamped object spec plus the Ink cost.

## Ink
- A limited resource shown as a bar. The backend sets each invention's cost, so complex or strong inventions cost more.
- B refills it from Ink pickups around the world. This stops spamming.
- Building also has a short cooldown (bottom-right bar).

## World and camera (Paper Mario: The Thousand-Year Door look)
- **Camera:** low, side-on, tilted down about 15–20°, with perspective. It follows B left and right. This replaces the isometric camera; tilt and distance are tunable settings.
- **World:** 3D terraces at several depths (foreground ledge, middle path, back terrace). B walks left/right **and** toward or away from the camera, and jumps with Space.
- **Characters and inventions are flat cutouts** standing in the 3D world, facing the camera. A's drawing is literally another flat PNG, like the characters. Side-view sketches map straight on: the booster is on the back because A drew it there.
- **Flat to look at, thick to stand on:** an invention's collider is its drawn silhouette extruded back into the scene by about the path depth (default 2 m, one setting per invention). B can cross a drawn plank anywhere along the path's depth.
- **Moving inventions stay in their plane:** riding or launching happens left/right and up/down at the depth where the invention was placed. B's depth is locked while riding.

## Demo level and flow
- **Who plays:** judges draw and press BUILD. A teammate plays B and narrates; the keyboard stays with the team for now.
- **Opening shot:** the camera is tight on B at **the gap**, with a dotted "draw here" hint and B saying "Too far to jump!". Every drawing becomes a bridge, never rejected (a connected stroke always spans, because it's stretched across). A drawing with a break drops B into the gap, and the next BUILD replaces it.
- **Reveal:** when B reaches the first checkpoint (just past the gap), the camera pulls back to show the world, then returns to the normal shot.
- **The open world:** a few obstacles visible at once, on different depth layers and taken in any order, each with many solutions:
  - a high ledge (mushroom, stairs, rocket): its puzzle area is only the approach to its left face
  - the gatekeeper on the front path, beside the gate at the foot of the island stairs: anything edible (or anything scary) opens it
  - a floating island with the goal flag (rocket skateboard, balloon, big bounce). The stairs reach it, so there are two routes up: build onto the high ledge and hop across to the stairs, or feed the gatekeeper
- **Length:** a good run is 60–90 seconds. The team practises a set route for the pitch; judges can improvise.
- **Teaching without words:**
  - a card at the board ("Draw something to get across the gap!") and a big physical BUILD button
  - B's short lines at obstacles
  - the "AI understood: …" card after each build
  - contextual key hints like "E to fire"

## Placement
- **Placement during Approve:** the game pauses and the camera eases out. A moves a ghost of the cutout in the plane with the arrow keys, rotates it with Q/E (B's E is ignored while paused), and confirms with Enter.
  No first-person view: it would break the diorama look and yank the shared screen away from B.

## Hardware
- Unity opens the webcam directly (`WebCamTexture`). P toggles a live preview for camera setup.
- Capture source (`GameConfig.captureSource`: Mock / Service / Webcam) and interpreter (`IInterpreter`) are separate choices, so Webcam + HTTP interpretation can run together.
- We trust the captured image to be good; no image-quality gating.

## Build order
Prove each step end to end before widening it.
1. **Camera → cutout** *(done)*: webcam preview (P), Enter → frame → cutout, cooldown replaces NEW BOARD.
   - Done when five different black-marker drawings become recognisable, correctly oriented cutouts in the **standalone build**, and repeated Enter presses and F5 don't break it. Every label is still `unknown`.
2. **Side-view world + drawings you can stand on** *(current step)*: the Paper Mario camera (tight opening on the gap; at the first checkpoint it pulls out to show the world, then back), paper B with Space jump, and the demo level greybox (start ledge, gap, hub, high ledge, floating island).
   BUILD at the gap stretches the drawing across it as the bridge, solid only where it's inked. Anywhere else it's a free build: a Support at a fixed size (3 × 2.5 m box, aspect kept), its silhouette extruded 2 m deep, standing just in front of B. No labels needed.
   - Done when a free build is standable at several depths, the high ledge is reachable with one invention plus a jump, the island is reachable by stacking and the flag wins, and F5 resets inventions, camera and gap.
3. **One drawing → one ability → one obstacle:** `HttpInterpreter` plus a fake backend; the interpreted attributes drive the invention. *(Unity side started: `InventionSpec` parses and clamps the full attribute response, free builds wait for it and pass a placement check before building, `glows` / surface traits are applied, and sockets solve by trait. Until the backend lands, old labels map to attributes via `InventionSpec.FromLabel`.)*
4. Approve screen with ghost placement; Ink bar and pickups. Make free-build placement more intuitive here (step 2's "beside B, on the side B faces" is hard to read and can clip into the level): snap to the obstacle or let the player nudge or confirm the spot, and keep step 2's build ghost (orange outline in free-build hint zones, G toggles) showing the real landing spot.
5. `bounce`, then `thrust` (with `contact` / `button` activation).
6. Plane lock while riding; B's E.
7. The rideable controller (mount, E to fire, A/D steer midair, Space to dismount).
