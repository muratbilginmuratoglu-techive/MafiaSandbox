using UnityEngine;

namespace MafiaSandbox.Vehicles
{
    /// <summary>
    /// Arcade car on a Rigidbody: W/S throttle & reverse, A/D steer, Space handbrake. Forward is the root's +Z
    /// (CityBuilder parents the GLB model with a 90° yaw so its glTF +X nose points down +Z).
    /// Speeds match the prototype: ~17 m/s top speed (≈60 km/h), vans/buses a bit slower.
    /// Traffic AI drives these through <see cref="SetInput"/> in step 4.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class VehicleController : MonoBehaviour
    {
        public string displayName = "Car";
        public bool isPlayerCar;
        public float maxSpeed = 17f;
        public float maxReverse = 6f;
        public float acceleration = 7f;
        public float brakeDecel = 16f;
        public float coastDecel = 2.5f;
        public float maxSteerDeg = 32f;
        public float wheelBase = 2.7f;
        public float length = 4.4f, width = 1.8f;

        public bool Controlled { get; private set; }
        public float Speed => speed;             // signed, m/s along forward

        Rigidbody rb; bool lifted; int groundMask; float fallV; static bool layersSet; static readonly Collider[] sweepHits = new Collider[32]; readonly System.Collections.Generic.Dictionary<MafiaSandbox.Combat.CharacterBody, float> hitAt = new System.Collections.Generic.Dictionary<MafiaSandbox.Combat.CharacterBody, float>(); int pedMask = -1;
        float speed, throttle, steer; bool handbrake;
        Transform[] wheels; Vector3[] wheelAxes;  // optional: nodes named Wheel_* spin for show (axle axis per wheel)

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            rb.mass = 1200f; rb.linearDamping = 0.05f; rb.angularDamping = 2f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            rb.centerOfMass = new Vector3(0f, -0.3f, 0f);
            // navmesh: carve a hole so pedestrians walk around (parked or driving)
            var obs = GetComponent<UnityEngine.AI.NavMeshObstacle>(); if (!obs) obs = gameObject.AddComponent<UnityEngine.AI.NavMeshObstacle>();
            obs.shape = UnityEngine.AI.NavMeshObstacleShape.Box; obs.size = new Vector3(width + 0.3f, 1.5f, length + 0.3f); obs.center = new Vector3(0f, 0.75f, 0f); obs.carving = true; obs.carveOnlyStationary = false; obs.carvingMoveThreshold = 0.3f;
            int vl = LayerMask.NameToLayer("Vehicles"); if (vl >= 0) gameObject.layer = vl;
            if (!layersSet) { layersSet = true; int ch = LayerMask.NameToLayer("Characters"), hb = LayerMask.NameToLayer("Hitbox"); if (vl >= 0 && ch >= 0) Physics.IgnoreLayerCollision(vl, ch, true); if (vl >= 0 && hb >= 0) Physics.IgnoreLayerCollision(vl, hb, true); }   // NPC bodies never stop a car; hits come from the sweep below
            // The city's road decals (crosswalks, lane paint) are thin one-sided planes a few cm above the asphalt, and a box
            // collider resting on the asphalt catches their edges like a wall. So cars don't touch the ground at all: the
            // collider floats 35 cm up and the car rides on a downward raycast (see FixedUpdate).
            var box = GetComponent<BoxCollider>(); if (box && !lifted) { box.center += Vector3.up * 0.5f; box.size = new Vector3(box.size.x, Mathf.Max(0.8f, box.size.y - 0.4f), box.size.z); lifted = true; }   // clears ramps and kerb lips; cars ride on raycasts
            rb.useGravity = false; rb.maxDepenetrationVelocity = 2f;
            groundMask = 1 << 0;   // Default: roads, terrain, ramps
            SetupWheels();
            body = transform.Find("Model"); if (body) bodyBaseRot = body.localRotation;
        }

        Transform body; Quaternion bodyBaseRot = Quaternion.identity; float bodyPitch;
        Transform[] steerPivots; bool[] isFront;

