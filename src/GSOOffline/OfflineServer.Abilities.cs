using System.Collections.Generic;
using UnityEngine;

namespace GSOOffline
{
    internal enum AbilityKind
    {
        Strike,         // the target: a basic hit x power (the default for targeted abilities)
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
    /// What one ability does. The game data only names abilities and gives level, mana, range and cooldown;
    /// the effects were server code. These rules rebuild them from the ability descriptions, and the numbers
    /// (multipliers, durations, radii) are reconstructions tuned for the feel of the game.
    /// </summary>
    internal class AbilityRule
    {
        public AbilityKind kind = AbilityKind.Strike;
        public float power = CombatRules.AbilityMultiplier;   // damage x basic hit, or heal fraction of max health
        public float radius;
        public int buff;              // client buff id (XMLs/Buffs) shown while the effect lasts; 0 = no icon
        public float duration;        // seconds
        public int bandage;           // item consumed by Bandage
        public int manaCap;           // ManaBurst: most mana spent
        public float stun, root;      // seconds the targets can't act / can't move
        public float dot;             // seconds of damage over time on the targets (power is split over it)
        public BuffEffect effect;     // Buff: what the effect does

        public static AbilityRule Of(AbilityKind kind) => new AbilityRule { kind = kind };
    }

    /// <summary>The server-side part of a player buff. Unset fields have no effect.</summary>
    internal class BuffEffect
    {
        public float damageDealt = 1f;     // x outgoing damage
        public float damageTaken = 1f;     // x incoming damage (0 = immune)
        public float attackSpeed = 1f;     // x time between basic attacks
        public float reflect;              // share of incoming damage dealt back to the attacker
        public int bonusHealthPercent;     // + max health while it lasts (Survivor, health boost)
        public bool payBackHealth;         // Survivor: the bonus is taken as damage when it ends
        public float manaPerSecond;
        public float healPercentPerSecond; // of max health
        public bool manaFromDamage;        // Embrace the pain
        public bool peace;                 // NPCs up to the player's Healing level don't aggro
        public bool absorb;                // Energy blade: damage taken is stored and released around the player
        public bool crossbowOnly, bowOnly; // attack speed / damage only with that weapon
        public float craftTime = 1f, gatherTime = 1f;
        public float alchemyXp = 1f;
    }

    public partial class OfflineServer
    {
        private static AbilityRule Strike(float power = CombatRules.AbilityMultiplier) => new AbilityRule { power = power };
        private static AbilityRule Sweep(float radius, float power = CombatRules.AbilityMultiplier) =>
            new AbilityRule { kind = AbilityKind.Sweep, radius = radius, power = power };
        private static AbilityRule Splash(float radius, float power = CombatRules.AbilityMultiplier) =>
            new AbilityRule { kind = AbilityKind.Splash, radius = radius, power = power };
        private static AbilityRule Buff(int icon, float seconds, BuffEffect e) =>
            new AbilityRule { kind = AbilityKind.Buff, buff = icon, duration = seconds, effect = e };
        private static AbilityRule Bandage(int item) => new AbilityRule { kind = AbilityKind.Bandage, bandage = item };

