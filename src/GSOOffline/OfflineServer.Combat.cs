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
        public const float AbilityMultiplier = 1.6f;
        public static int KillXp(int npcLevel) => 12 * Mathf.Max(1, npcLevel);
        // Armor skills share a kill's XP if the player was hit this recently before it (community wiki: 30 s).
        public const float ArmorXpWindow = 30f;
        // Silver in the loot bag of NPCs the wiki has no drop table for.
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
        private bool swingShown;   // a 3/20 attack animation was sent since the last 7/2
        private float castEndsAt = -1f;   // when an ability's cast animation is over
        private float nextPlayerSwing;
        private float respawnPlayerAt = -1f;
        private float aggroImmuneUntil;
        private float lastHitTaken = -1000f;
        private bool PlayerDead => respawnPlayerAt > 0f;

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
            StopAttacking();
        }

        /// <summary>
        /// Ends the player's attack on both sides. Each swing (3/20) puts the client's player into its attack
        /// animation, and only 7/2 takes it out again; without it the player stays stuck mid-swing after a kill.
        /// </summary>
        private void StopAttacking()
        {
            bool wasAttacking = autoAttacking || swingShown;
            autoAttacking = swingShown = false;
            castEndsAt = -1f;
            if (wasAttacking && character != null) Send(7, 2, character.name);
        }

        private void SelectTarget(int uid)
        {
            targetUid = uid;
            if (uid == 0) StopAttacking();
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
                CancelJob("attacking");
                ReadyWeapon();
                autoAttacking = true;
                Send(3, 1, character.name, 0);
                return;
            }

            int ability = character.GetAbilitySlot(slot);
            if (!SkillData.Abilities.TryGetValue(ability, out var a))
            {
                UseAbility(0);
                return;
            }
            CancelJob("attacking");
            if (RuleFor(a).kind != AbilityKind.Bandage) ReadyWeapon();
            UseSkillAbility(slot, a);
        }

        private readonly Dictionary<int, float> slotReadyAt = new Dictionary<int, float>();

        private static bool CanFight(NpcEntity npc) =>
            GameData.Npcs.TryGetValue(npc.typeId, out var info) && info.canFight;

        // ---- player attacks ------------------------------------------------------------------

        private ItemSave EquippedWeapon()
        {
            foreach (var it in character.items)
                if (it.equipped && ItemData.Get(it.typeId)?.slot == "Weapon") return it;
            return null;
        }

        // A weapon's "requires<Skill>" flag names the skill it trains (wands: Mental grim, lutes: Healing).
        private static readonly Dictionary<string, int> RequiresSkill = new Dictionary<string, int>
        {
            { "requiresSwordsmanship", 15 }, { "requiresFencing", 16 }, { "requiresArchery", 17 },
            { "requiresMacefighting", 18 }, { "requiresElementalgrim", 20 }, { "requiresMentalgrim", 21 },
            { "requiresHealing", 24 },
        };

        // Combat skill trained by the equipped weapon (Scr_SkillsHandler ids).
        private int WeaponSkill(ItemSave weapon)
        {
            if (weapon == null) return 23;   // Wrestling
            var flags = ItemData.Get(weapon.typeId)?.flags;
            if (flags != null)
                foreach (var kv in RequiresSkill)
                    if (flags.Contains(kv.Key)) return kv.Value;
            var ih = Scr_ItemHandler.instance;
            int t = weapon.typeId;
            if (ih.isBow(t) || ih.isCrossBow(t)) return 17;
            if (ih.isStaff(t)) return 20;
            if (flags != null)
            {
                if (flags.Contains("isDagger")) return 16;
                if (flags.Contains("isMace")) return 18;
                if (flags.Contains("isInstrument")) return 24;
            }
            return 15;   // Swordsmanship
        }

        /// <summary>
        /// Attack reach, as the client's Scr_SkillsHandler.getMeleeWeaponRange: the client stops walking at this
        /// distance, so a shorter server range would leave the player standing there never swinging.
        /// </summary>
        private float WeaponRange(ItemSave weapon)
        {
            const float slack = 1f;   // server and client NPC positions drift a little apart
            if (weapon == null) return 3f + slack;
            var ih = Scr_ItemHandler.instance;
            int t = weapon.typeId;
            if (ih.isBow(t) || ih.isCrossBow(t)) return 50f + slack;
            if (ih.isStaff(t)) return 25f + slack;
            if (ih.isWand(t) || ih.isInstrument(t)) return 20f + slack;
            if (ih.isMace(t)) return 4f + slack;
            if (ih.isDagger(t)) return 3f + slack;
            if (ih.isHalberd(t) || ih.isGreatsword(t)) return 4.5f + slack;
            if (ih.isBroadsword(t)) return 4f + slack;
            return 3f + slack;
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
                StopAttacking();
                return;
            }
            var weapon = EquippedWeapon();
            if (Vector3.Distance(NpcPosition(npc), playerPos) > WeaponRange(weapon)) return;   // walk closer

            nextPlayerSwing = Time.time + WeaponSpeed(weapon) * AttackSpeedMultiplier();
            Send(3, 20, character.name, 0);
            swingShown = true;   // attack id 0: basic attack animation for the equipped weapon
            Aggro(npc);

            int hit = RollPlayerHit(weapon, npc), skill = WeaponSkill(weapon);
            int projectile = WeaponProjectile(weapon);
            if (projectile > 0) ShootAtNpc(projectile, npc, () => { if (!npc.dead) DealDamage(npc, hit, skill); });
            else DealDamage(npc, hit, skill);
        }

        /// <summary>
        /// An ability puts the client's player into its attack animation like a swing does. Buffs, heals and
        /// sweeps with nothing to keep attacking would stay in it, so the cast ends once its attacktime is up.
        /// </summary>
        private void TickCastEnd()
        {
            if (castEndsAt < 0f || Time.time < castEndsAt) return;
            castEndsAt = -1f;
            if (!autoAttacking) StopAttacking();
        }

        // Every damage stat on the weapon: Damage, the physical kinds (SlashDamage...) and the elemental ones
        // (FireDamage, PsychicDamage, DmgPoison...), so staffs, wands and lutes count their damage too.
        private static int WeaponDamage(ItemSave weapon)
        {
            int sum = 0;
            if (weapon == null) return 0;
            foreach (var s in weapon.stats)
                if (s.key.EndsWith("Damage") || s.key == "DmgPoison") sum += s.value;
            return sum;
        }

        private int RollPlayerHit(ItemSave weapon, NpcEntity npc, float power = 1f)
        {
            if (Random.value >= CombatRules.HitChance) return 0;
            int max = CombatRules.PlayerMaxHit(WeaponDamage(weapon), SkillLevel(WeaponSkill(weapon)));
            int damage = Mathf.RoundToInt(Random.Range(1, max + 1) * power * DamageDealtMultiplier());
            if (GameData.Npcs.TryGetValue(npc.typeId, out var info))
                damage = Mathf.Max(1, damage - info.damageBlock / 10);
            return damage;
        }

        private void DealDamage(NpcEntity npc, int damage, int skill)
        {
            if (damage > 0 && damage < npc.health && GameData.Npcs.TryGetValue(npc.typeId, out var hurt))
                NpcSound(npc, hurt.sfxTakeHit);
            npc.health = Mathf.Max(0, npc.health - damage);
            Send(12, 0, npc.uid, npc.health, npc.maxHealth, 0);
            if (npc.health <= 0) KillNpc(npc, skill);
        }

        /// <summary>Dev: kill a visible NPC as the player would (bridge command killnpc).</summary>
        internal void DevKill(int uid)
        {
            var npc = world?.GetNpc(uid);
            if (npc == null || npc.dead)
            {
                Plugin.Log.LogInfo($"[dev] killnpc: no live NPC {uid}");
                return;
            }
            DealDamage(npc, npc.health, WeaponSkill(EquippedWeapon()));
        }

        private void KillNpc(NpcEntity npc, int skill)
        {
            npc.dead = true;
            npc.aggro = false;
            if (npc.uid == targetUid) StopAttacking();
            GameData.Npcs.TryGetValue(npc.typeId, out var info);
            npc.respawnAt = Time.time + (info?.respawnTime ?? 60);
            Send(11, 0, npc.uid, false, string.Empty);
            Send(13, 2, npc.uid, false);
            Send(13, 0, npc.uid, true);
            SetAnim(npc, info?.animDeath ?? NpcAnims.Death);
            if (info != null) NpcSound(npc, info.sfxDeath);

            int level = info?.level ?? 1;
            AwardKillXp(skill, CombatRules.KillXp(level));
            DropLoot(npc, info);
            OnNpcKilled(npc.typeId);
        }

        /// <summary>
        /// Kill XP goes to the weapon skill and, if the player was hit within <see cref="CombatRules.ArmorXpWindow"/>,
        /// is shared with the armor skills worn (light and/or heavy), split evenly as the community wiki describes.
        /// </summary>
        private void AwardKillXp(int weaponSkill, int xp)
        {
            var skills = new List<int> { weaponSkill };
            if (Time.time - lastHitTaken <= CombatRules.ArmorXpWindow)
            {
                foreach (var it in character.items)
                {
                    if (!it.equipped) continue;
                    var flags = ItemData.Get(it.typeId)?.flags;
                    if (flags == null) continue;
                    if (flags.Contains("isLightArmor") && !skills.Contains(25)) skills.Add(25);
                    if (flags.Contains("isHeavyArmor") && !skills.Contains(26)) skills.Add(26);
                }
            }
            foreach (int s in skills) AddXp(s, xp / skills.Count);
        }

        /// <summary>Stunned or rooted NPCs stop where they are (the client keeps walking to the last waypoint).</summary>
        private void HoldNpc(NpcEntity npc)
        {
            Vector3 here = NpcPosition(npc);
            Send(198, 25, npc.uid, here, here, here);
            Send(13, 2, npc.uid, false);
        }

        // ---- NPC AI --------------------------------------------------------------------------

        private void SetAnim(NpcEntity npc, int anim, bool restart = false)
        {
            if (npc.anim == anim && !restart) return;
            npc.anim = anim;
            Send(198, 26, npc.uid, anim);
        }

        private static float AnimLength(int anim)
        {
            var clips = EasyAnimationHandler.instance != null ? EasyAnimationHandler.instance.animations : null;
            return clips != null && anim >= 0 && anim < clips.Length && clips[anim] != null ? clips[anim].length : 1f;
        }

        // Idle or run, following the client's own moving flag (it walks NPCs along our waypoints); attack while a swing plays.
        private void UpdateAnim(NpcEntity npc, NpcInfo info)
        {
            if (Time.time < npc.attackAnimUntil) return;
            var c = Scr_NpcHandler.instance != null ? Scr_NpcHandler.instance.getNpc(npc.uid) : null;
            if (c == null) return;
            int idle = info?.animIdle ?? NpcAnims.Idle, run = info?.animRun ?? NpcAnims.Run;
            SetAnim(npc, c.moving ? run : idle);
        }

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
            Send(198, 30, npc.uid, false);   // run, not walk
            npc.nextSwing = Time.time + 1f;
        }

        private void MoveNpc(NpcEntity npc, Vector3 to)
        {
            if (Time.time < npc.nextWaypoint || Time.time < npc.rootedUntil || Time.time < npc.stunnedUntil) return;
            npc.nextWaypoint = Time.time + 0.5f;
            Send(198, 25, npc.uid, NpcPosition(npc), to, to);
            Send(13, 2, npc.uid, true);
        }

        private void Wander(NpcEntity npc, NpcInfo info)
        {
            npc.nextWander = Time.time + info.wanderFrequency * Random.Range(0.6f, 1.4f);
            Vector2 offset = Random.insideUnitCircle * info.wanderDistance;
            var origin = npc.spawnPos + new Vector3(offset.x, 30f, offset.y);
            // Keep strolls on the ground (the client walks NPCs in straight lines, including height).
            if (!Physics.Raycast(origin, Vector3.down, out var hit, 60f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return;
            if (Mathf.Abs(hit.point.y - npc.spawnPos.y) > 5f) return;
            Send(198, 30, npc.uid, true);   // walk
            npc.nextWaypoint = 0f;
            MoveNpc(npc, hit.point);
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
                if (!NpcShown(npc))
                {
                    npc.aggro = npc.attackingSent = npc.returning = false;
                    continue;
                }
                GameData.Npcs.TryGetValue(npc.typeId, out var info);
                Vector3 pos = NpcPosition(npc);
                float dist = Vector3.Distance(pos, playerPos);
                if (dist > Plugin.NpcViewDistance.Value) continue;
                UpdateAnim(npc, info);
                if (info == null) continue;

                if (!npc.aggro && !npc.returning && info.wandering && Time.time >= npc.nextWander && npc.uid != dialogueNpc?.uid)
                    Wander(npc, info);
                if (!info.canFight) continue;

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
                    && Time.time >= aggroImmuneUntil && dist <= info.aggroDistance && !CalmedBy(info))
                    Aggro(npc);
                if (!npc.aggro) continue;
                if (Time.time < npc.stunnedUntil) continue;   // stunned: no moving, no swinging

                if (PlayerDead || Vector3.Distance(pos, npc.spawnPos) > CombatRules.LeashDistance)
                {
                    Disengage(npc);
                    continue;
                }

                // Ranged NPCs (a projectile and a share of attacks that use it) fight from projectileattackdistance.
                bool ranged = info.projectile > 0 && info.projectileRate > 0;
                float reach = ranged ? Mathf.Max(info.attackDistance, info.projectileDistance) : info.attackDistance;
                if (dist > reach)
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
                SetAnim(npc, info.animAttack, true);
                npc.attackAnimUntil = Time.time + Mathf.Min(info.attackSpeed, AnimLength(info.animAttack));
                NpcSound(npc, info.sfxAttack);
                if (ranged && (dist > info.attackDistance || Random.Range(0, 100) < info.projectileRate))
                {
                    var shooter = npc;
                    var shooterInfo = info;
                    NpcShoots(shooter, info.projectile, () => { if (!PlayerDead && !shooter.dead) NpcHitsPlayer(shooterInfo, shooter); });
                }
                else NpcHitsPlayer(info, npc);
            }
        }

        private void NpcHitsPlayer(NpcInfo info, NpcEntity attacker)
        {
            int damage = 0;
            if (Random.value < CombatRules.HitChance)
            {
                int block = 0;
                foreach (var it in character.items)
                    if (it.equipped) block += StatSum(it, "Damageblock");
                damage = Mathf.Max(0, Random.Range(1, CombatRules.NpcMaxHit(info) + 1) - block / 2);
                lastHitTaken = Time.time;
                damage = ApplyDefensiveBuffs(damage, attacker);
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
            npc.attackAnimUntil = 0f;
            npc.stunnedUntil = npc.rootedUntil = 0f;
            npc.dotTicks = 0;
            Send(13, 0, npc.uid, false);
            Send(9, 1, npc.uid, npc.spawnPos);
            Send(12, 0, npc.uid, npc.health, npc.maxHealth, 0);
            SetAnim(npc, NpcAnims.IdleOf(npc.typeId));
        }

        // ---- player death --------------------------------------------------------------------

        private void PlayerDies()
        {
            StopAttacking();
            ClearBuffs();
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
            TickBuffs();
            TickNpcEffects();
            TickCastEnd();
            TickPlayerAttack(p);
            TickNpcs(p);
        }
    }
}
