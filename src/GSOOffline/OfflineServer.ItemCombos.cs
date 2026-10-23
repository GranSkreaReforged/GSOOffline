namespace GSOOffline
{
    /// <summary>
    /// Using one item on another (198/11: the client sends the target's and the used item's ids and types), as the
    /// original server's Scr_PlayerInventory.UseItemOnItem: the key from Ulan's dungeon, and upgrading gear
    /// (OfflineServer.Grades.cs).
    /// </summary>
    public partial class OfflineServer
    {
        private const int ItemKeyHalfA = 10460, ItemKeyHalfB = 10461, ItemFragileKey = 10463, ItemSoftClay = 10458, ItemKeyMould = 10462;

        private void RegisterItemCombos()
        {
            On(198, 11, OnUseItemOnItem);
        }

        private void OnUseItemOnItem(object[] c)
        {
            if (character == null || PlayerDead) return;
            int targetType = (int)c[4], usedType = (int)c[5];
            if (CountItem(targetType) == 0 || CountItem(usedType) == 0) return;
            if (GrantsGlovesCombo(targetType, usedType)) return;
            if (Pair(targetType, usedType, ItemKeyHalfA, ItemKeyHalfB))
            {
                TakeItems(ItemKeyHalfA, 1);
                TakeItems(ItemKeyHalfB, 1);
                GiveItem(ItemFragileKey);
                Notice("You combine the two pieces into a fragile key.");
                return;
            }
            if (Pair(targetType, usedType, ItemFragileKey, ItemSoftClay))
            {
                // The fragile key goes into the mould and isn't needed again (the original game kept it; #83).
                TakeItems(ItemSoftClay, 1);
                TakeItems(ItemFragileKey, 1);
                GiveItem(ItemKeyMould);
                Notice("You press the key in the clay and make a key mould.");
                return;
            }
            if (UpgradeWith((int)c[2], (int)c[3])) return;
            Notice($"You try to use {ItemData.Get(usedType)?.name} with {ItemData.Get(targetType)?.name}. Nothing interesting happens.");
        }

        // Commander Grant's gloves (quest 34): the upgrade tutorial, as the original scripted it. Combining his two
        // gloves always fails and destroys the spare pair, and he hands you his upgrade stone; the stone used on the
        // gloves upgrades them.
        private const int GrantStone = 10726, GrantGloveA = 10727, GrantGloveB = 10728, GrantGloves = 10729;

        private bool GrantsGlovesCombo(int targetType, int usedType)
        {
            if (Pair(targetType, usedType, GrantGloveA, GrantGloveB))
            {
                TakeItems(GrantGloveB, 1);
                Notice("Upgrade failed, your material was destroyed.", "red");
                Notice("Commander Grant: God damnit! You trample just destroyed my spare pair of gloves!", "orange");
                Notice("Commander Grant: Did they not teach you to be careful when upgrading at blacksmithing school?", "orange");
                Notice("Commander Grant: Here, take this VERY EXPENSIVE upgrade stone and try again.", "orange");
                GiveItem(GrantStone);
                return true;
            }
            if (targetType == GrantGloveA && usedType == GrantStone)
            {
                TakeItems(GrantStone, 1);
                TakeItems(GrantGloveA, 1);
                Notice("Upgrade successfull! Your item's grade has increased!", "green");
                GiveItem(GrantGloves);
                if (character.GetQuestPhase(QuestGrantsGloves) == 1)
                {
                    SetQuestPhase(QuestGrantsGloves, 2);
                    Notice("Commander Grant: Excellent! Now give me back my gloves", "orange");
                }
                return true;
            }
            return false;
        }

        // Either way round (the original only took the halves and the key one way).
        private static bool Pair(int x, int y, int a, int b) => (x == a && y == b) || (x == b && y == a);
    }
}
