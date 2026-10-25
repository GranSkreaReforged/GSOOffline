using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// The auction window, redrawn for the traders (#88): two lists side by side, what's for sale on the left and
    /// what's wanted on the right, with nothing to search or post. Each row shows the item's icon, the lot, its price
    /// and time left, in its grade's colour (for an order, the lowest grade it takes). A click asks to buy or sell, in
    /// the client's own confirmation (7 buys, sending /buyoffer; 20 sells, sending 25/13); a right click shows the item.
    /// The rows come straight from the server's boards, so they count down as you watch.
    /// </summary>
    internal static class AuctionWindow
    {
        private const float Left = 55f, Right = 50f, Top = 80f, Bottom = 55f, Gap = 12f, RowHeight = 35f, Header = 28f;
        private const int QuestionBuy = 7, QuestionSell = 20;   // Script_WindowController.answerQuestion's auction ids

        private static readonly AccessTools.FieldRef<Script_WindowController, List<Script_Window>> Windows =
            AccessTools.FieldRefAccess<Script_WindowController, List<Script_Window>>("windows");
        private static readonly Action<Script_WindowController, Rect> DragWindowTop =
            AccessTools.MethodDelegate<Action<Script_WindowController, Rect>>(AccessTools.Method(typeof(Script_WindowController), "dragWindowTop"));
        private static readonly Action<Script_WindowController, Rect, int> DrawCloseButton =
            AccessTools.MethodDelegate<Action<Script_WindowController, Rect, int>>(AccessTools.Method(typeof(Script_WindowController), "drawCloseButton"));
        private static readonly Action<Script_WindowController, Rect, string> DrawTitle =
            AccessTools.MethodDelegate<Action<Script_WindowController, Rect, string>>(AccessTools.Method(typeof(Script_WindowController), "drawTitle"));
        private static readonly Func<Script_WindowController, int, Rect> Resizeable =
            AccessTools.MethodDelegate<Func<Script_WindowController, int, Rect>>(AccessTools.Method(typeof(Script_WindowController), "resizeable"));
        private static readonly Action<Script_WindowController, int> DoForAllWindows =
            AccessTools.MethodDelegate<Action<Script_WindowController, int>>(AccessTools.Method(typeof(Script_WindowController), "doForAllWindows"));

        private static Vector2 scrollSales, scrollOrders;
        private static GUIStyle oneLine;

        public static void Draw(Script_WindowController wc, int id)
        {
            var w = Windows(wc)[id];
            DragWindowTop(wc, w.windowRect);
            DrawCloseButton(wc, w.windowRect, id);
            DrawTitle(wc, w.windowRect, w.windowTitle);
            w.windowRect = Resizeable(wc, w.windowUniqueId);

            var server = OfflineServer.Instance;
            var skin = wc.skin;
            var heading = skin.GetStyle("BoldOutlineText");
            int headingSize = heading.fontSize;
            heading.fontSize = 18;
            float paneWidth = (w.windowRect.width - Left - Right - Gap) / 2f;
            float paneHeight = w.windowRect.height - Top - Bottom - Header;
            GUI.Label(new Rect(Left, Top, paneWidth, Header), "For sale", heading);
            GUI.Label(new Rect(Left + paneWidth + Gap, Top, paneWidth, Header), "Wanted", heading);
            heading.fontSize = headingSize;

            Pane(wc, new Rect(Left, Top + Header, paneWidth, paneHeight), server.AuctionSaleRows(), true, ref scrollSales);
            Pane(wc, new Rect(Left + paneWidth + Gap, Top + Header, paneWidth, paneHeight), server.AuctionOrderRows(), false, ref scrollOrders);
            DoForAllWindows(wc, id);
        }

        private static void Pane(Script_WindowController wc, Rect area, List<OfflineServer.AuctionRow> rows, bool sales, ref Vector2 scroll)
        {
            var skin = wc.skin;
            var text = skin.GetStyle("PlainText");
            float rowWidth = area.width - 25f;
            GUI.Label(new Rect(area.x - 4f, area.y - 4f, area.width + 8f, area.height + 8f), string.Empty, skin.GetStyle("OutlineText"));
            scroll = GUI.BeginScrollView(area, scroll, new Rect(0f, 0f, area.width - 20f, Mathf.Max(area.height - 5f, 10f + rows.Count * RowHeight)));
            if (rows.Count == 0)
                GUI.Label(new Rect(10f, 10f, rowWidth - 10f, 25f), sales ? "Nothing for sale right now." : "No one is buying right now.", text);
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                float y = 5f + RowHeight * i;
                var box = new Rect(5f, y, rowWidth, 31f);
                GUI.Label(box, string.Empty, skin.GetStyle("OutlineText"));
                var old = GUI.contentColor;
                if (row.grade > 0 && Menucontroller.instance != null) GUI.contentColor = Menucontroller.instance.GetGradeColor(row.grade);
                OneLine(new Rect(50f, y + 7f, rowWidth - 50f, 25f), row.text, text);
                GUI.contentColor = old;
                if (GUI.Button(box, string.Empty, skin.GetStyle("OutlineTextButton")))
                {
                    if (Event.current.button == 0) Confirm(wc, row, sales);
                    else if (sales && ClientOffer(row.id) != null) wc.createItemInfo(ClientOffer(row.id).item);
                    else wc.createBasicItemInfo(Scr_ItemHandler.instance.GetItemTemplate(row.typeId));
                }
                var icon = new Rect(16f, y + 3f, 25f, 25f);
                if (icon.Contains(Event.current.mousePosition) && sales && ClientOffer(row.id) != null)
                    Menucontroller.instance.hoverItem = ClientOffer(row.id).item;
                GUI.DrawTexture(icon, Menucontroller.instance.getOtherIcon(37));
                GUI.DrawTexture(icon, Inventory.instance.getItemIcon(row.typeId));
            }
            GUI.EndScrollView();
        }

        // The client's copy of a sell offer: its confirm handler buys from there, and it holds the item for the tooltip.
        private static Scr_AuctionHouse.Offer ClientOffer(int offerId) =>
            Scr_AuctionHouse.instance?.offers.Find(o => o != null && o.offerId == offerId);

        private static void Confirm(Script_WindowController wc, OfflineServer.AuctionRow row, bool sales)
        {
            var ah = wc.window_auctionHouse;
            if (sales)
            {
                ah.buyingOffer = row.id;
                string what = row.grade > 0 ? $"{(Vowel(row.grade) ? "an" : "a")} {OfflineServer.GradeName(row.grade)} {row.name}" : $"{row.amount} {row.name}";
                wc.createQuestionTwoAnswers(QuestionBuy, "Confirmation", $"Are you sure you want to buy {what} for {row.price} silver?", "Yes", "No");
            }
            else
            {
                ah.selectedBuyOfferId = row.id;
                string what = row.grade > 0
                    ? $"{(Vowel(row.grade) ? "an" : "a")} {OfflineServer.GradeName(row.grade)} or better {row.name} (your lowest grade that qualifies)"
                    : $"{row.amount} {row.name}";
                wc.createQuestionTwoAnswers(QuestionSell, "Confirmation", $"Are you sure you want to sell {what} for {row.price} silver?", "Yes", "No");
            }
        }

        /// <summary>Dev: clicks row <paramref name="index"/> of a list and answers Yes, as the player would.</summary>
        internal static void DevClick(bool sales, int index, bool yes)
        {
            var wc = Script_WindowController.instance;
            var rows = sales ? OfflineServer.Instance.AuctionSaleRows() : OfflineServer.Instance.AuctionOrderRows();
            if (wc == null || index < 0 || index >= rows.Count) return;
            Confirm(wc, rows[index], sales);
            var question = Windows(wc).FindLast(x => x.windowType == 1 && x.windowActive);
            Plugin.Log.LogInfo($"[dev] auction click: {rows[index].text}: \"{question?.windowText}\"");
            if (!yes || question == null) return;
            AccessTools.Method(typeof(Script_WindowController), "button1").Invoke(wc, new object[] { sales ? QuestionBuy : QuestionSell });
            question.windowActive = false;
        }

        private static bool Vowel(int grade) => "aeiou".IndexOf(OfflineServer.GradeName(grade)[0]) >= 0;

        /// <summary>One line, in a smaller font if it doesn't fit, rather than wrapped into the next row.</summary>
        private static void OneLine(Rect r, string s, GUIStyle style)
        {
            if (oneLine == null || oneLine.font != style.font)
                oneLine = new GUIStyle(style) { wordWrap = false, clipping = TextClipping.Clip };
            // The skin's size is 0 (the font's own, which nothing reports), so a row that doesn't fit takes the largest
            // size that fits and draws no wider than the font's own size would.
            var content = new GUIContent(s);
            oneLine.fontSize = style.fontSize;
            float natural = oneLine.CalcSize(content).x;
            if (natural > r.width)
            {
                oneLine.fontSize = 30;
                while (oneLine.fontSize > 8 && oneLine.CalcSize(content).x > Mathf.Min(r.width, natural)) oneLine.fontSize--;
            }
            GUI.Label(r, s, oneLine);
        }
    }

    [HarmonyPatch(typeof(Script_WindowController), "WindowAuctionHouse")]
    internal static class AuctionWindowPatch
    {
        private static bool failed;

        private static bool Prefix(Script_WindowController __instance, int id)
        {
            if (OfflineServer.Instance == null || failed) return true;
            try
            {
                AuctionWindow.Draw(__instance, id);
            }
            catch (Exception e)
            {
                failed = true;   // the client's own window from now on
                Plugin.Log.LogError($"[auction] auction window failed, showing the client's own: {e}");
            }
            return false;
        }
    }
}
