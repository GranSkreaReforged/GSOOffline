using System.Collections.Generic;
using UnityEngine;

namespace GSOOffline
{
    internal enum AbilityKind
    {
        Strike,         // the target (the default for targeted abilities)
        Splash,         // the target and everything hostile within radius of it
        Sweep,          // everything hostile within radius of the player
        Buff,           // a timed effect on the player
        Heal,           // instant heal of the player
        HealOverTime,   // heal of the player spread over the duration
        Bandage,        // consumes a bandage to heal the player
        ManaBurst,      // spends all mana up to a cap for one big hit
        Taunt,          // pulls nearby enemies
        Utility,        // has no effect offline beyond animation and cooldown
        Unavailable,    // needs systems the offline server doesn't have; refused before any cost
    }

    /// <summary>
    /// What one ability does. The game data only names abilities and gives level, mana, range and cooldown; the
    /// effects were server code. Abilities the original server had run its code (OfflineServer.OriginalAbilities.cs)
    /// and use this only for the kind of target they need. The few it never had code for (Shatter, Frostburn, Ice
    /// hail, Frozen barrage, Inner frost, Iceblock, Thoracic trauma) are reconstructions: power x the original's
    /// basic blow for the weapon, or the effect described.
    /// </summary>
    internal class AbilityRule
    {
        public AbilityKind kind = AbilityKind.Strike;
        public float power = 1f;      // reconstructions: damage x the weapon's basic blow
        public float radius;
        public int buff;              // client buff id (XMLs/Buffs) shown while the effect lasts; 0 = no icon
        public float duration;        // seconds
        public int bandage;           // item consumed by Bandage
        public int bandageLevel;      // the bandage's level in the original's heal: (Healing + level) x 15
        public float bandageMax = 1.7f;   // ...times 0.9 to this
        public int manaCap;           // ManaBurst: most mana spent
        public BuffEffect effect;     // Buff: what the effect does

        public static AbilityRule Of(AbilityKind kind) => new AbilityRule { kind = kind };
    }

    /// <summary>The server-side part of a player buff that isn't one of the original's own (see OriginalAbilities).
    /// Unset fields have no effect.</summary>
    internal class BuffEffect
    {
        public float damageDealt = 1f;     // x outgoing damage (reconstructed abilities only)
        public float damageTaken = 1f;     // x incoming damage (0 = immune)
        public float attackSpeed = 1f;     // x time between basic attacks
        public int bonusHealthPercent;     // + max health while it lasts (Survivor, health boost)
        public bool payBackHealth;         // Survivor: the bonus is taken as damage when it ends
        public float manaPerSecond;
        public float healPercentPerSecond; // of max health (negative: damage over time)
        public bool poisonDamage;          // that damage is poison (Nirish's venom): poison resistance applies
        public bool plague;                // Sepitus's plague: heals hurt instead (Vitals.Heal)
        public bool peace;                 // NPCs up to the player's Healing level don't aggro
        public float craftTime = 1f, gatherTime = 1f;
        public float alchemyXp = 1f;
        public int dot;                    // damage every dotEvery seconds (ignite, ooze, weak poison)
        public float dotEvery = 1f;
        public int dotKind;                // its damage kind
        public bool dotTrue;               // true damage: no resistance
        public int dotGfx;                 // effect on the player with each tick
        public Dictionary<string, int> stats;   // + gear stats while it lasts (the Salvation and Despair potions)
        public float hitDamage = 1f;       // x your hits on creatures, as the original's Eos's Favor
        public float healingTaken = 1f;    // x heals you receive
    }

    public partial class OfflineServer
    {
        private static AbilityRule Strike(float power = 1f) => new AbilityRule { power = power };
        private static AbilityRule Sweep(float radius, float power = 1f) =>
            new AbilityRule { kind = AbilityKind.Sweep, radius = radius, power = power };
        private static AbilityRule Splash(float radius, float power = 1f) =>
            new AbilityRule { kind = AbilityKind.Splash, radius = radius, power = power };
        private static AbilityRule Buff(int icon, float seconds, BuffEffect e) =>
            new AbilityRule { kind = AbilityKind.Buff, buff = icon, duration = seconds, effect = e };
        private static AbilityRule Bandage(int item, int level, float max = 1.7f) =>
            new AbilityRule { kind = AbilityKind.Bandage, bandage = item, bandageLevel = level, bandageMax = max };

