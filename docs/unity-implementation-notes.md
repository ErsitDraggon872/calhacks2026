# Unity implementation notes

How the Unity half is put together, for Wesley, Henry, Andy and Person 4. The spec is `docs/unity-agent-handoff.md`.

## Station keys (Person 4: the firmware must send these)

| Button / key | Action |
|---|---|
| Enter (main or numpad) | BUILD (`Cast` action). A 3 s cooldown starts once the cutout appears, shown bottom right; a capture in flight also blocks it |
| P | Live webcam preview (camera setup) |
| Space | Reserved: Player B jump |
| Backspace | Reserved: reject during Approve (NEW BOARD is gone) |
| F5 | Reset level |
| F1 / F2 | Manual accept / reject (operator) |
| F3 | Debug overlay |
| F9 | Recalibrate (POST `/calibrate`) |
| WASD | Move |
| 1–5, 0 | Mock vision only: pick the next drawing (bridge, sun, fish, cat, scribble); 0 = empty board |

## Camera ownership

`GameConfig.captureSource` (or `-capture=mock|service|webcam` on a build) picks where board images come from. Interpreting them is a separate step (`IInterpreter`).
- **Webcam** (current): Unity opens the webcam itself (`BoardCamera`) and cuts the ink out locally (`InkExtractor`). The interpreter is `NoInterpreter` until `POST /interpret` exists (`docs/interpret-contract.md`), so every label is `unknown`. Use F1 to solve a socket.
  - **Device:** `GameConfig.cameraDevice` or `-camera=<name>`. Empty picks the first USB webcam, and never the integrated or a virtual camera. It keeps checking until one is plugged in, and notices unplugging.
  - **Readiness:** a capture needs a frame from the last second. Before that, BUILD says the camera can't see the board.
  - **Board space:** `boardCrop` is in the upright frame (normalized, origin bottom-left). The P preview shows exactly the pixels a capture uses, with the crop outlined.
  - **Ink:** thin dark strokes compared with the board within about a stroke's width, so hand shadows don't count. Blobs touching the crop edge (board frame, wall) and smudges under 2% of the drawing are dropped; dark enclosed areas (filled shapes) are kept. Tested on black marker only.
- **Service**: the Python service owns the camera (`BoardCamera` stays closed, since Windows lets only one process open a webcam).
- **Mock**: PNGs from `Resources/MockDrawings`, no hardware. The camera stays closed and the preview says it's disabled.

## JSON types Unity expects (Andy)

Field names are the contract. Unity parses with `JsonUtility`, so the **types** matter too:

- `/capture` request: `socket_id` string, `run_id` int, `accepted_labels` string array (`[]` for the gap, `["light"]` cave, `["fish"]` villager).
- `/capture` response: `id` string, `socket_id` string, `run_id` int (echo it), `empty_board` bool, `ink_px` int, `png_base64` string (RGBA PNG, ink on transparent), `width`/`height` int, `angle` float, `outline` float array.
- `/label/{id}`: `status` `"done"` or `"timeout"`, `run_id` int, `label` string (lowercase, e.g. `light`, `fish`, `cat`, `unknown`), `confidence` float, `provider` string, `latency_ms` number.
- `/health`: `camera` bool, `calibrated` bool, `provider` string, `provider_reachable` bool.
- `/calibrate`: `markers_found` int, `saved` bool.
- Errors: any non-2xx with `{"error": "..."}` or FastAPI's `{"detail": "..."}`. Unity matches the words `calibrat`, `empty`, `camera` to pick the subtitle.

`tools/fake_vision_service.py` implements all of this with the standard library only. Run `python tools/fake_vision_service.py` (see `--help` for `--uncalibrated`, `--label cat`, `--empty`), then set **Capture Source** to Service on `Assets/Resources/GameConfig.asset`. A build can also switch with `-capture=service` or `-capture=mock`.

## Where things live

- `Assets/Scripts/`: runtime code (assembly `Game`, namespace `CalHacks`). `RunManager` owns run_id, the CAST lock, socket binding, reset, checkpoints and win. Sockets are `GapSocket`, `CaveSocket` and `VillagerSocket` on a shared `PuzzleSocket` base.
- `Assets/Scripts/Tests/EditMode/`: label resolution, run_id discard, CAST lock, nearest socket, paper cutout.
- `Assets/Editor/`: builders. Everything under **Game** in the menu bar can be regenerated:
  - **Build Assets**: materials, `GameConfig`, all prefabs (overwritten in place; GUIDs kept).
  - **Rebuild Sandbox Scene** / **Rebuild Level Greybox**. The Level builder overwrites `Level.unity`, so once Henry has hand-edited the level, don't run it again.
  - **Setup Input Actions**, **Generate Mock Drawings**, **Build Windows Player**.
- `Assets/Prefabs/`: Player, CameraRig, Game (RunManager + HUD + subtitles + overlay), GapSocket, CaveSocket, VillagerSocket, Checkpoint, Exit, HintZone, Block/Wall/Rock.

## Henry: composing the level

- Drop socket prefabs where the obstacle is. The orange gizmo shows the cutout box and the 5 m CAST range.
- **Gap**: the `Deck` collider (disabled until solved) must cover the hole, and `Rails` keep the explorer on it. Leave the hole in the ground empty; falling below y = -5 respawns at the last checkpoint.
- **Cave**: `Darkness` covers the interior and `Blocker` closes the mouth. Scale both to fit the cave.
- **Villager**: the lane must be narrower than the villager plus the explorer (under 2.4 m), and `StepAside` must be clear of walls.
- Each socket's `Hint` child states the need. Edit its text, but never give away the answer.
- The `Game` object needs `Player`, `CameraRig` and `Start` assigned. Rebuilding Sandbox shows the wiring.

## Builds

`unity build . --target StandaloneWindows64 --execute-method BuildScript.PerformBuild` from a shell when the Editor is closed. **Game > Build Windows Player** does the same from inside the Editor. Either way the output is `Build/Windows/CalHacks2026.exe` (gitignored).