        /// <summary>
        /// Wheel nodes (Wheel_*) spin around their axle and the front pair steers. The axle is found from the wheel mesh
        /// itself (the thinnest axis of its bounds) so it does not depend on how the importer oriented the nodes; each
        /// wheel is re-parented under a pivot so steering (pivot yaw) and rolling (wheel spin) don't fight.
        /// </summary>
        /// <summary>Model-space wheel centres (x, y, z) + radius for models whose wheels are baked into the body mesh (the Regent).</summary>
        public Vector4[] fallbackWheels;

        void SetupWheels()
        {
            var model = transform.Find("Model"); var filters = GetComponentsInChildren<MeshFilter>();
            if (fallbackWheels != null && fallbackWheels.Length > 0 && model && filters.Length == 1)
            {
                model = filters[0].transform;   // mesh space: the importer may nest the mesh under an extra node
                // procedural wheels laid over the baked ones: tyre + hub, slightly bigger so they cover the static ones
                var cyl = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
                var tyre = new Material(Shader.Find("Standard")) { color = new Color(0.08f, 0.08f, 0.09f) }; tyre.SetFloat("_Glossiness", 0.2f);
                var hub = new Material(Shader.Find("Standard")) { color = new Color(0.75f, 0.76f, 0.78f) }; hub.SetFloat("_Glossiness", 0.7f); hub.SetFloat("_Metallic", 0.8f);
                int i = 0;
                foreach (var w in fallbackWheels)
                {
                    var g = new GameObject("Wheel_F" + (i++)); g.transform.SetParent(model, false); g.transform.localPosition = new Vector3(w.x, w.y, w.z);
                    g.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // cylinder axis (local Y) → model Z (the axle)
                    var mf = g.AddComponent<MeshFilter>(); mf.sharedMesh = cyl; var mr = g.AddComponent<MeshRenderer>(); mr.sharedMaterial = tyre;
                    g.transform.localScale = new Vector3(w.w * 2f, 0.14f, w.w * 2f);
                    var h = new GameObject("Hub"); h.transform.SetParent(g.transform, false); h.transform.localScale = new Vector3(0.6f, 1.02f, 0.6f);
                    var hf = h.AddComponent<MeshFilter>(); hf.sharedMesh = cyl; var hr = h.AddComponent<MeshRenderer>(); hr.sharedMaterial = hub;
                }
            }
            var list = new System.Collections.Generic.List<Transform>(); var axes = new System.Collections.Generic.List<Vector3>(); var pivots = new System.Collections.Generic.List<Transform>(); var fronts = new System.Collections.Generic.List<bool>();
            float maxFwd = 0f; var cand = new System.Collections.Generic.List<Transform>();
            foreach (var t in GetComponentsInChildren<Transform>()) if (t.name.StartsWith("Wheel_") && t.GetComponent<MeshFilter>()) cand.Add(t);
            foreach (var t in cand) maxFwd = Mathf.Max(maxFwd, Mathf.Abs(transform.InverseTransformPoint(t.position).z));
            foreach (var t in cand)
            {
                var mesh = t.GetComponent<MeshFilter>().sharedMesh; Vector3 ext = mesh ? Vector3.Scale(mesh.bounds.extents, t.localScale) : new Vector3(1, 1, 0.1f);
                Vector3 axle = ext.x <= ext.y && ext.x <= ext.z ? Vector3.right : ext.y <= ext.z ? Vector3.up : Vector3.forward;
                // make the spin direction consistent with the car's right vector
                if (Vector3.Dot(t.TransformDirection(axle), transform.right) < 0f) axle = -axle;
                var pivot = new GameObject(t.name + "_Pivot").transform;
                pivot.SetParent(t.parent, false); pivot.SetPositionAndRotation(t.position, t.rotation); pivot.localScale = Vector3.one;
                t.SetParent(pivot, true);
                list.Add(t); axes.Add(axle); pivots.Add(pivot);
                fronts.Add(transform.InverseTransformPoint(t.position).z > maxFwd * 0.5f);
            }
            wheels = list.ToArray(); wheelAxes = axes.ToArray(); steerPivots = pivots.ToArray(); isFront = fronts.ToArray();
            if (isPlayerCar) { var sb = new System.Text.StringBuilder("[MafiaSandbox] player car wheels: "); for (int k = 0; k < wheels.Length; k++) sb.Append(wheels[k].name).Append(transform.InverseTransformPoint(wheels[k].position).ToString("F2")).Append(isFront[k] ? "F " : "R "); Debug.Log(sb.ToString()); }
        }