        // Ability id -> rule. Targeted abilities missing here are plain strikes; untargeted ones are utilities.
        private static readonly Dictionary<int, AbilityRule> AbilityRules = new Dictionary<int, AbilityRule>
        {
            // Bandages (Healing). Healing other players has nobody to heal offline, so it heals you.
            { 72, Bandage(10367) }, { 73, Bandage(10368) }, { 82, Bandage(10369) }, { 83, Bandage(10370) },
            { 110, Bandage(10443) }, { 111, Bandage(10444) },
            { 78, Bandage(10367) }, { 79, Bandage(10368) }, { 80, Bandage(10369) }, { 81, Bandage(10370) },
            { 108, Bandage(10443) }, { 109, Bandage(10444) },

            // Heals
            { 94, new AbilityRule { kind = AbilityKind.Heal, power = 0.25f } },                         // Charm
            { 96, new AbilityRule { kind = AbilityKind.Heal, power = 0.15f } },                         // Purification
            { 156, new AbilityRule { kind = AbilityKind.Heal, power = 0.2f } },                         // Healing bird
            { 98, new AbilityRule { kind = AbilityKind.HealOverTime, power = 0.2f, duration = 10f, buff = 103 } },  // Healing music
            { 99, new AbilityRule { kind = AbilityKind.HealOverTime, power = 0.3f, duration = 15f, buff = 103 } },  // Healing aura
            { 158, new AbilityRule { kind = AbilityKind.HealOverTime, power = 0.3f, duration = 10f, buff = 103 } }, // Healing plant

            // Area attacks around the player
            { 85, Sweep(5f) }, { 87, Sweep(5f) }, { 161, Sweep(5f) }, { 162, Sweep(5f) },   // Slashing
            { 95, Sweep(6f, 2f) },        // Demolition
            { 113, Sweep(5f, 2f) },       // Blast
            { 119, new AbilityRule { kind = AbilityKind.Sweep, radius = 5f, power = 2f, dot = 6f } },   // Fire wall: applies Combustion
            { 121, Sweep(5f, 1.8f) },     // Firestorm
            { 122, Sweep(3f, 2f) }, { 172, Sweep(3f, 2f) },   // Landmine
            { 123, new AbilityRule { kind = AbilityKind.Sweep, radius = 5f, power = 1f, root = 3f } },  // Flashfreeze
            { 128, Sweep(6f, 1.8f) },     // Glacial spike
            { 129, Sweep(6f, 1.8f) },     // Shatter
            { 131, new AbilityRule { kind = AbilityKind.Sweep, radius = 6f, power = 1.6f, dot = 6f } },  // Frostburn
            { 132, Sweep(8f, 2f) },       // Ice hail
            { 133, Sweep(8f, 2.2f) },     // Frozen barrage

            // Area attacks around the target
            { 1, Splash(2f) },            // Fire ball: 2 m splash
            { 65, Splash(3f) },           // Explosive arrow
            { 120, Splash(4f, 2f) },      // Eruption

            // Strikes with extra effects
            { 57, new AbilityRule { power = 1.6f, stun = 3f } },      // Stranglevine
            { 58, new AbilityRule { power = 1.6f, stun = 2f } },      // Rockslide
            { 88, new AbilityRule { power = 1f, root = 4f } },        // Cripple
            { 89, new AbilityRule { power = 1f, stun = 3f } },        // Stun
            { 105, new AbilityRule { power = 1.4f, stun = 3f } },     // Stunning blow
            { 90, new AbilityRule { power = 0.5f, root = 5f } },      // Hunting net
            { 155, new AbilityRule { power = 1.4f, root = 4f, dot = 6f } },   // Thorny vines
            { 66, new AbilityRule { power = 1.8f, dot = 10f } },      // Poison arrow
            { 112, new AbilityRule { power = 3f, dot = 10f } },       // Combustion: damage over 10 seconds
            { 154, new AbilityRule { power = 2.2f, dot = 8f } },      // Venom arrow
            { 160, new AbilityRule { power = 1.6f, dot = 8f } },      // Poisonous stab
            { 165, new AbilityRule { power = 2f, dot = 30f } },       // Wounding shot: bleeds for 30 seconds
            { 91, Strike(2f) },           // Piercing shot (ignores resistances)
            { 86, Strike(2f) },           // Brutal slash
            { 116, Strike(2.2f) },        // Flame lance
            { 101, new AbilityRule { kind = AbilityKind.ManaBurst, manaCap = 400 } },   // Tritone
            { 173, new AbilityRule { kind = AbilityKind.ManaBurst, manaCap = 400 } },   // Condensed psyche
            { 174, new AbilityRule { kind = AbilityKind.ManaBurst, manaCap = 300 } },   // Condensed ice
            { 141, Of(AbilityKind.Taunt) },

            // Buffs
            { 15, Buff(15, 15f, new BuffEffect { damageTaken = 0.7f }) },                    // Thick skin
            { 26, Buff(16, 3f, new BuffEffect { damageTaken = 0f }) },                       // Heavy shield block
            { 100, Buff(7, 20f, new BuffEffect { damageDealt = 1.25f }) },                   // Strength
            { 102, Buff(8, 20f, new BuffEffect { peace = true }) },                          // Peace
            { 103, Buff(10, 15f, new BuffEffect { bonusHealthPercent = 30, payBackHealth = true }) },   // Survivor
            { 104, Buff(11, 20f, new BuffEffect { reflect = 0.2f }) },                       // Disperse
            { 84, Buff(0, 8f, new BuffEffect { attackSpeed = 0.6f }) },                      // Frenzy
            { 140, Buff(51, 15f, new BuffEffect { attackSpeed = 0.85f, damageDealt = 1.1f }) },   // Hack and slash
            { 61, Buff(79, 10f, new BuffEffect { attackSpeed = 0.5f, crossbowOnly = true }) },     // Focus
            { 115, Buff(36, 15f, new BuffEffect { reflect = 0.5f }) },                       // Flame barrier
            { 117, Buff(35, 20f, new BuffEffect { damageDealt = 1.2f }) },                   // Immolate
            { 125, Buff(0, 10f, new BuffEffect { damageTaken = 0.6f }) },                    // Ice barrier
            { 126, Buff(0, 4f, new BuffEffect { damageTaken = 0f }) },                       // Cryostasis
            { 130, Buff(0, 5f, new BuffEffect { damageTaken = 0f }) },                       // Iceblock
            { 134, Buff(0, 20f, new BuffEffect { damageDealt = 1.2f }) },                    // Inner frost
            { 139, Buff(50, 5f, new BuffEffect { absorb = true }) },                         // Energy blade
            { 142, Buff(52, 8f, new BuffEffect { damageTaken = 0.5f }) },                    // Stability
            { 143, Buff(53, 8f, new BuffEffect { damageTaken = 0f, peace = true }) },        // Truce
            { 170, Buff(96, 5f, new BuffEffect { damageTaken = 0f }) },                      // Traverse
            { 149, Buff(73, 20f, new BuffEffect { damageDealt = 1.3f }) },                   // Psychic glyph
            { 107, Buff(85, 60f, new BuffEffect { damageDealt = 1.1f }) },                   // Fire wisp
            { 150, Buff(74, 20f, new BuffEffect { damageDealt = 1.3f, bowOnly = true }) },   // Fire arrows
            { 168, Buff(92, 10f, new BuffEffect { manaPerSecond = 10f }) },                  // Mana surge
            { 169, Buff(93, 15f, new BuffEffect { manaFromDamage = true }) },                // Embrace the pain
            { 146, Buff(65, 300f, new BuffEffect { alchemyXp = 1.2f }) },                    // Ambitious apprentice
            // Movement buffs: the client's run speed can't be changed from the server, so only the icon shows.
            { 137, Buff(49, 7f, new BuffEffect()) },     // Charge
            { 114, Buff(33, 10f, new BuffEffect()) },    // Trail blaze
            { 167, Buff(91, 30f, new BuffEffect()) },    // Windwalking
            { 124, Buff(0, 30f, new BuffEffect()) },     // Waterwalk

            // Weapon handling with no server effect
            { 76, Of(AbilityKind.Utility) }, { 163, Of(AbilityKind.Utility) },   // pistol reloads
            { 151, Of(AbilityKind.Utility) },   // Friendly snowball

            // Need other players, pets or housing
            { 2, Of(AbilityKind.Unavailable) }, { 118, Of(AbilityKind.Unavailable) },
            { 135, Of(AbilityKind.Unavailable) },   // Portal
            { 136, Of(AbilityKind.Unavailable) },   // Fire cannon
            { 147, Of(AbilityKind.Unavailable) },   // Drop
            { 153, Of(AbilityKind.Unavailable) },   // Snowfall
            { 157, Of(AbilityKind.Unavailable) },   // Transfusion: moves your health to another player
            { 176, Of(AbilityKind.Unavailable) }, { 177, Of(AbilityKind.Unavailable) },   // summons
        };

