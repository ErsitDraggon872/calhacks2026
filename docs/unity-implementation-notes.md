# Unity implementation notes

How the Unity half is put together, for Wesley, Henry, Andy and Person 4. Start at `docs/README.md`; the design is `docs/superpowers/specs/2026-10-23-draw-to-physics-design.md` (`docs/unity-agent-handoff.md` is the superseded isometric spec).

## Station keys (Person 4: the firmware must send these)

| Button / key | Action |
|---|---|
| Enter (main or numpad) | BUILD (`Cast` action). Next to an unsolved socket the drawing goes to it; anywhere else it's a free build (see below). A 3 s cooldown starts once the cutout or invention appears, shown bottom right; a capture in flight also blocks it |
| P | Live webcam preview (camera setup) |
| G | Toggle the build ghost (on by default) |
| Space | Player B jump (about 1.2 m, `GameConfig.jumpHeight`; only when grounded) |
| Backspace | Reserved: reject during Approve (NEW BOARD is gone) |
| F5 | Reset level |
| F1 / F2 | Manual accept / reject (operator) |
| F3 | Debug overlay |
| F9 | Recalibrate (POST `/calibrate`) |
| WASD | Move: A/D left and right, W/S away from and toward the camera |
| 1–5, 0 | Mock vision only: pick the next drawing (bridge, sun, fish, cat, scribble); 0 = empty board |

## Camera ownership

`GameConfig.captureSource` (or `-capture=mock|service|webcam|replay|digital` on a build) picks where board images come from. **DigitalBoard** draws the SpacetimeDB browser whiteboard's strokes onto a clean board and runs the Webcam pipeline on it (`docs/coop-spacetimedb.md`). Interpreting them is a separate step (`IInterpreter`).
- **Webcam** (current): Unity opens the webcam itself (`BoardCamera`) and cuts the ink out locally (`InkExtractor`). The interpreter is `NoInterpreter` until `POST /interpret` exists (`docs/interpret-contract.md`), so every label is `unknown`. Use F1 to solve a socket.
  - Keep the frame rate capped (`GameConfig.targetFrameRate`, 60, set in `RunManager.Awake`). Uncapped, a maximized Game view or a fullscreen build pins the machine and the C270 stops delivering frames. While it is stalled, Unity's `WebCamTexture` blocks the main thread for about 0.4 s every second, sometimes for 10–20 s.
  - **Device:** `GameConfig.cameraDevice` or `-camera=<name>`. Empty picks the first USB webcam, and never the integrated or a virtual camera. It keeps checking until one is plugged in, and notices unplugging.
  - **Readiness:** a capture needs a frame from the last second. Before that, BUILD says the camera can't see the board.
  - **Board space:** `boardCrop` is in the upright frame (normalized, origin bottom-left). The P preview shows exactly the pixels a capture uses, with the crop outlined.
  - **Ink:** thin dark strokes compared with the board within about a stroke's width, so hand shadows don't count. Blobs touching the crop edge (board frame, wall) and smudges under 2% of the drawing are dropped; dark enclosed areas (filled shapes) are kept. Tested on black marker only.
- **Service**: the Python service owns the camera (`BoardCamera` stays closed, since Windows lets only one process open a webcam).
- **Mock**: PNGs from `Resources/MockDrawings`, no hardware. The camera stays closed and the preview says it's disabled.

## Side view (Paper Mario look)

- **Camera:** `CameraFollow` on the CameraRig. Perspective, FOV 35°, yaw 0 (looking along +Z), pitch 17°. It smooth-follows B on X and Y, but only 30% on depth, so walking in depth doesn't swing the shot. It has two framings: `Tight` (14 m, the opening shot on the gap) and `Wide` (24 m, looking a bit ahead). It blends between them over 1 s. Reaching the run's first checkpoint (just past the gap) calls `Reveal()`: it blends out to `Wide`, holds for 2.5 s (`revealHold`), then blends back to `Tight`. F5 snaps back to `Tight`. All of this is tunable on the component.
- **Player B:** a flat paper cutout (`Player/Visual/Paper`, texture drawn in code by `MockDrawingGenerator.Character` and run through `PaperTexture.MakePaper`). The quad is lowered by the CharacterController's skin width plus the paper below the drawn feet (`PaperTexture.LowestInkRow`), so the feet touch the ground. The body never rotates; `Visual`'s X scale flips to face left or right (`PlayerController.FacingSign`). The collider is still the `CharacterController` (step offset 0.3 m, so drawn stair-steps can be climbed).

