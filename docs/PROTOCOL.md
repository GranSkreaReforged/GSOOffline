# GSO client ↔ server protocol

This was reverse-engineered from `Scr_RPCSender` (client → server) and `Scr_RPCReceiver.OnEventCall` (server → client).

Every message is a Photon event: `(byte eventType, object[] data)`. `data[0]` is always an `int` sub-opcode. Below, `ev/sub` means that pair.

Types must match exactly, because the receiver casts with `(int)`, `(string)`, `(bool)` and `(Vector3)`. Lists inside strings use the formats shown. Numbers are parsed with the current culture, which is why the plugin forces `InvariantCulture`.

## Connection and login flow

| Step | Direction | Event | Payload |
|---|---|---|---|
| Login | C→S | 26/1 | username, encrypted pw, client version, steamId |
| Login result | S→C | 0/6 | `AccountLoginPacket` JSON, account name, `LoginResult` JSON (`resultCode > 0` = ok) |
| Create account | C→S | 0/7 | email, encrypted pw, steamId → reply 25/29 int code (1 = ok, client then logs in) |
| Re-request character list | C→S | 6/30 | → reply 7/55 `AccountLoginPacket` JSON |
| Create character | C→S | 7/47 | name → reply 3/60 packet JSON, int code; on success the client sends 7/50 |
| Delete character | C→S | 7/49 | name → reply 3/61 packet JSON, int code |
| Enter world | C→S | 7/50 | character name |
| Spawn local player | S→C | 2/42 | name, `key=value\n` block (see `Scr_PlayerHandler.addPlayer`) |
| Open character creator | S→C | 7/3 | name |
| Creator done | C→S | 7/4 | name |
| Login confirmed | S→C | 25/28 | int (> 0 ok) |
| Change scene | S→C | 3/28 | name, sceneId |
| Scene finished loading | C→S | 6/10 | — |
| Clear loading flag | S→C | 198/10 | — (must arrive before visibility updates) |
| Force position | S→C | 5/3 | name, int type (1 = fade), Vector3 |
| Logout | C→S | 7/7 | name |

## World streaming (S→C)

| Event | Payload |
|---|---|
| 2/2 visible NPCs | name, `uid_type_x,y,z_hp_maxhp_dead(0/1)_customName_animId` joined by `>` |
| 2/4 visible harvestables | name, `uid_type_hp_x,y,z` joined by `>` |
| 2/6 visible loot | name, list |
| 2/17 wayshrines | name, `id,id,id_homeId` |
| 16/1 weather | int time (0–2400), int cloudiness |
| 2/0 chat line | unused, text (optional `color=yellow\|` prefix), int channel |
| 2/41 notification | text |

## Client → server, by area

Payloads are listed after the sub-opcode. "name" means the player name.

- **Movement:** 17/1 sync (name, rot, scene, Vector3); 198/1 jump; 23/1 click-to-move target.
- **Chat:** 2/5 (name, msg). Messages starting with `/` are server commands.
- **Interact:** 3/11 object (name, interactable typeId) → reply 3/12 confirm; 3/8 NPC (name, uid); 3/9 dialogue option (name, optionId).
- **Targeting and combat:** 3/7 select NPC; 3/0 use ability (name, slot); 6/25 cancel attack; 8/7 set ability slot.
- **Items:**
  - 3/4 equip and 3/5 unequip (name, itemId)
  - 3/18 use (name, typeId)
  - 4/4 remove (itemId, typeId, amount, name)
  - 4/14 drop
  - 198/15 swap and 198/17 insert (index1, index2)
  - 198/16 sort
  - 3/17 collect loot (name, lootId)
- **Shops:** 8/8 buy (name, typeId, amount); 4/5 sell (name, typeId, uniqueId, amount).
- **Bank:** 4/11 add and 4/12 remove items; 3/37 silver.
- **Skills:** 8/4 start action (name, actionType, targetId), used for harvesting and crafting.
- **Travel:** 3/39 wayshrine teleport (name, id); 7/11 set home wayshrine.
- **Appearance:** 3/22 gender, 3/23 hair, 3/24 body type, 3/25 eyebrows, 3/26 body size, 4/6 hair colour (r, g, b); 8/9 fighting style and profession.
- **Ignored offline** (heartbeats and telemetry): 7/0, 3/47, 6/27, 7/18, 7/21, 27/0, 198/2, 198/3.

