using System.Collections.Generic;
using UnityEngine;
using MafiaSandbox.Combat;
using MafiaSandbox.Player;
using MafiaSandbox.World;

namespace MafiaSandbox.Npc
{
    /// <summary>Spawns wandering pedestrians on the pedestrian graph at start. Prefabs are assigned by CityBuilder from Assets/Characters.</summary>
    public class NpcSpawner : MonoBehaviour
    {
        public GameObject[] prefabs;
        public int count = 28;
        public RoadGraph graph;
        public readonly List<NpcCharacter> npcs = new List<NpcCharacter>();

        static readonly Dictionary<string, string> Names = new Dictionary<string, string> { { "Polis", "Officer" }, { "Doktor", "Medic" }, { "Itfaiyeci", "Firefighter" }, { "Vatandas", "Citizen" } };

        void Start()
        {
            if (!graph) graph = FindAnyObjectByType<RoadGraph>();
            if (prefabs == null || prefabs.Length == 0 || !graph || graph.pedNodes.Length == 0) { Debug.LogWarning("[MafiaSandbox] NpcSpawner: no prefabs or graph"); return; }
            var used = new HashSet<int>();
            for (int i = 0; i < count; i++)
            {
                int n = Random.Range(0, graph.pedNodes.Length); int guard = 0;
                while (used.Contains(n) && guard++ < 20) n = Random.Range(0, graph.pedNodes.Length);
                used.Add(n);
                var prefab = prefabs[i % prefabs.Length];
                var pos = graph.pedNodes[n].pos + new Vector3(Random.Range(-1f, 1f), 0.1f, Random.Range(-1f, 1f));
                npcs.Add(Spawn(prefab, pos, Random.Range(0f, 360f), i));
            }
        }

        public NpcCharacter Spawn(GameObject prefab, Vector3 pos, float yaw, int index)
        {
            var root = new GameObject("NPC " + prefab.name + " #" + index);
            root.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            var model = Instantiate(prefab, root.transform, false); model.name = "Model";
            var cc = root.AddComponent<CharacterController>(); cc.height = 1.8f; cc.radius = 0.3f; cc.center = new Vector3(0f, 0.92f, 0f); cc.stepOffset = 0.4f; cc.slopeLimit = 50f;
            root.AddComponent<CharacterAnimator>();
            var body = root.AddComponent<CharacterBody>();
            var npc = root.AddComponent<NpcCharacter>(); npc.graph = graph;
            string baseName = prefab.name.Split('_')[0];
            npc.displayName = (Names.TryGetValue(baseName, out var d) ? d : baseName) + " " + (index + 1);
            root.layer = CharacterBody.CharactersLayer >= 0 ? CharacterBody.CharactersLayer : 0;
            return npc;
        }
    }
}