        private static AbilityRule Of(AbilityKind kind) => AbilityRule.Of(kind);

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
        }

        private readonly List<ActiveBuff> buffs = new List<ActiveBuff>();
        private float buffTick;

        /// <summary>Starts (or refreshes) a buff from an ability or item, with its client icon.</summary>
        private void AddBuff(int source, int icon, float seconds, BuffEffect effect)
        {
            RemoveBuff(source, false);
            var b = new ActiveBuff { ability = source, icon = icon, until = Time.time + seconds, effect = effect };
            if (effect.bonusHealthPercent > 0)
            {
                b.bonusHealth = MaxHealth() * effect.bonusHealthPercent / 100;
                buffs.Add(b);
                character.currentHealth += b.bonusHealth;
                SendHealth();
            }
            else buffs.Add(b);
            if (effect.peace) CalmEnemies();
            // 4/9: name, buff id, time left and total time in tenths of a second
            int tenths = Mathf.RoundToInt(seconds * 10f);
            if (icon > 0) Send(4, 9, character.name, icon, tenths, tenths);
            Plugin.Log.LogInfo($"[buff] {source} on for {seconds}s (icon {icon})");
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
            if (b.effect.absorb && b.absorbed > 0 && expired) ReleaseAbsorbed(b.absorbed * 2);
            Plugin.Log.LogInfo($"[buff] {source} {(expired ? "expired" : "removed")}");
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

        private bool BuffApplies(BuffEffect e)
        {
            if (!e.crossbowOnly && !e.bowOnly) return true;
            var w = EquippedWeapon();
            var ih = Scr_ItemHandler.instance;
            if (w == null) return false;
            return e.crossbowOnly ? ih.isCrossBow(w.typeId) : ih.isBow(w.typeId) || ih.isCrossBow(w.typeId);
        }

        private float BuffProduct(System.Func<BuffEffect, float> f)
        {
            float m = 1f;
            foreach (var b in buffs)
                if (BuffApplies(b.effect)) m *= f(b.effect);
            return m;
        }

        private float DamageDealtMultiplier() => BuffProduct(e => e.damageDealt);
        private float AttackSpeedMultiplier() => BuffProduct(e => e.attackSpeed);
        internal float CraftTimeMultiplier() => BuffProduct(e => e.craftTime);
        internal float GatherTimeMultiplier() => BuffProduct(e => e.gatherTime);
        internal float AlchemyXpMultiplier() => BuffProduct(e => e.alchemyXp);
        private bool HasPeace() => buffs.Exists(b => b.effect.peace);

        private int BonusHealth()
        {
            int sum = 0;
            foreach (var b in buffs) sum += b.bonusHealth;
            return sum;
        }

        /// <summary>
        /// Incoming damage through the player's buffs: reduction, immunity, absorbing, reflecting and mana gain.
        /// Returns what the player actually takes.
        /// </summary>
        private int ApplyDefensiveBuffs(int damage, NpcEntity attacker)
        {
            if (damage <= 0 || buffs.Count == 0) return damage;
            int raw = damage;
            foreach (var b in buffs)
                if (b.effect.absorb)
                {
                    b.absorbed += damage;
                    damage = 0;
                }
            damage = Mathf.RoundToInt(damage * BuffProduct(e => e.damageTaken));
            float reflect = 0f;
            foreach (var b in buffs) reflect += b.effect.reflect;
            if (reflect > 0f && attacker != null && !attacker.dead)
                DealDamage(attacker, Mathf.Max(1, Mathf.RoundToInt(raw * reflect)), WeaponSkill(EquippedWeapon()));
            if (buffs.Exists(b => b.effect.manaFromDamage) && raw > 0)
            {
                character.currentMana = Mathf.Min(MaxMana(), character.currentMana + raw);
                SendMana();
            }
            return damage;
        }

        private void ReleaseAbsorbed(int damage)
        {
            var player = LocalPlayer;
            if (world == null || player == null) return;
            var hits = new List<NpcEntity>();
            foreach (var n in world.npcs)
                if (!n.dead && CanFight(n) && Vector3.Distance(NpcPosition(n), player.transform.position) <= 6f) hits.Add(n);
            foreach (var n in hits) DealDamage(n, damage / Mathf.Max(1, hits.Count), WeaponSkill(EquippedWeapon()));
        }

        /// <summary>Peace and Truce: enemies up to the player's Healing level stop fighting.</summary>
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
        }

