import {
  schema,
  table,
  t,
  SenderError,
  type InferSchema,
  type ReducerCtx,
} from 'spacetimedb/server';
import { type Identity, Timestamp } from 'spacetimedb';

// ThinkInk co-op: the Creator (A) draws, the Explorer (B) plays. The Explorer's Unity instance is authoritative for
// physics; this module only syncs the room, the drawing as it happens, finished inventions, Ink and level state.
//
// The drawing reaches the Explorer one of two ways, so the same room works with either input:
//   - digital canvas -> drawing_stroke rows (normalized polylines, batched while A draws)
//   - physical whiteboard (webcam) -> board_frame (one small ink PNG per room, replaced a few times a second)
// Either way, BUILD ends in publish_invention with the cutout PNG and the /interpret physics JSON.

const START_INK = 100;
const MIN_INK_COST = 5; // docs/interpret-contract.md clamps ink_cost to 5..100
const MAX_INK_COST = 100;
const MAX_STROKE_VALUES = 512; // 256 points per batch, flattened x0,y0,x1,y1...
const MAX_FRAME_BASE64 = 96 * 1024;
const MAX_INVENTION_BASE64 = 512 * 1024;
const MAX_PHYSICS_JSON = 16 * 1024;
const CODE_ALPHABET = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789'; // no I/O/0/1

const room = table(
  { name: 'room', public: true },
  {
    roomId: t.u64().primaryKey().autoInc(),
    roomCode: t.string().unique(),
    creatorId: t.identity(),
    explorerId: t.option(t.identity()),
    inkRemaining: t.u32(),
    levelId: t.string(),
    createdAt: t.timestamp(),
    // Set by a client that opened another device for this room (the browser test whiteboard), so that device's
    // room can be found and joined without typing the code.
    invite: t.string().default(''),
  }
);

// Who is in which room, and whether they're connected right now (the connection indicator).
const player = table(
  { name: 'player', public: true },
  {
    identity: t.identity().primaryKey(),
    roomId: t.u64().index('btree'),
    role: t.string(), // creator | explorer
    online: t.bool(),
    // Clients that only make HTTP calls (the browser test whiteboard) are never "online"; they call heartbeat instead,
    // and count as present while this is recent.
    lastSeen: t.timestamp().default(Timestamp.UNIX_EPOCH),
  }
);

const drawingStroke = table(
  { name: 'drawing_stroke', public: true },
  {
    strokeId: t.u64().primaryKey().autoInc(),
    roomId: t.u64().index('btree'),
    captureId: t.string(),
    // Order of batches within the drawing. A pen stroke split across batches repeats its last point as the next
    // batch's first, so every row is a self-contained polyline.
    sequence: t.u32(),
    points: t.array(t.f32()), // normalized 0..1, origin bottom-left, flattened x0,y0,x1,y1...
    createdAt: t.timestamp(),
  }
);

// Physical whiteboard: the latest ink snapshot of the board crop (black ink on white), small and throttled.
const boardFrame = table(
  { name: 'board_frame', public: true },
  {
    roomId: t.u64().primaryKey(),
    captureId: t.string(),
    pngBase64: t.string(),
    width: t.u32(),
    height: t.u32(),
    updatedAt: t.timestamp(),
  }
);

const invention = table(
  {
    name: 'invention',
    public: true,
    indexes: [{ accessor: 'by_room_capture', algorithm: 'btree', columns: ['roomId', 'captureId'] }],
  },
  {
    inventionId: t.u64().primaryKey().autoInc(),
    roomId: t.u64(),
    captureId: t.string(), // one submission; publishing the same capture again updates instead of duplicating
    imagePngBase64: t.string(),
    physicsJson: t.string(), // validated /interpret response, verbatim
    name: t.string(),
    message: t.string(),
    inkCost: t.u32(),
    inkCharged: t.bool(),
    positionX: t.f32(),
    positionY: t.f32(),
    status: t.string(), // pending | ready | spawned | activated | failed
    createdAt: t.timestamp(),
    updatedAt: t.timestamp(),
  }
);