## Free builds: inventions you can stand on

BUILD with no unsolved socket within 5 m makes an `Invention` (every one is a Support for now; labels are ignored).
- **Size:** each drawing is fitted inside a box keeping its aspect: the build slot's box at an obstacle, `GameConfig.inventionSize` (3 × 2.5 m) anywhere else. How big it was drawn on the board doesn't matter.
- **Build slots** (`BuildSlot`): a designer-placed box against a free-build obstacle. While B is on the floor inside the slot's trigger (any depth, front lane included), a free build lands in that box instead of beside B, pushed against the obstacle (`InventionPlacement.SlotBottomCentre`). A new build there replaces the previous one, and if it appears around B, B is lifted onto it (`InventionPlacement.LiftTo`). The level has one, against the high ledge's left face: 3 × 1.2 m (`BuildSlot.DefaultBox`), at the ledge's depth (z 0.5..3). B lands on 0.7–1.6 m steps from the floor and reaches the 2.3 m ledge from any of them (measured with the CharacterController), so anything up to about 4:1 wide makes a working step.
- **Everywhere else:** the build stands at B's feet height and depth, on the side B faces, 0.4 m clear of B (`InventionPlacement.BottomCentre`), then moves out of level geometry and other builds: the smallest shift toward the camera or further from B that clears them (`InventionPlacement.ClearSpot`, up to 4 m each way). With no room, nothing is built and "No room to build here!" shows.
- **Build ghost:** an orange dashed outline (the gap's "draw here" look) of the slot's front face, shown while B is inside a slot (`BuildGhost` on the Player). Every drawing lands inside it, and the slot stands where nothing hides it. G toggles it (`RunManager.BuildGhostVisible`). The ghost texture is drawn at `BuildSlot.DefaultBox`'s aspect.
- **Still to come:** the spec's Approve step (show the real drawing as the ghost, nudge, confirm).
- **Flat to look at, thick to stand on:** the visual is a camera-facing `Cutout`. The colliders are the paper silhouette rebuilt as 0.15 m-wide columns whose tops and bottoms are exact to the pixel (`GameConfig.inventionCell`, `SilhouetteBoxes`), so B stands on the drawn edge, not up to a cell above it. Each is `GameConfig.inventionDepth` (2 m; a slot sets its own) deep along Z and centred on the drawing. What you see is what you stand on: the paper cutout has no holes (`PaperTexture.MakePaper` fills enclosed areas), so an outlined shape is solid, and a diagonal line is a staircase.
- **Standing on the drawing, at any depth:** the drawing is flat but its collider is deep, and the camera looks down, so B near the back of the collider would look like they're floating above the drawing (about 0.3 m on a 2 m build, 0.47 m on the gap's 3.2 m bridge). While B stands on an invention, `PlayerController` slides B's picture to the drawing's depth (`planeSnapSpeed`, 8 m/s); on anything else it slides back. The body doesn't move.
- **Stacking:** building while standing on an invention puts the next one at that height, floating beside you.
- F5 removes every invention. The F3 overlay shows how many there are.

## The demo level (`Game/Rebuild Level Greybox`)

Everything runs along +X, with depth toward the back (+Z). Nothing solid stands in front of the paths, and invisible walls stop B walking off the front edge.
- **Start ledge** x −6..10: front path (z −1..1) and a raised back terrace (z 1..3, 0.8 m: jump onto it). B starts at x 7.5, within CAST range of the gap.
- **Gap** x 10..14.6 (`GapSocket` at x 12.3). Hint: "Too far to jump!". A dashed "draw here" outline shows where the bridge will go, until one stands. Every drawing becomes the bridge: stretched to 4.9 m (overlapping both ledges), its top 0.4 m above them (`railHeight`), 3.2 m deep, and solid only where it's inked, like a free build (no hidden deck). A drawing with a break drops B; the gap stays unsolved, so the next BUILD in range replaces the bridge. `Rails` stand while a bridge does. The gap is solved once B is past the far ledge (x 14.75), and BUILD stops binding to it. F1 always rebuilds the bridge from the fallback drawing (a full plank) and solves it.
- **Hub floor** x 14.6..40, z −2..3. Checkpoint at x 17.
- **High ledge** x 24..30 on the back layer (z 0.5..3), 2.3 m tall. Hint: "Too high to climb!" (x 20..30, whole depth). One invention plus a jump gets up: builds from the floor near it land in its build slot, x 21..24 against its left face.
- **Floating island** x 34..40, top at 4 m, with the `Exit` and a goal flag. Reach it by stacking inventions, or by building off the high ledge.
- The cave and villager sockets are no longer in the level. Their scripts and prefabs stay, and Sandbox still uses them.

