using UnityEngine;
using UnityEngine.AI;

namespace MafiaSandbox.World
{
    /// <summary>Spawns the neighbourhood dogs and cats at their home spots (prototype coordinates, mirrored to Unity).</summary>
    public class AnimalSpawner : MonoBehaviour
    {
        // html (x, z, radius, kind)
        static readonly (float x, float z, float r, Animal.Kind k)[] Homes = {
            (64, -118, 7, Animal.Kind.Dog), (-66, -118, 7, Animal.Kind.Cat), (25, 25, 13, Animal.Kind.Cat), (25, 25, 13, Animal.Kind.Dog), (66, 66, 8, Animal.Kind.Dog), (-66, -66, 8, Animal.Kind.Cat),
            (-66, 66, 8, Animal.Kind.Cat), (60, 1, 9, Animal.Kind.Dog), (60, 1, 9, Animal.Kind.Dog), (64, -66, 7, Animal.Kind.Cat), (22, 92, 7, Animal.Kind.Cat), (-25, -25, 9, Animal.Kind.Cat), (-60, 112, 6, Animal.Kind.Dog) };

        void Start()
        {
            foreach (var h in Homes)
            {
                Vector3 home = Data.Coords.ToUnity(h.x, 0f, h.z);
                if (NavMesh.SamplePosition(home + Vector3.up * 6f, out var hh, 12f, NavMesh.AllAreas)) home = hh.position;
                int n = 1 + Random.Range(0, 2);
                for (int i = 0; i < n; i++)
                {
                    float a = Random.value * 6.3f, rr = Random.value * h.r * 0.6f; Vector3 p = home + new Vector3(Mathf.Cos(a) * rr, 0f, Mathf.Sin(a) * rr);
                    if (!NavMesh.SamplePosition(p, out var nh, 2f, NavMesh.AllAreas)) continue;
                    Animal.Create(h.k, nh.position, home, h.r);
                }
            }
        }
    }
}
