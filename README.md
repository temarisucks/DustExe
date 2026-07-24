# Dust

A low-resolution exploration game presented as a battered behavioral-testing apparatus. Route a test through the title desk, configure one of five drone airframes at the coating bench, then enter a large resin-and-porcelain vivarium. The Mite, Kite, Triad, Cicada, and Cradle drones each have a distinct silhouette, float while idle, and bank or pitch in the direction of travel. Their core and frame can be painted independently from a twelve-color reagent bank. The visuals use a fixed 640x400 canvas, a hand-built bitmap alphabet, and a custom equipment cursor rather than stock interface art.

Each plate contains sealed cargo rooms in square, rectangular, and L-shaped layouts. Their interiors remain optically suppressed until the drone crosses a mechanical doorway, whose two leaves retract into the wall and remain open after the first traversal. A small language-free folder/report icon beneath the subject telemetry, or the `Q` key, opens a physical mission dossier listing required freight by name and code, whether its room has been revealed, and whether it is missing, latched, or transferred. Some plates replace two manifested cargo cases with a mandatory storage-circuit order: two wall switches are distributed between separate storage rooms, and the marked extraction hatch remains sealed until both have been flipped with `E`. The dossier tracks circuit state and every stage of the personnel contract while safely pausing all plate clocks. Approaching a case opens a persistent prompt with its full cargo type and serial, and `E` latches it from the case tile or one connected neighboring tile. Decoy cargo can be inspected but not carried. Rooms are dressed with wall-mounted cargo racks, specimen cabinets, breathing pressure tanks, valve manifolds, cable reels, sweeping work lights, salvage, and one guaranteed reclamation kiosk; all dressing remains non-blocking. Salvage enters a small sell inventory automatically when crossed. Standing beside the kiosk on a connected tile and pressing `E` opens a safe counter where the player can buy limited repairs and one-hit Aegis wards, sell salvage, question the animated shadow behind the counter, or leave. Repairs bought at full integrity are banked and deploy automatically after a later hit. The kiosk accepts both saved account funds and field credits recovered during the current plate without counting spent field credits again in the final payout. A breach drops any carried cargo at the point of impact, while scattered credit chips are collected automatically by moving over them. Reaching a released extraction hatch ends the job even when cargo is missing, but speed, recovered cargo, restored circuits, loose credits, and breaches all affect the final pay.

Each plate also carries a human distress contract. A person hidden in one storage room asks the drone to recover a randomly named worker stranded elsewhere in the facility. Interacting from an adjacent connected tile attaches the worker to an escort tether; returning them to the requester closes the rescue and marks both life signs safe. Leaving without completing the contract prints `You left [name] to die.` across a bloodstained Cycle Record. With Difficulty Scaling enabled, abandonment adds one extra effective scaling step to the next plate's dimensions, enemy count, and aggression without skipping a chapter or inflating pay.

The field office also issues personal contracts: decrypt a sealed archive, purge paired pressure valves, calibrate a three-node signal chain in sequence, or close two specimen-containment clamps. Small, Medium, and Large plates assign two, three, or four contracts per drone. Their fixtures are distributed between storage rooms, tracked in a dedicated dossier column, and paid or docked on the Cycle Record. In online runs, manifested cargo, circuit switches, personnel recovery, and every field contract have explicit owners; another drone can see the order but cannot complete it. If an owner leaves permanently or is defeated, the authority reassigns unfinished work so a mandatory switch cannot strand the remaining team.

The vivarium is occupied by four hollow negatives. Squares are slow, short-range pursuers built from counter-rotating square cages. Diamonds move faster, carry pulsing orbiting vanes, and investigate the last place they saw the drone. Hexes sense through porcelain, route around walls while the drone remains in range, and occasionally break into displaced signal bands. Sentries remain fixed while sweeping a full circle; after a fruitless watch they plunge below the floor, relocate, and emerge elsewhere, while a detection lets them fire a fast projectile. Each negative exposes its directional field of view. A new detection flashes a warning above the drone and plays `caught.wav`. A hit records a breach, drops cargo, destabilizes the drone, disrupts the optical feed, and relocates it to a safe corridor.

The drone has three integrity points by default. Every hit chips, cracks, and destabilizes its body; the third unhealed hit loses the plate and opens a dedicated reseed/eject terminal. Shop repairs restore integrity and visibly repair the frame, while an Aegis charge cancels one damaging hit before cargo, health, or position are affected. Purchased reserves survive a retry and carry into later plates in the same run. The Durable perk raises the limit to five. Restarts preserve the current plate difficulty while generating a fresh layout.

