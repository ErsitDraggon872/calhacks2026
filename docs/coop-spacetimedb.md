# Local browser drawing rooms (default)

Press Play in the existing Unity Editor, then F4 > **Create room + open drawing pad**. A browser room is created and Unity automatically joins as Explorer. The four-letter room code pairs the browser with the game. **No ThinkInk password or key file is required for this same-PC mode.**

The Editor starts `tools/start-local-board.ps1` automatically. It runs SpacetimeDB 2.13.0 on `http://127.0.0.1:3000`, with runtime/data/logs in gitignored `.utmp/spacetime/`. Nothing is installed system-wide. The browser and Unity use the local endpoint by default; identities are stored separately for each server. The loopback listener is for one PC; tablet/LAN deployment needs explicit network configuration later.

Manual startup: `powershell -NoProfile -ExecutionPolicy Bypass -File tools/start-local-board.ps1`, or **Game > Start Local Drawing Server**. Add `-Publish` after changing the server module. The first run downloads the runtime and publishes the module, so it can take a minute; progress and errors go to `.utmp/spacetime/setup.log` (the server's own output is in `server.log` and `server-error.log` next to it). The recognition backend remains separate on port 8000; local room connectivity itself needs no cloud account.

Cloud use remains available with `-thinkink-server=https://maincloud.spacetimedb.com` in Unity and the matching browser `server` query parameter. The cloud's existing custom shared-key check is unchanged. The older co-op/cloud reference below describes that optional mode.

---

# Co-op over SpacetimeDB (Creator draws, Explorer plays)

Player A (Creator) draws; Player B (Explorer) plays the level and gets A's drawings as inventions. They're on two
computers, joined by a 4-letter room code. SpacetimeDB carries the room, the drawing as it happens, finished
inventions, Ink, and resets. The Explorer's Unity is authoritative for physics; nothing syncs per frame.

- **Server module:** `server/spacetimedb/src/index.ts` (TypeScript), database **`thinkink`**, published to the local server by `tools/start-local-board.ps1` (and to Maincloud for cloud mode).
- **Unity side:** `Assets/Scripts/Multiplayer/`, all on the `Coop` prefab (already in `Level` and `Sandbox`).
- **Generated bindings:** `Assets/Scripts/Multiplayer/Bindings/` (don't edit; regenerate, see below).

## Playing it

1. Both machines run the game. The co-op panel (top right, F4 hides it) shows the connection.
2. A clicks **Create room (Creator)** and reads out the code. B types it and clicks **Join (Explorer)**.
3. A draws. B sees it live in the panel's whiteboard preview.
4. A presses **BUILD** (Enter or the button). B gets a "building..." note, then the invention appears next to them,
   and the Ink drops on both screens.
5. B's F5 resets the room for both: full Ink, empty board, no inventions.

**Testing alone:** in the lobby, click **Create room + open drawing pad**. A browser tab
(`tools/whiteboard/index.html`) opens as the Creator of a new room, and the game joins it as the Explorer by
itself. Drag on the tab to draw; BUILD sends the drawing as a support. Each tab is its own player. The page uses the
HTTP API, so it stays "online" through a `heartbeat` call every 4 s. **Leave** (in the panel) returns you to the lobby.

Two clients on one machine need different identities: start the second with `-thinkink-slot=B`. Other flags:
`-thinkink-server=<url>` and `-thinkink-db=<name>`. Without a slot flag, each client keeps its identity (and so its
room and role) across restarts.

## The digital board (one game machine)

This is the mode for the demo route. The browser whiteboard stands in for the physical whiteboard and the game runs
the same BUILD it runs on the webcam, so the gap, the gatekeeper and free builds all work, with real `/interpret`
readings. Only one machine runs Unity.

- **Turn it on:** `GameConfig.captureSource = DigitalBoard`, or `-capture=digital` on a build.
- **Who draws:** the judge, in `tools/whiteboard/index.html?mode=board`. The page is the room's Creator, and the game
  joins as the Explorer. Testing on one machine: the co-op panel's **Create room + open drawing pad** opens the page in
  board mode and joins by itself. From a tablet (not set up yet): the local server listens on loopback only, so
  the tablet needs a server it can reach, either the cloud or the laptop's server started with a LAN
  `--listen-addr`. Serve the page over the LAN (`python -m http.server 8080` in `tools/whiteboard`), open
  `http://<laptop-ip>:8080/index.html?mode=board&server=<that server>`, then type the page's room code into the
  co-op panel and click **Join**. The cloud also asks for the key once.
- **BUILD:** the page's BUILD publishes the drawing as a `pending` invention and nothing else. The game sees that row
  and runs `RunManager.Cast()`: `DigitalBoardFrameSource` draws that capture's strokes onto a white 992×572 board
  (the page's 960×540 canvas inside a 16 px border, so a drawing that runs to the page's edge isn't taken for the
  board's frame), then `WebcamVisionClient` cuts the ink out with the whole frame as the crop, `/interpret` says
  what it is, and the socket in range (or a free build) gets it. If BUILD is cooling down or a reading is in
  flight, browser BUILDs wait in order (up to 8 waiting), each with its own immutable frame until CAST completes. Unity's own BUILD key also works, on the latest strokes.
- **Clearing:** the page clears itself right after its BUILD (the game keeps its own copy of that capture's strokes
  until that BUILD has completed). CLEAR clears both. The game's F5 resets the room (`reset_level`) and the page notices
  `game_state.reset_count` change and empties its canvas.
- **Not used in this mode:** `ExplorerInventions` ignores the room's inventions (the `pending` rows only trigger
  BUILD and stay `pending` until a reset), and Ink isn't spent.
- **Status:** the debug overlay's vision line names the room (`digital board ABCD`), and a BUILD that can't get a
  board fails with why: the SpacetimeDB link, the sync key (cloud only), or no room yet.
- **If the network is down:** rooms still work, since the server is local. Real recognition needs the backend's
  internet; `THINKINK_FAKE=1` on the backend stands in for it.

## Two machines: the drawing can come from either input

The flow is the same for both inputs. Only the Creator's capture differs:

| | Digital canvas | Physical whiteboard |
|---|---|---|
| Live preview | `StrokeSync`: the canvas calls `BeginStroke` / `AddPoint` / `EndStroke` with 0–1 coordinates (origin bottom-left). Points are batched about 16×/s. | `BoardFrameSync`: the webcam board crop, ink-extracted and shrunk to 240 px, sent only when the ink changes (≤3×/s). |
| BUILD capture | An `IDrawingCapture` the canvas implements, or the built-in fallback that rasterises the synced strokes. | `WebcamDrawingCapture`: the same `BoardCamera` + `InkExtractor` cut-out as single-player CAST. |
| Turn it on | Default when the capture source isn't Webcam. | `GameConfig.captureSource = Webcam` or `-capture=webcam` on the **Creator's** machine. `boardCrop`, `inkThreshold` and `minInkPx` apply as usual. |

`CreatorBuild.input` (on the `Coop` prefab) can force `Webcam` or `Strokes`; `Auto` follows the capture source.

The Explorer's machine doesn't need a camera. In a room, the single-player CAST (`RunManager.Cast`) is switched off,
so drawings only come from the Creator.

## Plug-in points (for the canvas, /interpret and physics code)

- **Canvas → live strokes:** call `StrokeSync.Instance.BeginStroke(p)`, `AddPoint(p)`, `EndStroke()`.
  CLEAR is `CreatorBuild.Instance.ClearDrawing()`.
- **Canvas → BUILD image:** implement `IDrawingCapture.Capture(out error)` (ink-on-transparent PNG, cropped to the
  ink) and set `CreatorBuild.Instance.Capture = yourCanvas` in `Start`.
- **/interpret:** implement `IPhysicsInterpreter.InterpretAsync(png, captureId, ct)` and return the response JSON
  as-is (docs/interpret-contract.md). Set `CreatorBuild.Instance.Interpreter`. Until then, `SupportOnlyInterpreter`
  makes every drawing a 10-Ink platform. The Bitdeer key stays in the Python service; Unity only sees the JSON.
- **Physics on the Explorer:** subscribe to `ExplorerInventions.Spawned += (invention, remote) => ...`.
  `remote.PhysicsJson` holds the components. Call `remote.ReportActivated()` when E fires it and `remote.ReportIdle()`
  when it's spent; both screens see the status.

## Sync key (cloud only)

The local server has no key: its module has no `sync_key` row, so `authorize("")` lets any local device in, and
neither Unity nor the browser asks for one. Everything below is about the cloud database.

Rooms are closed to devices that haven't presented the shared sync key. The key isn't in git: it's in
`thinkink.key` at the repo root (gitignored). Give it to teammates privately.

- **Unity** sends it on connect. It looks for `-thinkink-key=<key>`, then the `THINKINK_KEY` environment variable,
  then a `thinkink.key` file in the project root (Editor) or next to the game (builds; for a macOS `.app`, put it
  beside the `.app`). The panel shows `key ok / checking / wrong / missing`.
- **Test whiteboard tab:** Unity passes the key in the link's `#fragment`, which browsers never send to a server. A
  tab opened any other way asks for it once.
- **`tools/fake_creator.sh`** reads `thinkink.key`.

Server side: `authorize(key)` records the caller's identity in a private `device` table. Every room reducer
(create, join, draw, BUILD, Ink, reset) rejects identities that aren't in it. The key is in a private `sync_key`
table that only the database owner can read. To rotate it (this de-authorizes every device):

```bash
spacetime call thinkink set_sync_key '"<new key, 16+ chars>"' "\"$(cat thinkink.key)\""   # then update thinkink.key
```

The key only controls who can write. The room, stroke and invention tables are still public, so anyone who knows
the database name can read them.

## What the server enforces

- Only the Creator can add strokes or board frames, clear, or publish inventions. Only the Explorer can spend Ink or
  change an invention's state. Roles come from the caller's identity, never from arguments.
- Ink cost is read from `physics_json.ink_cost` and clamped to 5–100. The client never passes a cost. Ink is charged
  once, when the Explorer accepts (`spend_ink`). If there isn't enough, the invention is marked failed with a reason.
- One invention per `(room, capture_id)`. BUILD publishes it as `pending` (image only), then `ready` with the JSON.
  Publishing a capture again never makes a second row. The Explorer spawns on `spawned`, once per invention id, so
  a reconnect doesn't duplicate anything.
- Lifecycle: `pending → ready → spawned ⇄ activated`, or `failed`. `reset_level` deletes the room's strokes, frame and
  inventions, refills Ink, and bumps `game_state.reset_count`; that's what makes the other client reset.
- Limits: 256 points per stroke batch, 96 KB board frames, 512 KB invention PNGs, 16 KB physics JSON.

All tables are public, and any client can read any room. That's fine for a demo, but it isn't private.

## Changing the server

Locally, `powershell -NoProfile -ExecutionPolicy Bypass -File tools/start-local-board.ps1 -Publish` rebuilds and
republishes the module. For the cloud:

```bash
cd server/spacetimedb && npm install          # once
cd .. && spacetime publish thinkink --server maincloud --yes   # needs `spacetime login`; owner-only after the first publish
spacetime generate --lang csharp --out-dir Assets/Scripts/Multiplayer/Bindings --module-path server/spacetimedb   # from the repo root
```

A breaking schema change needs `--delete-data=always`, which wipes every room. Useful checks:
`spacetime sql thinkink "SELECT * FROM invention"` and `spacetime logs thinkink -f`.

## Main invention abilities (integrated 2026-10-24)

The default input is the browser board (`DigitalBoard`). Use **Create room + open drawing pad** in F4's panel; it opens a browser tab, never a second Unity Editor. Play mode starts the local room server; no key is needed.

Free builds now run Andy's main components: bounce, thrust, lift, glide, roll, and attach. **E** activates or boards the nearest eligible drawing within 2.5 m; E again disembarks button-operated rides, and **Space** jumps off. Hold/toggle activation is honored for powered parts. Small lift capacity cannot support the 6 kg gameplay rider; gliders cap loaded descent. Walking off releases the rider. Gap bridges stay static and gatekeeper acceptance still uses identity traits. Rope/tether, hinges, push/pull and ink spending remain deferred in this mode.

Runtime regression fixtures (Play mode, same Editor): `unity command script eval_file --file tools/verify_invention_abilities.cs`. Read `Temp/ability-regressions-result.txt`; every Boolean result must be True. This temporarily creates test inventions and returns the player to spawn. It needs no cloud or recognition backend. Stop Play mode before running EditMode tests.
