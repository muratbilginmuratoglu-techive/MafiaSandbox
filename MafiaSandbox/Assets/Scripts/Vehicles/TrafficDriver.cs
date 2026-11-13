using UnityEngine;
using MafiaSandbox.Combat;

namespace MafiaSandbox.Vehicles
{
    /// <summary>
    /// AI driver for a <see cref="VehicleController"/>: follows the road graph lane by lane (pure-pursuit steering on a
    /// right-hand lane offset), slows for corners, stops for anything in front (cars, pedestrians, the player), waits for a
    /// junction grant, U-turns at dead ends, backs out when wedged, and floors it for a few seconds after hearing gunfire.
    /// </summary>
    [RequireComponent(typeof(VehicleController))]
    public class TrafficDriver : MonoBehaviour
    {
        public float cruise = 9f;
        public LayerMask obstacleMask;        // set by CityBuilder: Vehicles | Characters | Hitbox | Default
        public bool debugDraw;
        public static bool ShowDebug;          // F6: on-screen state labels
        public string State = "";

        VehicleController vc; TrafficManager tm;
        int prev = -1, cur = -1, next = -1, after = -1, heldJunction = -1;
        float waitT, scareT, stuckT, reverseT, uturnT, blockedT, overtakeT, troubleT, honkT, pedBlockT, wallT, idleT;
        static readonly RaycastHit[] hits = new RaycastHit[8];

        bool wasControlled;

        void Start()
        {
            vc = GetComponent<VehicleController>(); tm = TrafficManager.I ? TrafficManager.I : FindAnyObjectByType<TrafficManager>();
            if (!tm || tm.Nodes == null || tm.Nodes.Length == 0) { enabled = false; return; }
            if (tm.drivers.Count >= tm.maxCars) { gameObject.SetActive(false); return; }   // surplus traffic stays parked in the garage
            // AI cars don't physically collide with each other (no pile-ups, no climbing); the sensor still sees them and
            // the player's car still hits them
            var myCols = GetComponentsInChildren<Collider>(true);
            foreach (var d in tm.drivers) if (d) foreach (var oc in d.GetComponentsInChildren<Collider>(true)) foreach (var mc in myCols) Physics.IgnoreCollision(mc, oc, true);
            tm.drivers.Add(this);
            WorldEvents.OnNoise += OnNoise;
            cruise *= Random.Range(0.9f, 1.1f);
            Reinit(); SnapToLane();
        }