const gameState = table(
  { name: 'game_state', public: true },
  {
    roomId: t.u64().primaryKey(),
    currentLevel: t.string(),
    puzzleCompleted: t.bool(),
    activeInventionId: t.option(t.u64()),
    resetCount: t.u32(), // bumped by reset_level so both clients notice it
    updatedAt: t.timestamp(),
  }
);

// The shared sync key. Private: only the database owner can read it. Unset means no key is required.
const syncKey = table(
  { name: 'sync_key' },
  {
    id: t.u32().primaryKey(), // always 0: one key
    key: t.string(),
    setAt: t.timestamp(),
  }
);

// Identities that presented the sync key. Private. Authorization sticks to the identity, so a device sends the key
// once and stays authorized across reconnects (its token keeps the identity).
const device = table(
  { name: 'device' },
  {
    identity: t.identity().primaryKey(),
    authorizedAt: t.timestamp(),
  }
);

const spacetimedb = schema({ room, player, drawingStroke, boardFrame, invention, gameState, syncKey, device });
export default spacetimedb;

type Ctx = ReducerCtx<InferSchema<typeof spacetimedb>>;
type Role = 'creator' | 'explorer';

function requireRoom(ctx: Ctx, roomId: bigint) {
  const r = ctx.db.room.roomId.find(roomId);
  if (!r) throw new SenderError('No such room.');
  return r;
}

function roleIn(r: { creatorId: Identity; explorerId: Identity | undefined }, who: Identity): Role | null {
  if (r.creatorId.equals(who)) return 'creator';
  if (r.explorerId && r.explorerId.equals(who)) return 'explorer';
  return null;
}

/** Rooms, drawing and gameplay are for devices that presented the sync key (if one is set). */
function requireAuthorized(ctx: Ctx) {
  if (!ctx.db.syncKey.id.find(0)) return;
  if (!ctx.db.device.identity.find(ctx.sender)) {
    throw new SenderError('This device has not presented the ThinkInk sync key.');
  }
}

function requireRole(ctx: Ctx, roomId: bigint, role: Role | 'member') {
  requireAuthorized(ctx);
  const r = requireRoom(ctx, roomId);
  const mine = roleIn(r, ctx.sender);
  if (!mine) throw new SenderError('You are not in this room.');
  if (role !== 'member' && mine !== role) throw new SenderError(`Only the ${role} can do that.`);
  return r;
}

function setPlayer(ctx: Ctx, roomId: bigint, role: Role) {
  const row = { identity: ctx.sender, roomId, role, online: true, lastSeen: ctx.timestamp };
  if (ctx.db.player.identity.find(ctx.sender)) ctx.db.player.identity.update(row);
  else ctx.db.player.insert(row);
}

function newRoomCode(ctx: Ctx) {
  for (;;) {
    let code = '';
    for (let i = 0; i < 4; i++) code += CODE_ALPHABET[ctx.random.integerInRange(0, CODE_ALPHABET.length - 1)];
    if (!ctx.db.room.roomCode.find(code)) return code;
  }
}

function clearDrawingRows(ctx: Ctx, roomId: bigint) {
  for (const s of [...ctx.db.drawingStroke.roomId.filter(roomId)]) ctx.db.drawingStroke.strokeId.delete(s.strokeId);
  ctx.db.boardFrame.roomId.delete(roomId);
}

function clampCost(n: unknown) {
  const v = typeof n === 'number' && Number.isFinite(n) ? Math.round(n) : MIN_INK_COST;
  return Math.min(MAX_INK_COST, Math.max(MIN_INK_COST, v));
}

// ---- Connection --------------------------------------------------------------------------------------------

export const onConnect = spacetimedb.clientConnected(ctx => {
  const p = ctx.db.player.identity.find(ctx.sender);
  if (p) ctx.db.player.identity.update({ ...p, online: true, lastSeen: ctx.timestamp });
});

export const onDisconnect = spacetimedb.clientDisconnected(ctx => {
  const p = ctx.db.player.identity.find(ctx.sender);
  if (p) ctx.db.player.identity.update({ ...p, online: false, lastSeen: ctx.timestamp });
});

/** For HTTP-only clients: "still here". Call every few seconds. */
export const heartbeat = spacetimedb.reducer(ctx => {
  const p = ctx.db.player.identity.find(ctx.sender);
  if (p) ctx.db.player.identity.update({ ...p, lastSeen: ctx.timestamp });
});

