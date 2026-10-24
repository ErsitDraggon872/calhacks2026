# Handoff: Unity agent (Wesley + Henry tasks)

You are building the Unity half of a Cal Hacks 13.0 game (Oct 23–25, 2026; 38 hours of hacking). One player draws on a physical whiteboard; a camera captures it; a Python service crops and labels it; **your Unity game shows the drawing itself as a paper cutout at the obstacle and makes it work** (a bridge you can walk on, a light that opens a cave, a fish the villager accepts).

Full team plan (source of truth for anything not covered here): https://claude.ai/artifact/LEyWvcDRgaQMgg8ZQi8ScS

**Committed scope:** three sockets, black marker, one verified vision provider, reliable reset, an explicit manual fallback. Polish only after that works repeatedly.

## Your scope

| Owner | You build |
|---|---|
| Wesley (code) | All C# scripts, prefabs, input, camera, controller, vision client, sockets, behaviors, reset, overrides, debug overlay, desktop build |
| Henry (level) | The level scene: greybox, 3 sockets placed, villager, checkpoints, exit, art and sound after Hour 16 |

**Not yours:** the Python vision service (Andy), the button-station firmware and camera rig (Person 4). Do not change the API contract below without telling the user; Andy builds against it.

## Project facts

- Repo root **is** the Unity project: `C:\Users\wesle\UnityGames\calhacks2026`. Don't restructure it.
- Unity **6000.3.18f1**, URP template (`Assets/Settings/PC_RPAsset.asset`), Input System 1.19 with `Assets/InputSystem_Actions.inputactions`. Only `Assets/Scenes/SampleScene.unity` exists.
- `com.unity.pipeline` 0.9.1-exp.1 is in the manifest, and Unity CLI 1.0.0-beta.14 is installed, so a running Editor can be driven live.
- Uncommitted change at handoff: `com.unity.purchasing` removed from `Packages/manifest.json`. That's intentional (no IAP); leave it, and don't add or remove packages unless asked.
- Windows; shells are PowerShell and Git Bash.
- Target: **Windows desktop build** on one laptop. No WebGL, no multiplayer.

## How to work with the Unity CLI

Load the `unity-cli` skill first. Then:

1. `unity status --format json`. If no instance is "ready", ask the user to open the project (`unity open .`) and wait. **Don't hand-edit `.unity`, `.prefab` or `.asset` YAML.**
2. `unity command` lists what this Editor exposes. Use the listed commands, or `unity command eval '<C#>'`, to create GameObjects, add components, wire references, build prefabs and save scenes. Don't guess command names.
3. Write C# scripts as normal files under `Assets/Scripts/`. After writing, let the Editor recompile, then check the console for errors before continuing.
4. If the CLI can't connect after a script change, it's probably **Safe Mode** from a compile error: run `unity pipeline list`, fix the C#, then have the user restart Unity.
5. Verify in Play mode (`editor_play` or whatever `unity command` lists), and read the console. "It compiles" is not "it works."
6. Builds: add `Assets/Editor/BuildScript.cs` with a static `PerformBuild()` that builds `Level.unity` to `Build/Windows/`, then `unity build . --target StandaloneWindows64 --execute-method BuildScript.PerformBuild`. Make sure `Build/` is gitignored.
7. EditMode tests for pure logic (label resolution, run_id discard, nearest socket): `unity test . --mode EditMode`.

## Ownership (so Wesley's and Henry's work never collides)

| Path | Owner | Rule |
|---|---|---|
| `Assets/Scripts/`, `Assets/Prefabs/`, `Assets/Editor/` | Wesley | Every mechanic lives in a prefab. |
| `Assets/Scenes/Sandbox.unity` | Wesley | Test each prefab here first. |
| `Assets/Scenes/Level.unity` | Henry | Built only from prefabs; no new scripts written in-scene. |
| `Assets/Art/`, `Assets/Audio/` | Henry | After Hour 16. |

Even if you do both roles, keep the split: build and test in Sandbox, compose in Level. Branches: `wesley/<topic>` and `henry/<topic>`. Commit only when the user asks, and keep main working.

## Don't block on the Python service

Build an `IVisionClient` interface with two implementations, picked by a toggle on a `GameConfig` ScriptableObject:

- **`MockVisionClient`:** returns a PNG from `Assets/Resources/MockDrawings/` (draw a bridge, sun, fish, cat and a scribble) after ~100 ms for capture and ~1 s for the label. The label is chosen by a debug key or a cycling list. Use this until Andy's service is up.
- **`HttpVisionClient`:** async `UnityWebRequest` against `http://localhost:8000`, parsed with `JsonUtility` (all payloads are flat).

## API contract (Python service, localhost:8000)

