# Handoff: browser digital board and main invention physics

## Current integration

Work is on `wesley/interpret-backend`. Henry's co-op branch and the digital-board adaptation are merged, preserving the current puzzle banners and invention cards. Andy's `feature/thinkink-full-update` through `bb955d7` is integrated selectively: the browser pad remains the input, with his main free-invention physics and latest vehicle improvements. His local Unity drawing pad, alternate response classes and full HUD redesign are not imported.

The flow is browser strokes -> SpacetimeDB -> Unity board snapshot/ink extraction -> Python `POST /interpret` -> puzzle socket or free invention. SpacetimeDB syncs the drawing, room and reset; it does not perform AI recognition.

## Use one Unity Editor

- Project: `C:/Users/wesle/UnityGames/calhacks2026`, Unity **6000.3.18f1**.
- Run `unity status --format json` and target the existing root-project Editor with `--project-path`. Never automatically run `unity open`, open the old worktree in Unity, or launch a batch Editor alongside it.
- Stop Play mode before EditMode tests. Allow domain reloads to finish before the next command. After a branch merge, reload scenes changed on disk in the existing Editor.
- Default input: `CaptureSource.DigitalBoard` (`-capture=digital`). In Play mode, F4 > **Open browser drawing pad** opens Henry's browser page with `mode=board` and joins Unity as Explorer. No second Unity client is needed. Webcam and replay remain selectable via GameConfig/launch flags.
- Start the recognition backend separately (see `DEERAI-SETUP.md`). Keys remain local and uncommitted. The root checkout has the gitignored sync-key file copied from the existing worktree.

## Behavior

- Each browser BUILD keeps an immutable frame in FIFO order, up to 8 waiting drawings. Immediate CLEAR does not erase queued work. CAST is awaited instead of assuming capture completes synchronously; busy AI requests no longer discard a drawing after 5 seconds.
- Historical and duplicate pending rows never rebuild. F5 discards queued frames and resets the room. Room changes discard queued work and cancel the old board request. Source failures identify the digital board instead of reporting a camera error.
- Free inventions support bounce, thrust, lift, glide, roll and attach. E activates/boards the nearest eligible drawing within 2.5 m. Space jumps off; walking off releases the rider. Loaded gliders respect their descent limit, and weak lifts cannot carry the 6 kg gameplay rider.
- Andy's updated whole-word vehicle fallback makes recognized support-only cars/carts drive. Roll-only engines run for at least 15 seconds and use target horizontal velocity. Powered lift has modest horizontal travel and capacity feedback.
- Gap bridges remain anchored and solid only where ink exists. Gatekeeper acceptance still uses edible/hostile traits. Current invention cards remain and show E controls for rideable builds.
- Rope/tether, hinges, push/pull, ink economy and the alternate Unity drawing interface remain deferred. Explicit backend rejection remains rejection.

## Verified on Wesley's PC

- Unity compilation clean; **111/111 EditMode tests pass**. Backend **21/21 tests pass**.
- Simulated browser room through the real capture/HTTP pipeline with a fake backend: static ink bridge at gap; bridge refused at gatekeeper; apple accepted. Immediate CLEAR retained the submitted drawing. Reset restored sockets and emptied pending captures.
- Runtime deterministic checks all pass: parked-rocket activation, loaded glide (-2.48 m/s for a 2.5 limit), walking-off release, thrust, strong lift, cart drive, vehicle fallback, 15-second engine, button bounce, weak E-boarded lift and weak contact-boarded lift.
- Reusable runtime fixtures: in Play mode, `unity command script eval_file --file tools/verify_invention_abilities.cs`. Results are in `Temp/ability-regressions-result.txt`; all Boolean checks must be True. This uses no cloud or AI and temporarily moves the player to a test area before returning to spawn.
- Validation reused the existing Editor (PID 32524, port 7800). No additional Editor was launched.

## Still blocked / next checks

The Maincloud `thinkink` database schema endpoint returned **HTTP 503: database is paused** on 2026-10-24. The database owner must resume it before live browser/tablet checks can pass. Do not claim the live network flow has been verified.

Once resumed, verify browser BUILD at gap and gatekeeper, then F5 clearing the browser canvas. Real webcam and real AI recognition were not rechecked in this integration.

Keep local keys, temporary captures, import-only material changes, Run In Background and Pipeline override noise out of commits. Existing CameraFollow/CameraRig edits belong to Wesley's separate camera work.
