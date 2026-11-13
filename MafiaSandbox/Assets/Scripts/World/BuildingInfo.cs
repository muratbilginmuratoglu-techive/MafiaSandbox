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
        public Vector3[] shelves;    // item spawn points
        public Vector3[] seats;
        public Bounds footprint;

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan; Gizmos.DrawWireCube(footprint.center, footprint.size);
            Gizmos.color = Color.green; Gizmos.DrawSphere(door, 0.3f);
            Gizmos.color = Color.yellow; Gizmos.DrawSphere(counter, 0.25f);
            if (shelves != null) { Gizmos.color = Color.magenta; foreach (var s in shelves) Gizmos.DrawSphere(s, 0.15f); }
        }
    }
}
