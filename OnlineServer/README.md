# Dust Online Server

This is the dedicated, single-process lobby and relay server for Dust. It
provides username/password accounts, public lobby discovery, host-controlled run
settings, shared run seeds, transient-disconnect recovery, host migration, and
sequenced gameplay relaying.

The server does not need the Dust game files. Likewise, the game client must
never receive `Data/accounts.json`.

## Run locally

Install the [.NET 8 ASP.NET Core Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
or the .NET 8 SDK, then:

```powershell
dotnet run --project Dust.OnlineServer.csproj -c Release
```

The default listener is `http://0.0.0.0:5077`. A client on the same computer
connects to:

```text
ws://127.0.0.1:5077/ws
```

The production client has its public endpoint embedded and has no
server-address field. For developer-only local testing, launch the game from a
PowerShell process with the loopback override set:

```powershell
$env:DUST_ONLINE_SERVER_URL = "ws://127.0.0.1:5077/ws"
Start-Process -FilePath .\publish\Dust.exe
Remove-Item Env:DUST_ONLINE_SERVER_URL
```

The override accepts secure `wss://` endpoints, or plaintext `ws://` only for
loopback targets. Players should never need to set it.

Health information is available at `http://127.0.0.1:5077/health`.

To build a deployable framework-dependent directory:

```powershell
dotnet publish Dust.OnlineServer.csproj -c Release -o artifacts/server
dotnet artifacts/server/Dust.OnlineServer.dll
```

## Railway deployment

Railway is the supported managed-hosting target for the current single-process
server. From a repository whose root is the `Dust` folder:

1. Create a Railway service from the GitHub repository. The root
   `railway.json` selects `OnlineServer/Dockerfile` and `/health`.
2. Attach a persistent volume at `/data`.
3. Keep one replica.
4. Generate a public HTTP domain.
5. Verify `https://<domain>/health`. The current Dust release already embeds
   `wss://dustexe-production.up.railway.app/ws`; players enter only a username
   and password.

The process honors Railway's injected `PORT`; do not override it. Railway
terminates TLS at its edge while the container listens on HTTP internally.
Accounts persist in `/data/accounts.json`. Active lobbies remain in memory and
therefore end on a deployment or service restart.

If the Git repository contains `VariousIdeas` above this project, set Railway's
service Root Directory to `/Dust` and Config File Path to
`/Dust/railway.json`.

## General production checklist

Passwords are sent during signup/login, so an Internet-facing server **must use
TLS**. Put the service behind a TLS reverse proxy such as Caddy, nginx, or a
cloud load balancer. A custom production build must embed an endpoint such as:

```text
wss://dust.example.com/ws
```

On the hosting side:

1. Rent a small Windows or Linux VPS, or use a computer that remains online.
2. Install the .NET 8 ASP.NET Core Runtime and copy the published server folder.
3. Give the service a persistent working directory. Account records are stored
   in `Data/accounts.json` by default.
4. Run it as a background service and make it restart automatically after a
   crash/reboot.
5. Either open TCP port `5077`, or preferably bind it to localhost and expose
   only HTTPS port `443` through a reverse proxy.
6. Point a domain name at the server, enable TLS, then embed its `wss://`
   endpoint in the production game client. Do not expose a server-address field
   to players.
7. Back up `Data/accounts.json`. It contains salted password hashes, not plain
   passwords, but it is still sensitive server data.

If hosting from home without a reverse proxy service, forward the chosen TCP
port through the router and allow it through the host firewall. A VPS is
generally simpler and avoids residential-IP/CGNAT problems.

Lobbies and resume tokens are in memory. Restarting the service closes active
lobbies, but accounts remain. Run only one server process against a given
account file; multi-node lobby hosting would require a shared database and
message backplane.

### Reverse proxy shape

The proxy must support WebSocket upgrades and forward `/ws` to
`http://127.0.0.1:5077/ws`. For example, a minimal Caddy site is:

```caddyfile
dust.example.com {
    reverse_proxy 127.0.0.1:5077
}
```

Then override the direct listener so it is not publicly exposed:

```text
ASPNETCORE_URLS=http://127.0.0.1:5077
```

## Configuration

Settings live under `OnlineServer` in `appsettings.json`. Environment variables
use double underscores, for example
`OnlineServer__DisconnectGraceSeconds=20`.

| Setting | Default | Meaning |
| --- | ---: | --- |
| `AccountFile` | `Data/accounts.json` | Atomic JSON account database |
| `PasswordHashIterations` | `210000` | PBKDF2-SHA256 work factor (minimum 100,000) |
| `ResumeTokenHours` | `24` | Lifetime of in-memory resume tokens |
| `DisconnectGraceSeconds` | `15` | Reconnect window before lobby removal |
| `MaxWebSocketMessageBytes` | `81920` | Maximum complete WebSocket message |
| `MaxSnapshotPayloadBytes` | `65536` | Maximum nested snapshot JSON |
| `MaxInputPayloadBytes` | `16384` | Maximum nested input JSON |
| `InputMessagesPerSecond` | `35` | Per-connection input token refill rate |
| `SnapshotMessagesPerSecond` | `15` | Per-host snapshot token refill rate |
| `PeerSendTimeoutSeconds` | `3` | Disconnect a client that backpressures a lobby broadcast |
| `AllowedOrigins` | empty | Permitted browser Origin values; empty allows all |

Input and snapshot limiters permit a small 1.25-second burst. Only the host can
send snapshots. Authentication attempts are limited to five per connection per
minute. A reverse proxy should add IP-level connection and request limiting for
an Internet deployment.

## WebSocket protocol (version 1)

The endpoint is `/ws`. All messages are UTF-8 JSON text. A client request has:

```json
{
  "type": "lobby.list",
  "requestId": "client-generated-id",
  "payload": {}
}
```

`requestId` is optional, but strongly recommended. Direct responses echo it.
Server messages use the same outer shape, with response fields inside `data`:

```json
{
  "type": "lobby.list",
  "requestId": "client-generated-id",
  "data": { "revision": 4, "lobbies": [] }
}
```

Failures are:

```json
{
  "type": "error",
  "requestId": "client-generated-id",
  "data": {
    "code": "LOBBY_NOT_FOUND",
    "message": "That lobby no longer exists."
  }
}
```

### Authentication

Signup immediately authenticates the socket:

```json
{
  "type": "signup",
  "requestId": "1",
  "payload": { "username": "Drone_7", "password": "at-least-8-characters" }
}
```

`login` uses the same payload. Usernames are case-insensitively unique and may
contain 3-20 ASCII letters, numbers, `_`, or `-`. Passwords contain 8-128
characters.

Both commands return `auth.ok`:

```json
{
  "type": "auth.ok",
  "requestId": "1",
  "data": {
    "playerId": "9dc4d0d7-776b-4336-b121-029eace43779",
    "username": "Drone_7",
    "resumeToken": "opaque-token",
    "resumeExpiresAtUtc": "2026-07-24T03:00:00+00:00"
  }
}
```

Keep the token in memory and use it after a dropped socket:

```json
{
  "type": "resume",
  "requestId": "2",
  "payload": { "token": "opaque-token" }
}
```

The resumed socket receives `auth.ok` followed by its current `lobby.state`.
Each successful resume rotates the bearer token. If an old half-open socket is
still registered, the valid token atomically replaces and retires it so a real
reconnect cannot be stranded behind stale TCP state. Resume tokens are secrets
and must not be logged. Only one live connection per account is allowed.

### Lobby discovery and lifecycle

Authenticated client commands:

| Type | Payload | Result |
| --- | --- | --- |
| `lobby.list` | `{ "search": "" }` | `lobby.list` |
| `lobby.search` | `{ "search": "name, host, or ID" }` | Alias of `lobby.list` |
| `lobby.create` | `{ "name": "Night Shift", "maxPlayers": 4, "settings": {...} }` | `lobby.state` |
| `lobby.join` | `{ "lobbyId": "A1B2C3D4" }` | Broadcast `lobby.state` |
| `lobby.leave` | `{}` | `lobby.left`, then updated state to remaining members |
| `lobby.settings` | `{ "settings": {...} }` | Host-only; changed settings reset `runLevel` to 1 and broadcast `lobby.state` |
| `lobby.start` | `{}` | Host-only; broadcast `lobby.started` |
| `lobby.finish` | `{ "completed": true, "difficultyPenalty": false }` | Host-only; active run returns to `waiting` |

`lobby.refresh` is pushed to all authenticated sockets after directory
mutations. It contains the same `{ revision, lobbies }` data as an unfiltered
`lobby.list`.

`maxPlayers` may be 2-4. Four is both the default and the protocol maximum.

Run settings:

```json
{
  "mapSize": "medium",
  "mazeStrictness": "normal",
  "hollowAmount": "normal",
  "hollowTypes": ["square", "diamond", "hex", "sentry"],
  "difficultyScaling": true
}
```

Accepted sizes are `small`, `medium`, and `large`; strictness is `strict`,
`normal`, or `loose`; amount is `none`, `small`, `normal`, or `large`.

Every `lobby.state` includes:

```json
{
  "lobbyId": "A1B2C3D4",
  "name": "Night Shift",
  "revision": 5,
  "status": "waiting",
  "hostPlayerId": "guid",
  "authorityEpoch": 0,
  "maxPlayers": 4,
  "seed": null,
  "runLevel": 1,
  "settings": {},
  "players": [
    {
      "playerId": "guid",
      "username": "Drone_7",
      "joinOrder": 0,
      "connected": true
    }
  ]
}
```

`lobby.started` contains `lobbyId`, `revision`, `status`, a non-negative 64-bit
`seed`, `hostPlayerId`, `authorityEpoch`, `runLevel`, `settings`, and the same
ordered player roster. Every client generates the same maze from that seed and
run level. When `difficultyScaling` is enabled, finishing with
`"completed": true` advances `runLevel` for the next maze; an abandoned
survivor sets `"difficultyPenalty": true` and adds one extra level. Failed runs
keep the same level. Scaling-disabled runs never advance it, and a genuine
settings change resets it to 1. A run start and every host migration advance
`authorityEpoch`, invalidating snapshots from older hosts/runs.

When a socket drops, the member remains in the roster with `connected:false`
for 15 seconds. A successful `resume` reattaches it without changing its
identity or host role. Once grace expires, the member is removed. If it was the
host, host control migrates to the earliest connected joiner (then earliest
disconnected joiner); an empty lobby is deleted. Explicit `lobby.leave` is
immediate. The server retains the latest accepted host snapshot (at most 64
KiB) so a resumed member or newly elected host can recover the current run.
Gameplay input is rejected with `HOST_UNAVAILABLE` while the host is inside its
reconnect grace window; clients should visibly pause until it resumes or a new
host is elected. This prevents unacknowledged input from advancing beyond the
cached authoritative checkpoint.

### Gameplay relay

Any connected member can submit input:

```json
{
  "type": "game.input",
  "requestId": "input-72",
  "payload": {
    "clientSequence": 72,
    "payload": { "move": "north", "clientTick": 815 }
  }
}
```

The host sends authoritative snapshots with the same structure using
`game.snapshot`, and must include the current lobby authority epoch:

```json
{
  "type": "game.snapshot",
  "requestId": "snapshot-91",
  "payload": {
    "clientSequence": 91,
    "authorityEpoch": 3,
    "payload": { "tick": 1200, "players": [] }
  }
}
```

Its nested `payload` may be any JSON value up to 64 KiB. Non-host snapshots and
snapshots carrying an old `authorityEpoch` are rejected.

The server broadcasts every accepted message to **all** connected members,
including its sender:

```json
{
  "type": "game.event",
  "requestId": "input-72",
  "data": {
    "lobbyId": "A1B2C3D4",
    "kind": "input",
    "senderPlayerId": "guid",
    "clientSequence": 72,
    "serverSequence": 104,
    "authorityEpoch": 3,
    "serverTimeUnixMs": 1784775600000,
    "payload": { "move": "north", "clientTick": 815 }
  }
}
```

Only the sender sees its original `requestId`; other peers receive it as null.
Apply events in `serverSequence` order. The echo gives the sender canonical
acknowledgement. `clientSequence` must increase independently for each sender's
input stream and snapshot stream; duplicates/out-of-order messages are rejected.
A new run resets those counters and the server sequence to zero.

After an in-game resume, and after a disconnected host is evicted and replaced,
the affected peer receives `game.checkpoint`. When a snapshot has already been
cached, its data is:

```json
{
  "type": "game.checkpoint",
  "data": {
    "lobbyId": "A1B2C3D4",
    "available": true,
    "kind": "snapshot",
    "checkpoint": true,
    "senderPlayerId": "old-host-guid",
    "clientSequence": 91,
    "serverSequence": 104,
    "authorityEpoch": 4,
    "sourceAuthorityEpoch": 3,
    "serverTimeUnixMs": 1784775600000,
    "payload": {}
  }
}
```

The new host loads the cached payload, adopts the current `authorityEpoch`, then
publishes its own next snapshot. If no snapshot was accepted yet,
`available:false` is sent with the current epoch and sequence.

This is a host-authoritative relay: sequencing, membership, size, and rate
rules are enforced by the server, while the host owns the gameplay snapshot.
Competitive cheat prevention would require moving Dust's simulation and
collision validation into the dedicated server.

### Ping

`ping` is accepted before or after authentication and returns `pong`, a
`serverTimeUnixMs`, and the original payload as `echo`.

## Tests

With a server listening locally, Node 22+ can run the dependency-free
integration test:

```powershell
node Tests/smoke.mjs
```

It exercises signup, resume, lobby discovery, joining, starting, input echo and
ordering, snapshot authority rejection, checkpoint recovery, finishing,
reconnect grace, host migration, and authority epoch advancement. Use a
disposable account file while testing:

```powershell
dotnet run -c Release --no-build -- --OnlineServer:AccountFile=Data/smoke-accounts.json
```
