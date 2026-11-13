using System;
using UnityEngine;

namespace MafiaSandbox.Data
{
    /// <summary>
    /// Mirror of Assets/Data/city.json — exported from the HTML prototype by Assets/Tools/export_city_from_html.py.
    /// All positions are in glTF / three.js world space (right-handed). Convert with <see cref="Coords"/> before use.
    /// </summary>
    [Serializable]
    public class CityData
    {
        public HomeData home;
        public PropData[] props;
        public VehicleData[] vehicles;
        public BuildingData[] buildings;
        public ColliderData[] colliders;
        public GraphNode[] roadNodes;
        public GraphNode[] pedNodes;
        public LocationData[] locations;
    }

    [Serializable] public class HomeData { public float x, y, z; }

    [Serializable] public class PropData { public string name; public float[] pos; public float rotY; public float scale = 1f; }

    [Serializable]
    public class VehicleData
    {
        public string name; public float[] pos; public float heading; public bool ai; public bool player; public float length; public float width;
    }

    [Serializable]
    public class BuildingData
    {
        public string name, kind, label, owner;
        public float[] pos; public float rotY; public float[] min, max, door, counter, size;
        public SolidData[] solids; public ShelfData[] shelves; public SeatData[] seats; public KeeperData keeper; public KeeperData keeper2; public KeeperData ownerSpot; public SeatData safe; public SeatData register;
    }

    [Serializable] public class SolidData { public float x, z, w, d, h; }
    [Serializable] public class ShelfData { public float x, y, z; }
    [Serializable] public class SeatData { public float x, z; }
    [Serializable] public class KeeperData { public float x, z, rot; }

    [Serializable] public class ColliderData { public float[] min, max; public bool opaque, building; }

    [Serializable] public class GraphNode { public float x, y, z; public int[] adj; }

    [Serializable] public class LocationData { public string name; public float x, z, yaw; }

    /// <summary>
    /// glTF (right-handed, +Y up) → Unity (left-handed). Unity glTFast (4.x and newer) converts imported meshes by
    /// inverting the X axis, so world positions exported from the prototype get the same treatment here.
    /// A yaw of θ (radians, three.js rotation.y) becomes -θ degrees in Unity.
    /// If models ever appear mirrored against the roads, flip <see cref="FlipX"/> (older importers negated Z instead).
    /// </summary>
    public static class Coords
    {
        public static bool FlipX = true;

        public static Vector3 ToUnity(float x, float y, float z) => FlipX ? new Vector3(-x, y, z) : new Vector3(x, y, -z);
        public static Vector3 ToUnity(float[] p) => p == null || p.Length < 3 ? Vector3.zero : ToUnity(p[0], p[1], p[2]);
        public static Vector3 ToUnityXZ(float x, float z, float y = 0f) => ToUnity(x, y, z);

        /// <summary>three.js rotation.y (radians) → Unity euler Y (degrees).</summary>
        public static float YawToUnity(float radians) => -radians * Mathf.Rad2Deg;

        /// <summary>Axis-aligned box from min/max in glTF space → Unity center + size.</summary>
        public static Bounds BoundsToUnity(float[] min, float[] max)
        {
            var a = ToUnity(min); var b = ToUnity(max);
            var lo = Vector3.Min(a, b); var hi = Vector3.Max(a, b);
            return new Bounds((lo + hi) * 0.5f, hi - lo);
        }
    }
}
