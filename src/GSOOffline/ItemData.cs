using System.Collections.Generic;
using System.Xml;

namespace GSOOffline
{
    public class ItemStatRange
    {
        public string key;   // key in the client's item data string (see Scr_ItemHandler.loadItemFromDataString)
        public int min, max;
    }

    public class ItemTemplate
    {
        public int typeId;
        public string name;
        public string slot;   // null for non-equippable
        public int level;
        public int price;
        public readonly HashSet<string> flags = new HashSet<string>();   // isBroadsword, isLightArmor, Food, isFurniture...
        public int baseGrade = 2;
        public int food;
        public bool usable;
        public bool unique;
        public readonly List<ItemStatRange> stats = new List<ItemStatRange>();

        // Mirrors Scr_ItemHandler.ItemIsUnique: unique items are tracked per instance, others stack by type.
        public bool Stacks => !unique && typeId > 10000;
    }

    /// <summary>Item templates from Resources/Data/items, the same file the client reads.</summary>
    internal static class ItemData
    {
        public static readonly Dictionary<int, ItemTemplate> Templates = new Dictionary<int, ItemTemplate>();

        public static ItemTemplate Get(int typeId) => Templates.TryGetValue(typeId, out var t) ? t : null;

        // XML stat name -> item data string key. Most match; elemental damage/resistance names are inverted.
        private static string StatKey(string xmlStat)
        {
            switch (xmlStat)
            {
                case "DamagePoison": return "DmgPoison";
                case "ResistancePoison": return "ResPoison";
            }
            if (xmlStat.StartsWith("Damage") && xmlStat.Length > "Damage".Length)
                return xmlStat.Substring("Damage".Length) + "Damage";
            if (xmlStat.StartsWith("Resistance") && xmlStat.Length > "Resistance".Length)
                return xmlStat.Substring("Resistance".Length) + "Resistance";
            return xmlStat;
        }

        public static void Load()
        {
            foreach (XmlNode n in GameData.LoadXml("Data/items").DocumentElement.ChildNodes)
            {
                if (n.NodeType != XmlNodeType.Element) continue;
                var t = new ItemTemplate
                {
                    typeId = GameData.IntAttr(n, "TypeId"),
                    name = GameData.Attr(n, "Name"),
                    slot = GameData.Attr(n, "Slot"),
                    level = GameData.IntAttr(n, "Level", 1),
                    price = GameData.IntAttr(n, "Price"),
                };
                if (string.IsNullOrEmpty(t.slot)) t.slot = null;
                foreach (XmlNode s in n.ChildNodes)
                {
                    if (s.NodeType != XmlNodeType.Element) continue;
                    string stat = GameData.Attr(s, "Stat");
                    if (stat != null)
                        t.stats.Add(new ItemStatRange { key = StatKey(stat), min = GameData.IntAttr(s, "Min"), max = GameData.IntAttr(s, "Max") });
                    foreach (XmlAttribute a in s.Attributes)
                        if (a.Name != "Stat" && a.Name != "Min" && a.Name != "Max") t.flags.Add(a.Name);
                    if (GameData.Attr(s, "baseGrade") != null) t.baseGrade = GameData.IntAttr(s, "baseGrade");
                    if (GameData.Attr(s, "Food") != null) t.food = GameData.IntAttr(s, "Food");
                    if (GameData.Attr(s, "isUsable") != null) t.usable = true;
                    if (GameData.Attr(s, "isUnique") != null) t.unique = true;
                }
                Templates[t.typeId] = t;
            }
        }
    }
}
