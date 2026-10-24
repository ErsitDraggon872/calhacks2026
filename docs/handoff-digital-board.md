# Handoff: finish and verify the digital board (Henry's SpacetimeDB whiteboard as BUILD input)

For the next Claude Code session, cloud or local.
- **Integration:** merged into `wesley/interpret-backend` at Wesley's request. Preserve current puzzle banners and invention cards. Henry's browser board is the digital input; Andy's main physics abilities are integrated selectively.
- **Background:**
  - `docs/coop-spacetimedb.md`, section "The digital board (one game machine)": the design, as built.
  - `docs/interpret-contract.md`: unchanged, and must stay that way.
- **Can't happen in a cloud session:** compiling, tests and Play mode all need Unity 6000.3.18f1. A cloud session can do the code steps (part B) and leave the Unity checks (part C) for a session on Wesley's PC.

## What's on the branch
1. **`4bff346`: a real merge of `origin/henry/spacetimedb-coop` onto `origin/wesley/interpret-backend`.** No conflicts.
   - The merge-base was main's tip, and `ProjectSettings/` is untouched.
   - Adds the SpacetimeDB module (`server/`), the browser whiteboard (`tools/whiteboard/index.html`), `Assets/Scripts/Multiplayer/` and the `Coop` prefab in `Level` and `Sandbox`.
   - Adds the SDK `com.clockworklabs.spacetimedbsdk` v2.13.0 as a git package, referenced from `Game.asmdef`.
