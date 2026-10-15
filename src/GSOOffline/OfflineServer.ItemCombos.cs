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
                TakeItems(ItemSoftClay, 1);
                GiveItem(ItemKeyMould);
                Notice("You press the key in the clay and make a key mould.");
                return;
            }
            if (UpgradeWith((int)c[2], (int)c[3])) return;
            Notice($"You try to use {ItemData.Get(usedType)?.name} with {ItemData.Get(targetType)?.name}. Nothing interesting happens.");
        }

        // Either way round (the original only took the halves and the key one way).
        private static bool Pair(int x, int y, int a, int b) => (x == a && y == b) || (x == b && y == a);
    }
}
