using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using MafiaSandbox.Data;
using MafiaSandbox.World;
using MafiaSandbox.Player;
using MafiaSandbox.Vehicles;
using MafiaSandbox.Core;
using MafiaSandbox.UI;
using Unity.AI.Navigation;
using UnityEngine.AI;

namespace MafiaSandbox.EditorTools
{
    /// <summary>
    /// MafiaSandbox ▸ 1. Setup   — fixes import settings (character GLBs → Legacy animation) and input handler.
    /// MafiaSandbox ▸ 2. Build City Scene — rebuilds Assets/Scenes/City.unity from Assets/Data/city.json + the GLBs.
    /// Both are idempotent; run them again after pulling a new city.json or new props.
    /// </summary>
    public static class CityBuilder
    {
        const string PropsDir = "Assets/Props";
        const string CharsDir = "Assets/Characters";
        const string StaticGlb = "Assets/Data/CityStatic.glb";
        const string CityJson = "Assets/Data/city.json";
        const string ScenePath = "Assets/Scenes/City.unity";
        const string PlayerModel = "Polis_01";

        // ------------------------------------------------------------------ 1. setup
        [MenuItem("MafiaSandbox/1. Setup (import settings)", priority = 1)]
        public static void Setup()
        {
            // linear lighting like the three.js prototype: in gamma space sun + ambient add up past white on light pavement
            if (PlayerSettings.colorSpace != ColorSpace.Linear) { PlayerSettings.colorSpace = ColorSpace.Linear; Debug.Log("[MafiaSandbox] Color space → Linear"); }
            int fixedChars = 0, seen = 0;
            foreach (var file in Directory.GetFiles(CharsDir, "*.glb"))
            {
                var path = file.Replace('\\', '/'); seen++;
                if (SetLegacyAnimation(path)) fixedChars++;
            }
            EnsureLegacyInput();
            EnsureLayers();
            AssetDatabase.Refresh();
            Debug.Log($"[MafiaSandbox] Setup done. Character GLBs: {seen}, switched to Legacy animation: {fixedChars}. Now run MafiaSandbox ▸ 2. Build City Scene.");
        }

        /// <summary>
        /// glTFast's ScriptedImporter keeps its settings in private serialized fields, so we edit them through a SerializedObject:
        /// importSettings.animationMethod → Legacy. Works without a compile-time dependency on the package's internals.
        /// </summary>
        static bool SetLegacyAnimation(string path)
        {
            var importer = AssetImporter.GetAtPath(path);
            if (importer == null || importer.GetType().Name != "GltfImporter")
            { Debug.LogWarning($"[MafiaSandbox] {path} is not imported by glTFast yet (package still installing?). Re-run Setup after the import finishes."); return false; }
            var so = new SerializedObject(importer);
            SerializedProperty prop = null; var it = so.GetIterator();
            while (it.NextVisible(true)) { if (it.name == "animationMethod" && it.propertyType == SerializedPropertyType.Enum) { prop = it.Copy(); break; } }
            if (prop == null) { Debug.LogWarning($"[MafiaSandbox] {path}: could not find the animationMethod import setting. Set it to Legacy in the Inspector (Import Settings ▸ Animation ▸ Animation Method)."); return false; }
            int legacy = System.Array.IndexOf(prop.enumNames, "Legacy");
            if (legacy < 0 || prop.enumValueIndex == legacy) return false;
            prop.enumValueIndex = legacy;
            so.ApplyModifiedProperties();
            importer.SaveAndReimport();
            return true;
        }

