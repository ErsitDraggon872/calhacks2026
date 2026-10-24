# ThinkInk — Universal Ability Demo (incremental patch)

This is an **experimental hackathon demo**, built on the working Draw/Webcam + DeerAI + Unity integration. It preserves the existing paper-cutout pipeline, 3D compound BoxColliders, CharacterController player and Python backend. It does **not** convert the project to Rigidbody2D.

## Apply the patch safely (Mac)

1. **Stop Unity Play mode and stop the Python backend** (Ctrl+C).
2. Back up your project folder, especially if teammates changed it.
3. From inside your existing `calhacks2026-main` directory, run:

   ```bash
   git apply --check ~/Downloads/thinkink-universal-abilities.patch
   git apply ~/Downloads/thinkink-universal-abilities.patch
   ```

   If the check fails, do not use `--force`. It was generated against the last gameplay-fixes patch (`thinkink-colliders-rocket-popup-recognition.patch`) on top of the working curl backend.
4. Restart the same working local backend with your environment key: `python3 tools/thinkink_backend.py`.
5. Reopen Unity; open `Assets/Scenes/Level.unity`, press Play, select DRAW, sketch, BUILD, then approach the object and press **E**. **Space** jumps off a riding invention; **E** also detaches when no toggle/hold behavior is active.
6. When in doubt about image recognition, choose the corresponding intent hint in the digital drawing interface (AUTO remains the default).

Keep `BITDEER_API_KEY` in the terminal environment, **not** in Unity or project files. Restart the backend after changing its prompt.

## What this patch adds

Abilities (`support` plus up to **three** more): `bounce`, `thrust`, `lift`, `glide`, `roll`, `attach`, `tether`, and optional `hinge`, `push_pull`.

Activation modes recognized: `always`, `contact`, `button`, `hold`, `toggle`. Pressing **E** activates nearby inventions, holding E sustains hold-mode motor power, and the next E toggles toggle-mode motor power. Space dismounts.

Top-level `width_m` plus the 15 `attributes` fields (16 total): `mass_kg`, `load_capacity_kg`, `friction`, `elasticity`, `drag`, `stability`, `durability`, `force`, `speed`, `direction_deg`, `duration_s`, `energy_capacity`, `carry_capacity_kg`, `range_m`, `cooldown_s`. Values are validated on both the backend and Unity sides. Old five-field rocket/trampoline JSON remains accepted.

The extra attributes do not create a physically accurate material simulation. They are bounded gameplay approximations: mass and carrying capacity affect lift, load capacity/durability trigger fragile bridge failure, friction/elasticity set collider material, drag affects Rigidbody damping, speed caps movement, force/energy/duration tune motor behavior, range limits tether/push-pull, cooldown gates reuse, and stability influences rotation. The attribute `direction_deg` is a shared semantic hint; actual movement angles still come from individual ability components.

### New component parameters (same **eight** keys on every component)

- `lift`: `acceleration` 2–25, `angle_deg` 90; carrying depends mostly on `attributes.carry_capacity_kg` against a **6-kg gameplay rider load**.
- `glide`: `launch_speed` 1–6 means **maximum downward speed**, not launch impulse. Lower = slower descent.
- `roll`: `acceleration` 2–25 and left/right `angle_deg`, 0–10 s duration; simplified powered sliding along the ground, not real wheel axle joints.
- `attach`: player can board and travel with the invention. The paper cutout remains a moving platform; this is not a grappling/claw rig.
- `tether`: a single configurable distance joint to a fixed overhead point; visible line for the rope.
- `hinge` (stretch): a pivot about Z with an angle limit.
- `push_pull` (stretch): an E-triggered force on a nearby player in a specified direction, not general magnetic physics for all movable objects.

`support`, `bounce`, and `thrust` continue to use their previous conventions.

## Quick demo acceptance checks

| Drawing | Expected mechanics | Test |
| --- | --- | --- |
| Rocket with flame pointing down-left | support + thrust | Press E; rider boards and rocket moves up-right for limited fuel |
| Trampoline with up-right arrow | support + bounce | Landing launches up-right |
| Tiny bird | support + lift + attach + glide | Press E; weak carry capacity should make it descend with player |
| Giant broad-winged eagle | support + lift + attach + glide | Press E; high carry capacity should rise with player |
| Umbrella/parachute | support + glide + attach | Press E; descent capped |
| Wheeled cart/skateboard | support + roll + attach | Press E; cart travels along ground |
| Rope swing | support + tether + attach | Press E; fixed overhead tether constrains motion |
| Hinged seesaw | support + hinge | Push/collide; board pivots (rough demonstration) |
| Magnet/piston | support + push_pull | Press E near it; nearby character gets a directional impulse |

Real DeerAI inference depends on the clarity of the drawing. The AUTO hint will not always detect unfamiliar objects; choose the intent selector when testing mechanics.

### Offline mock responses (no Bitdeer calls)

Start `THINKINK_FAKE=1 python3 tools/thinkink_backend.py`. It still returns a rocket for ordinary goals; special **goal text** can exercise additional code paths: `tiny bird`, `giant bird`, `umbrella`, `spring`, `rope swing`, `skateboard`. These are deterministic wiring fixtures, **not** image recognition. For an HTTP request containing `goal: "tiny bird"`, the fake backend replies with a weak-LIFT JSON. The goal used by Unity defaults to `get across the gap`; to exercise a specific fake sample, modify `GameConfig.inventionGoal` temporarily.

## Constraints and known shortcomings

- This is a prototype, not a validated simulation or a proof that all combinations work. Physics depends on level scale, collider quality and art geometry.
- Colliders are an approximation made from many box colliders; arrows/text may still be included in the cutout shape. A separate annotation layer is the real fix.
- The overhead tether anchor is synthetic; for a polished level it should snap to an actual ceiling/anchor point.
- Hinges and roll are simplified approximations; rolling wheels/rigging or physical magnets are **not** implemented.
- `ink_cost` remains a provisional backend formula. Confirm pricing with your team before a scored demo.
- The 12-second Bitdeer timeout is unchanged; the larger schema/output may require disabling API-level JSON Schema (`BITDEER_USE_SCHEMA=0`) if your model rejects it.
- Backend automated tests run locally. Unity code was statically reviewed but **not compiled or Play-mode tested here**; resolve any Unity-specific issues from its Console before the live demo.