        AudioSource engine;
        public void SetControlled(bool on)
        {
            Controlled = on; if (!on) { throttle = 0f; steer = 0f; handbrake = true; StopNow(); }
            if (on && !engine && MafiaSandbox.Core.Sfx.I) engine = MafiaSandbox.Core.Sfx.I.Loop("engine", 0.35f, true, transform);
            if (engine) { engine.mute = !on; }
        }
        public void SetInput(float throttle01, float steer01, bool brake) { throttle = Mathf.Clamp(throttle01, -1f, 1f); steer = Mathf.Clamp(steer01, -1f, 1f); handbrake = brake; }

        void Update()
        {
            if (engine && Controlled) { float k = Mathf.Abs(speed) / Mathf.Max(1f, maxSpeed); engine.pitch = Mathf.Lerp(engine.pitch, 0.65f + k * 1.3f + (throttle != 0f ? 0.1f : 0f), Time.deltaTime * 4f); engine.volume = 0.3f + k * 0.25f; }
            if (Controlled)
            {
                throttle = Input.GetAxisRaw("Vertical");
                steer = Input.GetAxisRaw("Horizontal");
                handbrake = Input.GetKey(KeyCode.Space);
            }
            // wheels: roll with the ground speed, front pair follows the steering input
            if (wheels != null)
            {
                float deg = speed / 0.34f * Mathf.Rad2Deg * Time.deltaTime;
                float steerDeg = -steer * maxSteerDeg * 0.9f;
                for (int i = 0; i < wheels.Length; i++)
                {
                    if (Mathf.Abs(speed) > 0.05f) wheels[i].Rotate(wheelAxes[i], deg, Space.Self);
                    if (isFront[i]) { var pv = steerPivots[i]; Vector3 upLocal = pv.InverseTransformDirection(transform.up); pv.localRotation = Quaternion.Slerp(pv.localRotation, Quaternion.AngleAxis(steerDeg, upLocal), Time.deltaTime * 8f); }
                }
            }
            // body follows the slope (the rigidbody itself stays upright; only the visual tilts)
            if (body)
            {
                var target = Quaternion.AngleAxis(bodyPitch, Vector3.right) * bodyBaseRot;
                body.localRotation = Quaternion.Slerp(body.localRotation, target, Time.deltaTime * 6f);
            }
        }

        /// <summary>Seconds we've been pushing into something solid without moving (AI uses this to back out).</summary>
        public float WallT { get; private set; }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            { Vector3 was = rb.linearVelocity; was.y = 0f; if (Mathf.Abs(speed) > 1f && was.magnitude < 0.35f && Time.time - LastContactT < 0.25f) WallT += dt; else WallT = 0f; }
            // longitudinal
            if (handbrake) speed = Mathf.MoveTowards(speed, 0f, brakeDecel * dt);
            else if (Mathf.Abs(throttle) > 0.01f)
            {
                bool braking = Mathf.Sign(throttle) != Mathf.Sign(speed) && Mathf.Abs(speed) > 0.3f;
                if (braking) speed = Mathf.MoveTowards(speed, 0f, brakeDecel * dt);
                else speed = Mathf.MoveTowards(speed, throttle > 0f ? maxSpeed : -maxReverse, acceleration * dt);
            }
            else speed = Mathf.MoveTowards(speed, 0f, coastDecel * dt);

            // steering (bicycle-ish): yaw rate from steer angle and speed
            float steerAngle = steer * maxSteerDeg * Mathf.Lerp(1f, 0.45f, Mathf.Abs(speed) / maxSpeed);
            float yawRate = speed / wheelBase * Mathf.Tan(steerAngle * Mathf.Deg2Rad) * Mathf.Rad2Deg; // deg/s
            if (Mathf.Abs(speed) > 0.05f) rb.MoveRotation(rb.rotation * Quaternion.Euler(0f, yawRate * dt, 0f));

