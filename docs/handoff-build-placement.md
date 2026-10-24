# Handoff: make free-build placement and the build ghost actually work

For a Claude Code session on Wesley's PC (Unity 6000.3.18f1, Editor usually open). Wesley tried the build ghost at the high ledge and said "that's just not it". Your job is to redesign where free builds land, and how the player sees that before pressing Enter, so the high ledge reads at a glance.

- Branch: `wesley/side-view-world`. Push here. Draft PR: https://github.com/ErsitDraggon872/calhacks2026/pull/1 (into `main`).
- Background:
  - The design is `docs/superpowers/specs/2026-10-23-draw-to-physics-design.md`. See "Build order": step 2 is current; step 4's Approve screen with ghost placement is where this was meant to end up.
  - `docs/unity-implementation-notes.md`, section "Free builds".

## What the player sees now (the problem)

B walks up to the plum high ledge and "Too high to climb!" shows. The orange dashed ghost outline mostly disappears behind the ledge: only its left edge and part of its top show, poking out around the ledge. It doesn't say "draw something here and it'll be your step". The build itself behaves the same way. Underneath:

1. **Placement ignores the level.** A free build lands at B's feet height and **B's depth (Z)**, beside B on the side B faces, 0.4 m clear of B (`InventionPlacement.BottomCentre`). Nothing checks for level geometry. B can stand anywhere in z −0.5..3 in front of the ledge, which occupies z 0.5..3. So a build often lands **inside the ledge**: its colliders overlap the ledge's.
2. **The ghost is buried.** `BuildGhost` draws the front face of the largest build (3 × 2.5 m) at `B.z − inventionDepth/2 − 0.03`. Once B is deeper than about z 1.5, that face is still behind the ledge's front face (z 0.5) and gets hidden. In Wesley's screenshot you can only see the left edge and part of the top. URP `Unlit` has no `_ZTest` property, so it can't simply draw on top.
3. **"Beside B, on the side B faces" is hard to read.** The player has to work out facing and depth, and the build appears a fixed 0.4 m away whether that is useful or not.

## What exists (keep, change or replace)

| Piece | Where | Notes |
|---|---|---|
| Free build | `Assets/Scripts/Core/RunManager.cs` around line 225 (the decision) and 273–278 (placement + `Invention.Create`) | When no unsolved socket is within `castRange` (5 m), BUILD makes an invention; `socket_id` is `"free"`. |
| Placement math (pure) | `Assets/Scripts/Inventions/InventionPlacement.cs` | `BottomCentre`, `GhostBottomCentre`, `FitInside`. Tests are in `Assets/Scripts/Tests/EditMode/InventionPlacementTests.cs`; one of them, `EveryFittedBuildLandsInsideTheGhost`, pins the ghost/build contract. |
| Invention | `Assets/Scripts/Inventions/Invention.cs` | `Create(paper, size, bottomCentre, depth, mat, parent, cell)` makes a camera-facing `Cutout` visual plus one `BoxCollider` per `SilhouetteBoxes` rect, `depth` deep and centred on the Z it's given. |
| Ghost | `Assets/Scripts/Inventions/BuildGhost.cs` (on the Player prefab, with child quad `BuildGhost`) | It shows while B is inside a `HintZone` with `showBuildGhost` and `RunManager.BuildGhostVisible` is on (G toggles; action `BuildGhost` in `Assets/InputSystem_Actions.inputactions`). |
| Hint zones | `Assets/Scripts/Core/HintZone.cs` | They register with `BuildGhost` and expose `Contains(point)`. Only the level's "Hint High Ledge" zone has `showBuildGhost` (`SceneBuilder.AddHint(..., buildGhost: true)`). |
| Ghost art | `GameAssetBuilder.BuildMaterials` (`BuildGhost` material from `MockDrawingGenerator.DrawHereOutline(682, 568)`), `GameAssetBuilder.BuildPlayer` (the quad) | The same dashed orange plus pencil as the gap's `DrawHere`. |
| Config | `GameConfig`: `inventionSize` (3, 2.5), `inventionDepth` 2, `inventionCell` 0.15, `jumpHeight` 1.2, `castRange` 5 | |

Paper cutouts have no holes now (`PaperTexture.MakePaper` fills enclosed areas), so every drawing's collider is solid. B climbs ≤ 0.3 m steps (`CharacterController.stepOffset`) and jumps 1.2 m.

## Level geometry around the problem (`SceneBuilder.BuildLevel`)

