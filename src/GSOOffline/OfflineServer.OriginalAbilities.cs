using System.Collections.Generic;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// The original server's abilities (Scr_PlayerAttacksHandler.abilityAttack, Scr_PlayerBuffs, Scr_AbilitiesHandler,
    /// update 136), one case each with its own formula, damage kind and effects, and the later patch notes' numbers
    /// on top: Explosive arrow -27%, Energy blade -50%, Healing music and aura -15%, Tritone without its mana boost
    /// but twice the damage (all update 137). "Max" below is the original's calculateWeaponDamage: 10 x the weapon's
    /// level + 10 x the skill's. Fire wall and Firestorm keep the 136 numbers: update 140 lowered them by an amount
    /// it didn't give.
    /// </summary>
    public partial class OfflineServer
    {
        private const int PoisonForArrows = 10377, ItemSnowball = 10558, HuntersBow = 432;

        private static float U(float min, float max) => Random.Range(min, max);

        /// <summary>calculateWeaponDamage: 10 x the weapon's level + 10 x the skill's level.</summary>
        private int WeaponMax(int skill) => WeaponLevel(EquippedWeapon()) * 10 + SkillLevel(skill) * 10;

        /// <summary>A shot at an NPC; <paramref name="onHit"/> gets its damage when it lands.</summary>
        private void Shoot(int projectile, NpcEntity target, int damage, bool explicitDamage, System.Action<int> onHit)
        {
            if (target == null || target.dead) return;
            ShootAtNpc(projectile, target, () => { if (!target.dead) onHit(ShotDamage(projectile, damage, explicitDamage)); });
        }

        private int bulletsMain, bulletsOffhand;

        private int Bullets(bool mainHand) => mainHand ? bulletsMain : bulletsOffhand;

        private int slashingStage;   // Slashing: each use of the greatsword's flurry is the next of three (87, 161, 162)

        /// <summary>
        /// The original's code for ability <paramref name="a"/>. Buffs and heals take effect now; attacks return what
        /// happens when the cast strikes (<paramref name="release"/>). False if the original had no code for it.
        /// </summary>
        private bool CastOriginal(AbilityInfo a, NpcEntity t, Vector3 center, out System.Action release)
        {
            release = null;
            var player = LocalPlayer;
            Vector3 me = player.transform.position;
            var w = EquippedWeapon();
            int wl = WeaponLevel(w), D = StatDamage();
            int ele = SkillLevel(SkillElementalGrim), sw = SkillLevel(SkillSwords), ar = SkillLevel(SkillArchery);
            int heal = SkillLevel(SkillHealing), ha = SkillLevel(SkillHeavyArmor);
            int fire = StatDamageOf(DamageKinds.Fire), ice = StatDamageOf(DamageKinds.Ice), slash = StatDamageOf(DamageKinds.Slash);
            int pierce = StatDamageOf(DamageKinds.Piercing), blunt = StatDamageOf(DamageKinds.Blunt), poison = StatDamageOf(DamageKinds.Poison);
            switch (a.id)
            {
                // ---- Elemental grimoire ----
                case 1:   // Fire ball: a projectile that bursts 2 m around where it lands
                {
                    CountXp(SkillElementalGrim);
                    int dmg = (int)((50 + D + fire + ele * 25f) * U(0.7f, 1f));
                    release = () => Shoot(1, t, dmg, true, d => AoeDamage(NpcPosition(t), d, DamageKinds.Fire, 2f));
                    return true;
                }
                case 118:   // Flame bolt: the fire ball's projectile at its own damage
                    CountXp(SkillElementalGrim);
                    release = () => Shoot(1, t, 0, false, d => AoeDamage(NpcPosition(t), d, DamageKinds.Fire, 2f));
                    return true;
                case 54: case 55: case 56:   // staff fire, ice and electric attacks
                {
                    CountXp(SkillElementalGrim);
                    int kind = a.id == 54 ? DamageKinds.Fire : a.id == 55 ? DamageKinds.Ice : DamageKinds.Electric;
                    int dmg = WeaponMax(SkillElementalGrim) + D + StatDamageOf(kind);
                    dmg = (int)U(dmg * 0.9f, dmg * 1.3f);
                    release = () => { HitNpc(t, dmg, kind); PlaySound(278, me, 40); };
                    return true;
                }
                case 57:   // Stranglevine: earth damage and a 4 second stun
                {
                    CountXp(SkillElementalGrim);
                    int dmg = (int)((25 + D + StatDamageOf(DamageKinds.Earth) + ele * 5f) * U(0.7f, 1f));
                    release = () =>
                    {
                        HitNpc(t, dmg, DamageKinds.Earth);
                        StunNpc(t, 4f);
                        PlayEffect(12, NpcPosition(t));
                        PlaySound(249, NpcPosition(t), 40);
                    };
                    return true;
                }
                case 58:   // Rockslide
                {
                    CountXp(SkillElementalGrim);
                    int dmg = WeaponMax(SkillElementalGrim) + D + StatDamageOf(DamageKinds.Earth);
                    dmg = (int)U(dmg * 0.7f, dmg);
                    release = () =>
                    {
                        HitNpc(t, dmg, DamageKinds.Earth);
                        PlayEffect(13, NpcPosition(t));
                        PlaySound(221, NpcPosition(t), 40);
                    };
                    return true;
                }
                case 94:   // Charm: heals around you (offline: you)
                {
                    CountXp(SkillElementalGrim);
                    int max = ele * 20 + GearStat("Healing");
                    PlayEffect(26, me);
                    Heal((int)U(max * 0.7f, max));
                    OnHealed();
                    return true;
                }
                case 95:   // Demolition: earth damage 5 m around you
                {
                    CountXp(SkillElementalGrim);
                    int dmg = ele * 25 + D + StatDamageOf(DamageKinds.Earth);
                    dmg = (int)U(dmg * 0.7f, dmg);
                    PlayEffect(27, me);
                    release = () => AoeDamage(LocalPlayer.transform.position, dmg, DamageKinds.Earth, 5f);
                    return true;
                }
                case 96:   // Purification: the healing projectile (offline: on you)
                {
                    CountXp(SkillElementalGrim);
                    int max = ele * 20;
                    Heal((int)U(max * 0.7f, max));
                    OnHealed();
                    return true;
                }
                case 97:   // Flames
                case 166:  // Dragon flames
                {
                    CountXp(SkillElementalGrim);
                    int dmg = (int)((25 + D + fire + ele * 25f) * (a.id == 97 ? U(0.7f, 1f) : U(1.2f, 1.5f)));
                    if (a.id == 97) PlayPlayerEffect(31);
                    PlaySound(a.id == 97 ? 309 : 439, me, 50);
                    release = () => { HitNpc(t, dmg, DamageKinds.Fire); if (a.id == 166) PlayNpcEffect(t, 158); };
                    return true;
                }
                case 107:   // Fire wisp: 2 x Elemental grimoire fire damage for 27 s, taken out of your mana; two at once
                {
                    PlayPlayerEffect(45);
                    PlaySound(65, me, 30);
                    bool second = HasBuffFrom(AbilityFireWisp);
                    AddBuff(second ? AbilityFireWisp2 : AbilityFireWisp, second ? 86 : 85, 27f, null, ele * 2);
                    return true;
                }
                case 112:   // Combustion: 15 + 0.6 x (Damage + FireDamage) + 2.5 x Elemental grimoire fire a second, 10 s
                {
                    CountXp(SkillElementalGrim);
                    int dmg = (int)(15 + (D + fire) * 0.6f + ele * 2.5f);
                    release = () =>
                    {
                        AddDot(t, DotCombustion, dmg, DamageKinds.Fire, 1f, 10f);
                        Aggro(t);
                        PlaySound(152, NpcPosition(t), 40);
                        PlayNpcEffect(t, 49);
                    };
                    return true;
                }
                case 113:   // Blast: fire 5 m around the clicked spot; sets off landmines there
                {
                    CountXp(SkillElementalGrim);
                    int dmg = (int)((35 + fire + D + ele * 35f) * U(0.7f, 1f));
                    release = () =>
                    {
                        PlayEffect(28, center);
                        PlaySound(219, center, 40);
                        AoeDamage(center, dmg, DamageKinds.Fire, 5f);
                        foreach (var m in areas)
                            if (m.kind == AreaLandmine && Vector3.Distance(m.pos, center) < 5f) m.explode = true;
                    };
                    return true;
                }
                case 114:   // Trail blaze
                    PlayPlayerEffect(50);
                    PlaySound(354, me, 40);
                    if (!HasBuffFrom(a.id)) AddBuff(a.id, 33, 5f, null);
                    return true;
                case 115:   // Flame barrier: +15 fire, ice, earth and wind resistance, half of physical blows back, 15 s
                    CountXp(SkillElementalGrim);
                    PlayPlayerEffect(56);
                    AddBuff(a.id, 36, 15f, null);
                    return true;
                case 116:   // Flame lance: burns the target each second for 20 s while you stand still and have mana
                {
                    CountXp(SkillElementalGrim);
                    PlayEffect(51, me);
                    var lance = AddBuff(a.id, 0, 20f, null);
                    lance.target = t;
                    return true;
                }
                case 117:   // Immolate: 15% more fire damage, +10 fire and -10 ice resistance, 20 s
                    CountXp(SkillElementalGrim);
                    PlayPlayerEffect(53);
                    PlaySound(230, me, 30);
                    AddBuff(a.id, 35, 20f, null, (int)(GearStat(DamageKinds.DamageStat(DamageKinds.Fire)) * 1.15f));
                    return true;
                case 119:   // Fire wall: 5 s on the clicked spot, 35 + 0.8 x (Damage + FireDamage) + 10 x Elemental grimoire a second
                {
                    CountXp(SkillElementalGrim);
                    int dmg = (int)(35 + (D + fire) * 0.8f + ele * 10f);
                    PlaySound(205, center, 30);
                    areas.Add(new AreaEffect { kind = AreaFireWall, pos = center, damage = dmg, until = Time.time + 5f, next = Time.time });
                    return true;
                }
                case 120:   // Eruption: fire 5 m around the target; spreads its Combustion to everything hit
                {
                    CountXp(SkillElementalGrim);
                    int dmg = (int)((25 + fire + D + ele * 40f) * U(0.7f, 1f));
                    int combustion = (int)(15 + (D + fire) * 0.6f + ele * 6f);
                    PlaySound(309, me, 50);
                    release = () =>
                    {
                        Vector3 at = NpcPosition(t);
                        bool spread = t.dots.Exists(d => d.id == DotCombustion);
                        PlayEffect(57, at);
                        foreach (var n in world.npcs.ToArray())
                        {
                            if (n.dead || !CanFight(n) || Vector3.Distance(NpcPosition(n), at) >= 5f) continue;
                            HitNpc(n, dmg, DamageKinds.Fire);
                            if (!spread || n.dead) continue;
                            AddDot(n, DotCombustion, combustion, DamageKinds.Fire, 1f, 10f);
                            PlayNpcEffect(n, 49);
                        }
                    };
                    return true;
                }
                case 121:   // Firestorm: (0.8 x (Damage + FireDamage) + 10 x Elemental grimoire) a second 4 m around you, 8 s
                {
                    CountXp(SkillElementalGrim);
                    PlayPlayerEffect(53);
                    AddBuff(a.id, 0, 8f, null, (int)(((D + fire) * 0.8f + ele * 10f) * U(0.7f, 1f)));
                    return true;
                }
                case 122:   // Landmine: armed after 3 s, goes off when something moves within 2.5 m (or after 90 s); 6 m of fire
                {
                    CountXp(SkillElementalGrim);
                    int dmg = (int)((75 + (D + fire) * 2f + ele * 40f) * U(0.9f, 1f));
                    PlaceMine(center, dmg, 90f);
                    return true;
                }
                case 123:   // Flashfreeze: ice 4 m around you, slowing for 4 s
                {
                    CountXp(SkillElementalGrim);
                    int dmg = (int)((75 + (D + ice) * 2.5f + ele * 7.5f) * U(0.7f, 1f));
                    PlayEffect(61, me);
                    release = () => AoeDamage(LocalPlayer.transform.position, dmg, DamageKinds.Ice, 4f, 1);
                    return true;
                }
                case 124:   // Waterwalk
                    CountXp(SkillElementalGrim);
                    PlayPlayerEffect(60);
                    AddBuff(a.id, 0, 15f, null);
                    return true;
                case 125:   // Ice barrier: +30 slash, blunt and piercing resistance, 15 s
                    CountXp(SkillElementalGrim);
                    PlayPlayerEffect(59);
                    AddBuff(a.id, 0, 15f, null);
                    return true;
                case 126:   // Cryostasis: frozen in place 10 s, healing 5% of your health a second
                    CountXp(SkillElementalGrim);
                    PlayPlayerEffect(58);
                    AddBuff(a.id, 0, 10f, null);
                    StunPlayer(10f);   // and the original's stun with it: no abilities or items either
                    StopAttacking();
                    UpdateCanMove();
                    return true;
                case 127:   // Icy touch
                case 128:   // Glacial spike: 55 + 0.6 x (Damage + IceDamage) + 2.5 x Elemental grimoire (fire, as the original), and a freezing stack
                {
                    CountXp(SkillElementalGrim);
                    int dmg = (int)(55 + (D + ice) * 0.6f + ele * 2.5f);
                    release = () =>
                    {
                        PlayEffect(a.id == 127 ? 8 : 64, NpcPosition(t));
                        PlaySound(152, NpcPosition(t), 40);
                        HitNpc(t, dmg, DamageKinds.Fire);
                        FreezeNpc(t);
                    };
                    return true;
                }
                case 148:   // Torch throw: sets the target burning (Combustion) for 10 s
                {
                    int dmg = (int)(15 + (D + fire) * 0.6f + ele * 2.5f);
                    release = () => Shoot(39, t, dmg, true, d =>
                    {
                        AddDot(t, DotCombustion, d, DamageKinds.Fire, 1f, 10f);
                        Aggro(t);
                        PlaySound(152, NpcPosition(t), 40);
                        PlayNpcEffect(t, 49);
                    });
                    return true;
                }

                // ---- Swordsmanship and the other weapon skills ----
                case 48:   // Sword throw: the sword spins where it lands for 5 s (a tenth of the hit every half second, 2 m)
                {
                    CountXp(SkillSwords);
                    int max = WeaponMax(SkillSwords);
                    int dmg = Mathf.RoundToInt(U(max * 0.75f, max)) + slash + D + GearStat("Mana");
                    release = () => Shoot(9, t, dmg, true, d =>
                    {
                        HitNpc(t, d, DamageKinds.Slash);
                        areas.Add(new AreaEffect { kind = AreaSword, pos = NpcPosition(t), damage = (int)(d * 0.1f), until = Time.time + 5f, next = Time.time + 0.5f });
                    });
                    return true;
                }
                case 50:   // Broadsword fire attack: fire, and an ignite of a fifth of it a second for 5 s
                {
                    CountXp(SkillSwords);
                    int max = WeaponMax(SkillSwords);
                    int dmg = Mathf.RoundToInt(U(max * 0.75f, max)) + slash + D + fire;
                    release = () =>
                    {
                        AddDot(t, DotIgnite, (int)(dmg * 0.2f), DamageKinds.Fire, 1f, 5f, false);
                        HitNpc(t, dmg, DamageKinds.Fire);
                    };
                    return true;
                }
                case 51:   // Broadsword ice attack: a 2 second stun
                {
                    CountXp(SkillSwords);
                    int dmg = Mathf.RoundToInt(U(1, 1 + WeaponMax(SkillSwords) * 1.2f + slash + ice)) + D;
                    release = () => { StunNpc(t, 2f); HitNpc(t, dmg, DamageKinds.Ice); };
                    return true;
                }
                case 52:   // Broadsword electric attack: half of it again 3 m around the target
                {
                    CountXp(SkillSwords);
                    int dmg = Mathf.RoundToInt(U(1, 1 + WeaponMax(SkillSwords) * 1.2f + slash + StatDamageOf(DamageKinds.Electric))) + D;
                    release = () =>
                    {
                        AoeDamage(NpcPosition(t), (int)(dmg * 0.5f), DamageKinds.Electric, 3f);
                        HitNpc(t, dmg, DamageKinds.Electric);
                    };
                    return true;
                }
                case 53:   // Bash (mace fighting)
                {
                    CountXp(SkillMace);
                    int dmg = Mathf.RoundToInt(U(1, 1 + WeaponMax(SkillMace) * 1.2f + slash + blunt)) + D;
                    release = () => { HitNpc(t, dmg, DamageKinds.Blunt); PlaySound(278, me, 40); };
                    return true;
                }
                case 86:   // Brutal slash
                {
                    CountXp(SkillSwords);
                    int dmg = (int)((sw * 10f + wl * 10 + D + slash) * U(1.5f, 2f));
                    release = () => HitNpc(t, dmg, DamageKinds.Slash);
                    return true;
                }
                case 87: case 161: case 162:   // Slashing: three blows in turn, 3 m in front of you (the third 5 m and harder)
                {
                    CountXp(SkillSwords);
                    int stage = slashingStage;
                    slashingStage = (slashingStage + 1) % 3;
                    float per = stage == 0 ? 10f : stage == 1 ? 13f : 20f;
                    float r = stage == 2 ? U(1.8f, 2f) : U(1f, 1.5f);
                    int dmg = (int)((sw * per + wl * 10 + D + slash) * r);
                    float range = stage == 2 ? 5f : 3f;
                    PlaySound(stage == 0 ? 277 : stage == 1 ? 426 : 425, me, 30);
                    release = () =>
                    {
                        var p = LocalPlayer;
                        Vector3 at = p.transform.position + p.transform.forward * 3f;
                        PlayEffect(stage == 0 ? 145 : stage == 1 ? 143 : 144, at);
                        AoeDamage(at, dmg, DamageKinds.Slash, range);
                    };
                    return true;
                }
                case 88:   // Cripple: roots for 5 s
                {
                    CountXp(SkillSwords);
                    int dmg = (int)((sw * 10f + wl * 10 + D + slash) * U(0.7f, 1.3f));
                    release = () => { RootNpc(t, 5f); HitNpc(t, dmg, DamageKinds.Slash); };
                    return true;
                }
                case 89:   // Stun: blunt, a 5 second stun
                {
                    CountXp(SkillSwords);
                    int dmg = Mathf.RoundToInt(U(1, 1 + WeaponMax(SkillSwords) * 1.3f + slash));
                    release = () => { HitNpc(t, dmg, DamageKinds.Blunt); StunNpc(t, 5f); };
                    return true;
                }
                case 92:   // Mace multiple hit: everything 5 m around the target
                {
                    CountXp(SkillMace);
                    int dmg = Mathf.RoundToInt(U(1, 1 + WeaponMax(SkillMace) * 1.2f + slash + blunt)) + D;
                    release = () => { AoeDamage(NpcPosition(t), dmg, DamageKinds.Blunt, 5f); PlaySound(278, me, 40); };
                    return true;
                }
                case 93:   // Mace slow: 10 seconds (slash damage, as the original)
                {
                    CountXp(SkillMace);
                    int dmg = Mathf.RoundToInt(U(1, 1 + WeaponMax(SkillMace) * 1.3f + blunt)) + D;
                    release = () => { HitNpc(t, dmg, DamageKinds.Slash); SlowNpc(t, 10f); };
                    return true;
                }
                case 105:   // Stunning blow: blunt, a 3 second stun
                {
                    CountXp(SkillSwords);
                    int dmg = Mathf.RoundToInt(U(1, 1 + WeaponMax(SkillSwords) * 2f + blunt)) + D;
                    PlayPlayerEffect(42);
                    release = () => { HitNpc(t, dmg, DamageKinds.Blunt); StunNpc(t, 3f); };
                    return true;
                }
                case 106:   // Torch: burn
                {
                    CountXp(SkillSwords);
                    int dmg = WeaponMax(SkillSwords) + D + fire;
                    dmg = (int)U(dmg * 0.9f, dmg * 1.3f);
                    release = () => { HitNpc(t, dmg, DamageKinds.Fire); PlaySound(278, me, 40); };
                    return true;
                }
                case 138:   // Execution: twice as hard on a stunned target
                {
                    CountXp(SkillSwords);
                    int dmg = WeaponMax(SkillSwords) + D + slash;
                    dmg = (int)U(dmg * 1.2f, dmg * 1.3f);
                    release = () =>
                    {
                        int d = Time.time < t.stunnedUntil ? dmg * 2 : dmg;
                        HitNpc(t, d, DamageKinds.Slash);
                        PlaySound(364, NpcPosition(t), 50);
                    };
                    return true;
                }
                case 159: case 160:   // Rat dagger stab, and poison: a fifth of the hit every 5 s for 2 minutes
                {
                    CountXp(SkillSwords);
                    int offhand = WeaponLevel(EquippedInSlot("Shield"));
                    int dmg = (int)((sw * 10f + offhand * 10 + D + slash) * U(0.7f, 1.3f));
                    release = () =>
                    {
                        HitNpc(t, dmg, DamageKinds.Piercing);
                        if (a.id == 160) AddDot(t, DotWeakPoison, (int)(dmg * 0.2f), DamageKinds.Poison, 5f, 120f);
                    };
                    return true;
                }
                case 84:   // Frenzy: dagger blows 10% harder and every 0.8 s, 5 s
                    AddBuff(a.id, 0, 5f, null);
                    return true;
                case 85:   // Dagger slashing: five stabs over 2.5 s, each harder than the last
                {
                    CountXp(SkillFencing);
                    int max = WeaponMax(SkillFencing);
                    release = () =>
                    {
                        for (int k = 1; k <= 5; k++)
                        {
                            int stab = k;
                            Later(0.5f * (k - 1), () =>
                            {
                                if (PlayerDead || t.dead || LocalPlayer == null || Vector3.Distance(LocalPlayer.transform.position, NpcPosition(t)) >= 5f) return;
                                int dmg = Mathf.RoundToInt(U(1, 1 + max * (0.7f + stab * 0.1f) + StatDamageOf(DamageKinds.Piercing)));
                                HitNpc(t, dmg, DamageKinds.Piercing);
                                PlaySound(93, NpcPosition(t), 30);
                            });
                        }
                    };
                    return true;
                }
                case 100:   // Strength: 15 x Swordsmanship slash damage, 10 s
                    PlayPlayerEffect(35);
                    PlaySound(65, me, 30);
                    AddBuff(a.id, 7, 10f, null, sw * 15);
                    return true;
                case 103:   // Survivor: half your health again for 20 s, taken back when it ends
                    PlayPlayerEffect(35);
                    PlaySound(65, me, 30);
                    AddBuff(a.id, 10, 20f, new BuffEffect { payBackHealth = true }, (int)(MaxHealth() * 0.5f));
                    return true;
                case 104:   // Disperse: twice 10% (+0.25% per Swordsmanship level) of each blow you take hits everything 4 m around you, 20 s
                    PlayPlayerEffect(35);
                    PlaySound(65, me, 30);
                    AddBuff(a.id, 11, 20f, null);
                    return true;
                case 137:   // Charge
                    CountXp(SkillSwords);
                    PlayPlayerEffect(82);
                    PlaySound(65, me, 30);
                    AddBuff(a.id, 49, 7f, null);
                    return true;
                case 139:   // Energy blade: stores the magic damage you take for 5 s, then releases it 10 m around you
                    CountXp(SkillSwords);
                    PlayPlayerEffect(87);
                    PlaySound(370, me, 50);
                    AddBuff(a.id, 50, 5f, null);
                    return true;
                case 140:   // Hack and slash: faster sword blows, each 15% harder than the last (to double), 5 s
                    PlayPlayerEffect(91);
                    PlaySound(367, me, 30);
                    CountXp(SkillSwords);
                    AddBuff(a.id, 51, 5f, null);
                    return true;

                // ---- Heavy and light armor ----
                case 15:   // Thick skin: blocks up to 7 x Heavy armor more, 30 s
                    PlayPlayerEffect(10);
                    PlaySound(76, me, 40);
                    AddBuff(a.id, 15, 30f, null);
                    return true;
                case 26:   // Shield block: every blow blocked for 3 s, up to 8 x the shield's level more
                    PlayPlayerEffect(9);
                    PlaySound(78, me, 40);
                    AddBuff(a.id, 16, 3f, null);
                    return true;
                case 141:   // Taunt
                    CountXp(SkillHeavyArmor);
                    PlayPlayerEffect(92);
                    PlaySound(character.gender == 0 ? 374 : 375, me, 30);
                    TauntEnemies();
                    return true;
                case 142:   // Stability: half damage, 8 s
                    CountXp(SkillHeavyArmor);
                    PlayPlayerEffect(94);
                    PlaySound(376, me, 30);
                    AddBuff(a.id, 52, 8f, null);
                    return true;
                case 143:   // Truce: immune for 10 s, and nothing fights you meanwhile
                    CountXp(SkillHeavyArmor);
                    PlayPlayerEffect(96);
                    PlayEffect(97, me);
                    PlaySound(378, me, 30);
                    AddBuff(a.id, 53, 10f, new BuffEffect { damageTaken = 0f, peace = true });
                    CalmEnemies();
                    StopAttacking();
                    return true;
                case 149:   // Psychic glyph: psychic damage 8 m around you; +150 damage, +150 psychic damage and +20 psychic resistance while you stay on it, 30 s
                {
                    CountXp(SkillHeavyArmor);
                    int dmg = ha * 30 + 3 * (D + StatDamageOf(DamageKinds.Psychic));
                    PlayEffect(117, me);
                    release = () =>
                    {
                        AoeDamage(LocalPlayer.transform.position, dmg, DamageKinds.Psychic, 8f);
                        AddBuff(a.id, 73, 30f, null, GlyphResistance);
                    };
                    return true;
                }
                case 167:   // Windwalking
                    CountXp(SkillLightArmor);
                    PlayPlayerEffect(159);
                    PlaySound(66, me, 40);
                    AddBuff(a.id, 91, 30f, null, 2);
                    return true;
                case 168:   // Mana surge: 0.4 x Light armor more mana a second, 10 s
                    CountXp(SkillLightArmor);
                    PlaySound(440, me, 40);
                    AddBuff(a.id, 92, 10f, null, (int)(SkillLevel(SkillLightArmor) * 0.4f));
                    return true;
                case 169:   // Embrace the pain: the share of your health a blow takes comes back as that share of your mana, 15 s
                    CountXp(SkillLightArmor);
                    PlayPlayerEffect(161);
                    PlaySound(441, me, 40);
                    AddBuff(a.id, 93, 15f, null);
                    return true;
                case 170:   // Traverse: immune for 4 s
                    CountXp(SkillLightArmor);
                    PlayPlayerEffect(163);
                    PlaySound(442, me, 40);
                    AddBuff(a.id, 96, 4f, new BuffEffect { damageTaken = 0f }, 5);
                    StopAttacking();
                    return true;

                // ---- Archery ----
                case 61:   // Focus: crossbow shots every half second, 3 s
                    CountXp(SkillArchery);
                    AddBuff(a.id, 79, 3f, null);
                    return true;
                case 62:   // Fire arrow: fire, and an ignite of a fifth of it a second for 5 s
                    CountXp(SkillArchery);
                    release = () => Shoot(11, t, 0, false, d =>
                    {
                        HitNpc(t, d, DamageKinds.Fire);
                        AddDot(t, DotIgnite, (int)(d * 0.2f), DamageKinds.Fire, 1f, 5f, false);
                    });
                    return true;
                case 63:   // Ice arrow: a 2 second stun
                    CountXp(SkillArchery);
                    release = () => Shoot(12, t, 0, false, d => { HitNpc(t, d, DamageKinds.Ice); StunNpc(t, 2f); });
                    return true;
                case 64:   // Electric arrow: half of it again 3 m around
                    CountXp(SkillArchery);
                    release = () => Shoot(13, t, 0, false, d =>
                    {
                        HitNpc(t, d, DamageKinds.Electric);
                        AoeDamage(NpcPosition(t), (int)(d * 0.5f), DamageKinds.Electric, 3f);
                    });
                    return true;
                case 65:   // Explosive arrow: fire 5 m around where it lands, keeping Combustions there burning (update 137: 27% less)
                {
                    CountXp(SkillArchery);
                    int dmg = (int)((ar * 10f + wl * 10 + D + pierce) * U(1.7f, 2f) * 0.73f);
                    release = () => Shoot(10, t, dmg, true, d =>
                    {
                        Vector3 at = NpcPosition(t);
                        foreach (var n in world.npcs)
                            foreach (var dot in n.dots)
                                if (dot.id == DotCombustion && Vector3.Distance(NpcPosition(n), at) < 5f) dot.until = Time.time + 10f;
                        AoeDamage(at, d, DamageKinds.Fire, 5f);
                    });
                    return true;
                }
                case 66:   // Poison arrow: uses up a poison; poisons for 30 s (1 + 40% of the shot every 5 s), no blow
                {
                    CountXp(SkillArchery);
                    int dmg = (int)((ar * 10f + wl * 10 + D + pierce + poison) * U(0.8f, 1.5f));
                    TakeItems(PoisonForArrows, 1);
                    release = () => Shoot(14, t, dmg, true, d =>
                    {
                        AddDot(t, DotPoisonArrow, 1 + (int)(d * 0.4f), DamageKinds.Poison, 5f, 30f, false);
                        Aggro(t);
                    });
                    return true;
                }
                case 67:   // Weakness arrow: 15 less of every resistance for 28 s
                    CountXp(SkillArchery);
                    release = () => Shoot(15, t, 0, true, d => { WeakenNpc(t); Aggro(t); });
                    return true;
                case 90:   // Hunting net: slows for 8 s
                    CountXp(SkillArchery);
                    release = () => Shoot(19, t, 0, true, d => { SlowNpc(t, 8f); Aggro(t); });
                    return true;
                case 91:   // Piercing shot: past the target's defence roll
                {
                    CountXp(SkillArchery);
                    int dmg = (int)((ar * 10f + wl * 10 + D + pierce) * U(1.5f, 2f));
                    release = () => Shoot(20, t, dmg, true, d => HitNpc(t, d, DamageKinds.Piercing, HitNoDefence));
                    return true;
                }
                case 77: case 164:   // pistol shots (10 bullets a gun; the offhand pistol counts the shield slot's level)
                {
                    CountXp(SkillArchery);
                    bool main = a.id == 164;
                    if (main) bulletsMain--;
                    else bulletsOffhand--;
                    int level = main ? wl : WeaponLevel(EquippedInSlot("Shield"));
                    int dmg = (int)((ar * 10f + level * 10 + D + pierce) * U(0.9f, 1.3f));
                    PlayPlayerEffect(15);
                    PlaySound(305, me, 50);
                    release = () => Shoot(16, t, dmg, true, d => HitNpc(t, d, DamageKinds.Piercing));
                    return true;
                }
                case 76: case 163:   // pistol reloads
                {
                    bool main = a.id == 163;
                    int loaded = Bullets(main);
                    if (loaded >= 10)
                    {
                        Notice("The gun is already fully loaded.", "gray");
                        return true;
                    }
                    if (main) bulletsMain = 10;
                    else bulletsOffhand = 10;
                    PlaySound(306, me, 30);
                    Notice($"You load the gun with {10 - loaded} bullets.", "gray");
                    return true;
                }
                case 144:   // Vampiric arrow: half of it comes back to you as health
                {
                    CountXp(SkillArchery);
                    int dmg = (int)((ar * 10f + wl * 10 + D + pierce) * U(0.7f, 1.5f));
                    release = () => Shoot(32, t, dmg, true, d =>
                    {
                        HitNpc(t, d, DamageKinds.Piercing);
                        Heal(1 + (int)(d * 0.5f));
                    });
                    return true;
                }
                case 145:   // Ooze: and a fifth of it as poison a second for 5 s
                {
                    CountXp(SkillArchery);
                    int dmg = (int)((ar * 10f + wl * 10 + D + poison) * U(0.7f, 1f));
                    release = () => Shoot(35, t, dmg, true, d =>
                    {
                        HitNpc(t, d, DamageKinds.None);
                        AddDot(t, DotOoze, (int)(d * 0.2f), DamageKinds.Poison, 1f, 5f);
                    });
                    return true;
                }
                case 150:   // Fire arrows: bow shots burn, 45 s
                    CountXp(SkillArchery);
                    PlaySound(265, me, 30);
                    AddBuff(a.id, 74, 45f, null);
                    return true;
                case 151:   // Friendly snowball: no harm done
                    TakeItems(ItemSnowball, 1);
                    CountXp(SkillArchery);
                    release = () => Shoot(42, t, 0, true, d => { });
                    return true;
                case 152:   // Aggressive snowball: Snowmen take it without their ice resistance, the winter bosses 1000
                {
                    TakeItems(ItemSnowball, 1);
                    CountXp(SkillArchery);
                    int dmg = (int)((ar * 10f + D + ice) * U(0.7f, 1f));
                    release = () => Shoot(43, t, dmg, true, d =>
                    {
                        if (t.typeId == SnowBossType || t.typeId == SnowBossMinionType) HitNpc(t, 1000, DamageKinds.Ice, HitNoIceResistance);
                        else HitNpc(t, d, DamageKinds.Ice, t.typeId == SnowmanType ? HitNoIceResistance : 0);
                    });
                    return true;
                }
                case 154:   // Venom arrow: past poison resistance and block, then venom of a fifth of it a second for 5 s
                {
                    int dmg = (int)((ar * 20f + D + pierce + poison) * U(1.8f, 2f));
                    release = () => Shoot(46, t, dmg, true, d =>
                    {
                        HitNpc(t, d, DamageKinds.Poison, HitNoPoisonResistance | HitNoBlock);
                        AddDot(t, DotSpiderVenom, (int)(d * 0.2f), DamageKinds.Poison, 1f, 5f);
                    });
                    return true;
                }
                case 155:   // Thorny vines: roots for 6 s
                {
                    int dmg = (int)((ar * 10f + D + pierce + poison) * U(1.8f, 2f));
                    release = () => Shoot(47, t, dmg, true, d => { HitNpc(t, d, DamageKinds.Poison); RootNpc(t, 6f); });
                    return true;
                }
                case 165:   // Wounding shot (the hunter's bow): bleeds for 28 s
                {
                    CountXp(SkillArchery);
                    int dmg = (int)((ar * 10f + wl * 10 + D + pierce) * U(0.7f, 1.5f));
                    release = () => Shoot(56, t, dmg, true, d =>
                    {
                        HitNpc(t, d, DamageKinds.None);
                        AddDot(t, DotBleeding, 0, DamageKinds.None, 1f, 28f);
                    });
                    return true;
                }

                // ---- Healing (the lute) ----
                case 98:   // Healing music: heals you each second of the song
                case 99:   // Healing aura: heals everyone within 6 m (offline: you)
                    CountXp(SkillHealing);
                    PlayPlayerEffect(a.id == 98 ? 32 : 33);
                    PlaySound(heal > 30 ? 312 : heal > 15 ? 311 : 310, me, 40);
                    AddBuff(a.id, 103, Mathf.Max(1f, a.attackTime), null);
                    return true;
                case 101:   // Tritone: (10 x Healing + 10 x weapon level + half (Damage + PsychicDamage)) x 2 (update 137)
                {
                    CountXp(SkillHealing);
                    int dmg = (int)((heal * 10f + wl * 10 + 0.5f * (D + StatDamageOf(DamageKinds.Psychic))) * 2f * U(0.8f, 1.2f));
                    release = () => Shoot(25, t, dmg, true, d => HitNpc(t, d, DamageKinds.Psychic));
                    return true;
                }
                case 102:   // Peace: creatures up to your Healing level leave you be, 20 s
                    PlayPlayerEffect(85);
                    PlaySound(65, me, 30);
                    AddBuff(a.id, 8, 20f, new BuffEffect { peace = true });
                    return true;
                case 156:   // Healing bird: 70 + 30 x Healing x 1.5-2 + 3 x your Healing stat (offline: on you)
                    CountXp(SkillHealing);
                    Heal(70 + (int)(heal * 30f * U(1.5f, 2f)) + 3 * GearStat("Healing"));
                    OnHealed();
                    return true;
                case 158:   // Healing plant: for 30 s, the first time you drop under 30% health within 20 m it heals 30%
                    CountXp(SkillHealing);
                    PlayEffect(137, center);
                    PlaySound(90, center, 30);
                    areas.Add(new AreaEffect { kind = AreaHealingPlant, pos = center, until = Time.time + 30f, next = Time.time + 0.5f });
                    return true;

                case 146:   // Ambitious apprentice: 20% more alchemy experience from gathering, 20 minutes
                    if (HasBuffFrom(a.id))
                    {
                        Notice("'Ambitious Apprentice' effect is already active!", "gray");
                        return true;
                    }
                    PlaySound(395, me, 30);
                    PlayPlayerEffect(104);
                    AddBuff(a.id, 65, 1200f, new BuffEffect { alchemyXp = 1.2f });
                    return true;
            }
            return false;
        }

        // ---- buffs that act each second ------------------------------------------------------------------------

        private void TickOriginalBuffs()
        {
            var player = LocalPlayer;
            if (player == null || PlayerDead) return;
            Vector3 me = player.transform.position;
            foreach (var b in buffs.ToArray())
            {
                switch (b.ability)
                {
                    case AbilityFirestorm:
                        AoeDamage(me, b.amount, DamageKinds.Fire, 4f);
                        PlayPlayerEffect(55);
                        PlaySound(152, me, 30);
                        break;
                    case AbilityFlameLance:
                    {
                        var t = b.target;
                        float moved = Vector3.Distance(new Vector3(me.x, 0f, me.z), new Vector3(b.origin.x, 0f, b.origin.z));
                        if (t == null || t.dead || character.currentMana < 18 || moved >= 2f || Vector3.Distance(me, NpcPosition(t)) >= 50f)
                        {
                            RemoveBuff(b.ability, false);
                            Notice("Interrupted!", "gray");
                            break;
                        }
                        int dmg = (int)((25 + StatDamageOf(DamageKinds.Fire) + StatDamage() + SkillLevel(SkillElementalGrim) * 25f) * U(0.7f, 1f));
                        character.currentMana -= 18;
                        SendMana();
                        PlaySound(152, me, 30);
                        ShootAtNpc(27, t, () => { });
                        HitNpc(t, dmg, DamageKinds.Fire);
                        break;
                    }
                    case AbilityHealingMusic:
                    case AbilityHealingAura:
                    {
                        if (character.currentHealth >= MaxHealth()) break;
                        float stat = GearStat("Healing") * 3, skill = SkillLevel(SkillHealing) * (b.ability == AbilityHealingMusic ? 14f : 10f);
                        float amount = b.ability == AbilityHealingMusic ? U((skill + stat) * 0.7f, skill + stat) : U((skill + stat) * 0.6f, (skill + stat) * 0.8f);
                        Heal((int)(amount * 0.85f));   // update 137: about 15% less
                        break;
                    }
                    case AbilityCryostasis:
                        Heal((int)(MaxHealth() * 0.05f));
                        PlayEffect(70, me);
                        break;
                    case AbilityGlyph:
                        if (Vector3.Distance(me, b.origin) > 8f)
                        {
                            RemoveBuff(b.ability, false);
                            Notice("Psychic Glyph buff has worn off", "gray");
                        }
                        break;
                }
            }
        }

        // ---- things left on the ground --------------------------------------------------------------------------

        private const int AreaFireWall = 1, AreaLandmine = 2, AreaSword = 5, AreaHealingPlant = 6;

        private class AreaEffect
        {
            public int kind, damage, uid;
            public Vector3 pos;
            public float until, next, armedAt, radius;
            public bool explode;
        }

        private readonly List<AreaEffect> areas = new List<AreaEffect>();

        /// <summary>A landmine (the explosive's prop, spawned object 2), blasting <paramref name="blast"/> metres.</summary>
        private void PlaceMine(Vector3 pos, int damage, float lasts, float blast = 6f)
        {
            int uid = ExplosiveUidBase + nextExplosiveUid++ % 1000;
            Send(2, 35, character.name, uid + "_" + ObjExplosive + "_" + SceneWorld.Vec(pos) + "_" + Random.Range(0, 360));
            areas.Add(new AreaEffect { kind = AreaLandmine, pos = pos, damage = damage, uid = uid, armedAt = Time.time + 3f, until = Time.time + lasts, next = Time.time, radius = blast });
        }

        private void TickAreaEffects()
        {
            if (areas.Count == 0) return;
            if (world == null)
            {
                areas.Clear();
                return;
            }
            var player = LocalPlayer;
            foreach (var area in areas.ToArray())
            {
                bool over = Time.time >= area.until;
                if (area.kind == AreaLandmine)
                {
                    if (!over && !area.explode && Time.time >= area.armedAt && Time.time >= area.next)
                    {
                        area.next = Time.time + 0.25f;
                        foreach (var n in world.npcs)
                        {
                            if (n.dead || !CanFight(n) || Vector3.Distance(NpcPosition(n), area.pos) >= 2.5f) continue;
                            var c = Scr_NpcHandler.instance != null ? Scr_NpcHandler.instance.getNpc(n.uid) : null;
                            if (n.hasDest || c != null && c.moving) area.explode = true;
                        }
                        if (player != null && PlayerMoving() && Vector3.Distance(player.transform.position, area.pos) < 2.5f) area.explode = true;
                    }
                    if (!over && !area.explode) continue;
                    areas.Remove(area);
                    Send(25, 10, area.uid);
                    PlayEffect(GfxExplosion, area.pos);
                    PlaySound(SfxExplosion, area.pos, 40);
                    AoeDamage(area.pos, area.damage, DamageKinds.Fire, area.radius);
                    foreach (var other in areas)
                        if (other.kind == AreaLandmine && Vector3.Distance(other.pos, area.pos) < 10f) other.explode = true;
                    continue;
                }
                if (over)
                {
                    areas.Remove(area);
                    continue;
                }
                if (Time.time < area.next) continue;
                switch (area.kind)
                {
                    case AreaFireWall:
                        area.next += 1f;
                        PlayEffect(4, area.pos);
                        AoeDamage(area.pos, area.damage, DamageKinds.Fire, 5f, 2);
                        break;
                    case AreaSword:
                        area.next += 0.5f;
                        AoeDamage(area.pos, area.damage, DamageKinds.Slash, 2f);
                        break;
                    case AreaHealingPlant:
                        area.next += 0.5f;
                        if (player == null || PlayerDead || Vector3.Distance(player.transform.position, area.pos) >= 20f) break;
                        if ((float)character.currentHealth / MaxHealth() >= 0.3f) break;
                        Heal((int)(MaxHealth() * 0.3f));
                        PlayPlayerEffect(136);
                        PlaySound(414, player.transform.position, 40);
                        PlaySound(412, area.pos, 40);
                        areas.Remove(area);
                        break;
                }
            }
        }
    }
}