            // ground follow: ride on a raycast instead of resting on the mesh (see Awake)
            float vy; float gy = float.MinValue; float half = length * 0.5f - 0.4f; Vector3 fwd = transform.forward * half;
            float yNose = float.NaN, yTail = float.NaN;
            foreach (var off in new[] { Vector3.zero, fwd, -fwd })   // centre, nose and tail
                if (Physics.Raycast(rb.position + off + Vector3.up * 1.6f, Vector3.down, out var gh, 8f, groundMask, QueryTriggerInteraction.Ignore))
                {
                    if (gh.point.y > gy) { gy = gh.point.y; DebugGround = gh.collider.name + " y" + gy.ToString("F2"); }
                    if (off == fwd) yNose = gh.point.y; else if (off == -fwd) yTail = gh.point.y;
                }
            if (!float.IsNaN(yNose) && !float.IsNaN(yTail))
            {
                gy = (yNose + yTail) * 0.5f;                                   // sit on the slope instead of hovering at the high end
                bodyPitch = -Mathf.Atan2(yNose - yTail, 2f * half) * Mathf.Rad2Deg;
            }
            else bodyPitch = Mathf.MoveTowards(bodyPitch, 0f, 60f * dt);
            if (gy > float.MinValue)
            {
                float dy = gy - rb.position.y;
                float ny = Mathf.Abs(dy) > 2.5f ? rb.position.y + Mathf.Sign(dy) * 14f * dt                        // fell off something / teleported: catch up fast
                         : rb.position.y + dy * Mathf.Clamp01(1f - Mathf.Exp(-9f * dt));                           // otherwise glide onto the slope, no steps
                vy = (ny - rb.position.y) / dt; fallV = 0f;
            }
            else { fallV -= 9.81f * dt; vy = fallV; }
            Vector3 planar = transform.forward * speed;
            rb.linearVelocity = new Vector3(planar.x, vy, planar.z);
            SweepPedestrians(dt);
        }

        /// <summary>Running people over without the physics ever touching them: a box just ahead of the bumper finds bodies, they get hit and fly, the car doesn't notice.</summary>
        void SweepPedestrians(float dt)
        {
            float v = Mathf.Abs(speed); if (v < 2.5f) return;
            if (pedMask < 0) { int ch = LayerMask.NameToLayer("Characters"), hb = LayerMask.NameToLayer("Hitbox"), pl = LayerMask.NameToLayer("Player"); pedMask = (ch >= 0 ? 1 << ch : 0) | (hb >= 0 ? 1 << hb : 0) | (pl >= 0 ? 1 << pl : 0); }
            Vector3 dirF = transform.forward * Mathf.Sign(speed); float reach = 0.5f + v * dt * 2f;
            Vector3 c = rb.position + dirF * (length * 0.5f + reach * 0.5f) + Vector3.up * 0.9f;
            int n = Physics.OverlapBoxNonAlloc(c, new Vector3(width * 0.5f + 0.15f, 0.9f, reach * 0.5f + 0.3f), sweepHits, transform.rotation, pedMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var body = sweepHits[i].GetComponentInParent<MafiaSandbox.Combat.CharacterBody>(); if (!body || body.Dead) continue;
                if (hitAt.TryGetValue(body, out float t) && Time.time - t < 1.5f) continue; hitAt[body] = Time.time;
                var vi = body.GetComponent<VehicleInteraction>(); if (vi && vi.Current) continue;   // the driver of another car
                Vector3 dir = body.transform.position - rb.position; dir.y = 0f; dir = (dir.normalized * 0.6f + dirF * 0.8f + Vector3.up * 0.35f).normalized;
                Vector3 contact = body.ChestPos;
                body.Hit(body.ZoneNear(contact), contact, dir, v > 7f ? 99 : 1, v > 7f, Mathf.Clamp(v * 0.8f, 2f, 13f), Controlled ? "player" : "vehicle");
                MafiaSandbox.Core.Sfx.At("thud", contact);
                StartCoroutine(PassThrough(body, 2.5f));
            }
        }

        /// <summary>Stop dead (used when the driver bails out so the car can't roll over them).</summary>
        public void StopNow() { speed = 0f; throttle = 0f; handbrake = true; if (rb) rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f); }

        /// <summary>Ignore collisions between this car and a character for a while (after a hit), so the solver doesn't stop the car on the capsule.</summary>
        System.Collections.IEnumerator PassThrough(MafiaSandbox.Combat.CharacterBody body, float seconds)
        {
            var mine = GetComponentsInChildren<Collider>(); var theirs = body.GetComponentsInChildren<Collider>(true);
            foreach (var a in mine) foreach (var b in theirs) if (a && b) Physics.IgnoreCollision(a, b, true);
            yield return new WaitForSeconds(seconds);
            foreach (var a in mine) foreach (var b in theirs) if (a && b) Physics.IgnoreCollision(a, b, false);
        }

        public string LastContact = "-"; public float LastContactT;
        public Vector3 RbVelocity => rb ? rb.linearVelocity : Vector3.zero;
        public string DebugGround = "-";
        void OnCollisionStay(Collision c)
        {
            var n = c.GetContact(0).normal; LastContact = c.collider.name + "/" + LayerMask.LayerToName(c.collider.gameObject.layer) + " n" + n.ToString("F2"); LastContactT = Time.time;
            BlockInto(c);
        }

        /// <summary>
        /// We drive by setting the velocity directly, so pressing into a wall would make the solver fight us every step
        /// (the "glitching" against props). Kill the velocity component that points into the contact and nudge out.
        /// </summary>
        void BlockInto(Collision c)
        {
            if (c.collider.GetComponentInParent<MafiaSandbox.Combat.CharacterBody>() || c.collider.GetComponentInParent<MafiaSandbox.World.Animal>()) return;
            var ct = c.GetContact(0); var n = ct.normal; if (n.y > 0.4f || n.y < -0.55f) return;   // floors / ramps / roofs are not walls
            if (ct.point.y < rb.position.y + 0.62f) return;                                        // kerb lips, ramp edges, speed bumps: roll over them
            n.y = 0f; n.Normalize();
            float into = Vector3.Dot(transform.forward * speed, -n);
            if (into > 0f) { speed *= Mathf.Clamp01(1f - Mathf.Abs(Vector3.Dot(transform.forward, -n))); if (Mathf.Abs(speed) < 0.4f) speed = 0f; }
            rb.position += n * 0.01f;
        }

        void OnCollisionEnter(Collision c)
        {
            var animal = c.collider.GetComponentInParent<MafiaSandbox.World.Animal>();
            if (animal && !animal.Dead && Mathf.Abs(speed) > 2.5f) { animal.Hit(c.GetContact(0).point, transform.forward * Mathf.Sign(speed)); return; }
            // pedestrians: knock them down (ragdoll) when fast, nudge them when slow
            var body = c.collider.GetComponentInParent<MafiaSandbox.Combat.CharacterBody>();
            if (body != null && !body.Dead)
            {
                float v = Mathf.Abs(speed);
                Vector3 dir = (body.transform.position - transform.position); dir.y = 0f; dir = (dir.normalized * 0.8f + transform.forward * Mathf.Sign(speed) * 0.6f).normalized;
                var contact = c.GetContact(0).point;
                if (v > 3f)
                {
                    if (!(hitAt.TryGetValue(body, out float t0) && Time.time - t0 < 1.5f)) { hitAt[body] = Time.time; dir = (dir + Vector3.up * 0.35f).normalized; body.Hit(body.ZoneNear(contact), contact, dir, v > 7f ? 99 : 1, v > 7f, Mathf.Clamp(v * 0.8f, 2f, 13f), Controlled ? "player" : "vehicle"); MafiaSandbox.Core.Sfx.At("thud", contact); }
                    StartCoroutine(PassThrough(body, 2.5f));   // the body must not act as a wall for the car that hit it
                }
                else { var cc = body.GetComponent<CharacterController>(); if (cc && cc.enabled) cc.Move(dir * 0.4f); }
                return;
            }
            // lose speed on hard frontal/rear hits so we don't tunnel along walls
            float along = Vector3.Dot(c.relativeVelocity, transform.forward);
            if (Mathf.Abs(along) > 2f) speed *= 0.25f;
            BlockInto(c);
        }
    }
}
