using System.Collections.Generic;
using System.Xml;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// Pets: the item in the Pet slot. A walking pet is its PET creature from the game data (isPet="1", named
    /// like the item), walking beside the player and fighting what the player fights: "Follower pets now attack
    /// whoever the owner is attacking. Damage is calculated based on the owner's damage stats and weapon's level
    /// or wrestling skill if no weapon is equipped" (update 132). Pets can't be targeted (update 133) and their
    /// damage scales with the pet's grade (updates 136, 138, 139). The parrot and the bats are flying followers,
    /// which the client draws on the player's shoulder itself, and add damage by grade instead (update 136).
    /// </summary>
    public partial class OfflineServer
    {
        private const int FlyingFollowerUid = 1;
        // Flying followers' prefabs (Resources/1LoadingAssets/FlyingFollowers/Prefab_FlyingFollower_N), per item.
        private static readonly Dictionary<int, int> FlyingPetByItem = new Dictionary<int, int>
        {
            { 400, 1 },   // Parrot
            { 444, 2 }, { 445, 2 },   // Bat (Halloween 2019)
        };

        private static Dictionary<int, int> petNpcByItem;   // pet item type -> PET creature type
        private NpcEntity pet;
        private int petItemId, flyingShownFor;
        private float petNextHit, petTick;

        /// <summary>Pet items to their PET creature types, matched by name ("Pet Wraith" is the "Wraith").</summary>
        private static Dictionary<int, int> PetNpcs()
        {
            if (petNpcByItem != null) return petNpcByItem;
            var byName = new Dictionary<string, int>();
            foreach (XmlNode n in GameData.LoadXml("XMLs/NPCInfo").DocumentElement.ChildNodes)
                if (n.NodeType == XmlNodeType.Element && GameData.IntAttr(n, "isPet") == 1)
                    byName[(GameData.Attr(n, "name") ?? string.Empty).Trim().ToLowerInvariant()] = GameData.IntAttr(n, "id");
            petNpcByItem = new Dictionary<int, int>();
            foreach (var t in ItemData.Templates.Values)
            {
                if (!t.flags.Contains("isPet") || string.IsNullOrEmpty(t.name)) continue;
                string key = t.name.Replace("(untradeable)", string.Empty).Trim().ToLowerInvariant();
                if (key.StartsWith("pet ")) key = key.Substring(4);
                if (byName.TryGetValue(key, out int npc)) petNpcByItem[t.typeId] = npc;
            }
            Plugin.Log.LogInfo($"[pet] {petNpcByItem.Count} walking pets, {FlyingPetByItem.Count} flying");
            return petNpcByItem;
        }

        private ItemSave EquippedPet()
        {
            foreach (var it in character.items)
                if (it.equipped && ItemData.Get(it.typeId)?.slot == "Pet") return it;
            return null;
        }

        /// <summary>Flying follower's damage bonus: a tenth of its grade's multiplier (8.8% Damaged to 17.2% Mythic).</summary>
        private float FlyingPetDamage()
        {
            if (character == null) return 1f;
            var p = EquippedPet();
            if (p == null || !FlyingPetByItem.ContainsKey(p.typeId)) return 1f;
            return 1f + CombatRules.FlyingPetBonus(p.grade);
        }

        /// <summary>Four times a second: the pet in the Pet slot exists, follows, and fights the player's target.</summary>
        private void TickPet()
        {
            if (character == null || !sceneReady || world == null) return;
            petTick -= Time.deltaTime;
            if (petTick > 0f) return;
            petTick = 0.25f;

            var item = EquippedPet();
            int itemId = item != null ? item.id : 0;
            if (itemId != petItemId || (pet != null && !world.npcs.Contains(pet)))
                DismissPet();   // changed, taken off, or left behind in another scene
            petItemId = itemId;
            if (item == null) return;

            if (FlyingPetByItem.TryGetValue(item.typeId, out int flying))
            {
                if (flyingShownFor == item.id) return;
                flyingShownFor = item.id;
                Send(7, 41, $"{FlyingFollowerUid}_{flying}_{character.name}_{ItemData.Get(item.typeId)?.name}");
                return;
            }
            if (!PetNpcs().TryGetValue(item.typeId, out int npcType)) return;
            var player = LocalPlayer;
            if (player == null) return;
            Vector3 me = player.transform.position;
            if (pet == null)
            {
                Vector3 at = BesidePlayer(me);
                pet = new NpcEntity { uid = world.sceneId * 10000 + 9700, typeId = npcType, spawnPos = at, pos = at, health = 1, maxHealth = 1 };
                world.npcs.Add(pet);
                visibilityTimer = 0f;
                Plugin.Log.LogInfo($"[pet] {ItemData.Get(item.typeId)?.name} ({npcType}) comes along as {pet.uid}");
                return;
            }

            var target = PetTarget();
            if (target == null)
            {
                KeepBeside(pet, me);
                return;
            }
            GameData.Npcs.TryGetValue(npcType, out var info);
            float reach = (info != null ? info.attackDistance : 3f) + 0.5f;
            Vector3 targetPos = NpcPosition(target);
            if (Vector3.Distance(NpcPosition(pet), targetPos) > reach)
            {
                MoveNpc(pet, targetPos);
                return;
            }
            if (Time.time < petNextHit) return;
            float speed = info != null ? info.attackSpeed : 2f;
            petNextHit = Time.time + speed;
            pet.hasDest = false;
            Send(13, 2, pet.uid, false);
            Send(198, 28, pet.uid, targetPos);
            if (info != null)
            {
                SetAnim(pet, info.animAttack, true);
                pet.attackAnimUntil = Time.time + Mathf.Min(speed, AnimLength(info.animAttack));
            }
            var victim = target;
            int grade = item.grade;
            Later(info != null ? Mathf.Min(info.attackDelay, speed) : 0.25f, () => PetHits(victim, grade));
        }

        /// <summary>What the pet attacks: the player's target while the player is fighting it.</summary>
        private NpcEntity PetTarget()
        {
            if (PlayerDead || targetUid == 0 || Time.time - lastPlayerSwing > 6f) return null;
            var t = world.GetNpc(targetUid);
            return t != null && !t.dead && CanFight(t) && NpcShown(t) ? t : null;
        }

        /// <summary>
        /// The original's pet blow (Scr_PlayerPets.AttackHit): 10 + your Damage and damage of the pet's kind + 15 x your
        /// weapon's level (or Wrestling without one), 60-100% of it, times the pet's grade. It goes through the target's
        /// defences as yours do, but not your weapon's grade (update 139).
        /// </summary>
        private void PetHits(NpcEntity target, int grade)
        {
            if (pet == null || target.dead || PlayerDead) return;
            var weapon = EquippedWeapon();
            int kind = GameData.Npcs.TryGetValue(pet.typeId, out var petInfo) ? petInfo.damageType : 0;
            int max = 10 + StatDamageOf(kind) + StatDamage() + (weapon != null ? WeaponLevel(weapon) * 15 : SkillLevel(SkillWrestling) * 15);
            int damage = (int)(Random.Range(max * 0.6f, max) * WeaponGradeMultiplier(grade));
            Aggro(target);
            int hit = NpcDamageTaken(target, damage, kind, true, HitNoWeaponGrade);
            Plugin.Log.LogInfo($"[pet] hits {target.uid} for {hit} of {damage} (max {max}, grade {grade})");
            DealDamage(target, hit, WeaponSkill(weapon));
        }

        private void DismissPet()
        {
            if (pet != null)
            {
                world?.npcs.Remove(pet);
                pet = null;
                visibilityTimer = 0f;
            }
            if (flyingShownFor != 0)
            {
                Send(25, 22, FlyingFollowerUid);
                flyingShownFor = 0;
            }
        }

        /// <summary>/parrotsay: the parrot repeats the line over the player's shoulder.</summary>
        private void ParrotSay(string text)
        {
            var p = EquippedPet();
            if (p == null || !FlyingPetByItem.ContainsKey(p.typeId))
            {
                Notice("You have no parrot with you.", "gray");
                return;
            }
            Send(2, 55, character.name, text);
        }
    }
}
