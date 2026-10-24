# Draw-to-physics: design

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

## Physics components
Unlimited-feeling inventions come from *combining* a small set of tested parts, not from generating new physics.

| Component | Effect | Examples |
|---|---|---|
| Support | Collidable platform in the drawn shape | bridge, plank, giant spoon |
| Bounce | Launch impulse on contact | mushroom, trampoline, spring |
| Thrust | Limited directional propulsion (fuel) | rocket skateboard, booster, fan |
| Activation | Fires on contact, or on B pressing E | automatic spring vs controlled rocket |

Anything the AI can't read still becomes a Support in the drawn shape: never a hard failure.

## Backend's job (Andy)
- Check component names and combinations; reject unsupported combinations with a reason.
- Convert parameters to fixed, clamped, Unity-compatible values.
- Calculate the Ink cost from component, strength and size.
- Return an object spec Unity can instantiate safely. Unity still clamps defensively.

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
- **Opening shot:** the camera is tight on B at **the gap**, with a dotted "draw here" hint and B saying "Too far to jump!". Any drawing solves it, because anything unrecognised is still a solid Support. The first build never fails.
- **Reveal:** once the first invention lands, the camera pulls back and the world opens.
- **The open world:** a few obstacles visible at once, on different depth layers and taken in any order, each with many solutions:
  - a high ledge (mushroom, stairs, rocket)
  - a floating island with the goal flag (rocket skateboard, balloon, big bounce)
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
2. **Side-view world + drawings you can stand on** *(current step)*: the Paper Mario camera (tight opening on the gap, wide after the first build), paper B with Space jump, and the demo level greybox (start ledge, gap, hub, high ledge, floating island).
   BUILD at the gap still snaps across it. Anywhere else it's a free build: a Support at a fixed size (3 × 2.5 m box, aspect kept), its silhouette extruded 2 m deep, standing just in front of B. No labels needed.
   - Done when a free build is standable at several depths, the high ledge is reachable with one invention plus a jump, the island is reachable by stacking and the flag wins, and F5 resets inventions, camera and gap.
3. **One drawing → one ability → one obstacle:** `HttpInterpreter` plus a fake backend; the interpreted `support` component drives the invention.
4. Approve screen with ghost placement; Ink bar and pickups.
5. `bounce`, then `thrust` (with `contact` / `button` activation).
6. Plane lock while riding; B's E.
7. The rideable controller (mount, E to fire, A/D steer midair, Space to dismount).