        /// <summary>Layers: 8 = Characters (CharacterController capsules), 9 = Hitbox (per-bone hit capsules / ragdoll).</summary>
        public static void EnsureLayers()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) { Debug.LogWarning("[MafiaSandbox] TagManager not found; add layers 'Characters' (8) and 'Hitbox' (9) manually."); return; }
            var so = new SerializedObject(assets[0]); var layers = so.FindProperty("layers"); bool changed = false;
            void Set(int idx, string name) { var p = layers.GetArrayElementAtIndex(idx); if (p.stringValue != name) { p.stringValue = name; changed = true; } }
            Set(8, "Characters"); Set(9, "Hitbox"); Set(10, "Vehicles");
            if (changed) so.ApplyModifiedProperties();
            Physics.IgnoreLayerCollision(8, 9, true);
        }

        /// <summary>Scripts use the classic UnityEngine.Input. Make sure the project allows it (Active Input Handling = Both).</summary>
        static void EnsureLegacyInput()
        {
            try
            {
                var ps = Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings");
                var so = new SerializedObject(ps);
                var prop = so.FindProperty("activeInputHandler");
                if (prop != null && prop.intValue == 1)   // 1 = Input System Package only
                {
                    prop.intValue = 2;                    // 2 = Both
                    so.ApplyModifiedProperties();
                    EditorUtility.DisplayDialog("MafiaSandbox", "Active Input Handling was set to 'Both' so the classic Input API works.\nPlease restart the Unity Editor once, then run 'Build City Scene'.", "OK");
                }
            }
            catch (System.Exception e) { Debug.LogWarning("[MafiaSandbox] Could not check Active Input Handling automatically: " + e.Message + "\nIf movement does nothing, set Edit ▸ Project Settings ▸ Player ▸ Active Input Handling = Both."); }
        }

        // ------------------------------------------------------------------ 2. build
        /// <summary>Parked cars that stay: only the crews' own cars tucked inside their yards (behind the sidewalk line).</summary>
        static bool KeepParked(VehicleData v) => Mathf.Abs(v.pos[0]) >= 117f && v.name.StartsWith("Araba_SUV");

        /// <summary>
        /// The prototype's static mesh has a solid slab across the tunnel mouth (x −8…8, z 83…84, up to 6.5 m) that was only
        /// decoration there (its collision was AABB-based). Here the mesh collider would wall the tunnel off, so the slab's
        /// triangles are cut out of a copy of the mesh used for both rendering and collision.
        /// </summary>
        static Mesh TrimTunnelPortal(Mesh src, Transform node)
        {
            var verts = src.vertices; var tris = src.triangles; var keep = new List<int>(tris.Length); int cut = 0;
            for (int i = 0; i < tris.Length; i += 3)
            {
                bool inside = true;
                for (int k = 0; k < 3 && inside; k++)
                {
                    var w = node.TransformPoint(verts[tris[i + k]]);   // Unity world (x mirrored relative to the prototype)
                    inside = Mathf.Abs(w.x) <= 8.2f && w.z >= 82.8f && w.z <= 84.2f && w.y >= -0.2f && w.y <= 6.7f;
                }
                if (inside) { cut++; continue; }
                keep.Add(tris[i]); keep.Add(tris[i + 1]); keep.Add(tris[i + 2]);
            }
            if (cut == 0) return src;
            var m = Object.Instantiate(src); m.name = src.name + "_trimmed"; m.triangles = keep.ToArray(); m.RecalculateBounds();
            Debug.Log($"[MafiaSandbox] Tunnel portal: cut {cut} triangles from the static mesh");
            return m;
        }

        [MenuItem("MafiaSandbox/2. Build City Scene", priority = 2)]
        public static void BuildScene()
        {
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(CityJson);
            if (!json) { Debug.LogError("[MafiaSandbox] " + CityJson + " missing"); return; }
            AssetDatabase.ImportAsset(StaticGlb, ImportAssetOptions.ForceUpdate);   // fresh materials: an earlier weather pass had tinted the imported ground material white for good
            var city = JsonUtility.FromJson<CityData>(json.text);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var world = new GameObject("World").transform;
            DistrictBuilder.Extend(city, world);   // step 7: the gang / mafia districts beyond x = ±78

            // --- static geometry (ground, roads, bridge, overpass, tunnel, kerbs, markings)
            var staticPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(StaticGlb);
            int staticMeshes = 0;
            if (staticPrefab)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(staticPrefab); go.name = "Static"; go.transform.SetParent(world, false);
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                {
                    if (!mf.sharedMesh || mf.sharedMesh.vertexCount < 3) continue;
                    var trimmed = TrimTunnelPortal(mf.sharedMesh, mf.transform);
                    if (trimmed != mf.sharedMesh) { Directory.CreateDirectory("Assets/Scenes"); AssetDatabase.CreateAsset(trimmed, "Assets/Scenes/CityStatic_trimmed.asset"); mf.sharedMesh = trimmed; }
                    var mc = mf.gameObject.GetComponent<MeshCollider>(); if (!mc) mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh; staticMeshes++;
                }
                SetStaticRecursive(go, true);
            }
            else Debug.LogError("[MafiaSandbox] " + StaticGlb + " not found / not imported");

            // --- props
            var propsRoot = new GameObject("Props").transform; propsRoot.SetParent(world, false);
            var cache = new Dictionary<string, GameObject>(); var missing = new HashSet<string>(); int placed = 0;
            foreach (var p in city.props)
            {
                // the exporter listed every vehicle in props as well; those are placed (and driven) from city.vehicles, so a
                // static copy here would be a ghost car standing in the street
                if (p.name.StartsWith("Araba_") || p.name.StartsWith("Arac_") || p.name.StartsWith("Car_")) continue;
                var prefab = LoadProp(p.name, cache);
                if (!prefab) { missing.Add(p.name); continue; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab); go.name = p.name; go.transform.SetParent(propsRoot, false);
                go.transform.SetPositionAndRotation(Coords.ToUnity(p.pos), Quaternion.Euler(0f, Coords.YawToUnity(p.rotY), 0f));
                if (Mathf.Abs(p.scale - 1f) > 0.001f) go.transform.localScale = Vector3.one * p.scale;
                SetStaticRecursive(go, true); placed++;
            }

            // --- AABB colliders (walls, furniture, fences … exactly what the prototype collided with)
            var colRoot = new GameObject("Colliders").transform; colRoot.SetParent(world, false);
            int cols = 0;
            foreach (var c in city.colliders)
            {
                var b = Coords.BoundsToUnity(c.min, c.max);
                if (b.size.x < 0.02f || b.size.z < 0.02f || b.size.y < 0.02f) continue;
                var go = new GameObject(c.building ? "Wall" : "Col"); go.transform.SetParent(colRoot, false); go.transform.position = b.center;
                var bc = go.AddComponent<BoxCollider>(); bc.size = b.size;
                var cc = go.AddComponent<CityCollider>(); cc.opaque = c.opaque; cc.building = c.building;
                go.isStatic = true; cols++;
            }

            // --- buildings (markers with door / counter / shelves for the economy & AI steps)
            var bRoot = new GameObject("Buildings").transform; bRoot.SetParent(world, false);
            foreach (var b in city.buildings)
            {
                var go = new GameObject($"{b.kind}: {b.label}"); go.transform.SetParent(bRoot, false);
                var center = Coords.ToUnity(b.pos); go.transform.SetPositionAndRotation(center, Quaternion.Euler(0f, Coords.YawToUnity(b.rotY), 0f));
                var bi = go.AddComponent<BuildingInfo>();
                bi.kind = b.kind; bi.label = b.label; bi.ownerName = b.owner;
                bi.door = Coords.ToUnity(b.door); bi.counter = Coords.ToUnity(b.counter);
                // building-local points → world: local x along building's X, z along its Z (meta coordinates), rotated by rotY
                Vector3 Local(float lx, float ly, float lz) => go.transform.TransformPoint(Coords.ToUnity(lx, ly, lz));
                bi.keeperPos = Local(b.keeper.x, 0f, b.keeper.z); bi.keeperYaw = go.transform.eulerAngles.y + Coords.YawToUnity(b.keeper.rot);
                if (b.keeper2 != null && (b.keeper2.x != 0f || b.keeper2.z != 0f)) { bi.hasKeeper2 = true; bi.keeper2Pos = Local(b.keeper2.x, 0f, b.keeper2.z); bi.keeper2Yaw = go.transform.eulerAngles.y + Coords.YawToUnity(b.keeper2.rot); }
                if (b.ownerSpot != null && (b.ownerSpot.x != 0f || b.ownerSpot.z != 0f)) { bi.hasOwner = true; bi.ownerPos = Local(b.ownerSpot.x, 0f, b.ownerSpot.z); bi.ownerYaw = go.transform.eulerAngles.y + Coords.YawToUnity(b.ownerSpot.rot); }
                if (b.safe != null && (b.safe.x != 0f || b.safe.z != 0f)) { bi.hasSafe = true; bi.safePos = Local(b.safe.x, 0f, b.safe.z); }
                var shelves = new List<Vector3>(); foreach (var s in b.shelves) shelves.Add(Local(s.x, s.y, s.z)); bi.shelves = shelves.ToArray();
                var seats = new List<Vector3>(); foreach (var s in b.seats) seats.Add(Local(s.x, 0f, s.z)); bi.seats = seats.ToArray();
                var lo = Coords.ToUnityXZ(b.min[0], b.min[1]); var hi = Coords.ToUnityXZ(b.max[0], b.max[1]);
                var fl = Vector3.Min(lo, hi); var fh = Vector3.Max(lo, hi); fh.y = center.y + Mathf.Abs(b.size != null && b.size.Length > 2 ? b.size[2] : 3.5f);
                bi.footprint = new Bounds((fl + fh) * 0.5f, fh - fl);
            }

            // --- road & pedestrian graphs
            var graph = new GameObject("RoadGraph").AddComponent<RoadGraph>(); graph.transform.SetParent(world, false);
            graph.roadNodes = Convert(city.roadNodes); graph.pedNodes = Convert(city.pedNodes);

            // --- vehicles (parked ones now; traffic cars come alive in step 4)
            var vRoot = new GameObject("Vehicles").transform; vRoot.SetParent(world, false);
            VehicleController playerCar = null; int vCount = 0, skippedTraffic = 0, trafficCount = 0;
            int obstacleMask = 0; { foreach (var ln in new[] { "Default", "Vehicles", "Characters", "Hitbox" }) { int l = LayerMask.NameToLayer(ln); if (l >= 0) obstacleMask |= 1 << l; } }
            foreach (var v in city.vehicles)
            {
                // the prototype's filler cars (car park / warehouse lots) just sat there; the traffic and the crews' own cars remain
                if (v.player) continue;                                                     // no personal car any more: take any car on the street
                if (!v.ai && !KeepParked(v)) continue;                                      // parked filler cars looked abandoned
                var prefab = LoadProp(v.name, cache);
                if (!prefab) { missing.Add(v.name); continue; }
                var root = new GameObject("Vehicle " + v.name); root.transform.SetParent(vRoot, false);
                root.transform.SetPositionAndRotation(Coords.ToUnity(v.pos) + Vector3.up * 0.05f, Quaternion.Euler(0f, Coords.YawToUnity(v.heading) - 90f, 0f));
                var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab); model.name = "Model"; model.transform.SetParent(root.transform, false);
                model.transform.localRotation = Quaternion.Euler(0f, v.name == "Car_Regent" ? -90f : 90f, 0f);   // glTF nose (+X) → root forward (+Z); the Regent was modelled nose -X
                float h = v.name.StartsWith("Arac_Otobus") ? 3f : v.name.StartsWith("Arac_") ? 2.2f : 1.5f;
                var bc = root.AddComponent<BoxCollider>(); bc.size = new Vector3(v.width, h, v.length); bc.center = new Vector3(0f, h * 0.5f + 0.05f, 0f);
                var vc = root.AddComponent<VehicleController>(); vc.length = v.length; vc.width = v.width; vc.isPlayerCar = v.player;
                int vl = LayerMask.NameToLayer("Vehicles"); if (vl >= 0) root.layer = vl;
                vc.displayName = DisplayName(v.name);
                if (v.name == "Car_Regent") vc.fallbackWheels = new[] { new Vector4(1.74f, 0.36f, 0.86f, 0.37f), new Vector4(1.74f, 0.36f, -0.86f, 0.37f), new Vector4(-1.2f, 0.36f, 0.86f, 0.37f), new Vector4(-1.2f, 0.36f, -0.86f, 0.37f) };   // baked wheels: overlay spinning ones (model space, X mirrored by the importer)
                if (v.name.StartsWith("Arac_Otobus")) { vc.maxSpeed = 12f; vc.acceleration = 4f; vc.wheelBase = 6f; }
                else if (v.name.StartsWith("Arac_")) { vc.maxSpeed = 14f; vc.acceleration = 5f; vc.wheelBase = 3.2f; }
                if (v.player) playerCar = vc;
                if (v.ai) { var drv = root.AddComponent<TrafficDriver>(); drv.obstacleMask = obstacleMask; drv.cruise = v.name.StartsWith("Arac_Otobus") ? 7f : 9f; trafficCount++; }
                else vCount++;
            }
            var traffic = graph.gameObject.AddComponent<TrafficManager>(); traffic.graph = graph; traffic.maxCars = 18;

            // --- player
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{CharsDir}/{PlayerModel}.glb");
            PlayerController pc = null; PlayerCamera cam = null;
            var homePos = Coords.ToUnity(city.home.x, city.home.y, city.home.z) + Vector3.up * 0.1f;
            float homeYaw = -90f;  // face the plateau road (three.js +X = Unity -X)
            if (playerPrefab)
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
                var player = new GameObject("Player"); player.tag = "Player";
                model.name = "Model"; model.transform.SetParent(player.transform, false);
                player.transform.SetPositionAndRotation(homePos, Quaternion.Euler(0f, homeYaw, 0f));
                var cc = player.AddComponent<CharacterController>(); cc.height = 1.8f; cc.radius = 0.32f; cc.center = new Vector3(0f, 0.92f, 0f); cc.stepOffset = 0.4f; cc.slopeLimit = 50f;
                var anim = player.AddComponent<CharacterAnimator>();
                pc = player.AddComponent<PlayerController>(); pc.animator = anim;
                player.AddComponent<VehicleInteraction>();
                var pbody = player.AddComponent<MafiaSandbox.Combat.CharacterBody>(); pbody.isPlayer = true; pbody.maxHp = 10; pbody.hp = 10;
                var combat = player.AddComponent<MafiaSandbox.Combat.PlayerCombat>();
                player.AddComponent<Vitals>(); player.AddComponent<MafiaSandbox.Economy.PlayerInteraction>();
                int chLayer = LayerMask.NameToLayer("Characters"); if (chLayer >= 0) player.layer = chLayer;
                combat.shootMask = chLayer >= 0 ? ~(1 << chLayer) : ~0;
                if (!model.GetComponentInChildren<Animation>()) Debug.LogWarning("[MafiaSandbox] Player model has no Legacy Animation component — run MafiaSandbox ▸ 1. Setup first (then rebuild the scene).");
            }
            else Debug.LogError($"[MafiaSandbox] {CharsDir}/{PlayerModel}.glb missing");

            // --- camera, light, managers
            var mainCam = Camera.main ? Camera.main : new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            mainCam.tag = "MainCamera"; mainCam.farClipPlane = 600f; mainCam.nearClipPlane = 0.08f; mainCam.fieldOfView = 60f;
            cam = mainCam.gameObject.GetComponent<PlayerCamera>(); if (!cam) cam = mainCam.gameObject.AddComponent<PlayerCamera>();
            if (pc) { cam.target = pc.transform; pc.cameraTransform = mainCam.transform; pc.GetComponent<VehicleInteraction>().playerCamera = cam; }
            { int chL = LayerMask.NameToLayer("Characters"), hbL = LayerMask.NameToLayer("Hitbox"); int mask = ~0; if (chL >= 0) mask &= ~(1 << chL); if (hbL >= 0) mask &= ~(1 << hbL); cam.collisionMask = mask; }

            var sun = Object.FindAnyObjectByType<Light>();
            if (!sun) sun = new GameObject("Directional Light", typeof(Light)).GetComponent<Light>();
            sun.type = LightType.Directional; sun.shadows = LightShadows.Soft; sun.shadowStrength = 0.8f;
            if (!sun.GetComponent<DayNightCycle>()) sun.gameObject.AddComponent<DayNightCycle>();
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogStartDistance = 120f; RenderSettings.fogEndDistance = 420f;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;

            // --- pedestrians (placeholder wander brain; full AI in step 3)
            var charPrefabs = new List<GameObject>();
            foreach (var file in Directory.GetFiles(CharsDir, "*.glb")) { var pf = AssetDatabase.LoadAssetAtPath<GameObject>(file.Replace('\\', '/')); if (pf) charPrefabs.Add(pf); }
            var spawner = new GameObject("NpcSpawner").AddComponent<MafiaSandbox.Npc.NpcSpawner>(); spawner.transform.SetParent(world, false);
            spawner.prefabs = charPrefabs.ToArray(); spawner.graph = graph;
            foreach (var bi in bRoot.GetComponentsInChildren<BuildingInfo>()) if (bi.kind == "police") spawner.policeStation = bi.door + (bi.door - bi.footprint.center).normalized * 3f;
            foreach (var p in city.props) if (p.name == "Insaat_Bina") spawner.gangCenter = Coords.ToUnity(p.pos);
            if (spawner.gangCenter == Vector3.zero) spawner.gangCenter = Coords.ToUnity(25f, 0f, -104f);

            // --- navmesh: baked from the physics colliders on the Default layer (ground/roads/bridges mesh + wall boxes); vehicles carve at runtime
            var navGo = new GameObject("NavMesh"); navGo.transform.SetParent(world, false);
            // road surfaces get NavMesh area 3 ("Road"): pedestrians pay a high cost there (NpcBrain), so routes follow the
            // sidewalks and cross at the junctions (crosswalks). The volume stops 8 m short of each junction so the
            // crossing itself is cheap. Fleeing / chasing NPCs drop the cost and cut straight across.
            {
                var volRoot = new GameObject("RoadAreas").transform; volRoot.SetParent(world, false); int nvol = 0;
                for (int i = 0; i < city.roadNodes.Length; i++) foreach (int j in city.roadNodes[i].adj ?? new int[0])
                {
                    if (j <= i) continue; var a = Coords.ToUnity(city.roadNodes[i].x, 0f, city.roadNodes[i].z); var b = Coords.ToUnity(city.roadNodes[j].x, 0f, city.roadNodes[j].z);
                    Vector3 d = b - a; d.y = 0f; float len = d.magnitude; if (len < 20f) continue; d /= len;
                    float inner = len - 16f; var go = new GameObject("Road " + i + "-" + j); go.transform.SetParent(volRoot, false);
                    go.transform.SetPositionAndRotation((a + b) * 0.5f + Vector3.up * 1f, Quaternion.LookRotation(d, Vector3.up));
                    var vol = go.AddComponent<NavMeshModifierVolume>(); vol.size = new Vector3(10f, 6f, inner); vol.center = Vector3.zero; vol.area = 3; nvol++;
                }
                // sidewalks (area 4, cheap) on both sides of every road, running through the junctions
                for (int i = 0; i < city.roadNodes.Length; i++) foreach (int j in city.roadNodes[i].adj ?? new int[0])
                {
                    if (j <= i) continue; var a = Coords.ToUnity(city.roadNodes[i].x, 0f, city.roadNodes[i].z); var b = Coords.ToUnity(city.roadNodes[j].x, 0f, city.roadNodes[j].z);
                    Vector3 d = b - a; d.y = 0f; float len = d.magnitude; if (len < 8f) continue; d /= len; var right = Vector3.Cross(Vector3.up, d);
                    foreach (float side in new[] { -1f, 1f })
                    {
                        var go = new GameObject("Sidewalk " + i + "-" + j); go.transform.SetParent(volRoot, false);
                        go.transform.SetPositionAndRotation((a + b) * 0.5f + right * side * 8.2f + Vector3.up * 1f, Quaternion.LookRotation(d, Vector3.up));
                        var vol = go.AddComponent<NavMeshModifierVolume>(); vol.size = new Vector3(6.4f, 6f, len + 16f); vol.center = Vector3.zero; vol.area = 4; nvol++;
                    }
                }
                Debug.Log("[MafiaSandbox] Road / sidewalk areas: " + nvol);
            }
            var surface = navGo.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All; surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = 1 << 0; surface.overrideVoxelSize = true; surface.voxelSize = 0.12f; surface.overrideTileSize = true; surface.tileSize = 128;
            try
            {
                surface.BuildNavMesh();
                const string navPath = "Assets/Scenes/City_NavMesh.asset";
                Directory.CreateDirectory("Assets/Scenes");
                if (AssetDatabase.LoadAssetAtPath<NavMeshData>(navPath)) AssetDatabase.DeleteAsset(navPath);
                if (surface.navMeshData) { AssetDatabase.CreateAsset(surface.navMeshData, navPath); EditorUtility.SetDirty(surface); }
                Debug.Log("[MafiaSandbox] NavMesh baked → " + navPath);
            }
            catch (System.Exception e) { Debug.LogError("[MafiaSandbox] NavMesh bake failed: " + e.Message); }

            var gm = new GameObject("GameManager").AddComponent<GameManager>();
            gm.player = pc; gm.playerCamera = cam; gm.playerCar = playerCar; gm.homePosition = homePos; gm.homeYaw = homeYaw; gm.policeStationPos = spawner.policeStation;
            gm.gameObject.AddComponent<SimpleHud>(); gm.gameObject.AddComponent<GameUI>(); var mm = gm.gameObject.AddComponent<Minimap>(); mm.worldMinX = -175f; mm.worldMaxX = 175f; gm.gameObject.AddComponent<MafiaSandbox.Factions.FactionManager>(); gm.gameObject.AddComponent<EmoteWheel>(); gm.gameObject.AddComponent<DebugPanel>();
            var atmo = new GameObject("Atmosphere"); atmo.AddComponent<Atmosphere>(); atmo.AddComponent<AnimalSpawner>();
            // economy: item prefabs (Esya_*) for shelves, loot and drops
            var eco = new GameObject("Economy").AddComponent<MafiaSandbox.Economy.EconomyManager>();
            var itemPrefabs = new List<GameObject>();
            foreach (var guid in AssetDatabase.FindAssets("Esya_", new[] { PropsDir })) { var path = AssetDatabase.GUIDToAssetPath(guid); if (!path.EndsWith(".glb")) continue; var pf = AssetDatabase.LoadAssetAtPath<GameObject>(path); if (pf) itemPrefabs.Add(pf); }
            eco.itemPrefabs = itemPrefabs.ToArray();

            // --- save
            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!scenes.Exists(s => s.path == ScenePath)) { scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true)); EditorBuildSettings.scenes = scenes.ToArray(); }

            string miss = missing.Count > 0 ? "\nMissing prop prefabs: " + string.Join(", ", missing) : "";
            Debug.Log($"[MafiaSandbox] City built: {staticMeshes} static meshes, {placed} props, {cols} colliders, {city.buildings.Length} buildings, {vCount} parked vehicles, {trafficCount} traffic cars. Saved {ScenePath}.{miss}");
        }

        static GameObject LoadProp(string name, Dictionary<string, GameObject> cache)
        {
            if (cache.TryGetValue(name, out var go)) return go;
            go = AssetDatabase.LoadAssetAtPath<GameObject>($"{PropsDir}/{name}.glb");
            cache[name] = go; return go;
        }

        static RoadGraph.Node[] Convert(GraphNode[] src)
        {
            if (src == null) return new RoadGraph.Node[0];
            var arr = new RoadGraph.Node[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                var adj = new List<int>(); if (src[i].adj != null) foreach (var a in src[i].adj) if (a >= 0 && a < src.Length) adj.Add(a);
                arr[i] = new RoadGraph.Node { pos = Coords.ToUnity(src[i].x, src[i].y, src[i].z), adj = adj.ToArray() };
            }
            return arr;
        }

        static void SetStaticRecursive(GameObject go, bool on)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = on;
        }

        static readonly Dictionary<string, string> Names = new Dictionary<string, string> {
            {"Araba_Sedan_Kirmizi","Red Sedan"},{"Araba_Sedan_Mavi","Blue Sedan"},{"Araba_Hatchback_Beyaz","White Hatchback"},{"Araba_Hatchback_Yesil","Green Hatchback"},
            {"Araba_SUV_Siyah","Black SUV"},{"Araba_SUV_Gri","Grey SUV"},{"Araba_Pickup_Turuncu","Orange Pickup"},{"Araba_Taksi","Taxi"},{"Arac_Polis","Police Cruiser"},
            {"Arac_Ambulans","Ambulance"},{"Arac_Van_Teal","Teal Van"},{"Arac_Otobus","Bus"},{"Car_Regent","Midnight Regent (your car)"},{"Motosiklet_Kirmizi","Red Motorbike"},{"Motosiklet_Siyah","Black Motorbike"} };
        static string DisplayName(string n) => Names.TryGetValue(n, out var d) ? d : n.Replace('_', ' ');
    }
}
