using UnityEngine;

namespace MafiaSandbox.World
{
    /// <summary>Tag on the AABB colliders imported from the prototype. <c>opaque</c> blocks line of sight (used by NPC vision in step 3).</summary>
    public class CityCollider : MonoBehaviour
    {
        public bool opaque;
        public bool building;
    }
}
