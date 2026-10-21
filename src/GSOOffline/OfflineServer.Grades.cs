using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// Item grades, as the original server had them (Scr_ItemHandler, Scr_PlayerInventory): 1 Damaged, 2 Normal,
    /// 3 Unusual, 4 Rare, 5 Unique, 6 Epic, 7 Legendary, 8 Mythic.
    /// - A grade scales every stat of worn gear (x0.88 to x1.72) and a weapon's hits (x0.94 to x1.36), update 138's
    ///   multipliers (the original server had update 136's: x0.85-1.9 and x0.925-1.45).
    /// - Every new piece of gear rolls its grade around the item's base grade (a drop, a craft, a purchase).
    /// - Upgrading (used on an item, 198/11): another of the same item or an upgrade stone of the same grade is a
    ///   gamble: half the time the material is lost, otherwise the grade goes up by 1 (45%), 2 (4%) or 3 (1%).
    ///   A grade stone raises the item to its own grade; the bad grade stone copies its grade without being used up.
    ///   Stones only work up to an item level: small 25, medium 50, large 75.
    /// </summary>
    public partial class OfflineServer
    {
        private const int MaxGrade = 8;
        private const int SmallUpgradeStone = 437, MediumUpgradeStone = 438, LargeUpgradeStone = 439;
        private const int SmallGradeStone = 440, MediumGradeStone = 441, LargeGradeStone = 442, BadGradeStone = 443;

        private static readonly string[] GradeNames = { "", "damaged", "normal", "unusual", "rare", "unique", "epic", "legendary", "mythic" };

        public static float GradeMultiplier(int grade)
        {
            if (grade < 1 || grade > MaxGrade) return 1f;
            return 0.88f + 0.12f * (grade - 1);
        }

        public static float WeaponGradeMultiplier(int grade)
        {
            if (grade < 1 || grade > MaxGrade) return 1f;
            return 0.94f + 0.06f * (grade - 1);
        }

        /// <summary>A worn item's stat after its grade, cut to a whole number as the original did.</summary>
        private static int Graded(ItemSave it, int value) => (int)(value * GradeMultiplier(it.grade));

        // ---- the grade of a new item ----------------------------------------------------------

        /// <summary>
        /// A new item's grade: a walk from its base grade (Normal for items without one), as GenerateNewItemGrade.
        /// <paramref name="announce"/> is false for items that aren't the player's (an outlaw's gear).
        /// </summary>
        private int RollNewItemGrade(int baseGrade, bool announce)
        {
            int grade = baseGrade >= 4 ? RollRare(baseGrade) : RollCommon(baseGrade <= 0 ? 2 : baseGrade);
            if (announce && grade >= 4) Notice($"You received {(grade == 6 ? "an" : "a")} {GradeNames[grade]} item!", "grade" + grade);
            return grade;
        }

        // 30% a step down, 30% a step up, and again from there.
        private static int RollCommon(int grade)
        {
            int roll = Random.Range(0, 10);
            if (roll <= 2) grade = RollCommon(grade - 1);
            else if (roll >= 7) grade = RollCommon(grade + 1);
            return Mathf.Clamp(grade, 1, MaxGrade);
        }

        // Rare and better items only go up: 20% a step, and again.
        private static int RollRare(int grade)
        {
            if (Random.Range(0, 10) >= 8) grade = RollRare(grade + 1);
            return Mathf.Clamp(grade, 1, MaxGrade);
        }

        // ---- upgrading --------------------------------------------------------------------------

        /// <summary>Using one piece of gear on another (198/11). True if it was an upgrade, a grade stone or a copy.</summary>
        private bool UpgradeWith(int targetId, int usedId)
        {
            var item = FindItem(targetId);
            var material = FindItem(usedId);
            if (item == null || material == null || item.typeId >= 10000 || material.typeId >= 10000) return false;
            int level = ItemData.Get(item.typeId)?.level ?? 0;
            int m = material.typeId;
            if (m == item.typeId || m == SmallUpgradeStone && level <= 25 || m == MediumUpgradeStone && level <= 50 || m == LargeUpgradeStone && level <= 75)
            {
                if (item == material) return true;
                if (item.grade != material.grade)
                    Notice("Your items need to have an identical grade before you can combine them.");
                else if (item.grade >= MaxGrade)
                    Notice("Your item is already fully upgraded");
                else
                {
                    RemoveFromStack(material, 1);
                    SetGrade(item, RollUpgrade(item.grade));
                }
                return true;
            }
            if (m == SmallGradeStone && level <= 25 || m == MediumGradeStone && level <= 50 || m == LargeGradeStone && level <= 75)
            {
                if (CopyGrade(item, material)) RemoveFromStack(material, 1);
                return true;
            }
            if (m == BadGradeStone)
            {
                CopyGrade(item, material);
                return true;
            }
            return false;
        }

        private int RollUpgrade(int grade)
        {
            int roll = Random.Range(0, 100);
            if (roll < 50)
            {
                Notice("Upgrade failed, your material was destroyed.", "red");
                return grade;
            }
            if (roll < 95) Notice("Upgrade successfull! Your item's grade has increased!", "green");
            else if (roll < 99) Notice("Huge upgrade success!! Your item's grade has increased twice!!", "green");
            else Notice("Legendary upgrade success!!! Your item's grade has increased 3 times!!!", "green");
            return Mathf.Min(MaxGrade, grade + (roll < 95 ? 1 : roll < 99 ? 2 : 3));
        }

        /// <summary>The item takes the stone's grade if that's higher (the bad grade stone and the grade stones).</summary>
        private bool CopyGrade(ItemSave item, ItemSave stone)
        {
            if (item == stone) return false;
            if (item.grade >= MaxGrade)
            {
                Notice("Your item is already fully upgraded");
                return false;
            }
            if (item.grade >= stone.grade)
            {
                Notice("Your grade stone needs to have a higher grade than your item");
                return false;
            }
            SetGrade(item, stone.grade);
            Notice($"You upgrade the item to {GradeNames[item.grade]}{(item.grade >= 4 ? "!" : "")}", "grade" + item.grade);
            return true;
        }

        /// <summary>Dev: setgrade.</summary>
        internal void DevSetGrade(int itemId, int grade)
        {
            var item = FindItem(itemId);
            if (item != null) SetGrade(item, Mathf.Clamp(grade, 1, MaxGrade));
            Plugin.Log.LogInfo($"[dev] setgrade {itemId}: {(item != null ? "grade " + item.grade : "no such item")}");
        }

        private void SetGrade(ItemSave item, int grade)
        {
            if (grade == item.grade) return;
            Plugin.Log.LogInfo($"[item] {ItemData.Get(item.typeId)?.name} grade {item.grade} -> {grade}");
            item.grade = grade;
            Send(198, 16, WireId(item), grade);
            if (item.equipped)
            {
                GradeAura.Refresh(WireId(item), grade);
                SendHealth();
                SendMana();
                SendGearStats();
            }
        }
    }
}
