using UnityEngine;
using MafiaSandbox.Core;

namespace MafiaSandbox.Gang
{
    /// <summary>
    /// Street respect (the Schedule I style progression): XP from deals, hustles, venues, fights and missions. The rank gates crew size,
    /// products (pills from Hustler, powder from Enforcer), which venues you can lean on and what you can buy.
    /// </summary>
    public static class Respect
    {
        public static readonly string[] Ranks = { "Street Rat", "Hustler", "Enforcer", "Capo", "Underboss", "Boss" };
        public static readonly int[] Thresholds = { 0, 150, 500, 1200, 2500, 5000 };
        const string Key = "ms_xp_v1";
        static int xp = -1;
        public static int XP { get { if (xp < 0) xp = PlayerPrefs.GetInt(Key, 0); return xp; } }
        public static int Rank { get { int r = 0; for (int i = 0; i < Thresholds.Length; i++) if (XP >= Thresholds[i]) r = i; return r; } }
        public static string RankName => Ranks[Rank];
        public static float Progress { get { int r = Rank; if (r >= Thresholds.Length - 1) return 1f; return (XP - Thresholds[r]) / (float)(Thresholds[r + 1] - Thresholds[r]); } }
        public static int NextAt => Rank >= Thresholds.Length - 1 ? XP : Thresholds[Rank + 1];

        public static void Add(int amount, string why = null)
        {
            if (amount <= 0) return; int before = Rank; xp = XP + amount; PlayerPrefs.SetInt(Key, xp);
            if (Rank > before)
            {
                GameManager.I?.Banner("RANK UP", $"You are now a {RankName}", 4f);
                MafiaSandbox.UI.PhonePanel.Post("sal", Rank == 1 ? "People are starting to know your name. Pills are on my list now, and you can lean on small shops." : Rank == 2 ? "Enforcer, huh? Powder unlocked. Bigger crew, bigger shops." : Rank == 3 ? "Capo. The families are watching you now." : Rank == 4 ? "Underboss. There's only one rung left." : "Boss. This city answers to you.");
            }
        }
        public static void Reset() { xp = 0; PlayerPrefs.SetInt(Key, 0); }
    }
}
