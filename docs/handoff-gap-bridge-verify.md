# Handoff: verify the ink-only gap bridge and "no floating" in the Editor

For a Claude Code session on Wesley's PC (Unity 6000.3.18f1, Editor open). A cloud session wrote this change without Unity, so **nothing has been compiled, rebuilt, tested or played**. Your job: get it compiling, rebuild, get the tests green, check it in Play mode, fix whatever is broken within scope, and push.

- Branch: `claude/nifty-faraday-g4nw5a`. Push here. Draft PR into `main`: https://github.com/ErsitDraggon872/calhacks2026/pull/2. Its description is the full change list and checklist; tick the boxes as you verify.
- Background: `docs/superpowers/specs/2026-10-23-draw-to-physics-design.md` (see "Shape, behaviour and placement") and `docs/unity-implementation-notes.md` ("Free builds", "The demo level").

## What changed (two commits)

1. **The gap bridge is only solid where there's ink.** `GapSocket.CreateCutout` builds an `Invention` (new exact-size `Invention.Create(paper, bottomCentre, size, …)` overload) stretched across the gap: 4.9 m wide, top 0.4 m above the ledges (`railHeight`), 3.2 m deep (`depth`). The hidden `Deck` collider is gone (`GameAssetBuilder.BuildGapSocket`).
   - A capture shows `Rails` but doesn't solve. The next BUILD in range replaces the bridge.
   - The gap solves once B's x passes the far ledge (14.75), while a bridge stands.
   - F1 (`GapSocket.ForceSolve`) rebuilds from the fallback plank and solves.
   - F5 destroys the bridge and shows the `DrawHere` hint again. The hint hides while a bridge stands, because they share the plane z 0.
2. **B stands on the drawn edge, not above it.**
   - `SilhouetteBoxes` makes 0.15 m-wide columns whose tops and bottoms are exact to the pixel.
   - While B stands on an invention, `PlayerController` slides B's picture (`Visual`) to the drawing's depth (`planeSnapSpeed`). Otherwise, under the 17° camera, B near the back of a deep collider looked up to 0.47 m above the drawing.
   - `GameAssetBuilder.BuildPlayer` lowers the `Paper` quad by the CharacterController skin plus the paper under the drawn feet (`PaperTexture.LowestInkRow`), about 0.16 m.

## Steps

1. `unity status`. If Play mode is running, it's probably Wesley's: ask before stopping it.
2. **Compile:** `unity command script recompile`, then poll `script recompile_status` until it reports `completed`. Fix any errors. The overloads in `Invention.Create` differ only in the order of `Vector2 size` and `Vector3 bottomCentre`, so look there first if a call is ambiguous.
3. **Rebuild**, with Play mode stopped and prefabs before scenes: `unity command script eval --code 'CalHacks.EditorTools.GameAssetBuilder.BuildAll(); CalHacks.EditorTools.SceneBuilder.BuildSandbox(); CalHacks.EditorTools.SceneBuilder.BuildLevelForced(); return "ok";'`. Then check:
   - `GapSocket.prefab` has no `Deck` child, Depth is 3.2, and Solution Collider is empty.
   - In `Player.prefab`, the `Visual/Paper` y is about 0.16 m lower than 0.95.
4. **Tests:** `unity command test run --mode EditMode --format json`. Everything must be green. The new or changed tests are in `SilhouetteBoxesTests` (stretched break, full bar, pixel-exact top, diagonal steps) and `PaperTextureTests.LowestInkRowSkipsPaperBorder`. The algorithm and those tests were checked in a Python port, so a failure is more likely a C# or Unity detail than the maths.
5. **Play `Level.unity`.** Turn on `unity command editor.playmode set_run_in_background --enable true` first, and turn it off after. Mock vision: keys 1–5 pick bridge, sun, fish, cat, scribble; Enter is BUILD. B starts at x 7.5, in range of the gap.
   - [ ] **Bridge (mock 1):** B crosses. B probably has to hop onto it (see "Ask Wesley"). The gap solves past x 14.75, and the Checkpoint 1 reveal still fires at x 17.
   - [ ] **A drawing with a break drops B, and a redraw replaces it.** No mock drawing has a break, so make one with `script eval`: a texture of two separate blobs run through `PaperTexture.MakePaper`, then call the public `gap.ApplyCapture(new CalHacks.CaptureResponse(), paper)` on the `GapSocket`. B must fall through the middle and respawn. A normal BUILD from the start ledge then replaces it.
   - [ ] **F1** at the gap gives a solid plank and solves the gap. **F5** clears the bridge, hides the rails and shows the hint.
   - [ ] **High-ledge slot builds** still work. After the gap is solved, BUILD in the hub doesn't bind to it.
   - [ ] **No floating.** On the bridge, put B at z −1.4, 0 and +1.4; on a free build, at its front and back. Also use a curved drawing (sun or cat) and the scribble. B's drawn feet must sit on the drawn top edge in every screenshot. On level floors, the feet must touch the ground. Note how the picture's slide in depth reads (B grows or shrinks up to about 10%) and tune `planeSnapSpeed` if it pops.
   - How:
     - **Teleport B:** disable its `CharacterController`, set `transform.position`, enable it again.
     - **Press a key:** `InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.Enter)); InputSystem.Update();`, then queue an empty `KeyboardState` and update again.
     - **Screenshot:** `unity command capture game_view --source camera --save_path Temp/x.png`. It lands in `Assets/Temp/`; delete that folder with `AssetDatabase.DeleteAsset("Assets/Temp")` before committing.
     - Pass `--caller plugin --skill unity-cli` on every `unity command`.
6. Fix what's broken, re-run the tests, and send Wesley before/after screenshots of B on the bridge at the back of its depth.

## Ask Wesley (don't decide alone)

- **The hop onto the bridge.** The bridge's top is 0.4 m above the ledges, which is higher than the 0.3 m step offset. If he wants B to walk on, set `railHeight` to about 0.25 and move the `0.4f` in `BuildGapSocket`'s `DrawHere` position to match.
- **Overlaps still open** (waiting on his answer):
  - Slot builds don't check for other inventions in the slot (`RunManager.PlaceInvention`, slot branch).
  - A redraw while B stands on the bridge can spawn colliders around B. There's no `LiftTo` for the gap.
  - The bridge overlapping each ledge by 0.15 m is intentional (no seam).

## Rules

- **Never hand-edit `.unity`, `.prefab`, `.mat` or `.asset` YAML.** Scene and prefab changes go through `Assets/Editor/GameAssetBuilder.cs` and `SceneBuilder.cs`.
- Match the code style: namespace `CalHacks`, summary comments, `[SerializeField]` fields, pure logic in testable static classes.
- **Commit** the regenerated prefabs, scenes, textures and `.meta` files. Don't commit `Build/`, `Library/`, `Assets/Temp/`, logs, or the line-ending churn in `ProjectSettings/Packages/com.unity.pipeline/EditorPipelineConfig.json`. End commit messages with your session's Co-Authored-By line.
- **Done when:** it compiles, the EditMode tests are green, every checkbox above and in the PR is verified (or the failure is fixed), the regenerated assets are pushed, and the PR description is updated with what you saw.
