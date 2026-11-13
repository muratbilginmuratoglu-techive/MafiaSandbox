using System.Collections.Generic;
using MafiaSandbox.Combat;

namespace MafiaSandbox.Economy
{
    public class ItemDef
    {
        public string id, prop, name, kind; public int value; public int heal;
        public ItemDef(string id, string name, int value, string kind, int heal = 0) { this.id = id; prop = "Esya_" + id; this.name = name; this.value = value; this.kind = kind; this.heal = heal; }
    }

    public class ShopDef
    {
        public string title; public string[] stock, shelf, buysKinds, vault; public int registerMin, registerMax; public bool fence;
        public Dictionary<WeaponKind, int> weapons;
    }

    /// <summary>Item catalogue, shop definitions and the price model (ported 1:1 from the HTML prototype, prices in USD).</summary>
    public static class Items
    {
        public static readonly Dictionary<string, ItemDef> All = new Dictionary<string, ItemDef>();
        public static readonly Dictionary<string, ShopDef> Shops = new Dictionary<string, ShopDef>();
        public static readonly Dictionary<string, float> Markup = new Dictionary<string, float> { { "market", 1.0f }, { "cafe", 1.2f }, { "restaurant", 1.25f }, { "pharmacy", 1.15f }, { "gun", 1.0f }, { "pawn", 1.4f }, { "jeweler", 1.3f }, { "barber", 1.0f }, { "bank", 1f }, { "house", 1f }, { "police", 1f }, { "home", 1f } };
        public static readonly Dictionary<string, float> BuyRate = new Dictionary<string, float> { { "pawn", 0.55f }, { "jeweler", 0.65f }, { "pharmacy", 0.6f }, { "market", 0.6f }, { "gun", 0.6f }, { "cafe", 0.4f }, { "restaurant", 0.4f }, { "barber", 0.4f } };

        static Items()
        {
            void I(string id, string name, int value, string kind, int heal = 0) => All[id] = new ItemDef(id, name, value, kind, heal);
            I("Telefon", "Phone", 300, "valuable"); I("Laptop", "Laptop", 900, "valuable"); I("Saat", "Wristwatch", 450, "valuable");
            I("Para", "Cash bundle", 0, "cash"); I("Kola", "Cola", 2, "food", 8); I("Ekmek", "Bread", 2, "food", 12);
            I("Konserve", "Canned food", 4, "food", 18); I("Ilac", "First aid kit", 40, "med", 50); I("Mucevher", "Jewelry box", 1400, "valuable");
            I("Altin", "Gold bar", 2600, "valuable"); I("Sarap", "Wine", 25, "food", 5); I("Kulaklik", "Headphones", 200, "valuable");
            I("TV", "Television", 700, "valuable"); I("Tablo", "Painting", 550, "valuable"); I("Mermi", "Ammo box", 40, "misc");
            I("Paket", "Suspicious package", 650, "contraband"); I("Telsiz", "Police radio", 220, "misc"); I("Kelepce", "Handcuffs", 40, "misc");
            I("Sigara", "Cigarettes", 8, "misc"); I("Anahtar", "Keychain", 3, "misc"); I("Burger", "Burger", 8, "food", 30); I("Kahve", "Coffee", 4, "food", 6);

            ShopDef S(string kind, string title, string[] stock, string[] shelf, int r0, int r1, string[] buys) { var d = new ShopDef { title = title, stock = stock, shelf = shelf, registerMin = r0, registerMax = r1, buysKinds = buys }; Shops[kind] = d; return d; }
            string[] N = new string[0];
            S("market", "Market", new[] { "Kola", "Ekmek", "Konserve", "Sigara" }, new[] { "Kola", "Ekmek", "Konserve", "Sigara", "Kola" }, 60, 180, new[] { "food" });
            var gun = S("gun", "Gun Shop", N, new[] { "Mermi", "Mermi" }, 300, 900, new[] { "misc" });
            gun.weapons = new Dictionary<WeaponKind, int> { { WeaponKind.Rifle, 2400 }, { WeaponKind.Shotgun, 1800 }, { WeaponKind.Revolver, 900 }, { WeaponKind.Bat, 40 } };
            S("pawn", "Pawn Shop", new[] { "Saat", "Kulaklik" }, new[] { "Saat", "Kulaklik", "Laptop", "Tablo", "Telefon" }, 200, 600, new[] { "valuable", "misc", "contraband", "food", "med" }).fence = true;
            S("cafe", "Cafe", new[] { "Kahve", "Burger" }, new[] { "Kahve" }, 50, 160, N);
            S("restaurant", "Restaurant", new[] { "Burger", "Sarap", "Kola" }, new[] { "Burger" }, 120, 400, N);
            S("pharmacy", "Pharmacy", new[] { "Ilac" }, new[] { "Ilac", "Ilac" }, 150, 400, new[] { "med" });
            S("jeweler", "Jeweler", new[] { "Saat", "Mucevher" }, new[] { "Mucevher", "Altin", "Saat", "Mucevher", "Saat" }, 800, 2000, new[] { "valuable" });
            S("bank", "Bank", N, N, 3000, 6000, N).vault = new[] { "Altin", "Altin", "Altin", "Para" };
            S("barber", "Barber", N, new[] { "Sigara" }, 40, 150, N);
            S("house", "House", N, new[] { "Laptop", "TV", "Saat", "Tablo", "Telefon", "Mucevher" }, 0, 0, N);
            S("police", "Police Station", N, new[] { "Mermi", "Telsiz", "Kelepce" }, 0, 0, N);
            S("home", "Home", N, new[] { "Konserve", "Sigara", "Kola" }, 0, 0, N);
        }

        public static ItemDef Get(string id) => All.TryGetValue(id, out var d) ? d : null;
        public static ShopDef ShopFor(string kind) => Shops.TryGetValue(kind, out var d) ? d : null;
        /// <summary>Shops where taking from the shelf without paying is theft (they have a keeper who sells those goods).</summary>
        public static bool IsRetail(string kind) => kind == "market" || kind == "cafe" || kind == "restaurant" || kind == "pharmacy" || kind == "gun" || kind == "pawn" || kind == "jeweler" || kind == "barber";
    }

    [System.Serializable]
    public class InvItem { public string id; public int qty; public bool stolen; }
}
