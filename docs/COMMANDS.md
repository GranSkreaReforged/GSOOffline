# Command reference

Every command available for developing and running the GSO Offline Server. Run the scripts from the repo root, in PowerShell. Each script finds the game automatically (Steam libraries); `-GameDir` overrides that.

## build.ps1

Builds the plugin into `artifacts\build\<Configuration>\`, laid out like the game folder (`BepInEx\plugins\GSOOffline\GSOOffline.dll`) with an `INSTALL.txt`. It only reads the game; copying into it is opt-in. Every build it makes is a dev build, versioned `<version>-dev+<branch>.<commit>` (plus `.dirty` with uncommitted changes) and logged at startup.

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
| `-Version x.y.z` | Required. Run on `dev`. Makes the release build, bumps `Directory.Build.props`, dates `## [Unreleased]` in the CHANGELOG, commits "Release vX" with that version's notes as the message body, and tags `vX`. |
| `-DryRun` | Build and package into `dist\` only; no version bump, commit or tag |
| `-GameDir <path>` | Game folder override |

Output in `dist\`:
- `GSOOffline-x.y.z.zip`
- `GSOOffline-x.y.z-with-BepInEx.zip`
- `SHA256SUMS.txt`

It never pushes. Push `dev`, open the `dev` -> `main` pull request from the release commit, and push the tag after it's merged.

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
| `fx <id>` / `sfx <id>` | Plays an effect ("_GFX IDs") 3 m in front of the player, or a sound at the player |
| `projectiles` | The client's projectiles in flight (id, position, speed, model) |
| `playerstate` | The client's attack state for your character (attacking, combat stance, animation), action animation, weapon in hand and on the back |
| `loot` | The loot bags the server holds in this scene, and the live bags the client shows |
| `lootroll <npcType> [n]` | Rolls that NPC type's drop table *n* times (default 1000) and logs the totals |
| `killnpc <uid>` | Kills a visible NPC as the player would: XP, loot bag, quest triggers |
| `setlevel <skillId> <level>` | Raises a skill to that level (Scr_SkillsHandler ids, e.g. 15 Swordsmanship, 24 Healing) |
| `buffs` | Lists the player's active buffs with their effects, health and mana |
| `worldnpcs [type...]` | The server's NPCs in this scene (all, or those types), with position and whether quest rules show them |
| `invorder` | Server vs client inventory and bank, row by row (order, client id, amount) |
| `sortinv` | Sorts the inventory by name exactly as the window's "By name" button does |
| `probe x y z` | Every collider on a vertical line through the point and within 4 m of it |
| `npcground [n]` | The nearest *n* NPCs' height above the ground under them (sunken NPCs read negative), whether they're walking, their animation and facing |
| `sounds` | The sounds the client is playing right now (ids as in `Audio_Sound_<id>.wav`) |
| `playeranim` | The player's animator layers: weight, clip length and progress |
| `swingprofile [seconds]` | Each attack animation's weapon-hand speed curve (when the blow strikes), with the moments hits land and shots leave |
| `npchp <uid> <hp>` | Sets an NPC's health and max health (a training dummy for combat tests) |
| `openurl <url>` | Calls `Application.OpenURL` as a menu link would (checks `OpenUrlPatch`) |
| `boatprefabs` | The client's player-ship prefabs (`ab_PlayerShip_<n>`): name, seats, speed, crane and interactables |
| `boatstate` | Whether the player swims or sits in a boat, and every boat object the client has |
| `sail <seconds> [heading]` | Drives the player's boat forward like holding "move forward" (turning to a compass heading first), so the client's own sync and shore check run as in play |
| `ferries` | The server's ferry schedule next to the client's ferry objects, and whether the player is on a deck |
| `ferryboard <id> [height]` | Drops the player onto a ferry's deck from that height above its origin (default 4 m) |
| `routecheck` | Where this zone's ferry routes run over land or rocks |
| `routeplan <name> <margin> <cell> x,z x,z ...` | The shortest water route through the points (6 m clearance), printed as waypoints, with a map in `<game>\GSODevTools\<name>.png` |
| `watermap x1 z1 x2 z2 [cell]` | A water/land map of that rectangle in `<game>\GSODevTools\watermap.png` (north up) |
| `profile x1 z1 x2 z2 [step]` | The highest solid surface along a line (water is y 3.15) |
| `interactables <typeId>` / `interactablesnear [radius]` | The scene's client-side interactables of one type, or every one within a radius of the player (default 150 m) |
| `spawnprefabs [max]` | The client's spawned-object prefabs (`ab_spawnedObject_<n>`): size, components and interactables |
| `spawnobj <type> [dx dz \| f <ahead> [up]]` / `despawnobj <uid>` | Shows a spawned-object prefab next to or in front of the player (client only), or removes it |
| `oysters` | The Bal Sardan harbour oysters (position, stunned, time left), the gun being manned and the client's object count |
| `netoyster [stun] [offset]` | Drops the fishing vessel's net on the first oyster, optionally stunning it first or offset metres east (needs the vessel boarded) |

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
| World.StartTimeOfDay | 1000 | Time of day when you enter the world; the day/night cycle runs on from there (negative = client default) |
| Convenience.AutoLogin / AutoCharacter | empty | Skip the menus |
| Debug.LogUnhandledEvents | true | Log unimplemented client events |