        // Ability ids the combat rules check for.
        private const int AbilityThickSkin = 15, AbilityShieldBlock = 26, AbilityFocus = 61, AbilityFrenzy = 84,
            AbilityHealingMusic = 98, AbilityHealingAura = 99, AbilityStrength = 100, AbilitySurvivor = 103, AbilityDisperse = 104,
            AbilityFireWisp = 107, AbilityFireWisp2 = 1107, AbilityFlameBarrier = 115, AbilityFlameLance = 116, AbilityImmolate = 117,
            AbilityFirestorm = 121, AbilityIceBarrier = 125, AbilityCryostasis = 126, AbilityEnergyBlade = 139, AbilityHackAndSlash = 140,
            AbilityStability = 142, AbilityTruce = 143, AbilityGlyph = 149, AbilityFireArrows = 150, AbilityManaSurge = 168,
            AbilityEmbraceThePain = 169, AbilityTraverse = 170;
        private const int BuffManaSurge = 92, BuffLifeSurge = 103;   // Mana surge, and in Jotnar's chaps Life surge: regeneration

        // Ability id -> rule. Targeted abilities missing here are plain strikes; untargeted ones are utilities.
        private static readonly Dictionary<int, AbilityRule> AbilityRules = new Dictionary<int, AbilityRule>
        {
            // Bandages (Healing): wool 3, cotton 7, silk 15, jute 31, flax 43, ramie 76 (the original's bandage
            // levels). Healing other players has nobody to heal offline, so it heals you.
            { 72, Bandage(10367, 3) }, { 73, Bandage(10368, 7) }, { 82, Bandage(10369, 15) }, { 83, Bandage(10370, 31, 1.8f) },
            { 110, Bandage(10443, 43, 1.8f) }, { 111, Bandage(10444, 76, 1.8f) },
            { 78, Bandage(10367, 3) }, { 79, Bandage(10368, 7) }, { 80, Bandage(10369, 15) }, { 81, Bandage(10370, 31) },
            { 108, Bandage(10443, 43, 1.8f) }, { 109, Bandage(10444, 76, 1.8f) },

            // Heals (OriginalAbilities works out how much)
            { 94, Of(AbilityKind.Heal) },             // Charm
            { 96, Of(AbilityKind.Heal) },             // Purification
            { 156, Of(AbilityKind.Heal) },            // Healing bird
            { 98, Of(AbilityKind.HealOverTime) },     // Healing music
            { 99, Of(AbilityKind.HealOverTime) },     // Healing aura
            { 158, Of(AbilityKind.HealOverTime) },    // Healing plant

            // Area attacks around the player or a clicked spot
            { 85, Sweep(5f) }, { 87, Sweep(3f) }, { 161, Sweep(3f) }, { 162, Sweep(5f) },   // Slashing
            { 95, Sweep(5f) },            // Demolition
            { 113, Sweep(5f) },           // Blast
            { 119, Sweep(5f) },           // Fire wall
            { 121, Of(AbilityKind.Buff) },   // Firestorm: a storm around you for 8 seconds
            { 122, Sweep(6f) },           // Landmine
            { 123, Sweep(4f) },           // Flashfreeze
            { 149, Sweep(8f) },           // Psychic glyph
            // Reconstructions: the original had no code for these.
            { 128, Strike() },            // Glacial spike (the original's: a single target)
            { 129, Sweep(6f, 1.8f) },     // Shatter
            { 131, Sweep(6f, 1.6f) },     // Frostburn
            { 132, Sweep(8f, 2f) },       // Ice hail
            { 133, Sweep(8f, 2.2f) },     // Frozen barrage
            { 172, Sweep(6f) },           // Landmine (archery, the Storage guard's crossbow)

            // Area attacks around the target
            { 1, Splash(2f) },            // Fire ball
            { 65, Splash(5f) },           // Explosive arrow
            { 120, Splash(5f) },          // Eruption
            { 52, Splash(3f) },           // Broadsword electric attack
            { 64, Splash(3f) },           // Electric arrow
            { 92, Splash(5f) },           // Mace multiple hit

            // Strikes (stuns, roots and damage over time come with the original's code)
            { 91, Strike() },             // Piercing shot
            { 101, Strike() },            // Tritone
            { 171, Strike(1.6f) },        // Thoracic trauma (reconstruction)
            { 175, Strike() },            // Halberd ice attack
            { 173, Of(AbilityKind.ManaBurst) },   // Condensed psyche
            { 174, Of(AbilityKind.ManaBurst) },   // Condensed ice
            { 141, Of(AbilityKind.Taunt) },

            // Buffs (durations and effects: OriginalAbilities)
            { 15, Of(AbilityKind.Buff) }, { 26, Of(AbilityKind.Buff) }, { 61, Of(AbilityKind.Buff) }, { 84, Of(AbilityKind.Buff) },
            { 100, Of(AbilityKind.Buff) }, { 102, Of(AbilityKind.Buff) }, { 103, Of(AbilityKind.Buff) }, { 104, Of(AbilityKind.Buff) },
            { 107, Of(AbilityKind.Buff) }, { 114, Of(AbilityKind.Buff) }, { 115, Of(AbilityKind.Buff) }, { 116, Strike() },
            { 117, Of(AbilityKind.Buff) }, { 124, Of(AbilityKind.Buff) }, { 125, Of(AbilityKind.Buff) }, { 126, Of(AbilityKind.Buff) },
            { 137, Of(AbilityKind.Buff) }, { 139, Of(AbilityKind.Buff) }, { 140, Of(AbilityKind.Buff) }, { 142, Of(AbilityKind.Buff) },
            { 143, Of(AbilityKind.Buff) }, { 146, Of(AbilityKind.Buff) }, { 150, Of(AbilityKind.Buff) }, { 167, Of(AbilityKind.Buff) },
            { 168, Of(AbilityKind.Buff) }, { 169, Of(AbilityKind.Buff) }, { 170, Of(AbilityKind.Buff) },
            { 176, Of(AbilityKind.Buff) }, { 177, Of(AbilityKind.Buff) },   // summons (OfflineServer.Summons.cs)
            // Reconstructions of the ice spells with no original code.
            { 130, Buff(0, 5f, new BuffEffect { damageTaken = 0f }) },       // Iceblock
            { 134, Buff(0, 20f, new BuffEffect { damageDealt = 1.2f }) },    // Inner frost

            // Weapon handling
            { 76, Of(AbilityKind.Utility) }, { 163, Of(AbilityKind.Utility) },   // pistol reloads
            { 151, Strike() },   // Friendly snowball

            // Need other players, pets or housing
            { 2, Of(AbilityKind.Unavailable) },
            { 135, Of(AbilityKind.Unavailable) },   // Portal
            { 136, Of(AbilityKind.Unavailable) },   // Fire cannon
            { 147, Of(AbilityKind.Unavailable) },   // Drop
            { 153, Of(AbilityKind.Unavailable) },   // Snowfall
            { 157, Of(AbilityKind.Unavailable) },   // Transfusion: moves your health to another player
        };

