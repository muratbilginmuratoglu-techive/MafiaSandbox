using System.Collections.Generic;
using UnityEngine;

namespace MafiaSandbox.Combat
{
    public enum WeaponKind { Pistol, Revolver, Rifle, Shotgun, Knife, Bat }

    /// <summary>Weapon tuning, ported 1:1 from the HTML prototype (damage in "hits": a citizen has 3 hp).</summary>
    public class WeaponDef
    {
        public WeaponKind kind;
        public string name;         // HUD name
        public string clipPrefix;   // Pistol_*, Rifle_*, Shotgun_*, Knife_*, Bat_*
        public int damage = 1;
        public float rof = 0.3f;    // seconds between shots / swings
        public bool auto;
        public float spreadDeg = 0.6f;
        public int pellets = 1;
        public float kick = 0.9f;   // camera recoil
        public float aimFov = 46f;
        public float killRange = 0f; // shotgun: point-blank kill distance
        public bool ejectsShells = true;
        public float noise = 28f;   // hearing radius for NPCs (step 3)

        // melee
        public bool melee;
        public float range = 1.5f, arcCos = 0.62f, hitAt = 0.3f, impulse = 2.5f;
        public bool headKill;       // bat: head hit is lethal

        public static readonly Dictionary<WeaponKind, WeaponDef> All = new Dictionary<WeaponKind, WeaponDef>
        {
            { WeaponKind.Pistol,   new WeaponDef { kind = WeaponKind.Pistol,   name = "Pistol",        clipPrefix = "Pistol",  damage = 1, rof = 0.30f, auto = false, spreadDeg = 0.6f, pellets = 1, kick = 0.9f, aimFov = 46f, noise = 28f } },
            { WeaponKind.Revolver, new WeaponDef { kind = WeaponKind.Revolver, name = "Revolver",      clipPrefix = "Pistol",  damage = 2, rof = 0.60f, auto = false, spreadDeg = 0.4f, pellets = 1, kick = 1.7f, aimFov = 46f, ejectsShells = false, noise = 34f } },
            { WeaponKind.Rifle,    new WeaponDef { kind = WeaponKind.Rifle,    name = "Assault rifle", clipPrefix = "Rifle",   damage = 1, rof = 0.10f, auto = true,  spreadDeg = 1.1f, pellets = 1, kick = 0.45f, aimFov = 42f, noise = 34f } },
            { WeaponKind.Shotgun,  new WeaponDef { kind = WeaponKind.Shotgun,  name = "Shotgun",       clipPrefix = "Shotgun", damage = 1, rof = 1.00f, auto = false, spreadDeg = 5.5f, pellets = 8, kick = 2.6f, aimFov = 50f, killRange = 9f, ejectsShells = false, noise = 40f } },
            { WeaponKind.Knife,    new WeaponDef { kind = WeaponKind.Knife,    name = "Knife",         clipPrefix = "Knife",   melee = true, damage = 2, rof = 0.45f, range = 1.5f, arcCos = 0.62f, hitAt = 0.30f, impulse = 2.5f, aimFov = 55f, noise = 6f } },
            { WeaponKind.Bat,      new WeaponDef { kind = WeaponKind.Bat,      name = "Baseball bat",  clipPrefix = "Bat",     melee = true, damage = 2, rof = 0.85f, range = 2.1f, arcCos = 0.45f, hitAt = 0.42f, impulse = 6.5f, headKill = true, aimFov = 55f, noise = 10f } },
        };

        public static readonly WeaponKind[] Order = { WeaponKind.Pistol, WeaponKind.Rifle, WeaponKind.Shotgun, WeaponKind.Knife, WeaponKind.Bat, WeaponKind.Revolver }; // keys 1..6
    }
}
