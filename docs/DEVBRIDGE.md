# DevBridge: testing through the real client

The DevBridge is an opt-in developer hook in the plugin (`src/GSOOffline/DevBridge.cs`). When it is enabled, the plugin checks a command file twice a second. It runs each line in-game, then deletes the file. Results go to `BepInEx/LogOutput.log` with a `[dev]` prefix.

It lets you exercise server features end to end, through the real client code paths, without clicking through the UI. That is how everything in this repo was verified.

It is **off by default** and does nothing in normal play.

## Turning it on and off

`tools/dev/devbridge.ps1` handles the config for you. It finds the game the same way `build.ps1` does.

```powershell
# One-time: the game must have been started once with the plugin so the config file exists.
.\tools\dev\devbridge.ps1 -Enable -Account Tester -Character Testguy

# ...test...

.\tools\dev\devbridge.ps1 -Disable -ResetSaves   # normal login again; deletes test accounts/characters
```

`-Enable` sets three values in `BepInEx/config/gso.offline.server.cfg`:

| Setting | Effect |
|---|---|
| `Convenience.AutoLogin` | Logs in automatically with this account name. |
| `Convenience.AutoCharacter` | Enters the world with this character, creating it if it doesn't exist. A new character stops at the character creator (see `creationdone`). |
| `Debug.DevCommandFile` | `OfflineSaves/dev/cmd.txt`, the file the bridge watches. |

`-ResetSaves` deletes `OfflineSaves/accounts`, `characters` and `dev`. Never run it against a real player's saves.

## Sending commands

```powershell
# Fresh start: kill the game, delete the old log, launch, wait for the world to load.
.\tools\dev\devbridge.ps1 -Launch -Commands 'npcs 5' -Filter 'uid='

# Send to an already-running game. -Wait is the pause after each command, in seconds.
.\tools\dev\devbridge.ps1 -Commands 'client interactNpc 10018 $me', 'client sendDialogueOptionChoise $me 1' -Wait 1.5 -Filter 'dialogue'
```

The script prints the log lines produced since it started; `-Filter` is a regex applied to them. Use single quotes so PowerShell leaves `$me` alone.

You can also write lines to `OfflineSaves/dev/cmd.txt` by hand. Several lines per file are fine.

## Command reference

| Command | What it does |
|---|---|
| `client <method> [args...]` | Calls the public `Scr_RPCSender.<method>` with that many arguments, exactly as the game UI would. Arguments are parsed to the parameter types (int, float, bool, string, `Vector3` as `x,y,z`). `$me` is replaced with the player's name, and `_` in strings becomes a space (`/give_193` → `/give 193`). |
| `creationdone` | Presses "Done" in the character creator. Pick the class first with `client updateFightingStyleAndProfession <style> <profession>`. |
| `inv` | Dumps the client's inventory (id, type, name, amount, slot, equipped), equipment slots, silver and HP. |
| `npcs [n]` | Lists the nearest *n* client-side NPCs: uid, type, name, distance, HP, whether they can be interacted with. |
| `harvestables [n]` | Lists the nearest *n* harvestable nodes. |
| `near <uid>` | Moves the player next to a visible NPC or harvestable. |
| `goto x y z` | Moves the player on the client side only. The server learns the new position from the next sync. For server-side moves use `client sendChatMessage $me /tele_x_y_z` or `/scene_id_x_y_z`. |
| `shot <name>` | Saves a screenshot to `OfflineSaves/dev/<name>.png`. Read it back to check what the player would see. |
| `checkrecipes` | Compares the server's crafting recipe ids with the client's `Script_Crafting` list. All 341 should match. |

## Useful `client` calls

These are the message layouts most often needed. `docs/PROTOCOL.md` has the full list.

| Goal | Command |
|---|---|
| Talk to an NPC | `client interactNpc <uid> $me` |
| Pick a dialogue option | `client sendDialogueOptionChoise $me <optionId>` (the log line `[dialogue] node N options [..]` shows which ids are visible) |
| Interact with an object/door | `client attemptObjectInteract $me <interactable typeId>` (the nearest one of that type is used) |
| Harvest a node | `client startAction $me 1 <harvestable uid>` |
| Craft a recipe | `client startAction $me 2 <recipe id>` |
| Target and attack | `client selectNpcTarget $me <uid>`, then `client useAbility $me 0` |
| Put an ability on the bar | `client setAbilityToSlot $me <slot> <abilityId>`, then `client useAbility $me <slot>` |
| Shop | `client buyItemFromNPCShop <typeId> <amount> $me`, `client sellItemToNPCShop <typeId> <itemId> <amount> $me` |
| Bank | `client addBankItem <itemId> <typeId> <amount>`, `client removeBankItem ...`, `client addBankSilver <±amount>` |
| Chat / server commands | `client sendChatMessage $me /help`; also `/pos`, `/give_<id>_<n>`, `/quest_<id>_<phase>`, `/scene_<id>`, `/wayshrine_<id>`, `/silver_<n>`, `/save` |

## Log lines worth filtering on

| Prefix | Source |
|---|---|
| `[dev]` | Bridge command echoes and dumps |
| `[dialogue] node N options [...]` | Each dialogue node shown |
| `Quest Q -> phase P` | Quest progress |
| `[skill] ...` | Harvest and craft starts, refusals and cancellations |
| `[notice] ...` | Every message the server shows the player (errors included) |
| `Unhandled client event X/Y` | Client messages the server does not implement yet; this is the to-do list |
| `Client failed handling event` | The server sent something the client choked on |

## Pitfalls

- **Stale logs.** BepInEx only truncates `LogOutput.log` once the new game is running. `-Launch` deletes the log first; a hand-rolled wait loop must do the same, or it will match the previous run.
- **The DLL is locked while the game runs.** Close the game before `build.ps1` (`-Launch` and `-Disable` kill it).
- **New characters need `creationdone`**, otherwise they can't move.
- **Uids are per scene.** NPC uid = scene × 10000 + index; harvestable uid = 5,000,000 + scene × 10000 + index. They are stable between runs of the same scene, so a uid seen in `npcs` can be reused.
- **`near` can wedge you into scenery.** The server's range checks are deliberately loose (20 m for harvesting) because node transforms sit a few metres off their markers.
- **Clean up afterwards.** Run `-Disable -ResetSaves`, so the next normal launch doesn't auto-login as the test account.