        private static AbilityRule Of(AbilityKind kind) => AbilityRule.Of(kind);

        // Snowball event abilities are listed under Archery but are thrown by hand.
        private static readonly HashSet<int> AnyWeaponAbilities = new HashSet<int> { 151, 152, 153 };

        /// <summary>
        /// What a skill ability needs in hand or worn that the player lacks, or null. Weapon abilities (keys 2-5)
        /// come from the weapon in hand already. Elemental grimoire is the staff skill and Healing the lute skill
        /// (community wiki; bandage heals only need bandages), Swordsmanship needs a broadsword, greatsword or
        /// halberd (update 121), Archery a bow or crossbow, and the armor skills 7 alignment points (update 135).
        /// </summary>
        private string MissingEquipmentFor(AbilityInfo a, AbilityRule rule)
        {
            if (a.id == 165 && !Wears("huntersBow")) return "the hunter's bow in hand";   // Wounding shot
            if ((a.id == 66) && CountItem(PoisonForArrows) == 0) return "poison for the arrows";
            if ((a.id == 151 || a.id == 152) && CountItem(ItemSnowball) == 0) return "a snowball";
            if (a.weaponSkill || AnyWeaponAbilities.Contains(a.id) || rule.kind == AbilityKind.Bandage) return null;
            var w = EquippedWeapon();
            int t = w != null ? w.typeId : 0;
            var ih = Scr_ItemHandler.instance;
            switch ((a.skill ?? string.Empty).Replace(" ", string.Empty).ToLowerInvariant())
            {
                case "elementalgrim":
                    return w != null && (ih.isStaff(t) || ih.isWand(t)) ? null : "a staff or wand in hand";
                case "healing":
                    return w != null && ih.isInstrument(t) ? null : "a lute in hand";
                case "swordsmanship":   // or bare fists in Jin
                    return w != null && (ih.isBroadsword(t) || ih.isGreatsword(t) || ih.isHalberd(t)) || w == null && Wears("jin") ? null
                        : "a broadsword, greatsword or halberd in hand";
                case "archery":
                    return w != null && (ih.isBow(t) || ih.isCrossBow(t)) ? null : "a bow or crossbow in hand";
                case "lightarmor":
                    return WornAlignment() == Alignment.Light ? null : "more light armor on (chest, legs, head...)";
                case "heavyarmor":
                    return WornAlignment() == Alignment.Heavy ? null : "more heavy armor on (chest, legs, head...)";
            }
            return null;
        }

