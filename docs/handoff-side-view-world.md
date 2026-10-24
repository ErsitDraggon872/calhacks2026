# Handoff: verify step 2 (side-view world) in Unity

For a Claude Code session on Wesley's PC, which has Unity 6000.3.18f1. Step 2 was written in a cloud session **without a Unity Editor**. None of the Unity-facing C# has been compiled, the builders have never run, and nothing has been played. Your job is to get it compiling, passing and building, then hand the Play-mode checks to Wesley.

- Branch: `wesley/side-view-world`, based on `main`. Push fixes here.
- Draft PR: https://github.com/ErsitDraggon872/calhacks2026/pull/1 (into `main`). Its description has the full change list and the "Verify in Unity" checklist.
- Design: `docs/superpowers/specs/2026-10-23-draw-to-physics-design.md` ("World and camera", "Demo level and flow", "Build order" step 2). How the Unity half is put together: `docs/unity-implementation-notes.md`.

## What step 2 changed

- `Assets/Scripts/Player/CameraFollow.cs` (new, replaces the deleted `IsoCameraFollow`): perspective side camera with `Tight`/`Wide` framings and `SetFraming`/`Snap`.
- `Assets/Scripts/Player/PlayerController.cs`: Space jump latched from `GameInput.Jump.performed`, no body rotation, a `visual` child flips on X, `FacingSign`, `Radius`.
- `Assets/Scripts/Inventions/`: `SilhouetteBoxes` (pure), `InventionPlacement` (pure, includes `FitInside`), and `Invention` (MonoBehaviour: cutout visual plus one BoxCollider per silhouette box).
- `Assets/Scripts/Core/RunManager.cs`: free builds when no unsolved socket is in range (`socket_id` `"free"`), `inventionMaterial` field, inventions cleared on F5, camera reset to `Tight`.
- `Assets/Scripts/Sockets/GapSocket.cs`: single face at the deck's centre, `drawHint` quad, `Wide` framing on solve. `Cutout.AddParallelFace` is removed.
- `Assets/Scripts/Core/GameConfig.cs`: `jumpHeight`, `inventionSize`, `inventionDepth`, `inventionCell`. `GameInput.cs`: `Jump`.
- Editor:
  - `GameAssetBuilder`: PlayerPaper and DrawHere textures saved as PNGs in `Assets/Prefabs/Textures`, `AlphaClip` materials, paper Player prefab, perspective CameraRig.
  - `MockDrawingGenerator`: `Character()` and `DrawHereOutline()`.
  - `SceneBuilder.BuildLevel`: the new greybox; B starts at x 7.5.
- Tests: `Assets/Scripts/Tests/EditMode/SilhouetteBoxesTests.cs` and `InventionPlacementTests.cs`. These and `RunStateTests` already pass under .NET 8 with stand-in Unity types. The rest have never run.

New `.cs` files were committed **without `.meta` files**. Unity will generate them; commit them.

## Steps (Editor closed, PowerShell, repo root)

```powershell
git fetch origin; git checkout wesley/side-view-world; git pull
$U = "C:\Program Files\Unity\Hub\Editor\6000.3.18f1\Editor\Unity.exe"   # adjust if installed elsewhere

# 1. Compile + EditMode tests. Don't add -quit with -runTests.
& $U -batchmode -projectPath . -runTests -testPlatform EditMode -testResults TestResults.xml -logFile test.log

# 2. Regenerate assets and the level (the order matters: prefabs before the scene).
& $U -batchmode -quit -projectPath . -executeMethod CalHacks.EditorTools.GameAssetBuilder.BuildAll -logFile assets.log
& $U -batchmode -quit -projectPath . -executeMethod CalHacks.EditorTools.SceneBuilder.BuildLevelForced -logFile level.log
& $U -batchmode -quit -projectPath . -executeMethod CalHacks.EditorTools.SceneBuilder.BuildSandbox -logFile sandbox.log

# 3. Windows build -> Build/Windows/CalHacks2026.exe
& $U -batchmode -quit -projectPath . -executeMethod BuildScript.PerformBuild -logFile build.log
```

After each step, read the log:
- `error CS` lines are compile errors.
- `[Builder] ... has no serialized field` means a builder `Fields(...)` name doesn't match a field.
- Exceptions and `[Build] Failed` speak for themselves.

Fix, re-run that step, and carry on. If a project-local `unity` CLI or the unity-agent-plugin is available, it's fine to use that instead of raw `Unity.exe`.

### Known risks to check first
- **Compile:** `PlayerController` imports both `System` and `UnityEngine` (watch for ambiguous names). `SilhouetteBoxes` uses value-tuple dictionary keys. Check `CharacterController.Move` → `CollisionFlags`, `FindFirstObjectByType`, and the `TextureImporter` properties in `GameAssetBuilder.SaveTexture`.
- **Stale references before the rebuild:** `Level.unity`, `Sandbox.unity` and `CameraRig.prefab` point at the deleted `IsoCameraFollow` script. Missing-script warnings are expected until step 2. They're not errors to hand-fix; never hand-edit `.unity`, `.prefab` or `.asset` YAML.
- **Textures:** after `BuildAll`, `Assets/Prefabs/Textures/PlayerPaper.png` and `DrawHere.png` should exist, and the `PlayerPaper` and `DrawHere` materials should reference them. If the Player quad comes out 0.9 m wide (the fallback), the texture wasn't read back.
- **`InputSystem.actions`** must be non-null when `PlayerController.OnEnable` subscribes to `Jump`. `RunManager` already relies on the same thing.

## Rules

- All scene and prefab changes go through `Assets/Editor/GameAssetBuilder.cs` and `SceneBuilder.cs`; never hand-edit the YAML.
- Match the existing style: namespace `CalHacks`, summary comments, `[SerializeField]` fields.
- Never skip or disable a test to get green.
- Commit on `wesley/side-view-world`, with messages ending `Co-Authored-By: Claude <noreply@anthropic.com>` (use whichever model attribution your session gives you). Commit the generated `.meta` files, the regenerated prefabs, `Level.unity`, `Sandbox.unity` and the new textures. Don't commit `Build/`, `Library/` or logs.
- Push, then update PR #1's checklist with what passed.

## Done

- Compiles with no errors, all EditMode tests pass, the builders run clean, and the Win64 build succeeds.
- Then give Wesley the **Play-mode checks** (Mock capture, `Level.unity`). They need a human at the keyboard:
  - tight opening shot on the gap with the dashed outline and "Too far to jump!"
  - A/D/W/S movement and sprite flip; the camera doesn't swing on depth
  - a single 1.2 m jump
  - Enter at the gap bridges it and blends to the wide shot
  - a hub free build appears beside B and is standable at several Z depths
  - the high ledge is reachable with one invention plus a jump
  - the island is reachable by stacking, and the flag wins
  - F5 resets inventions, camera and gap
- Tune what he reports with the knobs:
  - `CameraFollow` distances and offsets
  - `GameConfig.jumpHeight`
  - the ledge height in `SceneBuilder.BuildLevel`
