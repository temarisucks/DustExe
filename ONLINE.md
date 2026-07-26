# Running Dust Online

Dust Online uses a small dedicated server. Players never connect directly to
one another, so the person who creates a lobby does not need to forward a game
port. The lobby creator is the simulation authority for that run, while the
server retains a recent checkpoint so another player can take over if the host
does not reconnect.

Current clients use gameplay protocol version 4. Alongside compact per-player
objective state and guest-side Hollow presentation smoothing, its checkpoints
carry the expanded enemy roster, empowerment timers, hostile projectiles, and
walls destroyed by empowered Turrets. Everyone in one lobby should use the same
published `Dust.exe`; protocol-1, protocol-2, and protocol-3 clients intentionally reject
version-4 world checkpoints instead of applying an incomplete mission state.

The relay uses a bounded outbound queue for every peer. Reliable inputs and
control messages remain ordered, while an unsent world snapshot is replaced by
the newest one. A connection that exceeds the queue or send deadline is closed
instead of stalling the rest of the lobby. Run-start player records are retained
for the duration of a plate so reconnecting or newly elected authorities rebuild
the same personal objective topology.

## Quick local test

Start the server from the source tree:

```powershell
dotnet run --project OnlineServer\Dust.OnlineServer.csproj -c Release
```

Or, after running `tools\PublishRelease.ps1`, start the self-contained Windows
server (no .NET installation required):

```powershell
cd publish-server
.\Dust.OnlineServer.exe
```

Keep that window open while anyone is playing. Its `Data\accounts.json` file is
created beneath the server folder and must remain with the server, never with
the game client.

The public production address is embedded in `Dust.exe`, so local testing
requires the developer-only `DUST_ONLINE_SERVER_URL` process override. From a
PowerShell window in the repository root, launch two clients with:

```powershell
$env:DUST_ONLINE_SERVER_URL = "ws://127.0.0.1:5077/ws"
Start-Process -FilePath .\publish\Dust.exe
Start-Process -FilePath .\publish\Dust.exe
Remove-Item Env:DUST_ONLINE_SERVER_URL
```

On each copy, choose **Online Play** and enter only a username and password.
Create a different account in each window, create a lobby in one, and join it
from the other.

`DUST_ONLINE_SERVER_URL` is intended only for developer testing and is not a
player setting. It accepts a secure `wss://` address or plaintext `ws://` only
when the target is loopback, such as `127.0.0.1` or `localhost`. The client
refuses to send a password over plaintext `ws://` to another computer. For a
multi-PC developer test, put the server behind TLS and override with its
`wss://` address.

## Recommended Internet deployment: Railway

Railway can keep Dust Online running without using your computer. The repository
already includes `railway.json` and a cloud-ready Dockerfile, so the service
uses Railway's assigned port, reports readiness through `/health`, and stores
accounts beneath `/data`.

### One-time setup

1. Put the contents of this `Dust` folder in a GitHub repository. `Dust` should
   be the repository root. If `VariousIdeas` is the repository root instead,
   set the Railway service's **Root Directory** to `/Dust` and its
   **Config File Path** to `/Dust/railway.json`.
2. In Railway, create a project, choose **Deploy from GitHub repo**, and select
   that repository. Leave the service's replica count at one.
3. Attach a Railway Volume to the Dust service with the exact mount path
   `/data`. This is required: `accounts.json` must survive deployments.
4. In the service's **Settings > Networking > Public Networking**, choose
   **Generate Domain**.
5. Open the generated HTTPS health address in a browser:

   ```text
   https://your-service.up.railway.app/health
   ```

   A healthy server returns JSON whose `status` is `ok`.
6. The current game release already embeds its production WebSocket endpoint:

   ```text
   wss://dustexe-production.up.railway.app/ws
   ```

   Players choose **Online Play** and enter only a username and password. If a
   future production deployment uses a different domain, update the embedded
   endpoint in the client source and publish a new `Dust.exe`; do not ask
   players to configure it.
7. Create the first account only after the `/data` volume is attached. Enable
   Railway volume backups if the accounts matter to you.