        /// <summary>Dev: raise a skill to a level (bridge command setlevel).</summary>
        internal void DevSetLevel(int skill, int level)
        {
            if (character == null || !SkillById.TryGetValue(skill, out var key)) return;
            AddXp(skill, XpForLevel(level) - character.GetXp(key));
            Plugin.Log.LogInfo($"[dev] {key} is level {SkillLevel(skill)}");
        }

        /// <summary>Dev: log the active buffs (bridge command buffs).</summary>
        internal void LogBuffs()
        {
            foreach (var b in buffs)
                Plugin.Log.LogInfo($"[dev] buff {b.ability} icon {b.icon} {b.until - Time.time:F1}s left: dealt x{b.effect.damageDealt} taken x{b.effect.damageTaken} speed x{b.effect.attackSpeed} reflect {b.effect.reflect} absorbed {b.absorbed}");
            Plugin.Log.LogInfo($"[dev] {buffs.Count} buffs; health {character?.currentHealth}/{(character != null ? MaxHealth() : 0)}, mana {character?.currentMana}/{(character != null ? MaxMana() : 0)}");
        }

        // ---- using abilities -----------------------------------------------------------------

        /// <summary>
        /// Abilities from XMLs/Abilities: level, mana, range and cooldown come from the data, the effect from
        /// <see cref="AbilityRules"/>. Damage is the kind's power x a basic hit with the equipped weapon.
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
            Plugin.Log.LogInfo($"[ability] {a.id} {a.name}: {rule.kind}");