The supplied `track.ogg` is decoded from Ogg Opus once in the background and loops throughout the title, customization, archive, settings, and Run Settings screens without depending on installed media codecs or temporary files. `Re_Dust.wav` is volume-trimmed and prepared away from the UI thread, then loops on its own music channel during a live plate so warning and movement effects remain independent. Loose-credit recovery uses `collect.wav`, a successful transfer uses `mazeclear.wav`, every character physically printed by the Cycle Record uses `type.wav`, and each revealed shopkeeper character uses `shopVoice.wav`. Starting, restarting, or advancing a plate first opens an animated cassette-loading console while maze generation and audio preparation run in the background. Completing a plate feeds the results through the unskippable animated Cycle Record printer, saves the result without blocking the feed, and deposits the job pay into the persistent account.

Play first opens Run Settings. Map Size selects Small, Medium, or Large; Maze Strictness controls how linear or interconnected the generated plate is; Hollow Amount selects None, Small, Normal, or Large; individual Square, Diamond, Hex, and Sentry types can be enabled; and Difficulty Scaling determines whether successive plates grow and make enemies more aggressive. The selected values are snapshotted when loading starts, so retries and subsequent plates cannot be changed accidentally from an open menu.

On first launch, Dust offers a short in-world orientation that introduces movement, interaction, the mission dossier, Hollow detection, cargo recovery, extraction, and active-perk operation. The offer is versioned, so this update presents it once even to profiles created by an earlier build; accepting or declining records the current orientation version and prevents it from appearing on every launch. Persistent in-game key legends have been removed in favor of that orientation and situational interaction prompts, while this README retains the complete external control reference.

## Online play

The title desk separates **Offline Play** from **Online Play**. Online Play
connects to the included dedicated Dust server and supports username/password
accounts, a searchable public lobby list, four-drone lobbies, host-controlled
run settings, and shared deterministic plates. The lobby host validates
movement and interactions, advances Hollows and Sentries, and publishes
sequenced world checkpoints; other clients predict their own grid hop and
interpolate remote drones.

The maze, revealed rooms, doors, objective fixtures, credits, and enemies are
shared by the team, while each drone receives its own named objective orders.
Drone integrity, equipped perks, and shop protection also remain per-player.
Guests render Hollows from a presentation-only predicted pose between
checkpoints. Both the host and Railway relay keep only the newest pending world
checkpoint for each peer, while reliable commands stay ordered; a stalled peer
is disconnected instead of delaying the entire lobby. The server also preserves
the immutable run-start roster so personal assignments reproduce correctly
through reconnects and host migration. A
disconnected player gets a short reconnect window. If the host does not return,
the server elects the longest-connected player, changes the authority epoch,
and restores the newest accepted checkpoint rather than allowing two peers to
author the same run. Joining a run already in progress is disabled in this
release.

The account server stores salted PBKDF2 password hashes and never sends the
account database to clients. The production `wss://` endpoint is embedded in
the game, so ordinary players only enter a username and password; there is no
server-address field. The client remembers the username, but never saves a
password or resume token. See [ONLINE.md](ONLINE.md) for developer-only local
testing, deployment, TLS, backups, and exactly what friends need.

## Achievements and perks

The Behavioral Archive on the title screen sorts twenty achievements into Easy, Moderate, Hard, and Extreme clearance ranks. Alongside the original records it tracks Baby Steps, Wimpy, I Did It?, I Did It!, Cage Match, Impossible Odds, and Love of the Game from the active run configuration. Unlocks, timestamps, streaks, and equipped perks persist between launches.

Achievement clearance opens eight equipable modifications:

- **Durable:** five integrity points instead of three
- **Money Magnet:** loose credits follow walkable routes toward the drone
- **Hop:** moves two spaces when possible and falls back to one when blocked
- **Camouflage:** `Space` hides the drone from Hollow vision for a few seconds
- **Mini Map:** reveals a local trace of tiles the drone has actually visited
- **Ghost Form:** `Space` opens a 3.5-second phase window through walls
- **Retracer:** leaves a persistent route trace through the plate
- **Hollow Killer:** `Space` erases all Hollows and Sentries within four tiles, then recharges for 45 seconds; both I Did It? and I Did It! are required

