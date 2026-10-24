# ThinkInk: The Great Gap — polished demo mode

**One problem, many inventions.** Draw with the mouse, optionally write a one-line action, then select **BRING TO LIFE**. DeerAI classifies the sketch using a small reusable physics vocabulary. The original cutout still comes from the drawing.

## Scope

- Core abilities: `support`, `bounce`, `thrust`, `lift`.
- The Unity `attach`/riding behavior is internal to powered inventions, not a separate AI label in demo mode.
- Three bounded strengths for thrust/bounce to reduce inconsistent results.
- Powered lift travels horizontally as well as rising; insufficient carrying capacity makes a small flyer sink.
- A single fixed gap level, not a catalogue of puzzle solutions.
- Existing broader prototype abilities remain available with `THINKINK_DEMO=0` (restart backend).
- UI: redesigned invention canvas, polished reveal, compact objective/timer/BUILD status, failure feedback.

## Install and start

1. Apply `thinkink-great-gap-polished-INCREMENTAL.patch` to the previous optimized + UI-cleanup project.
2. `python3 -m pip install Pillow` (if not installed).
3. `python3 tools/thinkink_backend.py` — uses the existing `BITDEER_API_KEY`, with `THINKINK_DEMO=1` by default.
4. In Unity open `Assets/Scenes/Level.unity`, press Play, select DRAW.

## Demo script (~90 sec)

1. Draw a rocket skateboard (or rocket), type `Blast me across the gap`. Click **BRING TO LIFE**. Get near it, press E.
2. F5. Draw a large spring, type `Launch me up and to the right`. Jump onto the spring.
3. If time permits, F5. Try a tiny bird and then an enormous wide-wing bird, both with `Carry me across`. The former should fail to lift the 6kg demo rider; the large one can lift the rider.

## Reality checks

- Test in the actual Unity Editor: the code was statically inspected and Python tests pass, but Editor compilation and real Bitdeer responses cannot be verified here.
- Weak-versus-strong flying uses simplified **game balance**, not real-world aerodynamics. Large wings must be reflected in the returned `width_m`.
- Unsupported drawings become solid cutouts rather than a fake working mechanic.
- `ink_cost` is provisional. Multiplayer/SpacetimeDB integration is not part of this patch.
- Existing scene geometry, single-player gameplay, camera and cutout pipeline are unchanged.
