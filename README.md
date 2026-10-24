# In-game drawing input (this patched version)

This version provides two **on-screen buttons in the running Unity Game view** at the top-right:
**DRAW** (mouse-based black ink on white) and **WEBCAM** (physical whiteboard).
You do **not** have to use the Unity Editor menu to switch sources.

1. Open `Assets/Scenes/Level.unity` with **Unity 6000.3.18f1**, then press Play. Digital DRAW is the initial mode in this ZIP.
2. Click and drag the left mouse button on the white canvas (click **DRAW** at top-right to reopen it).
3. Click **BUILD** on the pad (or press Enter) to place the transparent cutout in the world.
4. Press **P** to reopen/close the drawing pad. **Clear** erases the sketch.
5. To switch back, click **WEBCAM**; the game attempts to use an external USB webcam. Press **P** to view/hide camera preview.
6. **F5** resets the level. You can switch modes without restarting Play mode, except during an in-flight capture.

Important: this implements digital drawing and existing silhouette/colliders, but it **does not** connect `/interpret` to Bitdeer yet. In free build, objects are support-only. Built-in Editor mode selections (Game menu) still work as initial defaults.

# ThinkInk / Cal Hacks 2026 — Unity project

Unity project files, plus a **black-and-white digital drawing mode** for no-webcam tests. This copy is based on the ZIP supplied for review; it is **not** a Unity build or a Git checkout with history.

## Get it running locally

1. Install **Unity Hub** and Unity Editor **6000.3.18f1** (see `ProjectSettings/ProjectVersion.txt`), with your platform's build support if you want to export a standalone game.
2. Unzip the project anywhere on your machine, and in Unity Hub choose **Add project from disk** and select this project's root (the folder containing `Assets`, `Packages`, and `ProjectSettings`).
3. Let Unity import packages and compile. Open **`Assets/Scenes/Level.unity`**.
4. Choose **Game > Use Digital Drawing (No Webcam)** from the Unity menu. Do this **outside Play mode**. It saves the setting in `Assets/Resources/GameConfig.asset`.
5. Press **Play**. The white drawing pad opens automatically. Draw with **left mouse drag**, click **BUILD** or press **Enter**. The pad closes as Unity places the paper cutout. Press **P** to reopen it and draw the next object. Click **Clear** to erase without building.
6. For debugging use **F3** (overlay), **F5** (reset run, clears sketch and reopens the pad), **G** (toggle build ghost). Move Player B with **WASD**, jump with **Space**.

You can also select `Digital` on the **Capture Source** field of the `Assets/Resources/GameConfig.asset` inspector, or pass `-capture=digital` to a standalone executable. To restore the original webcam source, choose **Game > Use Webcam Drawing**.

## What works today

- Mouse-created pure black lines on a white 960×540 digital canvas.
- Transparent PNG cutout cropped tightly around the drawing; original full-board pixels are preserved in memory for future AI interpretation.
- Original paper-cutout rendering and approximated shape-based 3D colliders in the game world.
- The initial gap and free builds run using the existing gameplay paths.
- No webcam, Bitdeer API key, or Python service is needed for this **support-only** smoke test.

## Not yet wired up

- The current Unity runtime still calls the old `IVisionClient.CaptureAsync` + `GetLabelAsync` path. Digital and webcam modes use `NoInterpreter` (unknown). No actual request is sent to `POST /interpret`.
- `bounce`, `thrust`, `width_m`, `ink_cost`, approval, and the other fields in `docs/interpret-contract.md` are not yet consumed by the Unity gameplay code. The game currently creates only solid cutouts.
- The project is a side-view **3D world** using 3D colliders and depth movement (Paper Mario style), not a native Unity Physics2D scene.
- The shape is **approximately** preserved: the paper generator dilates ink and fills outlines, while the collider is built from grid-aligned boxes.

## For the backend teammate

The local/digital capture contains enough information to eventually populate `POST /interpret`:
`board_png_base64`, `cutout_png_base64`, `cutout_x`, `cutout_y`, `cutout_width`, `cutout_height`, `ink_px`, `capture_id`, `run_id`, `socket_id`, and `goal`.
Those HTTP request/response DTOs and the interpreter integration are **future work**, not implemented in this patch. See `docs/interpret-contract.md`.

## Tests

New EditMode tests: `Assets/Scripts/Tests/EditMode/DigitalInkExtractorTests.cs`.
In the Unity Editor: **Window > General > Test Runner > EditMode > Run All**.
The ZIP was reviewed and statically checked, but the Unity Editor was not available in the review environment, so compile/play-mode verification must be done on your computer.

## DeerAI integration (incremental patch)

The AI-enabled revision adds a local Python `POST /interpret` backend, Unity request/response
classes, and `InventionPhysics` (support / bounce / thrust). See
[`docs/DEERAI-SETUP.md`](docs/DEERAI-SETUP.md) for launch commands, environment variables,
and the known MVP limitations. The previous solid-only notes above describe the old version.
