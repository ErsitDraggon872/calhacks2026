# ThinkInk code review — supplied ZIP, Oct 24 2026

This review refers to the Unity code in this ZIP, not to unseen GitHub commits or a working build. Static inspection only; no Unity Editor available for play-mode or EditMode testing.

## Overall

There is real engineering behind the current demo: Unity project with prefabs/scenes generated via editor scripts, a side-view Paper Mario-style world, movement and checkpoints, webcam ink extraction, paper textures, grid-based colliders, mock capture client, and EditMode tests. It is not an empty scaffold. However, the **core pitch of AI-chosen physics for any drawing is not implemented in this revision**.

## Key problems, highest priority first

1. **Backend/Unity contract mismatch — HIGH, not fixed in this patch.** `docs/interpret-contract.md` specifies `POST /interpret` and JSON with status, components, width_m and cost. Runtime uses `IVisionClient.CaptureAsync` + `GetLabelAsync` and flat `LabelResponse`, and `HttpVisionClient` sends `/capture` and `/label/{id}`. `NoInterpreter` is used with the webcam. `RunManager.Cast` ignores labels for free builds; `Invention.Create` only creates the solid collider. The new AI response is neither requested nor consumed. Plan: add explicit `/interpret` request/response classes and an HTTP interpreter, then implement parameterized `bounce` and `thrust` in gameplay.
2. **Pre-scripted gap bypasses invention physics — HIGH, not fixed.** `GapSocket.OnCaptured` calls `Solve()` for *any* non-empty capture. It enables an invisible deck collider no matter what the player drew, making the tutorial look solved without the drawn invention being structurally responsible. Acceptable for a scripted intro, but judges should eventually see genuine drawing-based solutions.
3. **3D gameplay versus 2D contract — DESIGN RISK, not fixed.** `PlayerController` uses `CharacterController` and 3D physics; `Invention` creates 3D `BoxCollider` components. The newer design document is Paper Mario-style depth movement, not a literal 2D Unity Physics2D game. Define exact mapping for bounce/thrust (XY plane? depth lock? support during ride) before merging backend response data.
4. **Shape matching is approximate — DESIGN RISK, not fixed.** `PaperTexture.MakePaper` deliberately fills enclosed outline holes and dilates strokes. `SilhouetteBoxes` downsamples the silhouette into coarse box colliders (default ~0.15 m). This does not literally preserve the drawn geometry. Reasonable for the demo, but should not be described as pixel-perfect.
5. **AI safety, cost and state validation are pending — HIGH, not fixed.** There is no runtime decoding/validation of `/interpret` values, no Ink accounting, and no replay-safe response handling for that new endpoint. The older label path has `run_id` checks. When wiring the new endpoint, enforce the JSON field and component limits, request/response IDs, 12-second server timeout and deterministic cost formula.
6. **Webcam free-build memory retention — FIXED.** `WebcamVisionClient` previously added every captured board image to `pending`; `RunManager` does not request a label for free builds, so those images stayed allocated. Free builds now skip pending storage.
7. **Per-capture cancellation-token source lifetime — FIXED.** `RunManager.Cast` created a linked `CancellationTokenSource` each capture and did not dispose it. The updated code cleans it in a `finally` after the request resolves.
8. **Mock client empty-config edge case — FIXED.** `MockVisionClient.DescribeNext` could divide by zero when `mockDrawings` was empty; it now reports no drawings configured.
9. **Camera-heavy logic for digital tests — ADDRESSED.** Rather than forcing digital ink through brightness normalization and connected-component webcam logic, `DigitalInkExtractor` crops deterministic black strokes directly, preserving the full white board and transparent cutout separately.
10. **No root README in supplied archive — ADDRESSED.** This patch adds `README.md` and an editor menu for switching to Digital mode.

## What this patch added

- `CaptureSource.Digital` without renumbering existing capture sources.
- `DigitalDrawingCanvas`: black ink, white pad, Clear/BUILD, P toggle, player input lock while sketching.
- `DigitalInkExtractor`: transparent tight crop; bottom-left coordinates; deterministic B/W behavior.
- `DigitalVisionClient`: adapts the sketch into the existing capture interface without a webcam or Python server.
- `Game/Use Digital Drawing (No Webcam)` editor menu, plus a webcam restore menu.
- EditMode test cases for digital crop, orientation, alpha and empty board.
- Small defensive cleanup in old webcam/mock/cancellation paths.

## Suggested next implementation order

1. Get the digital-drawing demo working in the Unity Editor and standalone target.
2. Plug an `HttpInterpreter` / `/interpret` call into the capture path, returning the **actual documented** full JSON response. The system prompt alone is not sufficient.
3. Apply returned `width_m` to the cutout and add bounce and thrust behaviors with tests (and decide how movement interacts with 3D depth).
4. Implement approval, Ink cost, and robust server timeout/errors.
5. Make the gap challenge actually exercise the invention's collider/physics rather than only toggling an invisible deck.

Do not rewrite the entire project for a short hackathon. The existing scene and paper-cutout machinery are valuable; integrate incrementally.