## Items, dialogue and shops (S→C)

| Event | Payload |
|---|---|
| 2/1 add item | item data `Key=Value
...` (Id, Typeid, Amount, Tab, Grade, Slot, stat keys), player name. For stackables, Amount is the delta. |
| 8/0 equip | name, itemId, replaced itemId (0 = none). Send after the world scene has loaded, or the slots stay empty. |
| 3/6 unequip, 4/3 remove | name, itemId [, typeId, amount] |
| 198/37 swap, 198/38 insert | inventory indexes (server list order must match the client's) |
| 3/16 silver, 8/1 XP | name, value / name, skillId, amount |
| 8/13 health | name, current, max |
| 3/10 + 2/3 dialogue | confirm interact (name, uid), then node `nodeId>opt\|opt>var1\|var2`; `-` closes |
| 8/9 quest phase, 3/15 complete | name, quest, phase / name, quest. Rewards must be granted by the server. |
| 0/2 open shop, 2/37 refresh | name, shop name, `0>type-price-stock,...` / shop name, `merchantSilver>type-price-stock,...` |

| 8/4 start action (C→S) | name, type (1 harvest, 2 craft), target (harvestable uid / recipe id) |
| 8/5 action started, 3/13 complete, 3/14 cancel | name, type, target / name, type / name, type. The client re-issues 8/4 for the next item in a crafting batch on 3/13. |
| 13/1 harvestable status | uid, depleted (bool) |

| 3/7 select target (C→S) → 3/19 | name, npc uid (0 clears) |
| 3/0 use ability (C→S) → 3/1 | name, slot (0 = basic weapon attack) |
| 3/20 attack animation | name, attack id (0 = basic attack for the equipped weapon) |
| 12/0 NPC health | uid, current, max, damage type (the client shows the difference as a hit splash) |
| 11/0 NPC attacking | uid, attacking, target player name (the client animates swings at the NPC's attackSpeed) |
| 198/25 + 13/2 NPC move | uid, current pos, waypoint1, waypoint2 / uid, moving. The client walks in a straight line. |
| 13/0 NPC dead, 9/1 set NPC position | uid, dead / uid, pos |
| 8/2 player damage | name, signed delta (negative = damage), damage type |
| 1/2 player dead | name, dead |
| 4/0 ability cooldown | name, slot, current, total (hundredths of a second) |

Town guards are `aggressive` with 1500 damage in NPCInfo. The old server only aimed them at criminals.

Recipe ids are the client's `Script_Crafting.craftingIdCounter`, which counts each `Crafting` stat in item file order. An item without a `Level` attribute has level 0, not 1. The basic tools are hard-coded in `Scr_ItemHandler` (e.g. `isPickaxe` = item 59), not flagged in the XML.

Unity's `JsonUtility` cannot serialize lists of classes defined in a plugin assembly. Use it only for the game's own packet classes.

## Interactable typeIds (`Scr_Interactable.typeId`)

| Type | What |
|---|---|
| 1–8, 17, 18 | Workbenches: forge, anvil, stove, spinning wheel, loom, tailor, grinder, alchemy, carpentry, saw |
| 21 | Wayshrine |
| 27 | Campfire |
| 43 | Bank chest and other chests, wanted board, levers |
| 9–16, 20, 22–25, 28, 29, 33–38, 42, 45 | Doors, gates, hatches and portals. The server mapped each to a destination; this has to be rebuilt by pairing doors across scenes (`GSODevTools/extracted/markers.json`). |
| 37 | Generic doors (158 of them, mostly house and interior doors) |
| 54 | Chairs |

## Data sources

| Data | Source |
|---|---|
| NPC stats, AI parameters | `Resources/XMLs/NPCInfo` |
| Dialogue trees with quest requirements and updates | `Resources/XMLs/Dialogue` |
| Quests and phases | `Resources/XMLs/Quests` |
| Harvestables, drop tables | `Resources/XMLs/HarvestableInfo` |
| Items, crafting recipes | `Resources/Data/items` |
| Abilities, buffs, projectiles | `Resources/XMLs/Abilities`, `Buffs`, `Projectiles` |
| Wayshrines | `Resources/XMLs/Wayshrines` |
| NPC and harvestable spawn points | `Scr_NPCDummy` / `Scr_HarvestableDummy` in each scene |
| **Lost** | NPC loot tables, shop stock, door destinations, server-side formulas (HP, damage, XP) |