## JSON types Unity expects (Andy)

Field names are the contract. Unity parses with `JsonUtility`, so the **types** matter too:

- `/capture` request: `socket_id` string (`"free"` for a free build), `run_id` int, `accepted_labels` string array (`[]` for the gap and free builds, `["light"]` cave, `["fish"]` villager).
- `/capture` response: `id` string, `socket_id` string, `run_id` int (echo it), `empty_board` bool, `ink_px` int, `png_base64` string (RGBA PNG, ink on transparent), `width`/`height` int, `angle` float, `outline` float array.
- `/label/{id}`: `status` `"done"` or `"timeout"`, `run_id` int, `label` string (lowercase, e.g. `light`, `fish`, `cat`, `unknown`), `confidence` float, `provider` string, `latency_ms` number.
- `/health`: `camera` bool, `calibrated` bool, `provider` string, `provider_reachable` bool.
- `/calibrate`: `markers_found` int, `saved` bool.
- Errors: any non-2xx with `{"error": "..."}` or FastAPI's `{"detail": "..."}`. Unity matches the words `calibrat`, `empty`, `camera` to pick the subtitle.

`tools/fake_vision_service.py` implements all of this with the standard library only. Run `python tools/fake_vision_service.py` (see `--help` for `--uncalibrated`, `--label cat`, `--empty`), then set **Capture Source** to Service on `Assets/Resources/GameConfig.asset`. A build can also switch with `-capture=service` or `-capture=mock`.

## Where things live

- `Assets/Scripts/`: runtime code (assembly `Game`, namespace `CalHacks`). `RunManager` owns run_id, the CAST lock, socket binding, free builds, reset, checkpoints and win. `Inventions/` holds free builds (`Invention`, plus the pure `SilhouetteBoxes` and `InventionPlacement`). `Player/` holds `PlayerController` and `CameraFollow`. Sockets are `GapSocket`, `CaveSocket` and `VillagerSocket` on a shared `PuzzleSocket` base.
- `Assets/Scripts/Tests/EditMode/`: label resolution, run_id discard, CAST lock, nearest socket, paper cutout, ink extraction, silhouette boxes, invention placement.
- `Assets/Editor/`: builders. Everything under **Game** in the menu bar can be regenerated:
  - **Build Assets**: materials, `GameConfig`, all prefabs (overwritten in place; GUIDs kept).
  - **Rebuild Sandbox Scene** / **Rebuild Level Greybox**. The Level builder overwrites `Level.unity`, so once Henry has hand-edited the level, don't run it again.
  - **Setup Input Actions**, **Generate Mock Drawings**, **Build Windows Player**.
- `Assets/Prefabs/`: Player (paper cutout), CameraRig (side camera), Game (RunManager + HUD + subtitles + overlay), GapSocket, CaveSocket, VillagerSocket, Checkpoint, Exit, HintZone, Block/Wall/Rock.

## Henry: composing the level

- Drop socket prefabs where the obstacle is. The orange gizmo shows the cutout box and the 5 m CAST range.
- **Side view**: the camera looks along +Z from the front (−Z). Keep walls and props out of the front, and put obstacles on different depth layers.
- **Gap**: there is no deck collider; the bridge is the drawing itself. The socket's `span` width (4.9 m) must overlap both ledges, and its `depth` (3.2 m) must reach the inner faces of the `Rails`, which keep the explorer on it while a bridge stands. The far ledge, where the gap counts as solved, is the anchor's x plus half the span. The `DrawHere` quad marks where the drawing lands. Leave the hole in the ground empty; falling below y = -5 respawns at the last checkpoint.
- **Cave**: `Darkness` covers the interior and `Blocker` closes the mouth. Scale both to fit the cave.
- **Villager**: the lane must be narrower than the villager plus the explorer (under 2.4 m), and `StepAside` must be clear of walls.
- Each socket's `Hint` child states the need. Edit its text, but never give away the answer.
- The `Game` object needs `Player`, `CameraRig` and `Start` assigned. Rebuilding Sandbox shows the wiring.

## Builds

`unity build . --target StandaloneWindows64 --execute-method BuildScript.PerformBuild` from a shell when the Editor is closed. **Game > Build Windows Player** does the same from inside the Editor. Either way the output is `Build/Windows/CalHacks2026.exe` (gitignored).
