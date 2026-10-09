# Command reference

Every command available for developing and running the GSO Offline Server. Run the scripts from the repo root, in PowerShell. Each script finds the game automatically (Steam libraries); `-GameDir` overrides that.

## build.ps1

Builds the plugin into `artifacts\build\<Configuration>\`, laid out like the game folder (`BepInEx\plugins\GSOOffline\GSOOffline.dll`) with an `INSTALL.txt`. It only reads the game; copying into it is opt-in.

| Parameter | Default | Meaning |
|---|---|---|
| `-Configuration Debug\|Release` | Debug | Build configuration |
| `-GameDir <path>` | auto | Game folder to compile against (remembered in `GameDir.user.props`) |
| `-Deploy` | off | Copy the built files into the game (`<game>\BepInEx\plugins\GSOOffline\`). Close the game first. |
| `-InstallBepInEx` | off | Put the pinned BepInEx 5.4.23.5 (hash-checked) and `steam_appid.txt` into the game |
| `-Clean` | off | `dotnet clean` and empty `artifacts\build\<Configuration>` first |

## release.ps1 (PowerShell 7)

| Parameter | Meaning |
|---|---|
| `-Version x.y.z` | Required. Bumps `Directory.Build.props`, dates `## [Unreleased]` in the CHANGELOG, commits, and tags `vX`. |
| `-DryRun` | Build and package into `dist\` only; no version bump, commit or tag |
| `-GameDir <path>` | Game folder override |

Output in `dist\`:
- `GSOOffline-x.y.z.zip`
- `GSOOffline-x.y.z-with-BepInEx.zip`
- `SHA256SUMS.txt`

It never pushes.

## Developer tools

`devbridge.ps1`, `decompile.ps1`, the datamining scripts and the DevBridge command reference are in the GSODevTools repo (`..\GSODevTools\docs\COMMANDS.md`).

This plugin adds these bridge commands through `src/GSOOffline/DevCommands.cs`:

| Command | Meaning |
|---|---|
| `checkrecipes` | Verify server recipe ids against the client's |
| `npcbounds [n]` | The nearest *n* NPCs: rendered size, feet height above the ground, animation playing |
| `animclips` | The client's animation clip table (the ids in NPC animation messages) |
| `door [n]` | Lists doors.json, or goes through door *n* exactly as clicking it would (arrival included) |
| `clearspot x y z` | Runs the door arrival search at a point, logging why each candidate is rejected |
| `playerstate` | The client's attack state for your character (attacking, combat stance, animation), action animation, weapon in hand and on the back |
| `invorder` | Server vs client inventory and bank, row by row (order, client id, amount) |
| `sortinv` | Sorts the inventory by name exactly as the window's "By name" button does |
| `probe x y z` | Every collider on a vertical line through the point and within 4 m of it |

## In-game chat commands

Type these in the game's chat. Arguments are separated by spaces.

| Command | Meaning |
|---|---|
| `/help` | List the commands |
| `/pos` | Show your scene and position |
| `/tele x y z` | Teleport within the current scene |
| `/scene id [x y z]` | Go to a scene (defaults to its wayshrine) |
| `/wayshrine id`, `/wayshrines` | Travel to a wayshrine / list them |
| `/time 0-2400` | Set the time of day |
| `/give itemId [amount]` | Give yourself an item |
| `/silver n` | Set your silver |
| `/quest id [phase]` | Show or set a quest phase |
| `/save` | Save now (autosave runs every 60 s and on logout) |

The client sends `/useportal` itself when you confirm the snowy portal.

## Plugin settings (`<game>\BepInEx\config\gso.offline.server.cfg`)

| Section.Key | Default | Meaning |
|---|---|---|
| World.NpcViewDistance | 120 | NPC streaming radius (m) |
| World.HarvestableViewDistance | 90 | Harvestable streaming radius (m) |
| World.UnlockAllWayshrines | true | All wayshrines unlocked |
| World.StartTimeOfDay | 1000 | Time sent on scene load (negative = client default) |
| Convenience.AutoLogin / AutoCharacter | empty | Skip the menus |
| Debug.LogUnhandledEvents | true | Log unimplemented client events |