        private static AbilityRule RuleFor(AbilityInfo a)
        {
            if (AbilityRules.TryGetValue(a.id, out var r)) return r;
            return a.requiresTarget && !a.friendly ? Strike() : Of(AbilityKind.Utility);
        }

        // ---- player buffs --------------------------------------------------------------------

        private class ActiveBuff
        {
            public int ability;
            public int icon;
            public float until;
            public BuffEffect effect;
            public int bonusHealth;   // Survivor: the health added, taken back at the end
            public int absorbed;      // Energy blade: damage stored
            public int amount;        // the original's buff amount (Strength's slash damage, Fire wisp's fire damage...)
            public float multiplier = 1f;   // Hack and slash: the next blow's
            public Vector3 origin;    // where it was cast (Psychic glyph, Flame lance)
            public NpcEntity target;  // Flame lance: what it burns
            public float nextDot;     // damage over time: the next tick
            public int totalTenths;   // its whole length, for the icon's bar
        }

        private readonly List<ActiveBuff> buffs = new List<ActiveBuff>();
        private float buffTick;

        private bool HasBuff(int icon) => buffs.Exists(b => b.icon == icon);
        private bool HasBuffFrom(int source) => buffs.Exists(b => b.ability == source);
        private int BuffAmount(int source) => buffs.Find(b => b.ability == source)?.amount ?? 0;

        /// <summary>Starts (or refreshes) a buff from an ability or item, with its client icon.</summary>
        private ActiveBuff AddBuff(int source, int icon, float seconds, BuffEffect effect, int amount = 0)
        {
            RemoveBuff(source, false);
            var b = new ActiveBuff { ability = source, icon = icon, until = Time.time + seconds, effect = effect ?? new BuffEffect(), amount = amount };
            if (LocalPlayer != null) b.origin = LocalPlayer.transform.position;
            if (b.effect.bonusHealthPercent > 0 || amount > 0 && source == AbilitySurvivor)
            {
                b.bonusHealth = source == AbilitySurvivor ? amount : MaxHealth() * b.effect.bonusHealthPercent / 100;
                buffs.Add(b);
                character.currentHealth += b.bonusHealth;
                SendHealth();
            }
            else buffs.Add(b);
            if (source == AbilityFireWisp || source == AbilityFireWisp2) ManaStatsChanged();   // the wisp takes its fire out of your mana (summons do it in Summon)
            if (b.effect.stats != null) VitalStatsChanged();
            // 4/9: name, buff id, time left and total time in tenths of a second
            int tenths = Mathf.RoundToInt(seconds * 10f);
            b.totalTenths = tenths;
            if (icon > 0) Send(4, 9, character.name, icon, tenths, tenths);
            Plugin.Log.LogInfo($"[buff] {source} on for {seconds}s (icon {icon}, amount {amount})");
            BuffStatsChanged();
            return b;
        }

