using UnityEngine;

namespace MafiaSandbox.World
{
    /// <summary>Marker placed by CityBuilder for every enterable building (shops, houses, bank, police station, home). Economy/AI hook into this in later steps.</summary>
    public class BuildingInfo : MonoBehaviour
    {
        public string kind;          // market, gun, pawn, cafe, restaurant, pharmacy, jeweler, bank, barber, house, police, home
        public string label;
        public string ownerName;
        public Vector3 door;         // world (Unity) space
        public Vector3 counter;
        public Vector3 keeperPos;
        public float keeperYaw;
        public bool hasKeeper2; public Vector3 keeper2Pos; public float keeper2Yaw;   // bank: second teller
        public bool hasOwner; public Vector3 ownerPos; public float ownerYaw;         // houses: the resident
        public bool hasSafe; public Vector3 safePos;                                  // pawn / jeweler / bank vault
        public Vector3[] shelves;    // item spawn points
        public Vector3[] seats;
        public Bounds footprint;

        public static readonly System.Collections.Generic.List<BuildingInfo> All = new System.Collections.Generic.List<BuildingInfo>();
        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }
        /// <summary>The building whose footprint contains the point (ignoring height), or null when outdoors.</summary>
        public static BuildingInfo At(Vector3 p)
        {
            foreach (var b in All) { var f = b.footprint; if (p.x >= f.min.x - 0.3f && p.x <= f.max.x + 0.3f && p.z >= f.min.z - 0.3f && p.z <= f.max.z + 0.3f && p.y >= f.min.y - 1.5f && p.y <= f.max.y) return b; }
            return null;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan; Gizmos.DrawWireCube(footprint.center, footprint.size);
            Gizmos.color = Color.green; Gizmos.DrawSphere(door, 0.3f);
            Gizmos.color = Color.yellow; Gizmos.DrawSphere(counter, 0.25f);
            if (shelves != null) { Gizmos.color = Color.magenta; foreach (var s in shelves) Gizmos.DrawSphere(s, 0.15f); }
        }
    }
}