2. **`f409bb7`: the digital board.**
   - **Source switch:** `CaptureSource.DigitalBoard` (`-capture=digital`).
   - **`DigitalBoardFrameSource`:** an `IBoardFrameSource`, added by `RunManager` at runtime.
     - It caches the room's strokes per capture_id.
     - A `pending` invention row from the page triggers `RunManager.Cast()`, waiting up to 5 s if BUILD is busy.
     - If the strokes never arrived, it falls back to the cutout image carried in the `pending` row.
   - **`StrokeBoard`:** draws strokes onto a white 992×572 board (the page's 960×540 canvas plus a 16 px border, because InkExtractor drops ink blobs that touch the frame edge).
   - **`WebcamVisionClient`:** now takes the crop as a parameter. Webcam and Replay pass `boardCrop`; the digital board passes the whole frame.
   - **Whiteboard page, `?mode=board`:** BUILD only publishes `pending`, then clears. The page also clears when `game_state.reset_count` changes (Unity's F5).
   - **Two-machine co-op stays out of the way in this mode:** `ExplorerInventions` and the Explorer HUD line stand aside. The `Cast()` guard only blocks two-machine co-op.
   - **Tests:** `PaperTexture.DestroySafe` is now public and used in the touched texture paths, so they work in EditMode tests. 8 new tests are in `DigitalBoardTests`.
   - **Docs:** `README`, `coop-spacetimedb` and `unity-implementation-notes`.

## Already verified (Wesley's PC, second Editor on the worktree)
- [x] It compiles. The SDK git package resolves on Unity 6000.3.18f1.
- [x] EditMode: 95/95 pass (the existing 87 plus 8 new).
- [x] Mock, no `thinkink.key`, offline: the co-op panel shows "Offline · key missing", and a BUILD at the gap lays the bridge.
- [x] Replay: the fixture photo goes to `/interpret` (fake backend) and comes back as "rope bridge" at the gap.
- [x] DigitalBoard, not in a room: BUILD fails cleanly with "camera: SpacetimeDB connecting".
- [~] Webcam: the C270 sent no frames, probably because the other Editor in Play mode held it. BUILD failed cleanly. Redo with the camera free.

## B. Original review checklist (see integration update below)
1. **Review `f409bb7`** for bugs, especially in `DigitalBoardFrameSource`:
   - `TryCast` assumes `RunManager.Cast()` grabs the frame before its first `await`. It does today, because `WebcamVisionClient.CaptureAsync` is synchronous. If that ever changes, the capture is unpinned too early.
   - An old `pending` row seen before `RoomReady` must not trigger BUILD.
   - Rows keep arriving after `DrawingCleared` (the page clears right after BUILD).
2. **Error prefix:** `WebcamVisionClient` prefixes every source failure with `camera:`, so the digital board says "camera: SpacetimeDB connecting". Either make the prefix come from the source, or accept it.
3. **Optional (Wesley's call):** move `Assets/Scripts/Multiplayer/` into its own asmdef, so that if the SDK package fails to resolve, only co-op breaks and not the whole `Game` assembly. That means replacing `RunManager`'s and `Hud`'s references to `ThinkInkSession` with hooks. Ask before doing it.
4. Keep `docs/coop-spacetimedb.md` in step with any behaviour change.

## C. Unity checks (Wesley's PC only)
**Setup:**
- Run `unity status --format json`. Reuse the existing Editor for the root project, explicitly targeting `--project-path C:\Users\wesle\UnityGames\calhacks2026`. **Never automatically open another Editor or this worktree in Unity.** Stop Play mode before EditMode tests, and reload externally changed scenes when merging. If no Editor exists, report that rather than launching one.
- Start the fake backend on its own port, because 8000 may already be taken: `THINKINK_PORT=8011 THINKINK_FAKE=1 THINKINK_FAKE_SEQUENCE=bridge,bridge,apple,banana python tools/thinkink_backend.py`.
- Set `GameConfig` **in memory only**, never saved: `captureSource=DigitalBoard`, `interpretUrl=http://127.0.0.1:8011`. Afterwards reload it with `Resources.UnloadAsset`.
- Open `Assets/Scenes/Level.unity`. The player is at x 7.5, the gap at 12.3, the gatekeeper at 29.6. To move the player, disable its CharacterController, set the position, re-enable it, then `Physics.SyncTransforms()`.

1. **Simulated room** (no network), through `unity command eval`:
   - Set `ThinkInkSession`'s `Room` (a `new SpacetimeDB.Types.Room { RoomId = 4242, RoomCode = "SIMU" }`), `RoomReady = true` and `Role = Explorer` by reflection, then raise `RoomChanged`.
   - Raise the `StrokeAdded`, `InventionChanged` (a `pending` row) and `DrawingCleared` events by reading their backing delegate fields and calling `DynamicInvoke`.
   - [ ] **Gap:** bridge strokes, then a `pending` row: BUILD runs, the cutout spans the gap, and the fake backend answers "bridge".
   - [ ] **Gatekeeper:** a bridge drawing is refused. An apple drawing sent while the bridge reading is still in flight waits, then BUILDs and is accepted (`edible`).
   - [ ] Sending `DrawingCleared` right after a `pending` row doesn't lose that drawing.
   - [ ] F5 (`ResetRun`) empties the cached board.
2. **Live Maincloud room.** This needs Henry to **resume the paused `thinkink` database**: `https://maincloud.spacetimedb.com/v1/database/thinkink/schema?version=9` returned 503 "database is paused" on 2026-10-24. It also needs `thinkink.key` in the project root; Wesley has it, and it's never committed.
   - [ ] The co-op panel's "Test: open a whiteboard tab" opens the page with `mode=board`, and Unity joins as the Explorer.
   - [ ] Draw at the gap and BUILD on the page: the bridge appears and the page clears.
   - [ ] Gatekeeper: a bridge is refused, then an apple is accepted.
   - [ ] F5 in Unity empties the page canvas within about 1.5 s.
   - [ ] A tablet on the LAN: `python -m http.server 8080` in `tools/whiteboard`, open `index.html?mode=board`, then join by typing the room code in the panel.
3. [ ] Webcam with the C270 free: unchanged behaviour with or without the key.

**Don't commit these if they show up:**
- `ProjectSettings/ShaderGraphSettings.asset` (import noise)
- `runInBackground` in `ProjectSettings.asset`
- `Assets/Prefabs/Materials/*.mat`
- `ProjectSettings/Packages/com.unity.pipeline/EditorPipelineConfig.json`

Delete any `Assets/Temp` captures.

## Report back
Which boxes you ticked, the EditMode count, the commits, and anything still blocked (most likely the paused database).

## Integration update (2026-10-24)

- Browser DigitalBoard is the default capture source. SpacetimeDB syncs browser strokes and resets; Python `/interpret` still supplies recognition.
- Each browser BUILD retains a separate immutable frame in a FIFO (up to 8 waiting). CLEAR does not destroy queued frames. CAST completion is awaited; there is no 5-second discard while AI is busy. Reset/room changes discard queued work, and historical/duplicate rows do not rebuild.
- Main free-invention abilities: bounce, thrust, lift, glide, roll and attach. E activates/boards the nearest invention, Space disembarks. Gap geometry remains anchored; gatekeeper traits and invention cards remain intact.
- Root checkout reuses the locally available gitignored sync-key file. Do not print or commit it.
- Live cloud schema check: HTTP 503 `database is paused`. The database owner must resume it for live browser validation. Simulated-room and deterministic physics checks do not require the cloud.