Railway supplies TLS and the public domain, so you do not need Caddy, router
port forwarding, a domain purchase, or a server process on your own PC. Do not
create a TCP Proxy and do not manually set `PORT`; Dust uses Railway's injected
HTTP port.

Railway's public edge periodically closes long-lived WebSockets after 15
minutes. Dust automatically reconnects with its resume token and restores the
latest server checkpoint, so this should appear only as a short link-restoring
pause. An actual service restart or deployment still closes live lobbies
because lobby state is held in memory. Deploy updates when nobody is playing.

Railway references:

- [Deploying Dockerfiles](https://docs.railway.com/builds/dockerfiles)
- [Public networking and generated domains](https://docs.railway.com/networking/public-networking)
- [Persistent volumes](https://docs.railway.com/volumes)
- [Health checks](https://docs.railway.com/deployments/healthchecks)

## Other Internet deployment

An Internet-facing deployment needs one continuously running copy of
`Dust.OnlineServer` and a public TLS endpoint. The recommended shape is:

```text
Dust clients -> wss://dust.example.com/ws -> TLS reverse proxy -> 127.0.0.1:5077
```

On a Windows or Linux server:

1. Publish `OnlineServer\Dust.OnlineServer.csproj` and copy the output to the
   server.
2. Give the process a persistent working directory. Accounts are kept in
   `Data/accounts.json`.
3. Run the process as a service with automatic restart enabled.
4. Put Caddy, nginx, or a cloud HTTPS load balancer in front of it.
5. Point a domain at the server and forward WebSocket upgrades for `/ws`.
6. Build the resulting `wss://.../ws` address into the production game client.
   There is no player-facing server-address field; players enter only their
   username and password.
7. Back up `Data/accounts.json` and do not distribute it with the game.

A minimal Caddy configuration is:

```caddyfile
dust.example.com {
    reverse_proxy 127.0.0.1:5077
}
```

Bind the game service to loopback behind the proxy:

```text
ASPNETCORE_URLS=http://127.0.0.1:5077
```

The health endpoint is `/health`. Check it after deployment at
`https://dust.example.com/health`.

The included Dockerfile can build the same service from the repository root:

```powershell
docker build -f OnlineServer\Dockerfile -t dust-online .
docker run -d --restart unless-stopped -p 127.0.0.1:5077:5077 `
  -v dust-accounts:/data --name dust-online dust-online
```

Keep the loopback-only port mapping when a reverse proxy is running on the same
machine. The named `dust-accounts` volume preserves registered accounts across
container replacement.

## About Firebase

Firebase Hosting itself is designed to serve web content and places a
60-second limit on requests forwarded to a backend, so it is not a suitable
front door for Dust's persistent WebSocket.

The Google-hosted version of this architecture would run the Docker container
directly on **Google Cloud Run**, not Firebase Hosting. The server now honors
Cloud Run's injected `PORT`, but a durable Cloud Run release also needs the JSON
account store moved to Firestore (or another external database). Cloud Run
containers do not provide a durable local disk, and reconnecting sockets may
reach different instances. Until lobby/checkpoint state is externalized too,
that deployment must remain a single-instance service. Railway is therefore the
supported, lower-maintenance target for the current Dust server.

## What friends need

Friends need only the published `Dust.exe`. The production server endpoint is
already embedded, so they do not need an address, the server executable, .NET,
the account database, or any asset folders. Sending the EXE inside a ZIP
usually avoids messaging services renaming or stripping it.

Each friend creates an account from the Online Play screen using only a
username and password. Accounts belong to the embedded Dust server. If that
server is replaced without preserving `Data/accounts.json`, those accounts are
lost.

## Operational notes

- Never expose password signup or login over public `ws://`; use `wss://`.
- Only one server process should write to a given account file.
- Lobbies are deliberately in memory. Restarting the server closes active
  lobbies, while registered accounts remain.
- A disconnected player has a short reconnect grace period. If the host does
  not return, authority moves to the earliest connected player and the newest
  accepted checkpoint is restored.
- The first online release supports at most four drones in one lobby and does
  not allow joining a run already in progress.
- Keep the server and all clients on the same release. Maze generation is
  deterministic, so mixing builds whose generation rules differ can produce
  incompatible plates even when they receive the same seed.
