using System.Collections.Generic;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// Tunable combat numbers. The real formulas died with the server; these aim for the feel of the
    /// early game (a level-2 crab takes a handful of sword swings and can't kill a fresh character alone).
    /// </summary>
    internal static class CombatRules
    {
        public static int NpcHealth(int level) => 10 + 8 * Mathf.Max(1, level);
        public static int NpcMaxHit(NpcInfo n) => n.damage > 0 ? n.damage : 2 + 2 * Mathf.Max(1, n.level);
        public static int PlayerMaxHit(int weaponDamage, int skillLevel) => 4 + weaponDamage + skillLevel;
        public static float HitChance = 0.85f;
        public static int KillXp(int npcLevel) => 12 * Mathf.Max(1, npcLevel);
        public static int SilverDrop(int npcLevel) => Random.Range(0, 3 * Mathf.Max(1, npcLevel) + 1);
        public const float LeashDistance = 40f;
        public const float RespawnDelay = 5f;   // seconds dead before waking at the home wayshrine
        public const float RespawnProtection = 10f;

        // Town guards are flagged aggressive with huge damage: on the real server they only went after
        // criminals. There is no crime system offline, so they only fight back when attacked.
        public static bool IsGuard(NpcInfo n)
        {
            string name = (n.name ?? string.Empty).Trim().ToLowerInvariant();
            return name == "guard" || name.EndsWith(" guard");
        }
    }

    public partial class OfflineServer
    {
        private int targetUid;
        private bool autoAttacking;
        private float nextPlayerSwing;
        private float respawnPlayerAt = -1f;
        private float aggroImmuneUntil;
        private bool PlayerDead => respawnPlayerAt > 0f;

        // Bandage-heal abilities (Healing skill) -> bandage item consumed.
        private static readonly Dictionary<int, int> BandageAbilities = new Dictionary<int, int>
        {
            { 72, 10367 }, { 73, 10368 }, { 82, 10369 }, { 83, 10370 },
        };

        private void RegisterCombatHandlers()
        {
            On(3, 7, c => SelectTarget((int)c[2]));
            On(3, 0, c => UseAbility((int)c[2]));
            On(8, 7, c => AssignAbilitySlot((int)c[2], (int)c[3]));
            On(25, 12, c => { });   // auto-target slot hint
        }

        private void ResetCombat()
        {
            targetUid = 0;
            autoAttacking = false;
        }

        private void SelectTarget(int uid)
        {
            targetUid = uid;
            if (uid == 0) autoAttacking = false;
            Send(3, 19, character.name, uid);
        }

        private void AssignAbilitySlot(int slot, int abilityId)
        {
            character.SetAbilitySlot(slot, abilityId);
            Send(8, 6, character.name, slot, abilityId);
        }

        private void UseAbility(int slot)
        {
            if (character == null || PlayerDead) return;
            if (slot == 0)
            {
                var npc = world?.GetNpc(targetUid);
                if (npc == null || npc.dead || !CanFight(npc)) return;
                autoAttacking = true;
                Send(3, 1, character.name, 0);
                return;
            }

            int ability = character.GetAbilitySlot(slot);
            if (BandageAbilities.TryGetValue(ability, out int bandage))
            {
                if (CountItem(bandage) == 0)
                {
                    Notice("You have no bandages of that kind.", "red");
                    return;
                }
                TakeItems(bandage, 1);
                int level = SkillLevel(24);
                Heal(15 + 3 * level);
                AddXp(24, SkillData.BaseXp(level));
                Send(4, 0, character.name, slot, 300, 300);   // 3 s cooldown (hundredths)
                OnHealed();
                return;
            }
            Plugin.Log.LogInfo($"[combat] ability {ability} in slot {slot} not implemented; using a basic attack");
            UseAbility(0);
        }

        private static bool CanFight(NpcEntity npc) =>
            GameData.Npcs.TryGetValue(npc.typeId, out var info) && info.canFight;

        // ---- player attacks ------------------------------------------------------------------

        private ItemSave EquippedWeapon()
        {
            foreach (var it in character.items)
                if (it.equipped && ItemData.Get(it.typeId)?.slot == "Weapon") return it;
            return null;
        }

        // Combat skill trained by the equipped weapon (Scr_SkillsHandler ids).
        private int WeaponSkill(ItemSave weapon)
        {
            if (weapon == null) return 23;   // Wrestling
            var ih = Scr_ItemHandler.instance;
            int t = weapon.typeId;
            if (ih.isBow(t) || ih.isCrossBow(t)) return 17;
            if (ih.isStaff(t)) return 20;
            var flags = ItemData.Get(t)?.flags;
            if (flags != null)
            {
                if (flags.Contains("isDagger")) return 16;
                if (flags.Contains("isMace")) return 18;
            }
            return 15;   // Swordsmanship
        }

        private float WeaponRange(ItemSave weapon)
        {
            if (weapon == null) return 3.5f;
            var ih = Scr_ItemHandler.instance;
            if (ih.isBow(weapon.typeId) || ih.isCrossBow(weapon.typeId) || ih.isStaff(weapon.typeId)) return 25f;
            return 4f;
        }

        private float WeaponSpeed(ItemSave weapon)
        {
            var flags = weapon != null ? ItemData.Get(weapon.typeId)?.flags : null;
            if (flags == null) return 2.4f;
            if (flags.Contains("isDagger")) return 1.8f;
            if (flags.Contains("isGreatsword") || flags.Contains("isHalberd")) return 3.2f;
            return 2.4f;
        }

        private static int StatSum(ItemSave it, params string[] keys)
        {
            int sum = 0;
            if (it == null) return 0;
            foreach (var s in it.stats)
                foreach (string k in keys)
                    if (s.key == k) sum += s.value;
            return sum;
        }

        private void TickPlayerAttack(Vector3 playerPos)
        {
            if (!autoAttacking || PlayerDead || Time.time < nextPlayerSwing) return;
            var npc = world.GetNpc(targetUid);
            if (npc == null || npc.dead)
            {
                autoAttacking = false;
                return;
            }
            var weapon = EquippedWeapon();
            if (Vector3.Distance(NpcPosition(npc), playerPos) > WeaponRange(weapon)) return;   // walk closer

            nextPlayerSwing = Time.time + WeaponSpeed(weapon);
            Send(3, 20, character.name, 0);   // attack id 0: basic attack animation for the equipped weapon
            Aggro(npc);

            int skill = WeaponSkill(weapon);
            int damage = 0;
            if (Random.value < CombatRules.HitChance)
            {
                int weaponDamage = StatSum(weapon, "Damage", "SlashDamage", "BluntDamage", "PiercingDamage");
                damage = Random.Range(1, CombatRules.PlayerMaxHit(weaponDamage, SkillLevel(skill)) + 1);
                if (GameData.Npcs.TryGetValue(npc.typeId, out var info))
                    damage = Mathf.Max(1, damage - info.damageBlock / 10);
            }
            npc.health = Mathf.Max(0, npc.health - damage);
            Send(12, 0, npc.uid, npc.health, npc.maxHealth, 0);
            if (npc.health <= 0) KillNpc(npc, skill);
        }

        private void KillNpc(NpcEntity npc, int skill)
        {
            npc.dead = true;
            npc.aggro = false;
            autoAttacking = false;
            GameData.Npcs.TryGetValue(npc.typeId, out var info);
            npc.respawnAt = Time.time + (info?.respawnTime ?? 60);
            Send(11, 0, npc.uid, false, string.Empty);
            Send(13, 2, npc.uid, false);
            Send(13, 0, npc.uid, true);

            int level = info?.level ?? 1;
            AddXp(skill, CombatRules.KillXp(level));
            int silver = CombatRules.SilverDrop(level);
            if (silver > 0)
            {
                SetSilver(character.silver + silver);
                Notice($"You loot {silver} silver.");
            }
            OnNpcKilled(npc.typeId);
        }

        // ---- NPC AI --------------------------------------------------------------------------

        // The client walks NPCs along the waypoints we give it; its transform is the truth for position.
        private Vector3 NpcPosition(NpcEntity npc)
        {
            var c = Scr_NpcHandler.instance != null ? Scr_NpcHandler.instance.getNpc(npc.uid) : null;
            if (c != null) npc.pos = c.transform.position;
            return npc.pos;
        }

        private void Aggro(NpcEntity npc)
        {
            if (npc.aggro) return;
            npc.aggro = true;
            npc.nextSwing = Time.time + 1f;
        }

        private void MoveNpc(NpcEntity npc, Vector3 to)
        {
            if (Time.time < npc.nextWaypoint) return;
            npc.nextWaypoint = Time.time + 0.5f;
            Send(198, 25, npc.uid, NpcPosition(npc), to, to);
            Send(13, 2, npc.uid, true);
        }

        private void Disengage(NpcEntity npc)
        {
            npc.aggro = false;
            if (npc.attackingSent)
            {
                npc.attackingSent = false;
                Send(11, 0, npc.uid, false, string.Empty);
            }
            npc.returning = true;
            npc.nextWaypoint = 0f;
            MoveNpc(npc, npc.spawnPos);
        }

        private void TickNpcs(Vector3 playerPos)
        {
            foreach (var npc in world.npcs)
            {
                if (npc.dead)
                {
                    if (Time.time >= npc.respawnAt) RespawnNpc(npc);
                    continue;
                }
                if (!GameData.Npcs.TryGetValue(npc.typeId, out var info) || !info.canFight) continue;

                Vector3 pos = NpcPosition(npc);
                float dist = Vector3.Distance(pos, playerPos);
                if (dist > Plugin.NpcViewDistance.Value) continue;

                if (npc.returning)
                {
                    if (Vector3.Distance(pos, npc.spawnPos) < 1.5f)
                    {
                        npc.returning = false;
                        npc.health = npc.maxHealth;
                        Send(13, 2, npc.uid, false);
                        Send(12, 0, npc.uid, npc.health, npc.maxHealth, 0);
                    }
                    else MoveNpc(npc, npc.spawnPos);
                    continue;
                }

                if (!npc.aggro && info.aggressive && !CombatRules.IsGuard(info) && !PlayerDead
                    && Time.time >= aggroImmuneUntil && dist <= info.aggroDistance)
                    Aggro(npc);
                if (!npc.aggro) continue;

                if (PlayerDead || Vector3.Distance(pos, npc.spawnPos) > CombatRules.LeashDistance)
                {
                    Disengage(npc);
                    continue;
                }

                if (dist > info.attackDistance)
                {
                    if (npc.attackingSent)
                    {
                        npc.attackingSent = false;
                        Send(11, 0, npc.uid, false, string.Empty);
                    }
                    MoveNpc(npc, playerPos);
                    continue;
                }

                if (!npc.attackingSent)
                {
                    npc.attackingSent = true;
                    Send(13, 2, npc.uid, false);
                    Send(11, 0, npc.uid, true, character.name);   // client animates swings at attackSpeed
                }
                if (Time.time < npc.nextSwing) continue;
                npc.nextSwing = Time.time + info.attackSpeed;
                NpcHitsPlayer(info);
            }
        }

        private void NpcHitsPlayer(NpcInfo info)
        {
            int damage = 0;
            if (Random.value < CombatRules.HitChance)
            {
                int block = 0;
                foreach (var it in character.items)
                    if (it.equipped) block += StatSum(it, "Damageblock");
                damage = Mathf.Max(0, Random.Range(1, CombatRules.NpcMaxHit(info) + 1) - block / 2);
            }
            character.currentHealth = Mathf.Max(0, character.currentHealth - damage);
            Send(8, 2, character.name, -damage, info.damageType);
            SendHealth();
            if (character.currentHealth <= 0) PlayerDies();
        }

        private void RespawnNpc(NpcEntity npc)
        {
            npc.dead = false;
            npc.aggro = npc.returning = npc.attackingSent = false;
            npc.health = npc.maxHealth;
            npc.pos = npc.spawnPos;
            Send(13, 0, npc.uid, false);
            Send(9, 1, npc.uid, npc.spawnPos);
            Send(12, 0, npc.uid, npc.health, npc.maxHealth, 0);
        }

        // ---- player death --------------------------------------------------------------------

        private void PlayerDies()
        {
            autoAttacking = false;
            respawnPlayerAt = Time.time + CombatRules.RespawnDelay;
            Send(1, 2, character.name, true);
            foreach (var npc in world.npcs)
                if (npc.aggro) Disengage(npc);
        }

        private void TickPlayerRespawn()
        {
            if (!PlayerDead || Time.time < respawnPlayerAt) return;
            respawnPlayerAt = -1f;
            aggroImmuneUntil = Time.time + CombatRules.RespawnProtection;
            character.currentHealth = MaxHealth();
            Send(1, 2, character.name, false);
            SendHealth();
            if (GameData.Wayshrines.TryGetValue(character.homeWayshrine, out var w))
                Teleport(w.scene, w.pos);
            else
                Teleport(StartScene, StartPos);
        }

        private void TickCombat()
        {
            if (character == null || world == null || !sceneReady) return;
            var player = LocalPlayer;
            if (player == null) return;
            Vector3 p = player.transform.position;
            TickPlayerRespawn();
            TickPlayerAttack(p);
            TickNpcs(p);
        }
    }
}