        private void RemoveBuff(int source, bool expired)
        {
            var b = buffs.Find(x => x.ability == source);
            if (b == null) return;
            buffs.Remove(b);
            if (b.icon > 0 && !buffs.Exists(x => x.icon == b.icon)) Send(3, 35, character.name, b.icon);
            if (b.effect.payBackHealth && b.bonusHealth > 0 && expired)
                character.currentHealth = Mathf.Max(1, character.currentHealth - b.bonusHealth);
            if (b.bonusHealth > 0)
            {
                character.currentHealth = Mathf.Min(character.currentHealth, MaxHealth());
                SendHealth();
            }
            if (source == AbilityFireWisp || source == AbilityFireWisp2 || source == -BuffSkeletalBerserk || source == -BuffSkeleton) ManaStatsChanged();
            if (b.effect.stats != null) VitalStatsChanged();
            // Energy blade: what it stored goes off around you (10 m), at most 1000 x Swordsmanship.
            if (b.ability == AbilityEnergyBlade && b.absorbed > 0 && expired) ReleaseAbsorbed(Mathf.Min(b.absorbed, SkillLevel(SkillSwords) * 1000));
            Plugin.Log.LogInfo($"[buff] {source} {(expired ? "expired" : "removed")}");
            BuffEnded(b, expired);   // free to move again after a stun, root or Cryostasis
            BuffStatsChanged();
        }

        private void ClearBuffs()
        {
            if (character == null)
            {
                buffs.Clear();
                return;
            }
            foreach (var b in buffs.ToArray()) RemoveBuff(b.ability, false);
        }

        /// <summary>
        /// Death ends what the original's RemoveAllBuffs did: buffs XMLs/Buffs marks canberemoved (stuns, poisons,
        /// combat buffs) and the iconless ability states. Food, potions, Drunk, Survivor and passives carry on.
        /// </summary>
        private void EndBuffsOnDeath()
        {
            foreach (var b in buffs.ToArray())
                if (b.icon <= 0 || GameData.RemovableBuffs.Contains(b.icon)) RemoveBuff(b.ability, true);
        }

        private float BuffProduct(System.Func<BuffEffect, float> f)
        {
            float m = 1f;
            foreach (var b in buffs) m *= f(b.effect);
            return m;
        }

        // Hack and slash (the original's attackSpeedMultiplier -0.15).
        private float AttackSpeedMultiplier() => BuffProduct(e => e.attackSpeed) - (HasBuffFrom(AbilityHackAndSlash) ? 0.15f : 0f);
        internal float CraftTimeMultiplier() => BuffProduct(e => e.craftTime);
        internal float GatherTimeMultiplier() => BuffProduct(e => e.gatherTime);
        internal float AlchemyXpMultiplier() => BuffProduct(e => e.alchemyXp);
        private bool HasPeace() => buffs.Exists(b => b.effect.peace);

        // Psychic glyph: +150 damage and psychic damage, +20 psychic resistance, while you stay within 8 m of it.
        private const int GlyphDamage = 150, GlyphResistance = 20;
        private bool GlyphActive() => HasBuffFrom(AbilityGlyph);

        private int BonusHealth()
        {
            int sum = 0;
            foreach (var b in buffs) sum += b.bonusHealth;
            return sum;
        }

        /// <summary>Energy blade going off: everything within 10 m takes what it stored (the original's AoeDamage, type 0).</summary>
        private void ReleaseAbsorbed(int damage)
        {
            var player = LocalPlayer;
            if (world == null || player == null) return;
            PlaySound(368, player.transform.position, 50);
            PlayEffect(88, player.transform.position);
            AoeDamage(player.transform.position, damage, DamageKinds.None, 10f);
        }

        /// <summary>Truce: enemies up to the player's Healing level stop fighting. (Peace only keeps new ones off.)</summary>
        private void CalmEnemies()
        {
            if (world == null) return;
            int level = SkillLevel(24);
            foreach (var n in world.npcs)
                if (n.aggro && GameData.Npcs.TryGetValue(n.typeId, out var info) && info.level <= Mathf.Max(level, 1))
                    Disengage(n);
        }