// ---- Sync key -------------------------------------------------------------------------------------------------

/** Presents the sync key. Call once after connecting; idempotent. */
export const authorize = spacetimedb.reducer({ key: t.string() }, (ctx, { key }) => {
  const k = ctx.db.syncKey.id.find(0);
  if (!k) return;
  if (key !== k.key) throw new SenderError('Wrong ThinkInk sync key.');
  if (!ctx.db.device.identity.find(ctx.sender)) ctx.db.device.insert({ identity: ctx.sender, authorizedAt: ctx.timestamp });
});

/**
 * Sets or rotates the key. The first key can be set by anyone (do it right after publishing); after that,
 * the current key is required. An empty newKey turns the requirement off. Rotating de-authorizes every device.
 */
export const setSyncKey = spacetimedb.reducer({ newKey: t.string(), currentKey: t.string() }, (ctx, { newKey, currentKey }) => {
  const k = ctx.db.syncKey.id.find(0);
  if (k && currentKey !== k.key) throw new SenderError('Wrong current sync key.');
  if (newKey && newKey.length < 16) throw new SenderError('Use a sync key of at least 16 characters.');
  for (const d of [...ctx.db.device.iter()]) ctx.db.device.identity.delete(d.identity);
  if (k) ctx.db.syncKey.id.delete(0);
  if (newKey) {
    ctx.db.syncKey.insert({ id: 0, key: newKey, setAt: ctx.timestamp });
    ctx.db.device.insert({ identity: ctx.sender, authorizedAt: ctx.timestamp });
  }
});

// ---- Rooms -------------------------------------------------------------------------------------------------

export const createRoom = spacetimedb.reducer({ levelId: t.string() }, (ctx, { levelId }) => openRoom(ctx, levelId, ''));

/** create_room, tagged with an invite the inviting client is watching for (it then joins as Explorer). */
export const createRoomWithInvite = spacetimedb.reducer(
  { levelId: t.string(), invite: t.string() },
  (ctx, { levelId, invite }) => openRoom(ctx, levelId, invite.slice(0, 64))
);

function openRoom(ctx: Ctx, levelId: string, invite: string) {
  requireAuthorized(ctx);
  const r = ctx.db.room.insert({
    roomId: 0n,
    roomCode: newRoomCode(ctx),
    creatorId: ctx.sender,
    explorerId: undefined,
    inkRemaining: START_INK,
    levelId,
    createdAt: ctx.timestamp,
    invite,
  });
  ctx.db.gameState.insert({
    roomId: r.roomId,
    currentLevel: levelId,
    puzzleCompleted: false,
    activeInventionId: undefined,
    resetCount: 0,
    updatedAt: ctx.timestamp,
  });
  setPlayer(ctx, r.roomId, 'creator');
}

/** Joins as Explorer, or rejoins in whatever role you already have. An offline Explorer's seat can be taken over. */
export const joinRoom = spacetimedb.reducer({ roomCode: t.string() }, (ctx, { roomCode }) => {
  requireAuthorized(ctx);
  const r = ctx.db.room.roomCode.find(roomCode.trim().toUpperCase());
  if (!r) throw new SenderError('No room with that code.');
  const mine = roleIn(r, ctx.sender);
  if (mine) {
    setPlayer(ctx, r.roomId, mine);
    return;
  }
  if (r.explorerId) {
    const seated = ctx.db.player.identity.find(r.explorerId);
    if (seated && seated.online && seated.roomId === r.roomId) throw new SenderError('This room already has an Explorer.');
  }
  ctx.db.room.roomId.update({ ...r, explorerId: ctx.sender });
  setPlayer(ctx, r.roomId, 'explorer');
});

/** Back to the lobby. An Explorer frees the seat; a Creator's room stays (nobody can draw in it until they rejoin). */
export const leaveRoom = spacetimedb.reducer(ctx => {
  const p = ctx.db.player.identity.find(ctx.sender);
  if (!p) return;
  const r = ctx.db.room.roomId.find(p.roomId);
  if (r && r.explorerId && r.explorerId.equals(ctx.sender)) ctx.db.room.roomId.update({ ...r, explorerId: undefined });
  ctx.db.player.identity.delete(ctx.sender);
});

