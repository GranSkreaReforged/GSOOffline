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

## Interactable typeIds (`Scr_Interactable.typeId`)

| Type | What |
|---|---|
| 1–8, 17, 18 | Workbenches: forge, anvil, stove, spinning wheel, loom, tailor, grinder, alchemy, carpentry, saw |
| 21 | Wayshrine |
| 27 | Campfire |
| 43 | Bank chest and other chests, wanted board, levers |
| 9–16, 20, 22–25, 28, 29, 33–38, 42, 45 | Doors, gates, hatches and portals. The server mapped each to a destination; this has to be rebuilt by pairing doors across scenes (`extracted/markers.json`). |
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
