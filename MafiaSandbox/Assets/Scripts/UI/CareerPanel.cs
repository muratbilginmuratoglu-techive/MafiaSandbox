using UnityEngine;
using MafiaSandbox.Core;
using MafiaSandbox.Economy;

namespace MafiaSandbox.UI
{
    /// <summary>The three-way choice after the old stash: which trade starts your career. Must be answered (no Esc).</summary>
    public class CareerPanel : MonoBehaviour
    {
        public static CareerPanel I { get; private set; }
        public static bool IsOpen => I && I.open;
        bool open;
        void Awake() { I = this; }
        public void Open() { if (!Career.Has) open = true; }
        public void Close() { open = false; }

        static readonly string[] Title = { "BOOTLEG ALCOHOL", "DRUGS", "ARMS" };
        static readonly string[] Pitch =
        {
            "Cook moonshine at home. Cheap ingredients, steady money, easy for beginners. Sell it on the street to civilians.",
            "Grow weed first, then chemistry later (pills, powder). Highest street demand. Sold to civilians, cops watch for it.",
            "Assemble pistols from parts. Few buyers, but they pay well: only gang members buy guns, and they are dangerous people.",
        };
        static readonly string[] Steps =
        {
            "1. Buy a copper still at the PAWN SHOP\n2. Buy corn, sugar, yeast at the MARKET\n3. Place the still in your house (B)\n4. Brew, wait, collect\n5. Sell on the street (X)",
            "1. Buy a chemistry table at the MARKET\n2. Buy seeds and plant food at the MARKET\n3. Place the table in your house (B)\n4. Grow, wait, collect\n5. Sell on the street (X)",
            "1. Buy a gunsmith bench at the GUN SHOP\n2. Buy metal parts at the PAWN SHOP and gunpowder at the GUN SHOP\n3. Place the bench in your house (B)\n4. Assemble, wait, collect\n5. Sell to gang members (X)",
        };
        static readonly string[] Jer =
        {
            "Moonshine, huh. Dad always said a still pays for itself in a week. The gun shop has the copper, the market has the corn. Put it together at home.",
            "Weed is the safest way in. Get a table and seeds at the market, grow it at home. Watch for cops when you sell.",
            "Guns. Brave. Bench at the gun shop, parts at the gun shop. Gang boys are your buyers, so keep your head down.",
        };

        void OnGUI()
        {
            if (!open) return; float W = Screen.width, H = Screen.height;
            GUI.color = new Color(0, 0, 0, 0.6f); GUI.DrawTexture(new Rect(0, 0, W, H), Texture2D.whiteTexture); GUI.color = Color.white;
            var r = new Rect(W / 2f - 450, H / 2f - 250, 900, 500); UiSkin.DrawPanel(r, true);
            GUI.Label(new Rect(r.x + 20, r.y + 12, r.width - 40, 40), "HOW DO YOU MAKE A LIVING?", UiSkin.Title);
            var wrap = new GUIStyle(UiSkin.LabelDim) { wordWrap = true, fontSize = 13 }; var stepSt = new GUIStyle(UiSkin.Label) { wordWrap = true, fontSize = 12 };
            GUI.Label(new Rect(r.x + 20, r.y + 50, r.width - 40, 44), "You have Joshua's blood money. Pick the trade you will build your name on. It decides what you buy, what you make at home and who you sell to. You can branch out later as your rank grows.", wrap);
            for (int i = 0; i < 3; i++)
            {
                var c = new Rect(r.x + 20 + i * 290, r.y + 104, 270, 380);
                GUI.color = new Color(1, 1, 1, 0.08f); GUI.DrawTexture(c, Texture2D.whiteTexture); GUI.color = Color.white;
                GUI.Label(new Rect(c.x + 12, c.y + 8, c.width - 24, 26), Title[i], UiSkin.H2);
                GUI.Label(new Rect(c.x + 12, c.y + 38, c.width - 24, 90), Pitch[i], wrap);
                GUI.Label(new Rect(c.x + 12, c.y + 134, c.width - 24, 20), "HOW IT WORKS", UiSkin.H2);
                GUI.Label(new Rect(c.x + 12, c.y + 156, c.width - 24, 160), Steps[i], stepSt);
                if (GUI.Button(new Rect(c.x + 12, c.yMax - 50, c.width - 24, 38), "Choose " + Career.Names[i], UiSkin.ButtonOn)) Choose(i);
            }
        }
        void Choose(int i) { Career.Choose(i); open = false; PhonePanel.Post("jeremy", Jer[i]); GameManager.I?.Say("Career chosen: " + Career.Names[i]); }
    }
}
