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
        private float lastPlayerSwing, measureSwingAt = -1f;
        // The current swing's hit or shot, waiting for its moment in the animation: called once per strike of the
        // clip with (strike, strikes), so a flurry deals its damage a share at a time.
        private System.Action<int, int> pendingBlow;
        private bool measuringCast;          // the animation being measured is an ability's
        private float pendingBlowFallback;   // a cast's blow time (its attackdelay) when the clip isn't in ClipImpact
        // Seconds the client's basic attack animation lasts, per weapon type (measured as it plays).
        private readonly Dictionary<int, float> swingLength = new Dictionary<int, float>();
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
            pendingBlow = null;   // a swing from the old scene
            measureSwingAt = -1f;
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
            if (wasAttacking) ProfileEvent("end");
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
            MeasureSwing();
            if (!autoAttacking || PlayerDead || Time.time < nextPlayerSwing) return;
            var npc = world.GetNpc(targetUid);
            if (npc == null || npc.dead)
            {
                StopAttacking();
                return;
            }
            var weapon = EquippedWeapon();
            if (Vector3.Distance(NpcPosition(npc), playerPos) > WeaponRange(weapon)) return;   // walk closer

            // One swing per play of the attack animation. Damage per hit scales so damage per second stays that of
            // a swing every WeaponSpeed seconds, and attack speed buffs add damage rather than cut the animation short.
            float interval = swingLength.TryGetValue(weapon?.typeId ?? 0, out float len) ? len : WeaponSpeed(weapon);
            nextPlayerSwing = Time.time + interval;
            lastPlayerSwing = Time.time;
            ProfileSwingStarted(0);
            ResolvePendingBlow();
            measureSwingAt = Time.time + 0.25f;
            measuringCast = false;
            Send(3, 20, character.name, 0);
            swingShown = true;   // attack id 0: basic attack animation for the equipped weapon
            Aggro(npc);
            WeaponSounds(weapon, out int swingSfx, out int hitSfx);
            int projectile = WeaponProjectile(weapon);
            if (projectile == 0) PlaySound(swingSfx, playerPos);   // a shot's sound goes with the release

            int hit = RollPlayerHit(weapon, npc, interval / (WeaponSpeed(weapon) * AttackSpeedMultiplier()));
            int skill = WeaponSkill(weapon);
            System.Action land = () =>
            {
                if (npc.dead) return;
                if (hit > 0) PlaySound(hitSfx, NpcPosition(npc));
                DealDamage(npc, hit, skill);
            };
            pendingBlow = (strike, strikes) =>
            {
                if (npc.dead || PlayerDead || strike > 0) return;
                if (projectile == 0)
                {
                    land();
                    return;
                }
                var pl = LocalPlayer;
                if (pl != null) PlaySound(swingSfx, pl.transform.position);
                ShootAtNpc(projectile, npc, land);
            };
        }

        // When the blows land (or the shot leaves) in each attack clip, in seconds, measured in-game with the
        // swingprofile bridge command: the weapon hand's fastest movements, just before they stop. Unlisted basic
        // attacks land 40% in; unlisted casts at their attackdelay. Generic clip names are keyed with their length.
        private static readonly Dictionary<string, float[]> ClipImpact = new Dictionary<string, float[]>
        {
            { "169_standing_melee_attack_horizontal 1", new[] { 0.65f } },   // broadsword, dagger, mace; strike abilities
            { "2Hand-Sword-Attack1", new[] { 0.7f } },                       // greatsword
            { "Unarmed-Attack-R3", new[] { 0.5f } },                         // fists, wands
            { "Standing_1H_Magic_Attack_02", new[] { 0.5f } },               // staffs, Fire ball
            { "Standing_1H_Magic_Attack_03", new[] { 0.3f } },               // lutes
            { "2Hand-Bow-Attack3", new[] { 1f } },                           // bows and arrow abilities: the release
            { "Armature|Anim@3.2", new[] { 0.5f } },                         // halberds
            { "Armature|Anim@3.6", new[] { 0.55f, 1.2f, 1.85f, 2.35f, 3f } },   // Slashing: a flurry of five
            { "Armature|CrossbowShoot", new[] { 0.3f } },                    // crossbows (the hand doesn't move; a guess)
        };

        private void ResolvePendingBlow()
        {
            var blow = pendingBlow;
            pendingBlow = null;
            measureSwingAt = -1f;
            blow?.Invoke(0, 1);
        }

        /// <summary>
        /// Each 3/20 restarts the client's attack animation, which loops while attacking. Swinging on a timer of our
        /// own cut it off midway, so shortly after a swing starts (once the animator is in the attack state) this
        /// reads the clip's length and times the next swing to its end. Standing and walking attacks differ.
        /// </summary>
        private void MeasureSwing()
        {
            if (measureSwingAt < 0f || Time.time < measureSwingAt) return;
            measureSwingAt = -1f;
            var blow = pendingBlow;
            pendingBlow = null;
            float[] impacts = { 0f };
            var anim = LocalPlayer != null ? LocalPlayer.anim : null;
            if (anim != null && anim.animator != null && anim.attackId >= 0)
            {
                // Layer 5 holds the weapon's attack clip (layer 3, used on the move, has a placeholder).
                var a = anim.animator;
                bool next = a.IsInTransition(5);
                var state = next ? a.GetNextAnimatorStateInfo(5) : a.GetCurrentAnimatorStateInfo(5);
                var clips = next ? a.GetNextAnimatorClipInfo(5) : a.GetCurrentAnimatorClipInfo(5);
                if (state.length >= 0.5f && state.length <= 5f)
                {
                    if (measuringCast)
                    {
                        // Abilities reuse weapon clips whose length differs from their attacktime: end the cast
                        // (and resume basic attacks) when the clip does, rather than cut it or start it again.
                        castEndsAt = lastPlayerSwing + state.length;
                        nextPlayerSwing = castEndsAt;
                    }
                    else
                    {
                        swingLength[EquippedWeapon()?.typeId ?? 0] = state.length;
                        nextPlayerSwing = lastPlayerSwing + state.length;
                    }
                }
                string clip = clips.Length > 0 && clips[0].clip != null ? clips[0].clip.name : string.Empty;
                string key = clip + "@" + state.length.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
                if (!ClipImpact.TryGetValue(key, out impacts) && !ClipImpact.TryGetValue(clip, out impacts))
                    impacts = new[] { measuringCast ? pendingBlowFallback : 0.4f * Mathf.Min(state.length, 3f) };
            }
            else if (measuringCast) impacts = new[] { pendingBlowFallback };
            if (blow == null) return;
            for (int i = 0; i < impacts.Length; i++)
            {
                int strike = i, strikes = impacts.Length;
                float wait = lastPlayerSwing + impacts[i] - Time.time;
                if (wait > 0.02f) Later(wait, () => blow(strike, strikes));
                else blow(strike, strikes);
            }
        }

        /// <summary>
        /// The player's attack sounds were the server's to send and no data names them for basic attacks. These are
        /// the ids the ability data gives weapon blows: "Swing" and Brutal slash (321 swing, 320 impact), Stab (93),
        /// the arrows (34). Staffs, wands and lutes shoot projectiles, which bring their own impact sound.
        /// </summary>
        private static void WeaponSounds(ItemSave weapon, out int swing, out int hit)
        {
            swing = 321;
            hit = 320;
            if (weapon == null) return;
            var ih = Scr_ItemHandler.instance;
            int t = weapon.typeId;
            if (ih.isBow(t) || ih.isCrossBow(t))
            {
                swing = 34;
                hit = 0;
            }
            else if (ih.isStaff(t) || ih.isWand(t) || ih.isInstrument(t)) swing = hit = 0;
            else if (ih.isDagger(t)) hit = 93;
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
            ProfileEvent("hit");
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
            npc.hasDest = false;
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
            npc.dest = to;
            npc.hasDest = true;
            StepNpc(npc);
        }

        /// <summary>
        /// The client walks an NPC in a straight line to its waypoint, height included, so a far waypoint over
        /// a hump takes it through the ground. Each waypoint is a short step towards the destination, put on
        /// the ground, and the next one goes out before the NPC reaches it.
        /// </summary>
        private void StepNpc(NpcEntity npc)
        {
            if (Time.time < npc.nextWaypoint || Time.time < npc.rootedUntil || Time.time < npc.stunnedUntil) return;
            var c = Scr_NpcHandler.instance != null ? Scr_NpcHandler.instance.getNpc(npc.uid) : null;
            if (c == null) return;
            npc.nextWaypoint = Time.time + 0.5f;
            Vector3 from = c.transform.position;
            npc.pos = from;
            Vector3 flat = npc.dest - from;
            flat.y = 0f;
            float step = Mathf.Max(2f, c.walking ? c.walkspeed : c.movementSpeed);   // about a second of walking
            Vector3 to = npc.dest;
            if (flat.magnitude > step)
            {
                to = from + flat.normalized * step;
                to.y = Mathf.Lerp(from.y, npc.dest.y, step / flat.magnitude);
                float top = Mathf.Max(from.y, npc.dest.y) + 2.5f;
                if (GroundAt(to, top, top - Mathf.Min(from.y, npc.dest.y) + 8f, out var ground)) to = ground;
            }
            else if (flat.magnitude < 0.3f)
            {
                // Arrived. Without this the client keeps the run animation going until its own 2-second stall check.
                npc.hasDest = false;
                Send(13, 2, npc.uid, false);
                return;
            }
            Send(198, 25, npc.uid, from, to, to);
            Send(13, 2, npc.uid, true);
        }

        /// <summary>The first solid surface straight down from height top above p, ignoring NPCs and players.</summary>
        private static bool GroundAt(Vector3 p, float top, float depth, out Vector3 ground)
        {
            ground = p;
            float best = float.MaxValue;
            foreach (var h in Physics.RaycastAll(new Vector3(p.x, top, p.z), Vector3.down, depth, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (h.distance >= best || h.collider.GetComponentInParent<Scr_Npc>() != null
                    || h.collider.GetComponentInParent<Scr_Player>() != null || h.collider.GetComponentInParent<Scr_OtherPlayer>() != null)
                    continue;
                best = h.distance;
                ground = h.point;
            }
            return best < float.MaxValue;
        }

        /// <summary>Dev: how far the nearest client NPCs stand above the ground (bridge command npcground).</summary>
        internal void LogNpcGround(int count)
        {
            var player = LocalPlayer;
            if (player == null || Scr_NpcHandler.instance == null) return;
            Vector3 me = player.transform.position;
            var list = new List<Scr_Npc>(Scr_NpcHandler.instance.npcList);
            list.RemoveAll(x => x == null);
            list.Sort((a, b) => (a.transform.position - me).sqrMagnitude.CompareTo((b.transform.position - me).sqrMagnitude));
            for (int i = 0; i < list.Count && i < count; i++)
            {
                Vector3 p = list[i].transform.position;
                string above = GroundAt(p, p.y + 5f, 30f, out var g) ? (p.y - g.y).ToString("F2") : "no ground";
                var server = world?.GetNpc(list[i].npcUniqueId);
                Vector3 forward = list[i].transform.forward, toMe = me - p;
                forward.y = toMe.y = 0f;
                Plugin.Log.LogInfo($"[dev] {list[i].npcUniqueId} type={list[i].npcTypeId} d={Vector3.Distance(p, me):F1} moving={list[i].moving} anim={server?.anim} facing-me-off={Vector3.Angle(forward, toMe):F0}deg above-ground={above}");
            }
        }

        private void Wander(NpcEntity npc, NpcInfo info)
        {
            npc.nextWander = Time.time + info.wanderFrequency * Random.Range(0.6f, 1.4f);
            Vector2 offset = Random.insideUnitCircle * info.wanderDistance;
            // A spot on the ground within 5 m of the spawn's height (not a roof above it or a cliff below).
            if (!GroundAt(npc.spawnPos + new Vector3(offset.x, 0f, offset.y), npc.spawnPos.y + 5f, 10f, out var spot)) return;
            Send(198, 30, npc.uid, true);   // walk
            npc.nextWaypoint = 0f;
            MoveNpc(npc, spot);
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

                if (npc.hasDest) StepNpc(npc);
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
                    npc.hasDest = false;
                    Send(13, 2, npc.uid, false);
                    Send(11, 0, npc.uid, true, character.name);   // client animates swings at attackSpeed
                    Send(198, 28, npc.uid, playerPos);   // turn to the player: the client only turns NPCs as they walk
                }
                if (Time.time < npc.nextSwing) continue;
                npc.nextSwing = Time.time + info.attackSpeed;
                Send(198, 28, npc.uid, playerPos);
                SetAnim(npc, info.animAttack, true);
                npc.attackAnimUntil = Time.time + Mathf.Min(info.attackSpeed, AnimLength(info.animAttack));
                NpcSound(npc, info.sfxAttack);
                bool shoot = ranged && (dist > info.attackDistance || Random.Range(0, 100) < info.projectileRate);
                var attacker = npc;
                var attackerInfo = info;
                // The blow lands attackdelay into the swing, in time with the animation.
                Later(Mathf.Min(info.attackDelay, info.attackSpeed), () => NpcSwingLands(attacker, attackerInfo, shoot));
            }
        }

        private void NpcSwingLands(NpcEntity npc, NpcInfo info, bool shoot)
        {
            var player = LocalPlayer;
            if (npc.dead || !npc.aggro || PlayerDead || player == null || Time.time < npc.stunnedUntil) return;
            if (shoot)
            {
                NpcShoots(npc, info.projectile, () => { if (!PlayerDead && !npc.dead) NpcHitsPlayer(info, npc); });
                return;
            }
            // Stepping out of reach during the wind-up dodges the blow.
            if (Vector3.Distance(NpcPosition(npc), player.transform.position) > info.attackDistance + info.attackMissDistance)
            {
                Send(8, 2, character.name, 0, info.damageType);
                return;
            }
            NpcHitsPlayer(info, npc);
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
            npc.hasDest = false;
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
            TickSwingProfile();
            TickPlayerAttack(p);
            TickNpcs(p);
        }
    }
}