        /// <summary>True while an NPC of this level ignores the player because of Peace or Truce.</summary>
        private bool CalmedBy(NpcInfo info) => HasPeace() && info.level <= Mathf.Max(SkillLevel(24), 1);

        private void TickBuffs()
        {
            if (buffs.Count == 0 || character == null) return;
            for (int i = buffs.Count - 1; i >= 0; i--)
                if (i < buffs.Count && Time.time >= buffs[i].until) RemoveBuff(buffs[i].ability, true);
            TickDots();

            buffTick -= Time.deltaTime;
            if (buffTick > 0f) return;
            buffTick = 1f;
            float mana = 0f, heal = 0f;
            foreach (var b in buffs)
            {
                mana += b.effect.manaPerSecond;
                heal += b.effect.healPercentPerSecond;
            }
            if (mana > 0f && character.currentMana < MaxMana())
            {
                character.currentMana = Mathf.Min(MaxMana(), character.currentMana + Mathf.RoundToInt(mana));
                SendMana();
            }
            if (heal > 0f && !PlayerDead && character.currentHealth < MaxHealth())
                Heal(Mathf.Max(1, Mathf.RoundToInt(MaxHealth() * heal)));
            else if (heal < 0f && !PlayerDead)
            {
                // Damage over time: Nirish's venom (the original's spider venom buff, a share of health each second).
                float res = buffs.Exists(b => b.effect.poisonDamage) ? CombatRules.PlayerResistance(StatResistance(DamageKinds.Poison)) : 0f;
                PlayPlayerEffect(110);
                HurtPlayer(Mathf.RoundToInt(MaxHealth() * -heal * (1f - res)), SplashPoisoned);
            }
            TickOriginalBuffs();
        }

        /// <summary>Dev: raise a skill to a level (bridge command setlevel).</summary>
        internal void DevSetLevel(int skill, int level)
        {
            if (character == null || !SkillById.TryGetValue(skill, out var key)) return;
            AddXp(skill, XpForLevel(level) - character.GetXp(key), exact: true);
            Plugin.Log.LogInfo($"[dev] {key} is level {SkillLevel(skill)}");
        }

        /// <summary>Dev: log the active buffs (bridge command buffs).</summary>
        internal void LogBuffs()
        {
            foreach (var b in buffs)
                Plugin.Log.LogInfo($"[dev] buff {b.ability} icon {b.icon} {b.until - Time.time:F1}s left: amount {b.amount} x{b.multiplier} taken x{b.effect.damageTaken} speed x{b.effect.attackSpeed} absorbed {b.absorbed} dot {b.effect.dot}/{b.effect.dotEvery}s");
            Plugin.Log.LogInfo($"[dev] {buffs.Count} buffs; health {character?.currentHealth}/{(character != null ? MaxHealth() : 0)}, mana {character?.currentMana}/{(character != null ? MaxMana() : 0)}, damage {(character != null ? StatDamage() : 0)}");
        }

        // ---- using abilities -----------------------------------------------------------------

