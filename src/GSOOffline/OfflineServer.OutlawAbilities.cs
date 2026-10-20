using System.Collections.Generic;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// How a bounty outlaw fights, now that your weapons fight as the original's did. Its basic blow is yours with the
    /// same weapon (<see cref="BasicBlow"/>), its level standing in for the skill's. Its abilities are the ones its
    /// own weapon and shield rolled (Ability1..4, the slots your bar would get from them), by the original's formulas
    /// (<see cref="CastOriginal"/>) turned on you, each on its own cooldown: they stun, root, burn, slow, block and set
    /// fire to the ground as yours do. Left out: what only works for a player (mana, poison for arrows) and bleeding,
    /// which players don't do offline.
    /// </summary>
    public partial class OfflineServer
    {
        /// <summary>An outlaw's blow on its way to you.</summary>
        private class OutlawBlow
        {
            public int damage, kind, projectile, splash;
            public bool noBlock;               // past your block roll (Piercing shot, Venom arrow)
            public System.Action<int> onHit;   // what it leaves on you, given its damage before your defences
        }

        // The abilities an outlaw can use against you (the rest of its weapon's are skipped).
        private static readonly HashSet<int> OutlawAbilityIds = new HashSet<int>
        {
            1, 15, 26, 48, 50, 51, 52, 61, 62, 63, 64, 65, 66, 67, 86, 87, 88, 89, 90, 91, 115, 119, 121, 122, 154, 155, 165,
        };

        private static ItemSave OutlawWeapon(BountySave b) => b.gear.Find(i => ItemData.Get(i.typeId)?.slot == "Weapon");
        private static ItemSave OutlawShield(BountySave b) => b.gear.Find(i => ItemData.Get(i.typeId)?.slot == "Shield");
        private static int OutlawDamageOf(BountySave b, int kind) => BountyGearStat(b, DamageKinds.DamageStat(kind));

        /// <summary>The abilities on the outlaw's bar: a two-handed weapon's four, else two from the weapon and two from the shield.</summary>
        private static List<int> OutlawAbilityList(BountySave b)
        {
            var list = new List<int>();
            var w = OutlawWeapon(b);
            bool twoHanded = w != null && IsTwoHanded(w.typeId);
            var shield = twoHanded ? null : OutlawShield(b);
            for (int slot = 1; slot <= WeaponAbilitySlots; slot++)
            {
                var source = twoHanded || slot <= 2 ? w : shield;
                if (source == null) continue;
                int id = ItemAbilities(source)[twoHanded ? slot - 1 : (slot - 1) % 2];
                if (OutlawAbilityIds.Contains(id) && SkillData.Abilities.ContainsKey(id) && !list.Contains(id)) list.Add(id);
            }
            return list;
        }

        /// <summary>
        /// The basic blow's base and its roll (<see cref="BasicBlow"/>): broadsword 0.5-1, greatsword 0.5-1.5, halberd
        /// 0.8-1.25, bow 0.4-0.9 and crossbow 0.6-1.3 times 10 x level + 10 x weapon level + damage; the fire staff
        /// (1 + 1.3 x fire damage + 10 x level) x 0.7-1.
        /// </summary>
        private void OutlawBasicRange(BountySave b, out float baseDamage, out float lo, out float hi, out int kind)
        {
            var w = OutlawWeapon(b);
            int t = w?.typeId ?? 0, L = b.level, D = BountyGearStat(b, "Damage");
            float wl = WeaponLevel(w);
            if (w != null && IsFireStaff(t))
            {
                kind = DamageKinds.Fire;
                baseDamage = 1 + (D + OutlawDamageOf(b, kind)) * 1.3f + L * 10f;
                lo = 0.7f; hi = 1f;
                return;
            }
            bool shooter = w != null && (IsBow(t) || IsCrossBow(t));
            kind = shooter ? DamageKinds.Piercing : DamageKinds.Slash;
            baseDamage = L * 10f + wl * 10f + D + OutlawDamageOf(b, kind);
            if (w != null && IsBow(t)) { lo = 0.4f; hi = 0.9f; }
            else if (w != null && IsCrossBow(t)) { lo = 0.6f; hi = 1.3f; }
            else if (w != null && IsGreatsword(t)) { lo = 0.5f; hi = 1.5f; }
            else if (w != null && IsHalberd(t)) { lo = 0.8f; hi = 1.25f; }
            else { lo = 0.5f; hi = 1f; }
        }

        private OutlawBlow OutlawBasicBlow(BountySave b)
        {
            OutlawBasicRange(b, out float baseDamage, out float lo, out float hi, out int kind);
            return OutlawHit((int)(baseDamage * U(lo, hi)), kind, WeaponProjectile(OutlawWeapon(b)));
        }

        private static OutlawBlow OutlawHit(int damage, int kind, int projectile = 0, System.Action<int> onHit = null, bool noBlock = false) =>
            new OutlawBlow
            {
                damage = damage, kind = kind, projectile = projectile, onHit = onHit, noBlock = noBlock,
                splash = kind == DamageKinds.Poison ? SplashPoisoned : SplashPlain,
            };

        /// <summary>A shot of an arrow ability with its projectile's own damage (<see cref="ShotDamage"/>).</summary>
        private int OutlawShotDamage(BountySave b, int projectile)
        {
            if (!ProjectileDamages.TryGetValue(projectile, out var p)) return 1;
            int boost = 1 + (p.skill != 0 ? 10 * b.level : 0) + (p.weaponBoost ? 10 * WeaponLevel(OutlawWeapon(b)) : 0);
            int min = (int)(p.min / 100f * boost);
            int max = (int)(p.max / 100f * boost) + OutlawDamageOf(b, p.type) + BountyGearStat(b, "Damage");
            return Random.Range(1 + min, 1 + Mathf.Max(min, max));
        }

        /// <summary>Whether it may use the ability now: off cooldown, and a buff not already on it.</summary>
        private bool OutlawCanCast(BountyActor a, int id)
        {
            if (a.readyAt.TryGetValue(id, out float ready) && Time.time < ready) return false;
            switch (id)
            {
                case 15: return Time.time >= a.thickSkinUntil;
                case 26: return Time.time >= a.shieldBlockUntil;
                case 61: return Time.time >= a.focusUntil;
                case 115: return Time.time >= a.flameBarrierUntil;
                case 121: return !outlawAreasFor.Contains(a.npc.uid);
            }
            return true;
        }

        private readonly HashSet<int> outlawAreasFor = new HashSet<int>();   // outlaws whose Firestorm burns

        /// <summary>
        /// Before each swing: maybe one of its abilities (its own chance per swing, never the opening one, never the
        /// same twice running). True if the swing was a buff or a spell on the ground rather than a blow.
        /// </summary>
        private bool BountyPrepareSwing(NpcEntity npc)
        {
            if (!bountyByUid.TryGetValue(npc.uid, out var a)) return false;
            npc.blowProjectile = 0;
            a.pending = null;
            a.attackId = 0;
            if (Time.time < a.focusUntil) npc.nextSwing = Time.time + 0.5f;   // Focus: a bolt every half second
            int chance = a.save.abilityChance > 0 ? a.save.abilityChance : 25;
            if (++a.swings < 2 || Random.Range(0, 100) >= chance) return false;
            var ready = OutlawAbilityList(a.save).FindAll(id => OutlawCanCast(a, id));
            if (ready.Count == 0) return false;
            int pick = ready[Random.Range(0, ready.Count)];
            if (pick == a.lastAbility && ready.Count > 1) pick = ready[(ready.IndexOf(pick) + Random.Range(1, ready.Count)) % ready.Count];
            var info = SkillData.Abilities[pick];
            a.lastAbility = pick;
            a.attackId = pick;
            a.readyAt[pick] = Time.time + info.cooldown;
            Notice($"{a.save.name} uses {info.name}!", "orange");
            a.pending = OutlawCast(a, pick);
            if (a.pending == null) return true;
            npc.blowProjectile = a.pending.projectile;
            return false;
        }

        /// <summary>The original's formula for the ability, aimed at you; null for a buff or a spell on the ground.</summary>
        private OutlawBlow OutlawCast(BountyActor a, int id)
        {
            var b = a.save;
            var w = OutlawWeapon(b);
            int L = b.level, wl = WeaponLevel(w), D = BountyGearStat(b, "Damage"), W = wl * 10 + L * 10;
            int slash = OutlawDamageOf(b, DamageKinds.Slash), pierce = OutlawDamageOf(b, DamageKinds.Piercing);
            int fire = OutlawDamageOf(b, DamageKinds.Fire), poison = OutlawDamageOf(b, DamageKinds.Poison);
            Vector3 at = NpcPosition(a.npc), you = PlayerPos;
            switch (id)
            {
                // ---- swords ----
                case 86:   // Brutal slash
                    return OutlawHit((int)((L * 10f + wl * 10 + D + slash) * U(1.5f, 2f)), DamageKinds.Slash);
                case 87:   // Slashing: three blows in turn, the third harder
                {
                    int stage = a.slashingStage;
                    a.slashingStage = (stage + 1) % 3;
                    float per = stage == 0 ? 10f : stage == 1 ? 13f : 20f;
                    float r = stage == 2 ? U(1.8f, 2f) : U(1f, 1.5f);
                    PlaySound(stage == 0 ? 277 : stage == 1 ? 426 : 425, at, 30);
                    return OutlawHit((int)((L * per + wl * 10 + D + slash) * r), DamageKinds.Slash);
                }
                case 88:   // Cripple: roots you for 5 s
                    return OutlawHit((int)((L * 10f + wl * 10 + D + slash) * U(0.7f, 1.3f)), DamageKinds.Slash, onHit: d => RootPlayer(5f));
                case 89:   // Stun: blunt, a 5 second stun
                    return OutlawHit(Mathf.RoundToInt(U(1, 1 + W * 1.3f + slash)), DamageKinds.Blunt, onHit: d => StunPlayer(5f));
                case 48:   // Sword throw
                    return OutlawHit(Mathf.RoundToInt(U(W * 0.75f, W)) + slash + D, DamageKinds.Slash, 9);
                case 50:   // Broadsword fire attack: and an ignite of a fifth of it a second for 5 s
                    return OutlawHit(Mathf.RoundToInt(U(W * 0.75f, W)) + slash + D + fire, DamageKinds.Fire,
                        onHit: d => AddDotBuff(BuffIgnite, (int)(d * 0.2f), 1f, 5f, DamageKinds.Fire, false));
                case 51:   // Broadsword ice attack: a 2 second stun
                    return OutlawHit(Mathf.RoundToInt(U(1, 1 + W * 1.2f + slash + OutlawDamageOf(b, DamageKinds.Ice))) + D, DamageKinds.Ice,
                        onHit: d => StunPlayer(2f));
                case 52:   // Broadsword electric attack
                    return OutlawHit(Mathf.RoundToInt(U(1, 1 + W * 1.2f + slash + OutlawDamageOf(b, DamageKinds.Electric))) + D, DamageKinds.Electric);

                // ---- shields and armour: buffs on the outlaw (BountyBlockedHit) ----
                case 26:   // Heavy shield block: every blow blocked for 3 s
                    a.shieldBlockUntil = Time.time + 3f;
                    PlayEffect(9, at);
                    PlaySound(78, at, 40);
                    return null;
                case 15:   // Thick skin: blocks up to 7 x its level more, 30 s
                    a.thickSkinUntil = Time.time + 30f;
                    PlayEffect(10, at);
                    PlaySound(76, at, 40);
                    return null;

                // ---- archery ----
                case 61:   // Focus: crossbow bolts every half second, 3 s
                    a.focusUntil = Time.time + 3f;
                    return null;
                case 91:   // Piercing shot: past your block
                    return OutlawHit((int)((L * 10f + wl * 10 + D + pierce) * U(1.5f, 2f)), DamageKinds.Piercing, 20, noBlock: true);
                case 90:   // Hunting net: slows you 8 s (ShotEffects)
                    return OutlawHit(0, DamageKinds.Piercing, 19);
                case 62: case 63: case 64:   // fire arrow (ignites), ice arrow (stuns 2 s), electric arrow
                {
                    int p = id == 62 ? 11 : id == 63 ? 12 : 13;
                    return OutlawHit(OutlawShotDamage(b, p), ProjectileType(p), p);
                }
                case 65:   // Explosive arrow (update 137: 27% less)
                    return OutlawHit((int)((L * 10f + wl * 10 + D + pierce) * U(1.7f, 2f) * 0.73f), DamageKinds.Fire, 10);
                case 66:   // Poison arrow: no blow, poison for 30 s (1 + 40% of the shot every 5 s)
                {
                    int dmg = (int)((L * 10f + wl * 10 + D + pierce + poison) * U(0.8f, 1.5f));
                    return OutlawHit(0, DamageKinds.Poison, 14, d => AddDotBuff(BuffWeakPoison, 1 + (int)(dmg * 0.4f), 5f, 30f, DamageKinds.Poison, true, 110));
                }
                case 67:   // Weakness arrow: 15 less of every resistance for 28 s (ShotEffects)
                    return OutlawHit(0, DamageKinds.None, 15);
                case 154:   // Venom arrow: past poison resistance and block, then venom of a fifth of it a second for 5 s
                {
                    var blow = OutlawHit((int)((L * 20f + D + pierce + poison) * U(1.8f, 2f)), DamageKinds.None, 46,
                        d => AddDotBuff(BuffWeakPoison, (int)(d * 0.2f), 1f, 5f, DamageKinds.Poison, true, 110), noBlock: true);
                    blow.splash = SplashPoisoned;
                    return blow;
                }
                case 155:   // Thorny vines: roots you for 6 s
                    return OutlawHit((int)((L * 10f + D + pierce + poison) * U(1.8f, 2f)), DamageKinds.Poison, 47, d => RootPlayer(6f));
                case 165:   // Wounding shot (its bleeding left out)
                    return OutlawHit((int)((L * 10f + wl * 10 + D + pierce) * U(0.7f, 1.5f)), DamageKinds.None, 56);

                // ---- the fire staff ----
                case 1:   // Fire ball
                    return OutlawHit((int)((50 + D + fire + L * 25f) * U(0.7f, 1f)), DamageKinds.Fire, 1);
                case 115:   // Flame barrier: +15 fire, ice, earth and wind resistance, half of physical blows back, 15 s
                    a.flameBarrierUntil = Time.time + 15f;
                    PlayEffect(56, at);
                    return null;
                case 119:   // Fire wall where you stand: 5 s, 35 + 0.8 x fire damage + 10 x level a second
                    PlaySound(205, you, 30);
                    outlawAreas.Add(new OutlawArea { kind = OutlawFireWall, outlaw = a.npc, pos = you, damage = (int)(35 + (D + fire) * 0.8f + L * 10f),
                        until = Time.time + 5f, next = Time.time });
                    return null;
                case 121:   // Firestorm: (0.8 x fire damage + 10 x level) a second 4 m around the outlaw, 8 s
                    PlayEffect(53, at);
                    outlawAreasFor.Add(a.npc.uid);
                    outlawAreas.Add(new OutlawArea { kind = OutlawFirestorm, outlaw = a.npc, damage = (int)(((D + fire) * 0.8f + L * 10f) * U(0.7f, 1f)),
                        until = Time.time + 8f, next = Time.time + 1f });
                    return null;
                case 122:   // Landmine where you stand: armed after 3 s, goes off when you move within 2.5 m (or after 90 s); 6 m of fire
                {
                    int uid = ExplosiveUidBase + nextExplosiveUid++ % 1000;
                    Send(2, 35, character.name, uid + "_" + ObjExplosive + "_" + SceneWorld.Vec(you) + "_" + Random.Range(0, 360));
                    outlawAreas.Add(new OutlawArea { kind = OutlawLandmine, outlaw = a.npc, uid = uid, pos = you,
                        damage = (int)((75 + (D + fire) * 2f + L * 40f) * U(0.9f, 1f)), armedAt = Time.time + 3f, until = Time.time + 90f });
                    return null;
                }
            }
            return null;
        }

        /// <summary>
        /// From NpcHitsPlayer: the outlaw's swing or shot lands, its ability's blow if it used one, else its basic
        /// blow, times its weapon's grade, through your defences. False if the attacker isn't an outlaw.
        /// </summary>
        private bool OutlawHitsPlayer(NpcEntity npc, int projectile)
        {
            if (npc == null || !bountyByUid.TryGetValue(npc.uid, out var a)) return false;
            var blow = a.pending ?? OutlawBasicBlow(a.save);
            a.pending = null;
            var w = OutlawWeapon(a.save);
            int roll = Mathf.RoundToInt(blow.damage * (w != null ? WeaponGradeMultiplier(w.grade) : 1f));
            if (roll > 0)
            {
                int damage = PlayerDamageTaken(roll, blow.kind, npc, blow.noBlock ? 1000000 : CombatRules.BountyAccuracy);
                if (LogBlows) Plugin.Log.LogInfo($"[blow] {a.save.name} hits you for {damage} of {roll} {DamageKinds.Name(blow.kind)} (health {character.currentHealth - damage}/{MaxHealth()})");
                HurtPlayer(damage, blow.splash);
            }
            if (PlayerDead) return true;
            if (blow.projectile > 0) ShotEffects(blow.projectile, roll);
            blow.onHit?.Invoke(roll);
            return true;
        }

        // ---- fire on the ground ----------------------------------------------------------------------------------

        private const int OutlawFireWall = 1, OutlawLandmine = 2, OutlawFirestorm = 3;

        private class OutlawArea
        {
            public int kind, damage, uid;
            public NpcEntity outlaw;   // whose it is; a Firestorm burns around it wherever it goes
            public Vector3 pos;
            public float until, next, armedAt;
        }

        private readonly List<OutlawArea> outlawAreas = new List<OutlawArea>();

        private void TickOutlawAreas()
        {
            if (outlawAreas.Count == 0) return;
            Vector3 you = PlayerPos;
            foreach (var area in outlawAreas.ToArray())
            {
                bool over = Time.time >= area.until || area.kind == OutlawFirestorm && area.outlaw.dead;
                if (area.kind == OutlawLandmine)
                {
                    bool trodden = Time.time >= area.armedAt && PlayerMoving() && Vector3.Distance(you, area.pos) < 2.5f;
                    if (!over && !trodden) continue;
                    outlawAreas.Remove(area);
                    Send(25, 10, area.uid);
                    PlayEffect(GfxExplosion, area.pos);
                    PlaySound(SfxExplosion, area.pos, 40);
                    if (Vector3.Distance(you, area.pos) < 6f) OutlawAreaBurns(area);
                    continue;
                }
                if (over)
                {
                    outlawAreas.Remove(area);
                    if (area.kind == OutlawFirestorm) outlawAreasFor.Remove(area.outlaw.uid);
                    continue;
                }
                if (Time.time < area.next) continue;
                area.next += 1f;
                Vector3 at = area.kind == OutlawFirestorm ? NpcPosition(area.outlaw) : area.pos;
                if (area.kind == OutlawFireWall) PlayEffect(4, at);
                if (Vector3.Distance(you, at) < (area.kind == OutlawFireWall ? 5f : 4f)) OutlawAreaBurns(area);
            }
        }

        private void OutlawAreaBurns(OutlawArea area)
        {
            if (PlayerDead) return;
            HurtPlayer(PlayerDamageTaken(area.damage, DamageKinds.Fire, area.outlaw, CombatRules.BountyAccuracy), SplashPlain);
        }

        /// <summary>Zone change: the outlaws' fire went with the scene.</summary>
        private void ResetOutlawAreas()
        {
            outlawAreas.Clear();
            outlawAreasFor.Clear();
        }
    }
}
