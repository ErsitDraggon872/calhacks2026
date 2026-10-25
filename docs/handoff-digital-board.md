# Handoff: browser digital board and main invention physics

## Current integration

Work is on `wesley/interpret-backend`. Henry's co-op branch and the digital-board adaptation are merged, preserving the current puzzle banners and invention cards. Andy's `feature/thinkink-full-update` through `bb955d7` is integrated selectively: the browser pad remains the input, with his main free-invention physics and latest vehicle improvements. His local Unity drawing pad, alternate response classes and full HUD redesign are not imported.

The flow is browser strokes -> SpacetimeDB -> Unity board snapshot/ink extraction -> Python `POST /interpret` -> puzzle socket or free invention. SpacetimeDB syncs the drawing, room and reset; it does not perform AI recognition.

## Use one Unity Editor

- Project: `C:/Users/wesle/UnityGames/calhacks2026`, Unity **6000.3.18f1**.
- Run `unity status --format json` and target the existing root-project Editor with `--project-path`. Never automatically run `unity open`, open the old worktree in Unity, or launch a batch Editor alongside it.
- Stop Play mode before EditMode tests. Allow domain reloads to finish before the next command. After a branch merge, reload scenes changed on disk in the existing Editor.
- Default input: `CaptureSource.DigitalBoard` (`-capture=digital`). In Play mode, F4 > **Create room + open drawing pad** opens Henry's browser page with `mode=board` and joins Unity as Explorer. No second Unity client is needed. Webcam and replay remain selectable via GameConfig/launch flags.
- Start the recognition backend separately (see `DEERAI-SETUP.md`). A ThinkInk sync key is not needed for same-PC rooms. Existing cloud credentials remain local and uncommitted.

## Behavior

- Demo free builds place automatically, with no placement confirmation or paused result card. The dashed outline
  shows the size envelope before recognition and the exact fitted bounds for a waiting drawing; red means blocked.
  It uses the same resolver as spawning and is hidden while riding. G toggles it.
- Drawings ignore one another for placement and physics, but remain solid to the explorer and terrain. Unsupported
  space is skipped during the nearby search. If terrain leaves no room, the interpreted drawing is retained and
  automatically retried beside B as they move; the HUD says "Drawing ready! Move somewhere clear to build."
  No new capture or AI call is needed. Reset, room change and winning discard waiting drawings.
- Deferred co-op inventions retain server metadata through a completion callback; duplicate updates do not queue
  another copy, and removed rows are invalidated before spawning.
- Autonomous walkers, hoppers and flyers sweep their ink colliders before kinematic movement. They turn at walls
  and stop at ceilings instead of clipping; powered E rides continue using Rigidbody physics. Live fixture:
  `tools/verify_creature_collisions.cs`, results in `Temp/creature-collision-results.txt`.

- Each browser BUILD keeps an immutable frame in FIFO order, up to 8 waiting drawings. Immediate CLEAR does not erase queued work. CAST is awaited instead of assuming capture completes synchronously; busy AI requests no longer discard a drawing after 5 seconds.
- Historical and duplicate pending rows never rebuild. F5 discards queued frames and resets the room. Room changes discard queued work and cancel the old board request. Source failures identify the digital board instead of reporting a camera error.
- Free inventions support bounce, thrust, lift, glide, roll and attach. E activates/boards the nearest eligible drawing within 2.5 m. Boarding with E locks B on: no walking, and Space is the only way off. Just landing on a moving drawing carries B
while they stand on it; they can walk off, and E there boards and locks. While riding, E only runs the ride's own function (go / stop / hold) and the HUD shows just that and "space jump off". Loaded gliders respect their descent limit, and weak lifts cannot carry the 6 kg gameplay rider.
- Andy's updated whole-word vehicle fallback makes recognized support-only cars/carts drive. Roll-only engines run for at least 15 seconds and use target horizontal velocity. Powered lift has modest horizontal travel and capacity feedback.
- Gap bridges remain anchored and solid only where ink exists. Gatekeeper acceptance still uses edible/hostile traits. Current invention cards remain and show E controls for rideable builds.
- Rope/tether, hinges, push/pull, ink economy and the alternate Unity drawing interface remain deferred. Explicit backend rejection remains rejection.

## Same-PC room flow

Unity and the browser default to `http://127.0.0.1:3000`. Entering Play starts the local drawing server (`Assets/Editor/LocalBoardServer.cs` runs `tools/start-local-board.ps1` hidden); **Game > Start Local Drawing Server** does it by hand. **Create room + open drawing pad** creates the browser room and Unity joins it as Explorer. The four-letter code shows in both places. There is no ThinkInk key prompt or file for this flow; the cloud database and its shared-key check are unchanged and still reachable with `-thinkink-server=` / `?server=`.

The local runtime (SpacetimeDB 2.13.0), database data, signing files and logs live in gitignored `.utmp/spacetime/`; nothing is installed system-wide. The script logs to `.utmp/spacetime/setup.log`, because the domain reload on entering Play would kill anything reading its output from Unity. It holds a setup lock, so repeated Play presses don't start a second setup; if port 3000 is taken by something else, the log says so with the tail of `server-error.log`. `-Publish` rebuilds the module locally. The server binds only to loopback, so a tablet needs extra setup (see `coop-spacetimedb.md`). The AI recognition service is separate on port 8000.

## Verified on Wesley's PC (2026-10-24)

- **Wesley's manual run in the Editor, all passed:** room created with no password; live preview; bridge at the gap; bridge refused, then food accepted at the gatekeeper; car, rocket and trampoline with E and Space; F5 clears both the game and the browser; back-to-back BUILDs aren't lost.
- Compilation clean; **117/117 EditMode** (including six local/remote endpoint cases), run after the last key-file change. Backend **21/21**.
- Entering Play started the helper, `setup.log` ended in Ready, and Unity reported `local=True link=Online` with no error.
- Runtime deterministic checks all pass: parked-rocket activation, loaded glide (-2.48 m/s for a 2.5 limit), walking-off release, thrust, strong lift, cart drive, vehicle fallback, 15-second engine, button bounce, weak E-boarded lift and weak contact-boarded lift. Fixture: in Play mode, `unity command script eval_file --file tools/verify_invention_abilities.cs`, results in `Temp/ability-regressions-result.txt` (all Booleans must be True). It moves the player to a test area and back, so don't run it while someone is playing.
- Validation reused the one open Editor. No second Editor was launched.

## Decisions already made

- Keep Henry's browser pad as the input; Andy's in-Unity drawing canvas, alternate `InterpretResponse`/request classes and full HUD redesign stay out. Existing types were extended additively.
- Explicit backend rejection stays rejection (Andy's string-based rejected-to-ok heuristic was not imported).
- E starts parked vehicles even when the player stands on them; support contact expires after walking off; loaded glide accounts for gravity and rider mass; contact lifts register rider load.
- Deferred: rope/tether, hinge, push/pull, ink economy.

## Not yet checked

- Real webcam capture and real AI recognition on this branch (the manual run used the backend as it was running; recognition misses are backend/model issues, separate from room sync).
- Tablet/LAN drawing against the local server.

Keep local keys, `.utmp/`, temporary captures, Run In Background / pipeline override noise and the build's `InputSystem_Actions` `preloadedAssets` entry out of commits.
