using UnityEngine;
using MafiaSandbox.Combat;
using MafiaSandbox.World;

namespace MafiaSandbox.Npc
{
    /// <summary>Line-of-sight helpers shared by all brains. Walls and buildings block; benches, fences and other low/transparent props don't (CityCollider.opaque).</summary>
    public static class Perception
    {
        static readonly RaycastHit[] hits = new RaycastHit[24];
        static int mask = -1;

        static int Mask()
        {
            if (mask == -1)
            {
                mask = ~0;
                int ch = LayerMask.NameToLayer("Characters"), hb = LayerMask.NameToLayer("Hitbox"), vh = LayerMask.NameToLayer("Vehicles");
                if (ch >= 0) mask &= ~(1 << ch); if (hb >= 0) mask &= ~(1 << hb); if (vh >= 0) mask &= ~(1 << vh);
            }
            return mask;
        }

        /// <summary>True if nothing opaque lies between a and b.</summary>
        public static bool Clear(Vector3 a, Vector3 b)
        {
            Vector3 d = b - a; float len = d.magnitude; if (len < 0.01f) return true;
            int n = Physics.RaycastNonAlloc(a, d / len, hits, len, Mask(), QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var cc = hits[i].collider.GetComponent<CityCollider>();
                if (cc != null && !cc.opaque) continue;          // see-through street furniture
                if (hits[i].collider.GetComponentInParent<CharacterBody>()) continue;
                return false;
            }
            return true;
        }

        /// <summary>Can an observer at eye (facing forward) see the target body? fov in degrees (full cone).</summary>
        public static bool CanSee(Vector3 eye, Vector3 forward, CharacterBody target, float range, float fovDeg)
        {
            if (!target) return false;
            Vector3 tp = target.ChestPos; Vector3 d = tp - eye; float dist = d.magnitude;
            if (dist > range) return false;
            if (dist > 3f && Vector3.Angle(forward, d) > fovDeg * 0.5f) return false;
            return Clear(eye, tp);
        }
    }
}
