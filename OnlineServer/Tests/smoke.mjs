// Dependency-free protocol smoke test for Node 22+.
// Start the server, then run: node Tests/smoke.mjs

const endpoint = process.env.DUST_SERVER_URL ?? "ws://127.0.0.1:5077/ws";
const suffix = Date.now().toString(36).slice(-7);

class Peer {
  constructor(label) {
    this.label = label;
    this.messages = [];
    this.waiters = [];
  }

  async connect() {
    this.socket = new WebSocket(endpoint);
    this.socket.addEventListener("message", event => {
      const message = JSON.parse(event.data);
      const index = this.waiters.findIndex(waiter => waiter.predicate(message));
      if (index >= 0) {
        const [waiter] = this.waiters.splice(index, 1);
        clearTimeout(waiter.timer);
        waiter.resolve(message);
      } else {
        this.messages.push(message);
      }
    });
    await new Promise((resolve, reject) => {
      this.socket.addEventListener("open", resolve, { once: true });
      this.socket.addEventListener("error", reject, { once: true });
    });
  }

  send(type, requestId, payload = {}) {
    this.socket.send(JSON.stringify({ type, requestId, payload }));
  }

  wait(predicate, timeoutMs = 4000) {
    const existing = this.messages.findIndex(predicate);
    if (existing >= 0)
      return Promise.resolve(this.messages.splice(existing, 1)[0]);

    return new Promise((resolve, reject) => {
      const waiter = { predicate, resolve };
      waiter.timer = setTimeout(() => {
        this.waiters = this.waiters.filter(candidate => candidate !== waiter);
        reject(new Error(`${this.label}: timed out waiting for server message`));
      }, timeoutMs);
      this.waiters.push(waiter);
    });
  }

  request(type, requestId, payload = {}, responseType) {
    this.send(type, requestId, payload);
    return this.wait(message =>
      message.requestId === requestId &&
      (!responseType || message.type === responseType));
  }

  close() {
    this.socket.close();
  }
}

function assert(condition, message) {
  if (!condition)
    throw new Error(message);
}

const host = new Peer("host");
const guest = new Peer("guest");
const resumedHost = new Peer("resumed host");
const replacementHost = new Peer("replacement host");