// ---- Drawing (Creator) -------------------------------------------------------------------------------------

export const addStroke = spacetimedb.reducer(
  { roomId: t.u64(), captureId: t.string(), sequence: t.u32(), points: t.array(t.f32()) },
  (ctx, { roomId, captureId, sequence, points }) => {
    requireRole(ctx, roomId, 'creator');
    if (points.length < 2 || points.length % 2 !== 0) throw new SenderError('points must be x,y pairs.');
    if (points.length > MAX_STROKE_VALUES) throw new SenderError('Too many points in one batch.');
    const clamped = points.map(v => (Number.isFinite(v) ? Math.min(1, Math.max(0, v)) : 0));
    ctx.db.drawingStroke.insert({ strokeId: 0n, roomId, captureId, sequence, points: clamped, createdAt: ctx.timestamp });
  }
);

export const publishBoardFrame = spacetimedb.reducer(
  { roomId: t.u64(), captureId: t.string(), pngBase64: t.string(), width: t.u32(), height: t.u32() },
  (ctx, { roomId, captureId, pngBase64, width, height }) => {
    requireRole(ctx, roomId, 'creator');
    if (pngBase64.length > MAX_FRAME_BASE64) throw new SenderError('Board frame too large; send a smaller preview.');
    const row = { roomId, captureId, pngBase64, width, height, updatedAt: ctx.timestamp };
    if (ctx.db.boardFrame.roomId.find(roomId)) ctx.db.boardFrame.roomId.update(row);
    else ctx.db.boardFrame.insert(row);
  }
);

export const clearDrawing = spacetimedb.reducer({ roomId: t.u64() }, (ctx, { roomId }) => {
  requireRole(ctx, roomId, 'creator');
  clearDrawingRows(ctx, roomId);
});

/**
 * BUILD. Call it with an empty physicsJson as soon as the PNG exists (status "pending", the Explorer can show
 * "A is building..."), then again with the /interpret response (status "ready", or "failed" if it wasn't ok).
 * The same (room, captureId) only ever makes one row; a ready or later invention is never reopened.
 * The Ink cost is read from physics_json.ink_cost and clamped here, not taken as an argument.
 */
export const publishInvention = spacetimedb.reducer(
  {
    roomId: t.u64(),
    captureId: t.string(),
    imagePngBase64: t.string(),
    physicsJson: t.string(),
    positionX: t.f32(),
    positionY: t.f32(),
  },
  (ctx, args) => {
    requireRole(ctx, args.roomId, 'creator');
    if (!args.captureId) throw new SenderError('captureId is required.');
    if (args.imagePngBase64.length > MAX_INVENTION_BASE64) throw new SenderError('Drawing PNG too large.');
    if (args.physicsJson.length > MAX_PHYSICS_JSON) throw new SenderError('physics_json too large.');

    let status = 'pending';
    let name = '';
    let message = '';
    let inkCost = 0;
    if (args.physicsJson) {
      let parsed: Record<string, unknown>;
      try {
        parsed = JSON.parse(args.physicsJson);
      } catch {
        throw new SenderError('physics_json is not valid JSON.');
      }
      name = typeof parsed.name === 'string' ? parsed.name : '';
      message = typeof parsed.message === 'string' ? parsed.message : '';
      const ok = parsed.status === 'ok' && Array.isArray(parsed.components) && parsed.components.length > 0;
      status = ok ? 'ready' : 'failed';
      inkCost = ok ? clampCost(parsed.ink_cost) : 0;
      if (!ok && !message) message = 'The drawing could not be interpreted.';
    }

    const existing = [...ctx.db.invention.by_room_capture.filter([args.roomId, args.captureId])][0];
    if (existing) {
      if (existing.status !== 'pending') return; // already delivered: a retry or reconnect must not duplicate it
      ctx.db.invention.inventionId.update({
        ...existing,
        imagePngBase64: args.imagePngBase64 || existing.imagePngBase64,
        physicsJson: args.physicsJson,
        name,
        message,
        inkCost,
        positionX: args.positionX,
        positionY: args.positionY,
        status,
        updatedAt: ctx.timestamp,
      });
      return;
    }
    ctx.db.invention.insert({
      inventionId: 0n,
      roomId: args.roomId,
      captureId: args.captureId,
      imagePngBase64: args.imagePngBase64,
      physicsJson: args.physicsJson,
      name,
      message,
      inkCost,
      inkCharged: false,
      positionX: args.positionX,
      positionY: args.positionY,
      status,
      createdAt: ctx.timestamp,
      updatedAt: ctx.timestamp,
    });
  }
);