            int xpSkill = skill != 0 ? skill : WeaponSkill(EquippedWeapon());
            switch (rule.kind)
            {
                case AbilityKind.Bandage:
                {
                    TakeItems(rule.bandage, 1);
                    int level = SkillLevel(24);
                    Heal(BandageHeal(rule.bandage, level));
                    AddXp(24, SkillData.BaseXp(Mathf.Max(level, a.level)));
                    OnHealed();
                    return;
                }
                case AbilityKind.Heal:
                    Heal(Mathf.RoundToInt(MaxHealth() * rule.power) + 2 * SkillLevel(24));
                    if (skill != 0) AddXp(skill, SkillData.BaseXp(a.level));
                    OnHealed();
                    return;
                case AbilityKind.HealOverTime:
                    AddBuff(a.id, rule.buff, rule.duration, new BuffEffect { healPercentPerSecond = rule.power / rule.duration });
                    if (skill != 0) AddXp(skill, SkillData.BaseXp(a.level));
                    OnHealed();
                    return;
                case AbilityKind.Buff:
                    AddBuff(a.id, rule.buff, rule.duration, rule.effect);
                    if (skill != 0) AddXp(skill, SkillData.BaseXp(a.level));
                    return;
                case AbilityKind.Utility:
                    return;
                case AbilityKind.Taunt:
                    TauntEnemies();
                    if (skill != 0) AddXp(skill, SkillData.BaseXp(a.level));
                    return;
            }

            // Attacks
            if (skill != 0) AddXp(skill, SkillData.BaseXp(a.level));
            Vector3 p = LocalPlayer.transform.position;
            var targets = new List<NpcEntity>();
            if (rule.kind == AbilityKind.Sweep)
            {
                float reach = Mathf.Max(rule.radius, WeaponRange(EquippedWeapon()));
                foreach (var n in world.npcs)
                    if (!n.dead && CanFight(n) && Vector3.Distance(NpcPosition(n), p) <= reach) targets.Add(n);
                if (targets.Count == 0) Plugin.Log.LogInfo($"[ability] {a.name}: nothing within {reach}m");
            }
            else
            {
                targets.Add(npc);
                if (rule.kind == AbilityKind.Splash)
                {
                    Vector3 c = NpcPosition(npc);
                    foreach (var n in world.npcs)
                        if (n != npc && !n.dead && CanFight(n) && Vector3.Distance(NpcPosition(n), c) <= rule.radius) targets.Add(n);
                }
            }

            float power = rule.power;
            int burst = 0;
            if (rule.kind == AbilityKind.ManaBurst)
            {
                burst = Mathf.Min(character.currentMana, rule.manaCap);
                character.currentMana -= burst;
                SendMana();
                power = 1f;
            }