| Endpoint | Request | Response |
|---|---|---|
| POST `/capture` | `socket_id`, `run_id`, `accepted_labels` | `id`, `socket_id`, `run_id`, `empty_board`, `ink_px`, `png_base64` (RGBA crop), `width`, `height`, `angle`, `outline` (flat floats) |
| GET `/label/{id}` | (held open until done) | `status` (done or timeout), `socket_id`, `run_id`, `label`, `confidence`, `provider`, `latency_ms` |
| POST `/calibrate` | — | markers found, saved |
| GET `/health` | — | camera, calibrated, provider, provider reachable |

Errors: not calibrated, empty board, camera error → show a subtitle and stay unlocked.

**Hard rules:**

1. **Bind each capture to its socket.** At CAST, pick the nearest socket within range of the explorer. That `socket_id` goes in the request, and the label applies only to that socket, even if the explorer walks away. If no socket is in range, the capture is a free build (`socket_id` `"free"`): the drawing becomes a standable invention just in front of the explorer (see `docs/unity-implementation-notes.md`).
2. **Discard stale responses.** Every reset increments `run_id`. Drop any reply whose `run_id` isn't current, and cancel pending waits.
3. **CAST is ignored** while a request is in flight, or until NEW BOARD is pressed after the previous cast.

## Inputs

Put all of these in the Input Actions asset. The button station is a USB keyboard, so **tell the user these keys so Person 4's firmware sends the same ones.**

| Action | Key | Notes |
|---|---|---|
| Move | WASD | Camera-relative, isometric |
| BUILD (`Cast`) | Enter / numpad Enter | Player A's station button; 3 s cooldown afterwards (bottom-right bar). NEW BOARD and the board lock are gone. |
| Camera preview | P | Live webcam feed with the board crop outlined |
| Jump | Space | Reserved for Player B |
| Reset level | F5 | Under 5 s; increments run_id |
| Manual accept | F1 | Forces the bound/nearest socket's solution |
| Manual reject | F2 | Forces `unknown` |
| Debug overlay | F3 | provider, latency, run_id, cooldown, camera, health |
| Recalibrate | F9 | POST `/calibrate` |

## Game design you're implementing

**Camera and controller:** orthographic isometric camera (about 30° pitch, 45° yaw) with a smooth follow. Top-down controller, no jump: `CharacterController` or a kinematic body, grounded by a **3D downward raycast**. Colliders are 3D, never 2D.

**Level (Henry), in order:** start → **gap** → checkpoint → **dark cave** → checkpoint → **villager blocking the path** → **exit**. Obstacles state the need, never the answer. Station sign text: "1. Draw · 2. Press CAST · 3. Wipe · 4. Press NEW BOARD."

**Sockets:** each one has an id, a type, an anchor, a span, accepted labels, a hidden pre-placed `BoxCollider` (disabled), and a solved flag. When a drawing lands, show it as a quad scaled to that box (texture from `png_base64`, URP unlit, alpha-clipped). **Physics never comes from the drawing.**

| Socket | Accepts | On valid drawing |
|---|---|---|
| Gap (tutorial) | anything non-empty | Enable the walkable deck **on `/capture`**, without waiting for the label. Shape alone solves it. |
| Cave | `light` | The cutout becomes a light source; remove the darkness and the "too dark" blocker |
| Villager | `fish` | The villager takes it, says a line and steps aside. Wrong label → one rejection line, socket stays open. |

- **Label resolution:** a label in `accepted_labels` solves the socket. `cat` plays its one reaction and leaves the cutout as decor. `unknown` or a wrong label leaves a decor cutout with no collider. After each label, show the subtitle "Looks like: X."
- **Run state:** run_id, elapsed time, sockets solved, board locked, request in flight. Score = completion time.
- **Win:** reaching the exit shows the time. Reset (F5) restores every socket and the player in under 5 s.
- **Cut, don't build:** paint bar, pickups, NeoPixel, `/paint`, `/board`, wipe detection, extra easter eggs, ladder/key puzzles, jumping, menus, multiplayer, WebGL.

## Build order and checkpoints

Hours count from the start of hacking. Each step ends with a Play-mode check you actually ran.

1. Isometric camera + controller in Sandbox.
2. Socket prefab + `MockVisionClient` + CAST/NEW BOARD lock → mock bridge cutout at the gap, walkable.
3. Run manager: reset, run_id, checkpoints, respawn, win at the exit.
4. `HttpVisionClient` against Andy's service. **Hour 8:** a physical drawing appears in Unity and enables the bridge.
5. Light and villager sockets, subtitle, cat reaction, manual accept/reject, debug overlay.
6. Henry: `Level.unity` greybox with all 3 sockets. **Hour 16:** three rough puzzles playable; reset and manual recovery work.
7. `BuildScript` + Windows build. **Hour 24:** a full run works in the desktop build on the demo laptop.
8. Spawn effects, paper-world art, sound. **Hour 30:** features frozen.

## When to stop and ask the user

- Any change to the API contract or key bindings.
- Adding or removing packages, or changing project settings beyond Input System and build settings.
- No connected Editor, or Safe Mode you can't clear.
- Anything on the cut list.