try {
  await host.connect();
  const hostAuth = await host.request(
    "signup",
    "auth-host",
    { username: `qaH${suffix}`, password: "correct-horse-1" },
    "auth.ok");
  assert(hostAuth.data.resumeToken, "signup did not return a resume token");

  const created = await host.request(
    "lobby.create",
    "create",
    { name: `QA ${suffix}`, maxPlayers: 4 },
    "lobby.state");
  const lobbyId = created.data.lobbyId;
  assert(created.data.settings.difficultyScaling === true, "wrong default scaling");
  assert(created.data.runLevel === 1, "new lobby did not start at run level 1");

  await guest.connect();
  await guest.request(
    "signup",
    "auth-guest",
    { username: `qaG${suffix}`, password: "correct-horse-2" },
    "auth.ok");

  const listing = await guest.request(
    "lobby.search",
    "search",
    { search: suffix },
    "lobby.list");
  assert(listing.data.lobbies.some(lobby => lobby.lobbyId === lobbyId), "lobby missing");

  const joined = await guest.request(
    "lobby.join",
    "join",
    { lobbyId },
    "lobby.state");
  assert(joined.data.players.length === 2, "guest did not join");

  const startedForGuest = guest.wait(message => message.type === "lobby.started");
  const startedForHost = host.request(
    "lobby.start",
    "start",
    {},
    "lobby.started");
  const [hostStart, guestStart] = await Promise.all([startedForHost, startedForGuest]);
  assert(hostStart.data.seed === guestStart.data.seed, "run seed differs by peer");
  assert(hostStart.data.runLevel === 1, "first run did not start at level 1");
  assert(guestStart.data.runLevel === hostStart.data.runLevel, "run level differs by peer");

  const nonHostSnapshot = await guest.request(
    "game.snapshot",
    "bad-snapshot",
    {
      clientSequence: 1,
      authorityEpoch: guestStart.data.authorityEpoch,
      payload: {}
    },
    "error");
  assert(nonHostSnapshot.data.code === "HOST_ONLY", "non-host snapshot was accepted");

  const staleSnapshot = await host.request(
    "game.snapshot",
    "stale-snapshot",
    {
      clientSequence: 1,
      authorityEpoch: hostStart.data.authorityEpoch - 1,
      payload: {}
    },
    "error");
  assert(staleSnapshot.data.code === "STALE_AUTHORITY", "stale snapshot was accepted");

  const hostInput = host.wait(message =>
    message.type === "game.event" && message.data.kind === "input");
  const guestInput = guest.request(
    "game.input",
    "input-1",
    { clientSequence: 7, payload: { move: "north" } },
    "game.event");
  const [inputA, inputB] = await Promise.all([hostInput, guestInput]);
  assert(inputA.data.serverSequence === 1, "input sequence was not 1");
  assert(inputB.data.clientSequence === 7, "client sequence was not preserved");

  const guestSnapshot = guest.wait(message =>
    message.type === "game.event" && message.data.kind === "snapshot");
  const hostSnapshot = host.request(
    "game.snapshot",
    "snapshot-1",
    {
      clientSequence: 11,
      authorityEpoch: hostStart.data.authorityEpoch,
      payload: { tick: 42, drones: [] }
    },
    "game.event");
  const [snapshotA, snapshotB] = await Promise.all([hostSnapshot, guestSnapshot]);
  assert(snapshotA.data.serverSequence === 2, "snapshot sequence was not 2");
  assert(
    snapshotA.data.authorityEpoch === hostStart.data.authorityEpoch,
    "snapshot authority epoch was not preserved");
  assert(snapshotB.data.senderPlayerId === hostAuth.data.playerId, "snapshot sender wrong");

  const guestSawDrop = guest.wait(message =>
    message.type === "lobby.state" &&
    message.data.players.some(player =>
      player.playerId === hostAuth.data.playerId && !player.connected));
  host.close();
  await guestSawDrop;

  await resumedHost.connect();
  const resumedAuth = await resumedHost.request(
    "resume",
    "resume",
    { token: hostAuth.data.resumeToken },
    "auth.ok");
  const reattached = await resumedHost.wait(message =>
    message.type === "lobby.state" &&
    message.data.players.every(player => player.connected));
  assert(reattached.data.hostPlayerId === hostAuth.data.playerId, "host was not restored");
  const checkpoint = await resumedHost.wait(message =>
    message.type === "game.checkpoint");
  assert(checkpoint.data.available, "resume did not receive cached checkpoint");
  assert(checkpoint.data.payload.tick === 42, "checkpoint payload was not cached");
  assert(
    checkpoint.data.authorityEpoch === reattached.data.authorityEpoch,
    "checkpoint authority epoch is stale");

  await replacementHost.connect();
  await replacementHost.request(
    "resume",
    "resume-takeover",
    { token: resumedAuth.data.resumeToken },
    "auth.ok");
  const takeoverState = await replacementHost.wait(message =>
    message.type === "lobby.state" &&
    message.data.players.every(player => player.connected));
  assert(
    takeoverState.data.hostPlayerId === hostAuth.data.playerId,
    "resume takeover changed the host identity");
  const takeoverCheckpoint = await replacementHost.wait(message =>
    message.type === "game.checkpoint");
  assert(
    takeoverCheckpoint.data.payload.tick === 42,
    "resume takeover did not recover the checkpoint");

  const guestSawSecondDrop = guest.wait(message =>
    message.type === "lobby.state" &&
    message.data.players.some(player =>
      player.playerId === hostAuth.data.playerId && !player.connected));
  replacementHost.close();
  await guestSawSecondDrop;

  const migratedState = await guest.wait(
    message =>
      message.type === "lobby.state" &&
      message.data.hostPlayerId !== hostAuth.data.playerId,
    25000);
  const migrationCheckpoint = await guest.wait(message =>
    message.type === "game.checkpoint");
  assert(migrationCheckpoint.data.available, "new host did not receive checkpoint");
  assert(
    migrationCheckpoint.data.authorityEpoch === reattached.data.authorityEpoch + 1,
    "host migration did not advance authority epoch");
  assert(
    migratedState.data.authorityEpoch === migrationCheckpoint.data.authorityEpoch,
    "migrated state and checkpoint epochs differ");

  const hostFinished = guest.request(
    "lobby.finish",
    "finish",
    { completed: true, difficultyPenalty: true },
    "lobby.state");
  const completedState = await hostFinished;
  assert(completedState.data.runLevel === 3, "scaled survivor penalty did not advance twice");

  const secondStart = await guest.request(
    "lobby.start",
    "start-2",
    {},
    "lobby.started");
  assert(secondStart.data.runLevel === 3, "next run did not preserve advanced level");

  const failedState = await guest.request(
    "lobby.finish",
    "finish-failed",
    { completed: false },
    "lobby.state");
  assert(failedState.data.runLevel === 3, "failed run advanced the run level");

  const resetState = await guest.request(
    "lobby.settings",
    "settings-reset",
    {
      settings: {
        mapSize: "small",
        mazeStrictness: "normal",
        hollowAmount: "normal",
        hollowTypes: ["square", "diamond", "hex", "sentry"],
        difficultyScaling: false
      }
    },
    "lobby.state");
  assert(resetState.data.runLevel === 1, "settings change did not reset run level");

  const fixedStart = await guest.request(
    "lobby.start",
    "start-fixed",
    {},
    "lobby.started");
  assert(fixedStart.data.runLevel === 1, "fixed-difficulty run did not start at level 1");

  const fixedFinished = await guest.request(
    "lobby.finish",
    "finish-fixed",
    { completed: true },
    "lobby.state");
  assert(fixedFinished.data.runLevel === 1, "fixed-difficulty run advanced");

  await guest.request("lobby.leave", "guest-leave", {}, "lobby.left");
  console.log("Dust online protocol smoke test passed.");
} finally {
  host.close();
  guest.close();
  resumedHost.close();
  replacementHost.close();
}