        /// <summary>
        /// Abilities from XMLs/Abilities: level, mana, range and cooldown come from the data, what they do from the
        /// original server's code (<see cref="CastOriginal"/>) or, for the few it lacked, <see cref="AbilityRules"/>.
        /// </summary>
        private void UseSkillAbility(int slot, AbilityInfo a)
        {
            if (slotReadyAt.TryGetValue(slot, out float ready) && Time.time < ready) return;
            var rule = RuleFor(a);
            if (rule.kind == AbilityKind.Unavailable)
            {
                Notice($"{a.name} isn't available offline.", "gray");
                return;
            }
            int skill = SkillIdByName((a.skill ?? string.Empty).Replace(" ", string.Empty));
            if (skill != 0 && SkillLevel(skill) < a.level)
            {
                Notice($"You need level {a.level} {a.skill} to use {a.name}.", "red");
                return;
            }
            string needs = MissingEquipmentFor(a, rule);
            if (needs != null)
            {
                Notice($"You need {needs} to use {a.name}.", "red");
                return;
            }
            if (character.currentMana < a.manaCost)
            {
                Notice("Not enough mana.", "red");
                return;
            }
            if (rule.kind == AbilityKind.Bandage && CountItem(rule.bandage) == 0)
            {
                Notice($"You have no {ItemData.Get(rule.bandage)?.name.ToLowerInvariant() ?? "bandages"}.", "red");
                return;
            }
            if ((a.id == 77 || a.id == 164) && Bullets(a.id == 164) <= 0)
            {
                Notice("The gun is not loaded!", "red");
                PlaySound(304, LocalPlayer.transform.position, 40);
                return;
            }

            NpcEntity npc = null;
            bool needsEnemy = rule.kind == AbilityKind.Strike || rule.kind == AbilityKind.Splash || rule.kind == AbilityKind.ManaBurst;
            if (needsEnemy)
            {
                npc = world?.GetNpc(targetUid);
                if (npc == null || npc.dead || !CanFight(npc)) return;
                float range = Mathf.Max(a.range, WeaponRange(EquippedWeapon()));
                if (Vector3.Distance(NpcPosition(npc), LocalPlayer.transform.position) > range + 1f)
                {
                    Notice("You are too far away.", "gray");
                    return;
                }
            }

            // Click-target abilities land on the ground spot the client sent just before (23/1), else at your feet.
            Vector3 center = LocalPlayer.transform.position;
            if (a.clickTarget && clickTargetPos.HasValue)
            {
                Vector3 spot = clickTargetPos.Value;
                clickTargetPos = null;
                if (Vector3.Distance(spot, center) > a.range + 1f)
                {
                    Notice("You are too far away.", "gray");
                    return;
                }
                center = spot;
            }

            // Using any other ability puts out Flame lance (Scr_PlayerAttacksHandler.onAbilityUse).
            if (a.id != AbilityFlameLance) RemoveBuff(AbilityFlameLance, false);

            // Costs, cooldown and the cast itself
            character.currentMana -= a.manaCost;
            if (a.manaCost > 0) SendMana();
            float cooldown = rule.kind == AbilityKind.Bandage ? Mathf.Max(a.cooldown, 3f) : a.cooldown;
            slotReadyAt[slot] = Time.time + cooldown;
            int cd = Mathf.RoundToInt(cooldown * 100f);
            Send(4, 0, character.name, slot, cd, cd);
            Send(3, 20, character.name, a.id);
            swingShown = true;   // ability id doubles as the attack animation id
            ProfileSwingStarted(a.id);
            castEndsAt = Time.time + a.attackTime;
            ResolvePendingBlow();
            nextPlayerSwing = Mathf.Max(nextPlayerSwing, castEndsAt);   // basic attacks resume after the cast
            lastPlayerSwing = Time.time;
            measureSwingAt = Time.time + 0.25f;   // MeasureSwing moves castEndsAt to the clip's real end
            measuringCast = true;
            WeaponSounds(EquippedWeapon(), out int weaponSwingSfx, out int weaponHitSfx);
            PlaySound(a.startSfx.Length > 0 ? Pick(a.startSfx) : DefaultCastSound(rule, weaponSwingSfx), LocalPlayer.transform.position);
            PlayPlayerEffect(a.startGfx > 0 ? a.startGfx : AbilityCasterGfx.TryGetValue(a.id, out int casterGfx) ? casterGfx : 0);
            if (AbilityGroundGfx.TryGetValue(a.id, out int groundGfx)) PlayEffect(groundGfx, center);
            Plugin.Log.LogInfo($"[ability] {a.id} {a.name}: {rule.kind}");

            if (rule.kind == AbilityKind.Bandage)
            {
                TakeItems(rule.bandage, 1);
                CountXp(SkillHealing);
                int maxHeal = (SkillLevel(SkillHealing) + rule.bandageLevel) * 15;
                Heal((int)Random.Range(maxHeal * 0.9f, maxHeal * rule.bandageMax));
                Notice("You successfully apply a bandage. It heals some health.", "green");
                OnHealed();
                return;
            }

            if (npc != null) OnAbilityCastAt(npc, a);   // quests that ask for a spell on something (A Magical Journey)
            if (!CastOriginal(a, npc, center, out var release)) release = CastReconstructed(a, rule, npc, center, skill);
            // The blows land when the cast animation strikes: MeasureSwing finds the clip, and attackdelay is the
            // fallback for clips that haven't been measured.
            if (release != null)
            {
                var hit = weaponHitSfx;
                int targetGfx = a.hitGfx > 0 ? a.hitGfx : AbilityTargetGfx.TryGetValue(a.id, out int g) ? g : 0;
                pendingBlow = (strike, strikes) =>
                {
                    if (strike > 0 || PlayerDead) return;
                    if (npc != null && !npc.dead)
                    {
                        PlayNpcEffect(npc, targetGfx);
                        PlaySound(a.hitSfx.Length > 0 ? Pick(a.hitSfx) : hit, NpcPosition(npc));
                    }
                    release();
                };
                pendingBlowFallback = Mathf.Min(a.attackDelay, a.attackTime);
            }
            if (npc != null) autoAttacking = !npc.dead;
        }

