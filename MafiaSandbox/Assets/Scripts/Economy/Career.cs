using UnityEngine;

namespace MafiaSandbox.Economy
{
    /// <summary>The trade the player picked after finding the old stash: bootleg alcohol, drugs or arms. Saved between launches.</summary>
    public static class Career
    {
        public const int None = -1, Moonshine = 0, Drugs = 1, Arms = 2;
        public static readonly string[] Names = { "Bootleg alcohol", "Drugs", "Arms dealing" };
        public static readonly string[] KitIds = { "KitStill", "KitLab", "KitBench" };
        public static readonly string[] KitShop = { "gun", "market", "gun" };
        public static readonly string[] StarterRecipe = { "moon", "weed", "gun" };
        public static readonly string[] StarterProduct = { "Moonshine", "Weed", "Guns" };
        public static readonly string[] StationNames = { "Copper still", "Chemistry table", "Gunsmith bench" };
        /// <summary>Ingredients for the first batch and the shop kind that sells each one.</summary>
        public static readonly (string id, int n, string shop)[][] FirstBatch =
        {
            new[] { ("Corn", 2, "market"), ("Sugar", 2, "market"), ("Yeast", 1, "market") },
            new[] { ("Seeds", 2, "market"), ("PlantFood", 1, "market") },
            new[] { ("MetalParts", 3, "gun"), ("Gunpowder", 1, "gun") },
        };
        const string KChosen = "ms_career_v2", KStash = "ms_joshua_v1", KCollect = "ms_collects_v2";
        static int chosen = -2; static int stash = -1; static int collects = -1;

        public static int Chosen { get { if (chosen == -2) chosen = PlayerPrefs.GetInt(KChosen, -1); return chosen; } }
        public static bool Has => Chosen >= 0;
        public static void Choose(int c) { chosen = c; PlayerPrefs.SetInt(KChosen, c); PlayerPrefs.Save(); }
        public static bool StashTaken { get { if (stash < 0) stash = PlayerPrefs.GetInt(KStash, 0); return stash == 1; } set { stash = value ? 1 : 0; PlayerPrefs.SetInt(KStash, stash); PlayerPrefs.Save(); } }
        public static int Collects { get { if (collects < 0) collects = PlayerPrefs.GetInt(KCollect, 0); return collects; } }
        public static void AddCollect() { collects = Collects + 1; PlayerPrefs.SetInt(KCollect, collects); PlayerPrefs.Save(); }
        public static string Kit => Has ? KitIds[Chosen] : null;
        public static string Recipe => Has ? StarterRecipe[Chosen] : null;
        public static int RecipeIndex { get { if (!Has) return -1; for (int i = 0; i < HomeLab.Recipes.Length; i++) if (HomeLab.Recipes[i].id == StarterRecipe[Chosen]) return i; return -1; } }
        /// <summary>Your chosen trade's first product is open to you from day one, whatever your rank.</summary>
        public static bool IsStarterRecipe(string recipeId) => Has && StarterRecipe[Chosen] == recipeId;
        public static bool IsStarterProduct(string product) => Has && StarterProduct[Chosen] == product;
        public static bool IsKit(string id) { foreach (var k in KitIds) if (k == id) return true; return false; }
        public static int KitStation(string id) { for (int i = 0; i < KitIds.Length; i++) if (KitIds[i] == id) return i; return -1; }
    }
}