Camouflage, Ghost Form, and Hollow Killer share one physical `Space` channel, so only one can be fitted at a time. All other unlocked passive perks can be combined. During a plate, every equipped modification occupies its own pictographic socket in a compact perk strip instead of sharing a text label. Passive sockets remain steadily lit, while an active socket communicates ready, firing, and recharge states graphically with its lamp, pulse, and draining cooldown mask. Credits, achievement progress, equipped perks, drone customization, and the orientation version are retained alongside brightness, volume, resolution, and borderless-fullscreen settings in `%LOCALAPPDATA%\Dust\settings.json`.

## Controls

- **Title:** Up/Down chooses a route; Enter/Space throws its latch
- **Run Settings:** Up/Down selects a field; Left/Right adjusts it; Enter/Space toggles or confirms; Begin Run starts loading
- **Customize:** Up/Down or Tab/Shift+Tab changes bench; Left/Right indexes options; Enter/Space applies
- **Settings:** Up/Down selects an instrument, Left/Right adjusts it
- **Achievements:** Left/Right changes archive bank; Up/Down or the mouse wheel indexes records; Enter/Space equips or removes a selected perk
- **Gameplay:** WASD/Arrow keys move one point; Q or the small folder/report icon button beneath subject telemetry opens the paused mission file, and Q/Esc closes it; E latches assigned cargo, flips storage switches, operates field-contract fixtures, answers human distress prompts, returns escorted workers, or enters a kiosk from its cell or one connected neighboring tile; Space activates the fitted active perk; loose credits and salvage collect automatically; R abandons and regenerates the current plate
- **Shop:** WASD/Arrow keys select commands and stock; Enter/Space/E confirms; Esc returns to the command row or leaves; mouse input is also supported
- **Failure:** Enter/Space/R reseeds the current difficulty; Esc ejects to the title
- **Cycle Record:** input is locked while the report prints; after it finishes, arrows/WASD select Next Plate or Eject and Enter/Space confirms
- **Esc:** eject to the title, or exit from the title
- **F11 / Alt+Enter:** toggle borderless fullscreen

## Build

From this folder:

```powershell
dotnet run
```

To create the standalone Windows build:

```powershell
.\tools\PublishRelease.ps1
```

The finished game will be `publish\Dust.exe`; the matching Windows server will
be `publish-server\Dust.OnlineServer.exe`.

For a framework-dependent server build instead:

```powershell
dotnet publish OnlineServer\Dust.OnlineServer.csproj -c Release -o publish-server
dotnet publish-server\Dust.OnlineServer.dll
```

The game and server are independent artifacts. Friends receive the game EXE
and sign in with only a username and password; the embedded endpoint connects
them to the server that stays on the cloud host you operate.

For cloud hosting, the server is ready to deploy to Railway from the repository
Dockerfile. Railway runs it independently of your computer, supplies the secure
`wss://` domain, and keeps account data on an attached `/data` volume. Follow
the exact setup in [`ONLINE.md`](ONLINE.md).

## Sharing the game

The published `Dust.exe` is a self-contained, single-file build for 64-bit Windows. It already contains the .NET runtime, game assemblies, icon, music, and sound effects, so no DLLs, asset folders, or separate .NET installation need to travel with it. For delivery, place the EXE in a ZIP so browsers and messaging services are less likely to rename or strip it. Dust creates each player's private save automatically at `%LOCALAPPDATA%\Dust\settings.json`; that file is not required and should only be shared when intentionally transferring customization, credits, achievements, and settings.

## Source layout

`GameForm` remains one lightweight WinForms host, but its partials and support classes are grouped by responsibility:

- `App/` - entry point, shared state, lifecycle, frame loop, and coordinates
- `Gameplay/` - maze/room generation, loading orchestration, cargo missions, health, achievements, perks, Hollow/Sentry behavior, navigation, and collision
- `Progression/` - stable achievement/perk identifiers, definitions, clearance state, equipment, and streak logic
- `Rendering/` - chamber, loading console, rooms, cargo, drones, enemies, health/perk effects, and the bitmap lab font
- `UI/` - title/customize/settings/archive/result rendering, input, custom cursor, window chrome, and display control
- `Audio/` - embedded effects, background preparation, independent looping music, cue priority, and volume control
- `Settings/` - validated settings, account balance, customization, progression, and persistent JSON storage
- `Online/` - ordered WebSocket transport, lobby models, client session state, and co-op synchronization
- `OnlineServer/` - dedicated account, lobby, relay, reconnect, checkpoint, and host-migration service
- `Assets/Audio/` plus embedded `track.ogg` - effects, the live-plate score, and the Ogg Opus menu score