        /// <summary>
        /// The abilities the original had no code for: power x the weapon's basic blow on the target, or around
        /// the player or clicked spot for sweeps; mana bursts add half the mana spent, as before.
        /// </summary>
        private System.Action CastReconstructed(AbilityInfo a, AbilityRule rule, NpcEntity npc, Vector3 center, int skill)
        {
            CountXp(skill != 0 ? skill : WeaponSkill(EquippedWeapon()));
            switch (rule.kind)
            {
                case AbilityKind.Buff:
                    AddBuff(a.id, rule.buff, rule.duration, rule.effect);
                    return null;
                case AbilityKind.Utility:
                case AbilityKind.Heal:
                case AbilityKind.HealOverTime:
                case AbilityKind.Taunt:
                    return null;
            }
            var weapon = EquippedWeapon();
            var blow = BasicBlow(weapon);
            int type = DamageKinds.OfAbility(a.name);
            if (type == DamageKinds.None) type = blow.type;
            float power = rule.power * BuffProduct(e => e.damageDealt);
            int burst = 0;
            if (rule.kind == AbilityKind.ManaBurst)
            {
                burst = Mathf.Min(character.currentMana, rule.manaCap);
                character.currentMana -= burst;
                SendMana();
                power = 1f + burst / 100;   // the original Tritone's mana multiplier
            }
            int damage = Mathf.RoundToInt(blow.damage * power);
            bool aroundPlayer = rule.kind == AbilityKind.Sweep;
            return () =>
            {
                if (aroundPlayer) AoeDamage(a.clickTarget ? center : LocalPlayer.transform.position, damage, type, rule.radius);
                else if (npc != null && !npc.dead) HitNpc(npc, damage, type);
            };
        }

        // Most abilities name no sound. Attacks sound like the weapon; heals and buffs use Charm's heal sound (308).
        private static int DefaultCastSound(AbilityRule rule, int weaponSwing)
        {
            switch (rule.kind)
            {
                case AbilityKind.Heal:
                case AbilityKind.HealOverTime:
                case AbilityKind.Buff:
                case AbilityKind.Taunt:
                    return 308;
                case AbilityKind.Utility:
                case AbilityKind.Bandage:
                    return 0;
                default:
                    return weaponSwing;
            }
        }

        /// <summary>Taunt: everything within 15 m (plus a metre per 6 Heavy armor levels) up to your Heavy armor level comes for you.</summary>
        private void TauntEnemies()
        {
            var player = LocalPlayer;
            if (world == null || player == null) return;
            int level = SkillLevel(SkillHeavyArmor);
            var target = world.GetNpc(targetUid);
            if (target != null && !target.dead && CanFight(target)) Aggro(target);
            foreach (var n in world.npcs)
                if (!n.dead && CanFight(n) && GameData.Npcs.TryGetValue(n.typeId, out var info) && info.level <= level
                    && Vector3.Distance(NpcPosition(n), player.transform.position) < 15 + level / 6)
                {
                    Aggro(n);
                    PlayNpcEffect(n, 93);
                }
        }
    }
}