- **Hub floor:** x 14.6..40, z −2..3, top at y 0. The front edge has an invisible wall at z −2.
- **High ledge:** x 24..30, **z 0.5..3** (the back layer), top at **y 2.3**. The front lane z −2..0.5 stays open in front of it.
- **Hint High Ledge zone:** x 22.5..30, y 0..2, z −0.5..3. It doesn't cover the front lane z −2..−0.5, and it ends at y 2, so it switches off once B is on top.
- **Floating island:** x 34..40, z −1.5..2.5, y 3..4, with the Exit and flag. It's meant to be reached by stacking inventions or building off the ledge. It has no hint zone.
- **Camera:** looks along +Z (yaw 0, pitch 17°). Anything at greater Z than a solid front face is hidden behind it.
- Spec "done when" for step 2: the high ledge is reachable with one invention plus a jump, and the island is reachable by stacking.

## Directions to consider (pick and justify; these aren't settled)

- **Build slots at obstacles.** A designer places a slot in front of the ledge: a fixed bottom-centre and box in the open front lane, its top roughly 1.1–2 m so one jump reaches the ledge. A free build made inside that obstacle's hint zone snaps to the slot, and the ghost is drawn there, always fully visible. Elsewhere, free builds keep today's placement. This is the gap's socket idea without labels, and it reads at a glance.
- **Fixed build lane.** Always build in the front lane at a fixed Z (for example z −1), never at B's depth. Then clamp X so the box doesn't overlap level colliders (`Physics.OverlapBox` against non-trigger, non-invention geometry; slide away from the obstacle). The ghost lives in that lane, in front of everything.
- **Approve step now.** After the capture, show the actual drawing as the ghost (not the generic box), let A/D nudge it along a few snap points, and Enter confirms. This is spec step 4; it's bigger but it's where this was always headed.
- **Ghost visibility,** whatever the placement: keep it in front of geometry. Either place it where nothing occludes it, or use a custom unlit shader with `ZTest Always`. Hand-write a tiny `.shader` file and reference it from `GameAssetBuilder`. Don't hand-edit the `.mat`.

Whatever you choose:
- Keep the invariant that the drawing lands inside the ghost, and keep or adapt the test.
- Keep G as the toggle.
- Keep stacking on the island working: building while standing on an invention puts the next one at that height.

Ask Wesley before committing to a direction if it changes how the game plays (for example, removing free placement).

## How to work here (learned the hard way)

- **Drive the open Editor with the `unity` CLI**, not batch `Unity.exe`; the project is open in it. Run `unity status` first. Pass `--caller plugin --skill unity-cli` on every `unity command`.
  - Rebuild: `unity command script eval --code 'CalHacks.EditorTools.GameAssetBuilder.BuildAll(); CalHacks.EditorTools.SceneBuilder.BuildSandbox(); CalHacks.EditorTools.SceneBuilder.BuildLevelForced(); return "ok";'`. Prefabs must be built before scenes. Make sure Play mode is **stopped** first, or `NewScene` throws.
  - Compile: `unity command script recompile`, then poll `script recompile_status` until it says `completed`.
  - Tests: `unity command test run --mode EditMode --format json`. There are 47 now, all green.
- **Play-mode checks:**
  - Before playing, turn on `unity command editor.playmode set_run_in_background --enable true`, because an unfocused Editor stops ticking. Turn it back off afterwards.
  - Teleport B: disable its `CharacterController`, set `transform.position`, enable it again.
  - Press a key for real: `InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.G)); InputSystem.Update();`, then queue an empty `KeyboardState` and update again.
  - Screenshot: `unity command capture game_view --source camera --save_path Temp/x.png`. It lands in `Assets/Temp/`; delete that folder with `AssetDatabase.DeleteAsset("Assets/Temp")` before committing.
- **If Play mode is already running when you start,** it's probably Wesley's session. Ask before stopping it or moving B.
- **Never hand-edit `.unity`, `.prefab`, `.mat` or `.asset` YAML.** All scene and prefab changes go through `Assets/Editor/GameAssetBuilder.cs` and `SceneBuilder.cs`.
- Match the code style: namespace `CalHacks`, summary comments, `[SerializeField]` fields, pure logic in testable static classes.
- **Commits:** commit generated `.meta` files, prefabs, scenes and textures. Don't commit `Build/`, `Library/`, `Assets/Temp/`, logs, or the `ProjectSettings/Packages/com.unity.pipeline/EditorPipelineConfig.json` line-ending churn. End messages with your session's Co-Authored-By line.

## Done when

- **At the high ledge,** from any depth B can stand at, the ghost is fully visible and shows a spot that works. Drawing almost anything and pressing Enter makes a step that B can jump from onto the ledge. Check this in Play mode with screenshots from the front lane and the back of the lane.
- **No overlaps:** a free build never overlaps level geometry.
- **The island** is still reachable (stacking or building off the ledge).
- **Toggles and resets:** G still toggles the ghost, and F5 clears inventions.
- **Tests:** EditMode tests are green, with placement covered by pure tests.
- **Docs:** update the "Free builds" section of `docs/unity-implementation-notes.md` and the PR description.
- **Wesley's sign-off:** send him before and after screenshots from the same spot as his (B at about x 23, standing at the back of the hub, facing the ledge).