// ---- Gameplay (Explorer) -----------------------------------------------------------------------------------

/**
 * The Explorer accepts a ready invention: charges its Ink once and marks it spawned. Spawn the object when the row
 * turns "spawned". Not enough Ink marks it failed instead, so both screens show why.
 */
export const spendInk = spacetimedb.reducer({ inventionId: t.u64() }, (ctx, { inventionId }) => {
  const inv = ctx.db.invention.inventionId.find(inventionId);
  if (!inv) throw new SenderError('No such invention.');
  const r = requireRole(ctx, inv.roomId, 'explorer');
  if (inv.inkCharged || inv.status !== 'ready') return;
  if (r.inkRemaining < inv.inkCost) {
    ctx.db.invention.inventionId.update({
      ...inv,
      status: 'failed',
      message: `Not enough Ink (${inv.inkCost} needed, ${r.inkRemaining} left).`,
      updatedAt: ctx.timestamp,
    });
    return;
  }
  ctx.db.room.roomId.update({ ...r, inkRemaining: r.inkRemaining - inv.inkCost });
  ctx.db.invention.inventionId.update({ ...inv, inkCharged: true, status: 'spawned', updatedAt: ctx.timestamp });
});

const NEXT_STATUS: Record<string, string[]> = {
  spawned: ['activated', 'failed'],
  activated: ['activated', 'spawned', 'failed'], // re-activations and "fuel ran out" are fine
};

export const updateInventionState = spacetimedb.reducer(
  { inventionId: t.u64(), status: t.string() },
  (ctx, { inventionId, status }) => {
    const inv = ctx.db.invention.inventionId.find(inventionId);
    if (!inv) throw new SenderError('No such invention.');
    requireRole(ctx, inv.roomId, 'explorer');
    if (!(NEXT_STATUS[inv.status] ?? []).includes(status)) {
      throw new SenderError(`Can't go from ${inv.status} to ${status}.`);
    }
    ctx.db.invention.inventionId.update({ ...inv, status, updatedAt: ctx.timestamp });
    const gs = ctx.db.gameState.roomId.find(inv.roomId);
    if (gs && status === 'activated') {
      ctx.db.gameState.roomId.update({ ...gs, activeInventionId: inventionId, updatedAt: ctx.timestamp });
    }
  }
);

export const setPuzzleCompleted = spacetimedb.reducer(
  { roomId: t.u64(), completed: t.bool() },
  (ctx, { roomId, completed }) => {
    requireRole(ctx, roomId, 'explorer');
    const gs = ctx.db.gameState.roomId.find(roomId);
    if (gs) ctx.db.gameState.roomId.update({ ...gs, puzzleCompleted: completed, updatedAt: ctx.timestamp });
  }
);

/** Either player: full Ink, empty board, no inventions. resetCount tells the other client to reset too. */
export const resetLevel = spacetimedb.reducer({ roomId: t.u64() }, (ctx, { roomId }) => {
  const r = requireRole(ctx, roomId, 'member');
  ctx.db.room.roomId.update({ ...r, inkRemaining: START_INK });
  clearDrawingRows(ctx, roomId);
  for (const inv of [...ctx.db.invention.by_room_capture.filter(roomId)]) ctx.db.invention.inventionId.delete(inv.inventionId);
  const gs = ctx.db.gameState.roomId.find(roomId);
  if (gs) {
    ctx.db.gameState.roomId.update({
      ...gs,
      puzzleCompleted: false,
      activeInventionId: undefined,
      resetCount: gs.resetCount + 1,
      updatedAt: ctx.timestamp,
    });
  }
});