            var weapon = EquippedWeapon();
            int targetGfx = a.hitGfx > 0 ? a.hitGfx : AbilityTargetGfx.TryGetValue(a.id, out int g) ? g : 0;
            AbilityProjectile.TryGetValue(a.id, out int projectile);
            bool vampiric = a.id == 144, execution = a.id == 138;
            var releases = new List<System.Action<int, int>>();
            foreach (var t in targets)
            {
                Aggro(t);
                var target = t;
                int hit = RollPlayerHit(weapon, t, power) + burst / 2;
                if (execution && t.health * 4 <= t.maxHealth) hit *= 2;   // double damage on a badly hurt target
                // Damage over time replaces part of the up-front hit; a flurry splits the rest between its strikes.
                int upFront = rule.dot > 0f ? hit / 2 : hit;
                System.Action<int, int> land = (strike, strikes) =>
                {
                    if (target.dead) return;
                    PlayNpcEffect(target, targetGfx);
                    PlaySound(a.hitSfx.Length > 0 ? Pick(a.hitSfx) : weaponHitSfx, NpcPosition(target));
                    if (strike == 0 && hit > 0) ApplyNpcEffects(target, rule, hit, xpSkill);
                    int share = upFront / strikes + (strike == strikes - 1 ? upFront % strikes : 0);
                    DealDamage(target, share, xpSkill);
                    if (vampiric && share > 0) Heal(share / 2);
                };
                releases.Add((strike, strikes) =>
                {
                    if (target.dead) return;
                    if (projectile == 0) land(strike, strikes);
                    else if (strike == 0) ShootAtNpc(projectile, target, () => land(0, 1));
                });
            }
            // The blows land when the cast animation strikes: MeasureSwing finds the clip, and attackdelay is the
            // fallback for clips that haven't been measured.
            if (releases.Count > 0)
            {
                pendingBlow = (strike, strikes) => { foreach (var r in releases) r(strike, strikes); };
                pendingBlowFallback = Mathf.Min(a.attackDelay, a.attackTime);
            }
            if (npc != null) autoAttacking = !npc.dead;
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

        // Reconstructed: a bandage heals more the better it is and the higher the Healing level.
        private static int BandageHeal(int bandage, int level)
        {
            int tier;
            switch (bandage)
            {
                case 10367: tier = 0; break;   // wool
                case 10368: tier = 1; break;   // cotton
                case 10369: tier = 2; break;   // silk
                case 10370: tier = 3; break;   // jute
                case 10443: tier = 4; break;   // flax
                default: tier = 5; break;      // ramie
            }
            return 15 + 20 * tier + 3 * level;
        }

        private void TauntEnemies()
        {
            var player = LocalPlayer;
            if (world == null || player == null) return;
            int level = Mathf.Max(SkillLevel(26), 1);
            foreach (var n in world.npcs)
                if (!n.dead && CanFight(n) && GameData.Npcs.TryGetValue(n.typeId, out var info) && info.level <= level
                    && Vector3.Distance(NpcPosition(n), player.transform.position) <= 15f)
                    Aggro(n);
        }

        // ---- effects on NPCs -----------------------------------------------------------------

        private void ApplyNpcEffects(NpcEntity npc, AbilityRule rule, int hit, int skill)
        {
            if (rule.stun > 0f)
            {
                npc.stunnedUntil = Mathf.Max(npc.stunnedUntil, Time.time + rule.stun);
                PlayNpcEffect(npc, 21);
            }
            if (rule.root > 0f) npc.rootedUntil = Mathf.Max(npc.rootedUntil, Time.time + rule.root);
            if (rule.stun > 0f || rule.root > 0f) HoldNpc(npc);
            if (rule.dot > 0f)
            {
                int ticks = Mathf.Max(1, Mathf.RoundToInt(rule.dot));
                npc.dotDamage = Mathf.Max(1, (hit - hit / 2) / ticks);
                npc.dotTicks = ticks;
                npc.dotSkill = skill;
                npc.nextDot = Time.time + 1f;
            }
        }

        private void TickNpcEffects()
        {
            if (world == null) return;
            foreach (var n in world.npcs)
            {
                if (n.dead || n.dotTicks <= 0 || Time.time < n.nextDot) continue;
                n.dotTicks--;
                n.nextDot = Time.time + 1f;
                DealDamage(n, n.dotDamage, n.dotSkill);
            }
        }
    }
}
