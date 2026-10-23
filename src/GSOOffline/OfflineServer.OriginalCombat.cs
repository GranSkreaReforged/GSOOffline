using System.Collections.Generic;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// A projectile's damage as the original server's Projectiles.xml had it: shot without a damage of its own, it
    /// rolls min..max percent of 1 + 10 x the skill's level (+ 10 x the weapon's or shield's level), plus your
    /// Damage and the damage stat of its kind on top of the maximum.
    /// </summary>
    internal class ProjectileDamage
    {
        public int min, max, type, skill;
        public bool weaponBoost, shieldBoost;

        public ProjectileDamage(int min, int max, int type, int skill = 0, bool weaponBoost = false, bool shieldBoost = false)
        {
            this.min = min;
            this.max = max;
            this.type = type;
            this.skill = skill;
            this.weaponBoost = weaponBoost;
            this.shieldBoost = shieldBoost;
        }
    }

    /// <summary>Damage over time on an NPC: the original's NPC buffs (ignite, combustion, ooze, venom, burns, bleeding,
    /// weak poison) and the poison arrow's poison.</summary>
    public class NpcDot
    {
        public int id;           // the original's buff id, so a second dose of the same one doesn't stack
        public int amount;       // damage per tick (bleeding: worked out from the NPC's health each tick)
        public int type;         // DamageKinds index
        public float interval;   // seconds between ticks
        public float nextAt, until;
        public bool byPlayer;    // counts the player's gear and grade, as the original passed the caster along
    }

    /// <summary>
    /// The original server's combat (update 136, Scr_PlayerAttacksHandler, Scr_NpcHandler, Scr_PlayerHandler,
    /// Scr_Projectile), with the later patch notes' changes on top where they give numbers. Every hit is worked out
    /// as the original did it: your stats, the attack's own formula and damage kind, then the target's resistance to
    /// that kind, its defence roll against your accuracy and the damage it blocks.
    /// </summary>
    public partial class OfflineServer
    {
        // Skill ids (Scr_SkillsHandler).
        private const int SkillSwords = 15, SkillFencing = 16, SkillArchery = 17, SkillMace = 18, SkillWrestling = 23,
            SkillHealing = 24, SkillLightArmor = 25, SkillHeavyArmor = 26;

        private const int SnowmanType = 267, SnowBossType = 323, SnowBossMinionType = 324;

        // Projectiles.xml (the original server's copy: the client's has no damage).
        private static readonly Dictionary<int, ProjectileDamage> ProjectileDamages = new Dictionary<int, ProjectileDamage>
        {
            { 1, new ProjectileDamage(60, 100, DamageKinds.Fire, SkillElementalGrim) },          // fire ball, flame bolt
            { 2, new ProjectileDamage(1, 100, DamageKinds.Fire, SkillElementalGrim, true) },     // fire staff
            { 3, new ProjectileDamage(1, 100, DamageKinds.Earth, SkillElementalGrim, true) },
            { 4, new ProjectileDamage(1, 100, DamageKinds.Wind, SkillElementalGrim, true) },
            { 5, new ProjectileDamage(1, 70, DamageKinds.Piercing, SkillArchery, true) },        // arrow
            { 6, new ProjectileDamage(30, 120, DamageKinds.Piercing, SkillArchery, true) },      // crossbow bolt
            { 7, new ProjectileDamage(50, 80, DamageKinds.Ice, 0, true) },                       // ice wand
            { 8, new ProjectileDamage(50, 80, DamageKinds.Electric, 0, true) },                  // electric wand
            { 9, new ProjectileDamage(50, 150, DamageKinds.Slash, SkillSwords, true) },          // sword throw
            { 10, new ProjectileDamage(75, 150, DamageKinds.Fire, SkillArchery, true) },         // explosive arrow
            { 11, new ProjectileDamage(75, 120, DamageKinds.Fire, SkillArchery, true) },         // fire arrow
            { 12, new ProjectileDamage(75, 120, DamageKinds.Ice, SkillArchery, true) },          // ice arrow
            { 13, new ProjectileDamage(75, 120, DamageKinds.Electric, SkillArchery, true) },     // electric arrow
            { 14, new ProjectileDamage(0, 0, 0) }, { 15, new ProjectileDamage(0, 0, 0) },       // poison, weakness arrow
            { 16, new ProjectileDamage(1, 200, DamageKinds.Piercing, SkillArchery, true) },      // gun bullet
            { 17, new ProjectileDamage(75, 120, DamageKinds.Ice) },                              // guard freeze arrow
            { 18, new ProjectileDamage(75, 120, DamageKinds.Ice) },                              // water spirit
            { 19, new ProjectileDamage(0, 0, 0) },                                               // hunting net
            { 20, new ProjectileDamage(120, 150, DamageKinds.Piercing, SkillArchery, true) },    // piercing shot
            { 21, new ProjectileDamage(20, 100, DamageKinds.Poison) },                           // crawler
            { 23, new ProjectileDamage(75, 120, DamageKinds.Psychic, SkillElementalGrim) },      // skull spell, Roke's staff
            { 24, new ProjectileDamage(75, 120, DamageKinds.Psychic, SkillHealing) },            // lute
            { 25, new ProjectileDamage(75, 120, DamageKinds.Psychic, SkillHealing) },            // Tritone
            { 26, new ProjectileDamage(20, 100, DamageKinds.Poison) },                           // silk crab
            { 27, new ProjectileDamage(0, 0, DamageKinds.Fire) },                                // flame lance
            { 28, new ProjectileDamage(1, 100, DamageKinds.Ice, SkillElementalGrim, true) },     // ice staff
            { 29, new ProjectileDamage(20, 100, DamageKinds.Blunt) },                            // golem rock
            { 30, new ProjectileDamage(80, 120, DamageKinds.Piercing) },                         // lizardman spear
            { 31, new ProjectileDamage(50, 200, DamageKinds.Psychic) },                          // wraith boss
            { 32, new ProjectileDamage(0, 0, DamageKinds.Piercing) },                            // vampiric arrow
            { 35, new ProjectileDamage(50, 100, 0) },                                            // ooze
            { 37, new ProjectileDamage(0, 0, DamageKinds.Earth) },                               // stranglevine
            { 38, new ProjectileDamage(50, 100, 0) },                                            // spider venom
            { 42, new ProjectileDamage(0, 0, DamageKinds.Ice) }, { 43, new ProjectileDamage(0, 0, DamageKinds.Ice) },   // snowballs
            { 45, new ProjectileDamage(75, 80, DamageKinds.Psychic, SkillElementalGrim) },       // diminish
            { 46, new ProjectileDamage(50, 100, DamageKinds.Poison) },                           // venom arrow
            { 47, new ProjectileDamage(50, 100, DamageKinds.Poison) },                           // thorny vines
            { 53, new ProjectileDamage(0, 0, DamageKinds.Psychic) },                             // dark tamer
            { 54, new ProjectileDamage(0, 0, DamageKinds.Piercing) },                            // dagger throw
            { 55, new ProjectileDamage(0, 0, DamageKinds.Wind) },                                // harpy cyclone
            { 57, new ProjectileDamage(0, 0, DamageKinds.Ice) },                                 // large snowball
        };

        private static int ProjectileType(int projectile) => ProjectileDamages.TryGetValue(projectile, out var p) ? p.type : 0;

        // ---- your stats, as Scr_StatsHandler.getStat: gear plus the buffs that add to them ----------------------

        private int WeaponLevel(ItemSave it) => it != null ? ItemData.Get(it.typeId)?.level ?? 0 : 0;

        private ItemSave EquippedInSlot(string slot)
        {
            foreach (var it in character.items)
                if (it.equipped && ItemData.Get(it.typeId)?.slot == slot) return it;
            return null;
        }

        /// <summary>Damage: gear, Psychic glyph (7.5 x 20 while you stay on it), and food (twice its value).</summary>
        private int StatDamage() => GearStat("Damage") + (GlyphActive() ? GlyphDamage : 0) + IconAmount(BuffFood);

        /// <summary>A kind's damage stat (SlashDamage, FireDamage...): gear plus Strength, Immolate, Fire wisp and the glyph.</summary>
        private int StatDamageOf(int kind)
        {
            if (kind <= 0 || kind >= DamageKinds.Count) return 0;
            int v = GearStat(DamageKinds.DamageStat(kind));
            if (kind == DamageKinds.Slash) v += BuffAmount(AbilityStrength);
            if (kind == DamageKinds.Fire) v += BuffAmount(AbilityImmolate) + BuffAmount(AbilityFireWisp) + BuffAmount(AbilityFireWisp2);
            if (kind == DamageKinds.Psychic && GlyphActive()) v += GlyphDamage;
            return v;
        }

        /// <summary>A kind's resistance before the soft cap: gear plus Flame barrier, Ice barrier, Immolate, the glyph,
        /// resistance potions and Weakness.</summary>
        private int StatResistance(int kind)
        {
            if (kind <= 0 || kind >= DamageKinds.Count) return 0;
            int v = GearStat(DamageKinds.ResistanceStat(kind)) + BuffResistance(kind);
            if (HasBuffFrom(AbilityFlameBarrier) && (kind == DamageKinds.Fire || kind == DamageKinds.Ice || kind == DamageKinds.Earth || kind == DamageKinds.Wind)) v += 15;
            if (HasBuffFrom(AbilityIceBarrier) && IsPhysical(kind)) v += 30;
            if (HasBuffFrom(AbilityImmolate)) v += kind == DamageKinds.Fire ? 10 : kind == DamageKinds.Ice ? -10 : 0;
            if (kind == DamageKinds.Psychic && GlyphActive()) v += GlyphResistance;
            return v;
        }

        private static bool IsPhysical(int kind) => kind == DamageKinds.Slash || kind == DamageKinds.Blunt || kind == DamageKinds.Piercing;

        // The original's player accuracy: 0-99, half that on the move.
        private float movingUntil;
        private Vector3 lastMovePos;

        private void TrackMovement(Vector3 p)
        {
            if ((p - lastMovePos).sqrMagnitude > 0.0004f) movingUntil = Time.time + 0.5f;
            lastMovePos = p;
        }

        private bool PlayerMoving() => Time.time < movingUntil;

        private int PlayerAccuracy() => Random.Range(0, PlayerMoving() ? 50 : 100);

        // ---- combat experience ---------------------------------------------------------------------------------

        // Each combat skill you use (a swing, an ability, a blow you blocked with your armor) is counted for 20
        // seconds; a kill's experience is shared evenly between the skills counted then (Scr_SkillsHandler.AddCombatXp).
        private readonly Dictionary<int, float> xpCountedUntil = new Dictionary<int, float>();

        private void CountXp(int skill)
        {
            if (skill > 0) xpCountedUntil[skill] = Time.time + CombatRules.XpCounterSeconds;
        }

        private void AwardKillXp(NpcInfo info)
        {
            int level = info?.level ?? 1;
            int xp = CombatRules.KillXp(level) * (info != null && info.boss ? CombatRules.BossXpMultiplier : 1);
            var skills = new List<int>();
            foreach (var kv in xpCountedUntil)
                if (Time.time < kv.Value) skills.Add(kv.Key);
            if (skills.Count == 0)
            {
                Plugin.Log.LogInfo($"[combat] {info?.name} killed with no combat skill used in the last {CombatRules.XpCounterSeconds}s: no experience");
                return;
            }
            foreach (int s in skills)
            {
                int share = Mathf.RoundToInt((float)xp / skills.Count);
                share = (int)(share * CombatRules.XpShareAgainst(SkillLevel(s), level));
                AddXp(s, share);
            }
        }

        // ---- the player's blows ------------------------------------------------------------------------------

        /// <summary>A blow as the original rolled it, before the target's defences.</summary>
        private struct Blow
        {
            public int damage, type, projectile;   // projectile 0: a melee blow
            public bool explicitDamage;            // the shot carries this damage (otherwise its template rolls it)
        }

        private float punchStunReadyAt;   // a punch stuns at most every 10 seconds

        /// <summary>The basic attack with what's in hand (weaponAttack, bowAttack, staffAttack, luteAttack...).</summary>
        private Blow BasicBlow(ItemSave weapon)
        {
            var ih = Scr_ItemHandler.instance;
            int t = weapon?.typeId ?? 0;
            int wl = WeaponLevel(weapon), D = StatDamage();
            var b = new Blow { explicitDamage = true };
            if (weapon != null && (ih.isBroadsword(t) || ih.isGreatsword(t) || ih.isHalberd(t)))
            {
                CountXp(SkillSwords);
                float r = ih.isBroadsword(t) ? Random.Range(0.5f, 1f) : ih.isGreatsword(t) ? Random.Range(0.5f, 1.5f) : Random.Range(0.8f, 1.25f);
                b.damage = (int)((SkillLevel(SkillSwords) * 10f + wl * 10 + D + StatDamageOf(DamageKinds.Slash)) * r);
                b.damage = HackAndSlash(b.damage);
                b.type = t == 401 ? DamageKinds.Piercing : DamageKinds.Slash;   // the pitchfork pierces
            }
            else if (weapon != null && ih.isDagger(t))
            {
                CountXp(SkillFencing);
                b.damage = Mathf.RoundToInt(Random.Range(1f, 1 + (wl * 10 + SkillLevel(SkillFencing) * 10) * 0.7f + StatDamageOf(DamageKinds.Piercing))) + D;
                if (HasBuffFrom(AbilityFrenzy)) b.damage = (int)(b.damage * 1.1f);
                b.type = DamageKinds.Piercing;
            }
            else if (weapon != null && ih.isMace(t))
            {
                CountXp(SkillMace);
                b.damage = Mathf.RoundToInt(Random.Range(1f, 1 + (wl * 10 + SkillLevel(SkillMace) * 10) + StatDamageOf(DamageKinds.Blunt))) + D;
                b.type = DamageKinds.Blunt;
            }
            else if (weapon != null && (ih.isBow(t) || ih.isCrossBow(t)))
            {
                CountXp(SkillArchery);
                float r = ih.isBow(t) ? Random.Range(0.4f, 0.9f) : Random.Range(0.6f, 1.3f);
                b.damage = (int)((SkillLevel(SkillArchery) * 10f + wl * 10 + D + StatDamageOf(DamageKinds.Piercing)) * r);
                if (ih.isCrossBow(t) && Wears("barrage") && HasBuffFrom(AbilityFocus)) b.damage = (int)(b.damage * 0.85f);   // the Barrage's faster shots
                b.projectile = ih.isBow(t) ? (HasBuffFrom(AbilityFireArrows) ? 41 : 5) : 6;
                b.type = DamageKinds.Piercing;
            }
            else if (weapon != null && ih.isInstrument(t))
            {
                CountXp(SkillHealing);
                b.damage = (int)((SkillLevel(SkillHealing) * 15f + wl * 15 + (D + StatDamageOf(DamageKinds.Psychic))) * Random.Range(0.8f, 1.2f));
                b.projectile = 24;
                b.type = DamageKinds.Psychic;
            }
            else if (weapon != null && (ih.isStaff(t) || ih.isWand(t)))
            {
                CountXp(SkillElementalGrim);
                int ele = SkillLevel(SkillElementalGrim);
                b.projectile = WeaponProjectile(weapon);
                if (t == 380)   // Bal Sardan wand
                    b.damage = (int)((1 + (D + StatDamageOf(DamageKinds.Fire)) * 1.3f + ele * 5f + SkillLevel(SkillHealing) * 5f) * Random.Range(0.7f, 1f));
                else if (t == 274 || t == 473 || ih.isWand(t))
                {
                    // Roke's and Lydia's staffs and the wands shoot their projectile's own damage.
                    if (!ProjectileDamages.ContainsKey(b.projectile)) b.projectile = 7;
                    b.explicitDamage = false;
                }
                else if (t == 352 || (ItemData.Get(t)?.flags.Contains("isIceStaff") ?? false))
                    b.damage = (int)((1 + (D + StatDamageOf(DamageKinds.Ice)) * 1.3f + ele * 5f) * Random.Range(0.7f, 1f));
                else
                    b.damage = (int)((1 + (D + StatDamageOf(DamageKinds.Fire)) * 1.3f + ele * 10f) * Random.Range(0.7f, 1f));
                b.type = ProjectileType(b.projectile);
                if (b.type == 0 && t != 380) b.type = DamageKinds.Fire;
            }
            else
            {
                // Fists, tools and anything else: a punch, which stuns at most every 10 seconds. In Jin it counts
                // Swordsmanship (and Hack and slash builds it up), in Li it slashes, and Bruno's bracers add half.
                bool jin = Wears("jin"), li = Wears("li");
                int skill = jin ? SkillSwords : SkillWrestling;
                b.type = li ? DamageKinds.Slash : DamageKinds.Blunt;
                CountXp(skill);
                b.damage = Mathf.RoundToInt(Random.Range(1f, 1 + (wl * 10 + SkillLevel(skill) * 10) * 0.3f + StatDamageOf(b.type))) + D;
                if (jin) b.damage = HackAndSlash(b.damage);
                if (Wears("brunosBracers")) b.damage = (int)(b.damage * 1.5f);
            }
            return b;
        }

        /// <summary>Hack and slash: each sword blow while it lasts adds 15% to the next, up to double, and keeps it going 5 s.</summary>
        private int HackAndSlash(int damage)
        {
            var hs = buffs.Find(x => x.ability == AbilityHackAndSlash);
            if (hs == null || hs.multiplier >= 2f) return damage;
            hs.multiplier += 0.15f;
            hs.until = Time.time + 5f;
            PlayPlayerEffect(91);
            return (int)(damage * hs.multiplier);
        }

        /// <summary>
        /// The basic attack interval (Scr_ItemHandler.getWeaponAttackTime). Focus makes crossbows fire every half
        /// second and Frenzy daggers stab every 0.8.
        /// </summary>
        private float WeaponSpeed(ItemSave weapon)
        {
            var ih = Scr_ItemHandler.instance;
            if (weapon != null && ih.isCrossBow(weapon.typeId) && HasBuffFrom(AbilityFocus)) return 0.5f;
            if (weapon != null && ih.isDagger(weapon.typeId) && HasBuffFrom(AbilityFrenzy)) return 0.8f;
            return BaseWeaponSpeed(weapon);
        }

        /// <summary>The interval without your buffs: an outlaw's weapon swings at this.</summary>
        private static float BaseWeaponSpeed(ItemSave weapon)
        {
            var ih = Scr_ItemHandler.instance;
            if (weapon == null) return 1.3f;
            int id = weapon.typeId;
            if (ih.isBow(id)) return 1.66f;
            if (ih.isCrossBow(id)) return 3.32f;
            if (ih.isMace(id)) return 1.3f;
            if (ih.isDagger(id)) return 1f;
            if (ih.isHalberd(id) || ih.isGreatsword(id) || ih.isStaff(id) || ih.isWand(id)) return 1.66f;
            if (ih.isBroadsword(id)) return 1.3f;
            if (ih.isInstrument(id) || id == 395) return 3f;   // lutes, the wooden pillar
            return 1.3f;
        }

        /// <summary>
        /// A shot's damage when it lands (Scr_ProjectileHandler, Scr_Projectile.calculateDamage): a damage of its own
        /// rolls 1 more than itself; otherwise its template's percentages of the skill and weapon boost.
        /// </summary>
        private int ShotDamage(int projectile, int damage, bool explicitDamage)
        {
            if (explicitDamage) return damage + 1;
            if (!ProjectileDamages.TryGetValue(projectile, out var p)) return damage + 1;
            int boost = 1;
            if (p.skill != 0) boost += 10 * SkillLevel(p.skill);
            if (p.weaponBoost) boost += 10 * WeaponLevel(EquippedWeapon());
            if (p.shieldBoost) boost += 10 * WeaponLevel(EquippedInSlot("Shield"));
            int min = (int)(p.min / 100f * boost);
            int max = (int)(p.max / 100f * boost) + StatDamageOf(p.type) + StatDamage();
            return Random.Range(1 + min, 1 + Mathf.Max(min, max));
        }

        /// <summary>An NPC's shot: its template's percentages of the NPC's damage, else that damage.</summary>
        private static int NpcShotDamage(int projectile, int npcDamage)
        {
            int min = npcDamage, max = npcDamage;
            if (ProjectileDamages.TryGetValue(projectile, out var p) && (p.min != 0 || p.max != 0))
            {
                min = (int)(p.min / 100f * npcDamage);
                max = (int)(p.max / 100f * npcDamage);
            }
            return Random.Range(1 + min, 1 + Mathf.Max(min, max));
        }

        // ---- damage an NPC takes ------------------------------------------------------------------------------

        /// <summary>Options for <see cref="NpcDamageTaken"/>, as the original's special shots set them.</summary>
        private const int HitNoDefence = 1, HitNoBlock = 2, HitNoPoisonResistance = 4, HitNoIceResistance = 8, HitNoWeaponGrade = 16;

        /// <summary>
        /// Scr_NpcHandler.calculateTakenDamage: the NPC's resistance to the kind, then (from the player) the light
        /// armor passive, burning Fire arrows, a flying follower and the weapon's grade, then the NPC's defence roll
        /// against your accuracy, which takes off up to its damage block.
        /// </summary>
        private int NpcDamageTaken(NpcEntity npc, int damage, int type, bool byPlayer, int options = 0)
        {
            npc.hitSplash = SplashPlain;
            if (!GameData.Npcs.TryGetValue(npc.typeId, out var info)) return Mathf.Max(0, damage);
            if ((npc.typeId == SnowBossType || npc.typeId == SnowBossMinionType) && type != DamageKinds.Ice) damage = 0;
            if (damage <= 0)
            {
                npc.hitSplash = SplashBlocked;
                return 0;
            }
            if (type > 0 && type < DamageKinds.Count)
            {
                bool ignore = type == DamageKinds.Poison && (options & HitNoPoisonResistance) != 0
                    || type == DamageKinds.Ice && (options & HitNoIceResistance) != 0;
                int res = ignore ? 0 : info.resistance[type] - (Time.time < npc.weakenedUntil ? 15 : 0);
                damage = Mathf.RoundToInt(damage * (0.01f * (100f - res)));
                if (res < 0) npc.hitSplash = SplashWeakened;
            }
            if (byPlayer && damage > 0)
            {
                if (alignment == Alignment.Light) damage = (int)(damage * CombatRules.LightAlignmentDamage);
                foreach (var d in npc.dots)
                    if (d.id >= 87 && d.id <= 89) damage = (int)(damage * 1.05f);   // each Fire arrows burn
                damage = (int)(damage * FlyingPetDamage());
                var weapon = EquippedWeapon();
                if (weapon != null && (options & HitNoWeaponGrade) == 0) damage = (int)(damage * WeaponGradeMultiplier(weapon.grade));
            }
            int accuracy = byPlayer ? PlayerAccuracy() : 100;
            int outlaw = (options & HitNoDefence) != 0 ? -1 : BountyBlockedHit(npc, damage, type, accuracy);   // outlaws block as you do
            if (outlaw >= 0) damage = outlaw;
            int defence = (options & HitNoDefence) != 0 || outlaw >= 0 ? 0 : info.defence > 0 ? info.defence : info.level;
            if (Random.Range(0, defence) > Random.Range(0, accuracy + 1))
            {
                int block = (options & HitNoBlock) != 0 ? 0 : info.damageBlock > 1 ? Random.Range(1, info.damageBlock) : info.damageBlock;
                damage -= block;
            }
            if (damage <= 0)
            {
                npc.hitSplash = SplashBlocked;
                return 0;
            }
            return damage;
        }

        /// <summary>A blow of yours lands on an NPC: through its defences, onto its health.</summary>
        private int HitNpc(NpcEntity npc, int damage, int type, int options = 0, bool reflectable = true)
        {
            if (npc == null || npc.dead) return 0;
            Aggro(npc);
            int dealt = NpcDamageTaken(npc, damage, type, true, options);
            if (LogBlows) Plugin.Log.LogInfo($"[blow] you hit {npc.uid} for {dealt} of {damage} {DamageKinds.Name(type)} (health {Mathf.Max(0, npc.health - dealt)}/{npc.maxHealth})");
            DealDamage(npc, dealt, WeaponSkill(EquippedWeapon()), reflectable);
            return dealt;
        }

        /// <summary>
        /// Scr_PlayerAttacksHandler.AoeDamage on NPCs: everything that can fight strictly within range of pos takes
        /// the damage. Effect 1 slows them 4 s; 2 sets them burning (Combustion, 30% for 6 s) unless they already are.
        /// </summary>
        private int AoeDamage(Vector3 pos, int damage, int type, float range, int effect = 0, bool byPlayer = true)
        {
            if (world == null) return 0;
            int hits = 0;
            foreach (var n in world.npcs.ToArray())
            {
                if (n.dead || !CanFight(n) || Vector3.Distance(pos, NpcPosition(n)) >= range) continue;
                if (byPlayer) HitNpc(n, damage, type);
                else DealDamage(n, NpcDamageTaken(n, damage, type, false), WeaponSkill(EquippedWeapon()));
                if (effect == 1) SlowNpc(n, 4f);
                else if (effect == 2 && !n.dots.Exists(d => d.id == DotCombustion)) AddDot(n, DotCombustion, (int)(damage * 0.3f), DamageKinds.Fire, 1f, 6f, byPlayer);
                hits++;
            }
            return hits;
        }

        // ---- NPC effects ---------------------------------------------------------------------------------------

        // The original's NPC buff ids, kept so a dose of one doesn't stack with itself.
        private const int DotIgnite = 9, DotWeakPoison = 27, DotCombustion = 32, DotOoze = 54, DotSpiderVenom = 78,
            DotBleeding = 90, DotPoisonArrow = 1001;

        /// <summary>Starts a damage-over-time effect, unless the NPC already has that one (setBuff ignores a second dose).</summary>
        private void AddDot(NpcEntity npc, int id, int amount, int type, float interval, float duration, bool byPlayer = true, bool refresh = false)
        {
            if (npc == null || npc.dead) return;
            var d = npc.dots.Find(x => x.id == id);
            if (d != null)
            {
                if (refresh) d.until = Time.time + duration;
                return;
            }
            npc.dots.Add(new NpcDot { id = id, amount = amount, type = type, interval = interval, nextAt = Time.time + interval, until = Time.time + duration, byPlayer = byPlayer });
        }

        /// <summary>A stun (NPC buff 2): it can't move or swing; when it ends, it can't be stunned again for 5 s (buff 47).</summary>
        private void StunNpc(NpcEntity npc, float seconds)
        {
            if (npc == null || npc.dead) return;
            if (Time.time < npc.stunImmuneUntil)
            {
                Plugin.Log.LogInfo($"[combat] {npc.uid} is immune to stuns for {npc.stunImmuneUntil - Time.time:0.0}s");
                return;
            }
            if (Time.time < npc.stunnedUntil) return;   // setBuff: no second dose while it lasts
            npc.stunnedUntil = Time.time + seconds;
            npc.stunImmuneUntil = npc.stunnedUntil + CombatRules.StunImmunity;
            PlayNpcEffect(npc, 21);
            HoldNpc(npc);
        }

        private void RootNpc(NpcEntity npc, float seconds)
        {
            if (npc == null || npc.dead || Time.time < npc.rootedUntil) return;
            npc.rootedUntil = Time.time + seconds;
            PlayNpcEffect(npc, 20);
            HoldNpc(npc);
        }

        /// <summary>A slow (NPC buff 1): it walks instead of running.</summary>
        private void SlowNpc(NpcEntity npc, float seconds)
        {
            if (npc == null || npc.dead || Time.time < npc.slowedUntil) return;
            npc.slowedUntil = Time.time + seconds;
            PlayNpcEffect(npc, 20);
            Send(198, 30, npc.uid, true);
        }

        /// <summary>Freezing (NPC buff 41, 15 s): five stacks chill it for 10 s, its swings a quarter slower.</summary>
        private void FreezeNpc(NpcEntity npc)
        {
            if (npc == null || npc.dead) return;
            npc.freezeUntil = Time.time + 15f;
            if (Time.time < npc.chilledUntil) return;   // already chilled: no effect
            if (++npc.freezeStacks < 5) return;
            npc.freezeStacks = 0;
            npc.chilledUntil = Time.time + 10f;
            PlayEffect(69, NpcPosition(npc));
        }

        /// <summary>The weakness arrow (NPC buff 76): 15 less of every resistance for 28 s.</summary>
        private void WeakenNpc(NpcEntity npc)
        {
            if (npc == null || npc.dead || Time.time < npc.weakenedUntil) return;
            npc.weakenedUntil = Time.time + 28f;
        }

        private void TickNpcEffects()
        {
            if (world == null) return;
            foreach (var n in world.npcs.ToArray())
            {
                if (n.dead)
                {
                    n.dots.Clear();
                    continue;
                }
                if (n.slowedUntil > 0f && Time.time >= n.slowedUntil)
                {
                    n.slowedUntil = 0f;
                    Send(198, 30, n.uid, !n.aggro);   // back to running if it's fighting
                }
                if (n.freezeStacks > 0 && Time.time >= n.freezeUntil) n.freezeStacks = 0;
                for (int i = n.dots.Count - 1; i >= 0 && i < n.dots.Count; i--)
                {
                    var d = n.dots[i];
                    if (Time.time >= d.until)
                    {
                        n.dots.RemoveAt(i);
                        continue;
                    }
                    if (Time.time < d.nextAt) continue;
                    d.nextAt += d.interval;
                    int amount = d.amount;
                    if (d.id == DotBleeding) amount = Mathf.Min(CombatRules.NpcBleedCap, (int)(n.maxHealth * CombatRules.NpcBleedShare));
                    else if (d.id == DotSpiderVenom) amount = (int)(amount * 1.5f);   // the venom bow's poison ticks half again
                    if (d.id == DotCombustion || d.id >= 87 && d.id <= 89) PlaySound(265, NpcPosition(n), 30);
                    if (d.id == DotOoze) PlayEffect(38, NpcPosition(n));
                    else if (d.id == DotSpiderVenom || d.id == DotWeakPoison) PlayEffect(110, NpcPosition(n));
                    DealDamage(n, NpcDamageTaken(n, amount, d.type, d.byPlayer), WeaponSkill(EquippedWeapon()));
                    if (n.dead) break;
                }
            }
        }

        // ---- damage the player takes --------------------------------------------------------------------------

        /// <summary>
        /// Scr_PlayerHandler.takeDamage and calculateResistances: the heavy armor passive, Energy blade storing
        /// magic, Flame barrier and Disperse striking back, your effective resistance to the kind, then your armor's
        /// block roll (its skill against the attacker's accuracy), Stability, and the 48% cap of update 140.
        /// Returns the damage after all of it (0 while Truce or Traverse lasts).
        /// </summary>
        private int PlayerDamageTaken(int damage, int type, NpcEntity attacker, int accuracy)
        {
            if (damage <= 0 || PlayerDead) return 0;
            if (type != 0 && !IsPhysical(type))
                foreach (var b in buffs)
                    if (b.ability == AbilityEnergyBlade) b.absorbed += damage;   // released when it ends
            if (alignment == Alignment.Heavy) damage = (int)(damage * CombatRules.HeavyAlignmentTaken);
            if (character.scene == SceneKeshk && type == DamageKinds.Poison && HasBuff(BuffSnakebite))
            {
                PlayPlayerEffect(109);
                damage *= 3;
            }
            if (attacker != null && !attacker.dead && IsPhysical(type) && HasBuffFrom(AbilityFlameBarrier))
                DealDamage(attacker, NpcDamageTaken(attacker, (int)(damage * 0.5f), type, false), WeaponSkill(EquippedWeapon()));
            if (HasBuffFrom(AbilityDisperse) && LocalPlayer != null)
            {
                float level = SkillLevel(SkillSwords) * 0.0025f;
                AoeDamage(LocalPlayer.transform.position, (int)(damage * 2 * (0.10f + level)), DamageKinds.Slash, 4);
            }

            // calculateResistances
            if (type > 0 && type < DamageKinds.Count)
                damage = (int)(damage * (0.01f * (100f - CombatRules.EffectiveResistance(StatResistance(type)))));
            int guaranteed = 0, tempBlock = 0;
            if (HasBuffFrom(AbilityShieldBlock))
            {
                guaranteed = 1000;
                var shield = EquippedInSlot("Shield");
                var weapon = EquippedWeapon();
                tempBlock += shield != null ? WeaponLevel(shield) * 8 : weapon != null ? WeaponLevel(weapon) * 8 : SkillLevel(SkillHeavyArmor) * 7;
            }
            if (HasBuffFrom(AbilityThickSkin)) tempBlock += SkillLevel(SkillHeavyArmor) * 7;
            int armorSkill = ArmorSkillWorn();
            int skillBoost = 0;
            if (armorSkill != 0)
            {
                CountXp(armorSkill);
                skillBoost = SkillLevel(armorSkill);
            }
            int block = GearStat("Damageblock");
            if (Random.Range(guaranteed, skillBoost + guaranteed + 1) > Random.Range(0, accuracy + 1) && block + tempBlock > 0)
            {
                int blocked = Random.Range(1, block + 1 + tempBlock);
                damage -= blocked;
                PlayBlockSound();
            }
            if (damage <= 0) return 0;

            if (type == DamageKinds.Fire && HasBuff(BuffSmolder))   // Smolderfish: fire heals you
            {
                Heal(Random.Range(140, 200));
                PlayPlayerEffect(153);
            }
            if (HasBuffFrom(AbilityStability))
            {
                damage = (int)(damage * 0.5f);
                PlayPlayerEffect(95);
            }
            if (HasBuffFrom(AbilityEmbraceThePain) && character.currentMana < MaxMana())
            {
                character.currentMana = Mathf.Min(MaxMana(), character.currentMana + (int)((float)damage / MaxHealth() * MaxMana()));
                SendMana();
            }
            damage = Mathf.RoundToInt(damage * BuffProduct(e => e.damageTaken));   // Truce, Traverse and the reconstructed ice spells
            return Mathf.Min(damage, Mathf.CeilToInt(MaxHealth() * CombatRules.MaxHitShare));
        }

        /// <summary>The armor skill your worn armor trains and blocks with: the kind you wear more pieces of, heavy on a tie.</summary>
        private int ArmorSkillWorn()
        {
            int light = 0, heavy = 0;
            foreach (var it in character.items)
            {
                if (!it.equipped) continue;
                var flags = ItemData.Get(it.typeId)?.flags;
                if (flags == null) continue;
                if (flags.Contains("isLightArmor")) light++;
                if (flags.Contains("isHeavyArmor")) heavy++;
            }
            if (light > heavy) return SkillLightArmor;
            if (heavy > 0) return SkillHeavyArmor;
            return 0;
        }

        private void PlayBlockSound()
        {
            var player = LocalPlayer;
            if (player == null) return;
            int[] sounds = { 178, 179, 142, 143 };   // sword and shield blocks
            PlaySound(sounds[Random.Range(0, sounds.Length)], player.transform.position, 30);
        }

        /// <summary>
        /// Damage the original server's scripts dealt (boss and dungeon hazards), now taken as it was: through your
        /// resistance to its kind, your block and the 48% cap. Returns what it took.
        /// </summary>
        private int HurtByOriginal(int originalDamage, int damageKind, int splash = SplashPlain)
        {
            int damage = PlayerDamageTaken(originalDamage, damageKind, null, 100);
            HurtPlayer(damage, splash);
            return damage;
        }

        /// <summary>Dev: the numbers of a fight against an NPC type (bridge command hitcheck).</summary>
        internal void DevHitCheck(int typeId, int abilityId)
        {
            if (character == null || !GameData.Npcs.TryGetValue(typeId, out var info)) return;
            var weapon = EquippedWeapon();
            var res = new List<string>();
            for (int k = 1; k < DamageKinds.Count; k++)
                if (info.resistance[k] != 0) res.Add(DamageKinds.Name(k) + " " + info.resistance[k]);
            var blow = BasicBlow(weapon);
            Plugin.Log.LogInfo($"[dev] {info.name} ({typeId}) level {info.level}{(info.boss ? " boss" : "")}: health {info.health}, hits 1-{CombatRules.NpcMaxHit(info) - 1} " +
                $"{DamageKinds.Name(info.damageType)}, defence {(info.defence > 0 ? info.defence : info.level)}, block {info.damageBlock}, accuracy {info.accuracy}; " +
                $"resistances [{string.Join(", ", res.ToArray())}]. Your basic blow now: {blow.damage} {DamageKinds.Name(blow.type)}" +
                $"{(blow.projectile > 0 ? " (shot " + blow.projectile + ")" : "")} every {WeaponSpeed(weapon):0.00}s; health {MaxHealth()} (combat level {CombatLevel()}); ability {abilityId}");
        }
    }
}