        /// <summary>Teleport onto the right-hand lane of the current segment (spawn, or recovery when hopelessly off-track).</summary>
        void SnapToLane()
        {
            if (cur >= 0 && next >= 0 && cur != next)
            {
                Vector3 pa = tm.Nodes[cur].pos, pb = tm.Nodes[next].pos; Vector3 seg = pb - pa; seg.y = 0f; float len = Mathf.Max(0.01f, seg.magnitude);
                Vector3 rel = transform.position - pa; rel.y = 0f; float t = Mathf.Clamp01(Vector3.Dot(rel, seg / len) / len);
                Vector3 lp = LanePoint(cur, next, t); lp.y = transform.position.y;
                transform.SetPositionAndRotation(lp, Quaternion.LookRotation(seg / len, Vector3.up));
                var rb = GetComponent<Rigidbody>(); if (rb) { rb.position = lp; rb.rotation = transform.rotation; rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
                troubleT = 0f; blockedT = 0f; overtakeT = 0f; reverseT = 0f; stuckT = 0f;
            }
        }

        /// <summary>Out of sight, out of the jam: move the car to a random free lane spot far from the player (GTA-style respawn).</summary>
        bool Relocate()
        {
            var cam = Camera.main; Vector3 avoid = cam ? cam.transform.position : transform.position;
            if (!tm.RandomLaneSpot(avoid, 60f, out int a, out int b, out Vector3 p, out Quaternion r)) return false;
            if (heldJunction >= 0) { tm.Release(heldJunction, this); heldJunction = -1; }
            var rb = GetComponent<Rigidbody>();
            p.y += 0.2f; transform.SetPositionAndRotation(p, r); if (rb) { rb.position = p; rb.rotation = r; rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            prev = -1; cur = a; next = b; after = tm.NextNode(next, cur);
            troubleT = 0f; blockedT = 0f; overtakeT = 0f; reverseT = 0f; stuckT = 0f; idleT = 0f; vc.SetInput(0f, 0f, true);
            return true;
        }

        /// <summary>Nobody is watching this car (far from the player or behind the camera)?</summary>
        bool Unseen()
        {
            var cam = Camera.main; if (!cam) return true;
            Vector3 d = transform.position - cam.transform.position; if (d.sqrMagnitude > 70f * 70f) return true;
            return Vector3.Dot(cam.transform.forward, d.normalized) < 0.2f;
        }

        /// <summary>Snap the route to wherever the car is now (start, or after the player jacked it and got out).</summary>
        void Reinit()
        {
            prev = -1; heldJunction = -1; uturnT = 0f; reverseT = 0f; stuckT = 0f; blockedT = 0f; overtakeT = 0f;
            cur = tm.Nearest(transform.position);
            // next: the neighbour best aligned with our heading
            var n = tm.Nodes[cur]; float best = -2f; next = n.adj != null && n.adj.Length > 0 ? n.adj[0] : cur;
            if (n.adj != null) foreach (var a in n.adj) { float d = Vector3.Dot(transform.forward, (tm.Nodes[a].pos - n.pos).normalized); if (d > best) { best = d; next = a; } }
            // if we're already past `cur` along the way to `next`, treat cur as prev
            Vector3 seg = tm.Nodes[next].pos - tm.Nodes[cur].pos;
            if (Vector3.Dot(transform.position - tm.Nodes[cur].pos, seg) > 0.5f) { prev = cur; }
            after = tm.NextNode(next, cur);
            if (tm.IsJunction(cur) && Vector3.Distance(transform.position, tm.Nodes[cur].pos) < 7f && tm.Request(cur, this)) heldJunction = cur;
        }

        void OnGUI()
        {
            if (!ShowDebug || !Camera.main) return;
            Vector3 sp = Camera.main.WorldToScreenPoint(transform.position + Vector3.up * 1.8f);
            if (sp.z < 0f || sp.z > 80f) return;
            var st = new GUIStyle(GUI.skin.label) { fontSize = 13, normal = { textColor = Color.yellow } };
            GUI.Label(new Rect(sp.x - 90f, Screen.height - sp.y - 10f, 260f, 20f), State, st);
        }

        void OnDestroy() { WorldEvents.OnNoise -= OnNoise; if (tm) tm.drivers.Remove(this); }

        void OnNoise(Vector3 pos, float radius, GameObject src, bool melee)
        {
            if (melee) return;
            if ((pos - transform.position).sqrMagnitude < 25f * 25f) scareT = 6f;
        }

        Vector3 LanePoint(int a, int b, float t)
        {
            Vector3 pa = tm.Nodes[a].pos, pb = tm.Nodes[b].pos; Vector3 dir = (pb - pa); dir.y = 0f; dir.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, dir);
            float off = overtakeT > 0f ? -tm.laneOffset * 0.6f : tm.laneOffset;   // overtaking: squeeze past on the left of the blocker
            return Vector3.Lerp(pa, pb, t) + right * off;
        }

        void Update()
        {
            if (vc.Controlled) { wasControlled = true; if (heldJunction >= 0) { tm.Release(heldJunction, this); heldJunction = -1; } return; }
            if (wasControlled) { wasControlled = false; Reinit(); }
            float dt = Time.deltaTime; scareT -= dt;
            if (transform.position.y < -15f) { Reinit(); SnapToLane(); var rbF = GetComponent<Rigidbody>(); if (rbF) rbF.position = new Vector3(rbF.position.x, tm.Nodes[cur].pos.y + 0.3f, rbF.position.z); return; }   // fell through the world
            if (cur < 0 || next < 0 || cur == next) { vc.SetInput(0f, 0f, true); return; }

            Vector3 pa = tm.Nodes[cur].pos, pb = tm.Nodes[next].pos; Vector3 seg = pb - pa; seg.y = 0f; float segLen = seg.magnitude; Vector3 segDir = seg / Mathf.Max(0.01f, segLen);
            Vector3 rel = transform.position - pa; rel.y = 0f; float along = Vector3.Dot(rel, segDir);

            // advance to the next segment once we pass the node (or get very close to it)
            float reach = tm.IsDeadEnd(next) ? 5f : 2.5f;
            if (along > segLen - (reach - 1f) || Vector3.Distance(transform.position, pb) < reach)
            {
                int nn = after >= 0 ? after : tm.NextNode(next, cur);
                prev = cur; cur = next; next = nn; after = tm.NextNode(next, cur);
                if (heldJunction >= 0 && heldJunction != cur) { tm.Release(heldJunction, this); heldJunction = -1; }
                uturnT = (nn == prev) ? 6f : 0f;      // dead end: turn around
                if (nn == prev && Unseen()) { SnapToLane(); uturnT = 0f; vc.SetInput(0f, 0f, true); return; }   // nobody's looking: just flip onto the opposite lane
                pa = tm.Nodes[cur].pos; pb = tm.Nodes[next].pos; seg = pb - pa; seg.y = 0f; segLen = seg.magnitude; segDir = seg / Mathf.Max(0.01f, segLen); rel = transform.position - pa; rel.y = 0f; along = Vector3.Dot(rel, segDir);
            }
            if (heldJunction == cur && Vector3.Distance(transform.position, tm.Nodes[cur].pos) > 7f) { tm.Release(heldJunction, this); heldJunction = -1; }

            // pursuit target on the lane, a few metres ahead (further when fast)
            float look = 4f + Mathf.Abs(vc.Speed) * 0.6f;
            float tAhead = Mathf.Clamp01((along + look) / Mathf.Max(0.01f, segLen));
            Vector3 target = LanePoint(cur, next, tAhead);
            if (tAhead >= 1f && tm.Nodes[next].adj != null && tm.Nodes[next].adj.Length > 1)
            {
                // peek into the following segment so corners are cut smoothly
                if (after >= 0 && after != cur) { float extra = (along + look) - segLen; target = LanePoint(next, after, Mathf.Clamp01(extra / Mathf.Max(0.01f, Vector3.Distance(tm.Nodes[next].pos, tm.Nodes[after].pos)))); }
            }

            // desired speed: corners, junction grants, obstacles, scare
            float desired = cruise * (scareT > 0f ? 1.6f : 1f);
            float distNext = Vector3.Distance(transform.position, pb);
            if (tm.Nodes[next].adj != null && tm.Nodes[next].adj.Length > 1 && distNext < 14f)
            {
                if (after >= 0 && after != next) { float turn = Vector3.Angle(segDir, (tm.Nodes[after].pos - pb).normalized); desired = Mathf.Min(desired, Mathf.Lerp(cruise, 3.5f, Mathf.Clamp01(turn / 90f))); }
            }
            if (tm.IsJunction(next) && distNext < 11f)
            {
                if (heldJunction != next)
                {
                    if (tm.Request(next, this)) { heldJunction = next; waitT = 0f; }
                    else { waitT += dt; if (distNext < 7.5f) desired = 0f; }
                }
            }
            if (tm.IsDeadEnd(next) && distNext < 14f) desired = Mathf.Min(desired, 3f);   // dead end ahead: crawl, turn around before the road runs out
            if (uturnT > 0f) { uturnT -= dt; desired = Mathf.Min(desired, 3f); }

            // obstacles ahead: box cast from the bumper
            float sense = 3f + Mathf.Abs(vc.Speed) * 1.1f;
            Vector3 origin = transform.position + Vector3.up * 0.95f + transform.forward * (vc.length * 0.5f - 0.3f);
            int n = Physics.BoxCastNonAlloc(origin, new Vector3(vc.width * 0.5f + 0.15f, 0.5f, 0.3f), transform.forward, hits, transform.rotation, sense, obstacleMask, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue; string obsName = "-"; bool yieldToOncoming = false, queued = false, wallAhead = false; CharacterBody pedAhead = null;
            for (int i = 0; i < n; i++)
            {
                var h = hits[i]; if (h.collider.transform.IsChildOf(transform)) continue;
                if (h.normal.y > 0.55f) continue;                                                                           // ground, kerbs, the hill ramp: not obstacles
                var body = h.collider.GetComponentInParent<CharacterBody>(); if (body && body.Dead && body.Ragdolled) continue;   // drive over the fallen, not the living
                if (h.distance < nearest)
                {
                    nearest = h.distance; obsName = h.collider.name + "/" + LayerMask.LayerToName(h.collider.gameObject.layer); pedAhead = body;
                    // head-on with another AI car: only one of the pair manoeuvres, the other (higher id) just waits
                    var other = h.collider.GetComponentInParent<TrafficDriver>();
                    float facing = other ? Vector3.Dot(transform.forward, other.transform.forward) : 0f;
                    yieldToOncoming = other && other.enabled && !other.vc.Controlled && facing < -0.3f && GetEntityId() > other.GetEntityId();
                    queued = other && facing > 0.5f;                                                                       // same direction: it's a queue, be patient
                    wallAhead = !other && !body && h.collider.gameObject.layer == 0 && !h.collider.GetComponent<MafiaSandbox.World.CityCollider>() && h.normal.y < 0.55f;   // static world geometry
                }
            }
            bool blockedByObstacle = false;
            if (nearest < float.MaxValue) { float lim = nearest < 2.2f ? 0f : Mathf.Lerp(0f, cruise, (nearest - 2.2f) / 9f); if (lim < desired) { desired = lim; blockedByObstacle = lim < 0.5f; } }
            // standing behind something for a while (head-on with another car, a parked car in the lane): back up a bit with the nose
            // swinging right, then pass on the left for a few seconds
            overtakeT -= dt;
            if (blockedByObstacle && Mathf.Abs(vc.Speed) < 0.3f && reverseT <= 0f) blockedT += dt; else blockedT = Mathf.Max(0f, blockedT - dt);
            // a pedestrian standing in the lane: honk after a moment and they step aside
            honkT -= dt;
            if (pedAhead && blockedByObstacle) { pedBlockT += dt; if (pedBlockT > 1.5f && honkT <= 0f) { honkT = 2.5f; var brain = pedAhead.GetComponent<MafiaSandbox.Npc.NpcBrain>(); if (brain) brain.Honk(transform.position, transform.forward); MafiaSandbox.Core.Sfx.At("horn", transform.position + transform.forward * 2f); } }
            else pedBlockT = 0f;
            if (pedAhead) blockedT = Mathf.Min(blockedT, 2f);                                                               // don't ram around people, wait for them
            if (blockedT > (queued ? 12f : 4f)) { blockedT = 0f; if (!yieldToOncoming) { reverseT = 1.1f; overtakeT = 7f; } }
            Vector3 lanePosFor(float al) { var lp = LanePoint(cur, next, Mathf.Clamp01(al / Mathf.Max(0.01f, segLen))); lp.y = transform.position.y; return lp; }
            // a wall across our own lane while we're nicely aligned with the road: this edge isn't drivable, drop it and turn back
            if (wallAhead && blockedByObstacle && Vector3.Dot(transform.forward, segDir) > 0.95f
                && Vector3.Distance(lanePosFor(along), transform.position) < 1.5f
                && Physics.Raycast(transform.position + Vector3.up * 2.2f, transform.forward, out var tall, vc.length * 0.5f + 3.5f, 1 << 0, QueryTriggerInteraction.Ignore) && tall.normal.y < 0.3f && !tall.collider.GetComponent<MafiaSandbox.World.CityCollider>())
                wallT += dt;   // tall wall straight across our lane (not a kerb, step or ramp edge)
            else wallT = 0f;
            if (wallT > 4f) { wallT = 0f; tm.BlockEdge(cur, next); int wallNode = next; next = cur; cur = wallNode; prev = -1; after = tm.NextNode(next, cur); if (heldJunction >= 0) { tm.Release(heldJunction, this); heldJunction = -1; } SnapToLane(); return; }   // turn around in place, on the opposite lane
            if (vc.WallT > 1.5f && reverseT <= 0f) { reverseT = 1.2f; overtakeT = 5f; troubleT += 3f; }                      // nosed into a wall/car the sensor can't see: back out
            // hopelessly off the road or wedged for long: when nobody is looking, quietly put the car back on its lane
            Vector3 lanePos = LanePoint(cur, next, Mathf.Clamp01(along / Mathf.Max(0.01f, segLen))); lanePos.y = transform.position.y;
            bool offTrack = (lanePos - transform.position).sqrMagnitude > 7f * 7f;
            if (offTrack || (Mathf.Abs(vc.Speed) < 0.3f && desired > 0.5f) || blockedByObstacle) troubleT += dt; else troubleT = Mathf.Max(0f, troubleT - dt * 2f);
            // jammed for long (whatever the reason) and nobody's looking: respawn elsewhere, else at least straighten up on the lane
            if (Mathf.Abs(vc.Speed) < 0.5f) idleT += dt; else idleT = Mathf.Max(0f, idleT - dt * 3f);
            if ((troubleT > 10f || idleT > 20f) && Unseen()) { if (Relocate()) return; troubleT = 5f; idleT = 12f; }

            // steering
            Vector3 to = target - transform.position; to.y = 0f;
            float ang = Vector3.SignedAngle(transform.forward, to, Vector3.up);
            float steer = Mathf.Clamp(ang / 30f, -1f, 1f);
            if (uturnT > 0f && Mathf.Abs(ang) > 60f) steer = ang > 0f ? 1f : -1f;

            // stuck → back out for a moment
            float speed = vc.Speed;
            if (desired > 1.5f && Mathf.Abs(speed) < 0.3f) stuckT += dt; else stuckT = Mathf.Max(0f, stuckT - dt);
            if (stuckT > 5f) { reverseT = 1.6f; stuckT = 0f; }
            float throttle; bool brake = false;
            if (reverseT > 0f) { reverseT -= dt; throttle = -1f; steer = overtakeT > 0f ? -0.7f : -steer; }
            else if (desired <= 0.05f) { throttle = 0f; brake = Mathf.Abs(speed) < 2f; if (speed > 0.5f) throttle = -1f; }
            else throttle = desired > speed + 0.4f ? 1f : desired < speed - 0.6f ? -0.6f : 0.15f;
            vc.SetInput(throttle, steer, brake);
            State = $"{cur}>{next}>{after} v{speed:0.0} want{desired:0.0} bl{blockedT:0} ov{overtakeT:0} tr{troubleT:0} obs{(nearest < float.MaxValue ? nearest.ToString("0.0") + ":" + obsName : "-")} j{(heldJunction >= 0 ? "H" : tm.IsJunction(next) && distNext < 11f ? "W" + waitT.ToString("0") : "-")} st{stuckT:0} rv{reverseT:0.0} ut{uturnT:0}";
            if (debugDraw) { Debug.DrawLine(transform.position + Vector3.up, target + Vector3.up, Color.yellow); Debug.DrawRay(origin, transform.forward * sense, nearest < float.MaxValue ? Color.red : Color.green); }
        }
    }
}
