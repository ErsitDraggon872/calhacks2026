# ThinkInk — Digital Drawing → Bitdeer → Unity

## 1. Apply patch

From the project root, while Unity is closed:

```sh
git apply --check ~/Downloads/thinkink-deerai-INCREMENTAL.patch
git apply ~/Downloads/thinkink-deerai-INCREMENTAL.patch
```

Do **not** apply if `--check` fails. This patch targets the project's **DRAW + WEBCAM selector** revision.

## 2. Run backend (separate Terminal)

The local Python service uses only Python's standard library and listens at `http://127.0.0.1:8000`.

```sh
export BITDEER_API_KEY='YOUR_ACTUAL_BITDEER_KEY'
export BITDEER_MODEL='Qwen/Qwen3.8-27B'
python3 tools/thinkink_backend.py
```

Keep the API key on your machine, never in C# scripts, Git or screenshots. Confirm health in another Terminal with `curl http://127.0.0.1:8000/health`.

If the model rejects `response_format`, try `export BITDEER_USE_SCHEMA=0` and restart backend. Backend still validates and clamps the JSON itself. The exact vision model and structured-output support should be tested with the live account.

### Offline smoke test (no API key)

```sh
THINKINK_FAKE=1 python3 tools/thinkink_backend.py
```

The mock always returns a **rocket regardless of drawing**. It verifies wiring and physics only; it does not do vision recognition. Turn fake mode off for the demo.

## 3. Unity

Use Unity **6000.3.18f1**, open `Assets/Scenes/Level.unity`, press Play, select **DRAW**, draw in black, hit **BUILD**. The drawing PNG goes to localhost `/interpret`; read the interpreted object in Console `[DeerAI]`. The game spawns the *original extracted paper cutout* at the model's `width_m` and adds its physics behaviors. Use **E** when near button-activated inventions. Trampolines bounce on landing.

The `GameConfig` asset contains `enableAiPhysics` (enabled by default in code) and `inventionGoal`. If your existing serialized asset has different values, inspect the Inspector. If the server is off, BUILD shows an error and preserves the drawing rather than silently spawning a support-only object.

You may also choose **WEBCAM**; Unity captures and crops the image before sending it to the same backend.

## Important implementation boundaries

* AI DRAW and WEBCAM currently create **free builds**; the older socket-based `/capture` + `/label` puzzle logic remains for Mock/Service modes. AI builds do not automatically complete socket objectives.
* Unity uses `CharacterController` + 3D colliders; `InventionPhysics` adapts bounce, E-thrust and moving-platform riding to that architecture. Tuning and Unity Play-mode testing are still needed.
* No A-side approve/reject menu or spending Ink budget has been added. Backend sends an `ink_cost` for display. The team design specifies a **5–100 cost range but no exact formula** in `docs/interpret-contract.md`; the backend uses a clearly labeled provisional formula in `ink_cost()` until the team supplies the official one.
* The unit-test command is `python3 -m unittest discover -s tools -p 'test_thinkink_backend.py'`. No real Bitdeer requests are made by these tests.
* New input data is validated twice: by Python and Unity. Secret keys are never sent to the Unity client. Don't expose this development server publicly.
* This local endpoint is intended for **Unity Editor or standalone** on the same computer; browser/WebGL will need a deployed HTTPS backend and CORS configuration, and must not attempt to call the browser user's localhost.
