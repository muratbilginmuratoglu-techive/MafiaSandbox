using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using MafiaSandbox.Combat;
using MafiaSandbox.Player;
using MafiaSandbox.World;

namespace MafiaSandbox.Npc
{
    /// <summary>
    /// Populates the city at start: civilians on the pedestrian graph, police around the station, the crew at the construction
    /// site, a keeper behind every shop counter. Prefabs come from Assets/Characters (assigned by CityBuilder).
    /// </summary>
    public class NpcSpawner : MonoBehaviour
    {
        public GameObject[] prefabs;
        public int civilians = 26, police = 8, gang = 5;
        public RoadGraph graph;
        public Vector3 policeStation;      // station door (Unity space)
        public Vector3 gangCenter;         // construction building
        public readonly List<NpcBrain> npcs = new List<NpcBrain>();
        int counter;

        static readonly Dictionary<string, string> Names = new Dictionary<string, string> { { "Polis", "Officer" }, { "Doktor", "Medic" }, { "Itfaiyeci", "Firefighter" }, { "Vatandas", "Citizen" } };

        GameObject Pick(string prefix)
        {
            var list = new List<GameObject>(); foreach (var p in prefabs) if (p && p.name.StartsWith(prefix)) list.Add(p);
            if (list.Count == 0) list.AddRange(prefabs);
            return list[Random.Range(0, list.Count)];
        }

        void Start()
        {
            if (!graph) graph = FindAnyObjectByType<RoadGraph>();
            if (prefabs == null || prefabs.Length == 0 || !graph || graph.pedNodes.Length == 0) { Debug.LogWarning("[MafiaSandbox] NpcSpawner: no prefabs or graph"); return; }

            // civilians (and medics)
            for (int i = 0; i < civilians; i++)
            {
                var n = graph.pedNodes[Random.Range(0, graph.pedNodes.Length)].pos + new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f));
                bool medic = i % 9 == 4;
                var prefab = medic ? Pick("Doktor") : (Random.value < 0.8f ? Pick("Vatandas") : Pick("Itfaiyeci"));
                var c = Spawn<CivilianBrain>(prefab, n, Random.Range(0f, 360f)); c.isMedic = medic; c.bravery = Random.value * 0.6f;
            }
            // the camp under the bridge (prototype coordinates: x, z, pose, fire index)
            var fires = new[] { Data.Coords.ToUnity(50f, 0f, 0f), Data.Coords.ToUnity(61f, 0f, 5f), Data.Coords.ToUnity(69f, 0f, -4f) };
            var campers = new (float x, float z, string pose, int fire)[] { (48.6f, 1.4f, "Crouch_Idle", 0), (51.6f, -1.2f, "Idle", 0), (59.2f, 3.8f, "Talk", 1), (60.2f, 6.4f, "Talk", 1), (67.5f, -2.4f, "Dance", 2), (63f, -7.5f, "Crouch_Idle", -1), (70.5f, 6.5f, "Idle", 2) };
            CamperBrain talkPartner = null;
            foreach (var cp in campers)
            {
                Vector3 hp = Data.Coords.ToUnity(cp.x, 0f, cp.z); Vector3 face = cp.fire >= 0 ? fires[cp.fire] : hp + Vector3.forward;
                var c = Spawn<CamperBrain>(Pick("Vatandas"), hp, Mathf.Atan2(face.x - hp.x, face.z - hp.z) * Mathf.Rad2Deg);
                c.homeSpot = c.transform.position; c.pose = cp.pose; c.fire = cp.fire >= 0 ? fires[cp.fire] : (Vector3?)null; c.displayName = "Camper";
                if (cp.pose == "Talk") { if (talkPartner) { c.partner = talkPartner; talkPartner.partner = c; } else talkPartner = c; }
            }
            // police
            for (int i = 0; i < police; i++)
            {
                var p = policeStation + new Vector3(Random.Range(-6f, 6f), 0f, Random.Range(-6f, 6f));
                if (i >= 3) p = graph.pedNodes[Random.Range(0, graph.pedNodes.Length)].pos;   // some already out on patrol
                var b = Spawn<PoliceBrain>(Pick("Polis"), p, Random.Range(0f, 360f)); b.stationPos = policeStation; b.walkSpeed = 1.9f; b.runSpeed = 6.2f;
            }
            // gang
            for (int i = 0; i < gang; i++)
            {
                float a = i / (float)gang * Mathf.PI * 2f; var p = gangCenter + new Vector3(Mathf.Cos(a) * 5f, 0f, Mathf.Sin(a) * 5f);
                var g = Spawn<GangBrain>(Pick("Vatandas"), p, Random.Range(0f, 360f)); g.homeSpot = p; g.displayName = "Crew " + (i + 1); g.runSpeed = 6f;
            }
            // shopkeepers
            foreach (var bi in FindObjectsByType<BuildingInfo>(FindObjectsSortMode.None))
            {
                if (bi.kind == "home" || bi.kind == "police") continue;
                if (bi.kind == "house") { if (bi.hasOwner && Random.value < 0.6f) { var o = Spawn<KeeperBrain>(Pick("Vatandas"), bi.ownerPos, bi.ownerYaw); o.building = bi; o.role = "owner"; o.displayName = string.IsNullOrEmpty(bi.ownerName) ? "Resident" : bi.ownerName; } continue; }
                var k = Spawn<KeeperBrain>(Pick("Vatandas"), bi.keeperPos, bi.keeperYaw); k.building = bi; k.displayName = bi.label + " keeper";
                if (bi.hasKeeper2) { var k2 = Spawn<KeeperBrain>(Pick("Vatandas"), bi.keeper2Pos, bi.keeper2Yaw); k2.building = bi; k2.displayName = bi.label + " teller"; }
            }
        }

        public T Spawn<T>(GameObject prefab, Vector3 pos, float yaw) where T : NpcBrain
        {
            if (NavMesh.SamplePosition(pos, out var hit, 6f, NavMesh.AllAreas)) pos = hit.position;
            int idx = ++counter;
            var root = new GameObject($"NPC {prefab.name} #{idx}");
            root.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            root.layer = CharacterBody.CharactersLayer >= 0 ? CharacterBody.CharactersLayer : LayerMask.NameToLayer("Characters");
            var model = Instantiate(prefab, root.transform, false); model.name = "Model";
            var cap = root.AddComponent<CapsuleCollider>(); cap.height = 1.8f; cap.radius = 0.3f; cap.center = new Vector3(0f, 0.9f, 0f);
            var agent = root.AddComponent<NavMeshAgent>();
            root.AddComponent<CharacterAnimator>();
            root.AddComponent<CharacterBody>();
            var brain = root.AddComponent<T>(); brain.graph = graph;
            string baseName = prefab.name.Split('_')[0];
            brain.displayName = (Names.TryGetValue(baseName, out var d) ? d : baseName) + " " + idx;
            npcs.Add(brain);
            return brain;
        }
    }
}
