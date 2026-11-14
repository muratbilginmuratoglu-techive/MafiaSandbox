using UnityEngine;
using MafiaSandbox.Player;

namespace MafiaSandbox.Vehicles
{
    /// <summary>Lives on the player. E near a vehicle = get in; E while driving = get out on the left side.</summary>
    public class VehicleInteraction : MonoBehaviour
    {
        public float enterRange = 3.2f;
        public PlayerCamera playerCamera;

        public VehicleController Current { get; private set; }
        public VehicleController Nearby { get; private set; }

        PlayerController pc; CharacterController cc; Renderer[] renderers; Collider[] ownColliders;
        static readonly Collider[] hits = new Collider[64];

        void Awake()
        {
            pc = GetComponent<PlayerController>(); cc = GetComponent<CharacterController>();
            if (!playerCamera) playerCamera = FindAnyObjectByType<PlayerCamera>();
        }

        void Update()
        {
            if (Current)
            {
                transform.position = Current.transform.position;   // ride along (hidden)
                if (Input.GetKeyDown(KeyCode.E)) Exit();
                return;
            }
            Nearby = FindNearby();
            if (Nearby && Input.GetKeyDown(KeyCode.E)) Enter(Nearby);
        }

        VehicleController FindNearby()
        {
            int vl = LayerMask.NameToLayer("Vehicles"); int mask = vl >= 0 ? 1 << vl : ~0;   // only vehicle colliders: the sphere used to fill up with ground / hitbox colliders and miss the car
            int n = Physics.OverlapSphereNonAlloc(transform.position + Vector3.up, enterRange, hits, mask, QueryTriggerInteraction.Ignore);
            VehicleController best = null; float bd = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var v = hits[i].GetComponentInParent<VehicleController>();
                if (!v) continue;
                float d = Vector3.Distance(hits[i].ClosestPoint(transform.position + Vector3.up), transform.position + Vector3.up);
                if (d < bd) { bd = d; best = v; }
            }
            return best;
        }

        public void Enter(VehicleController v)
        {
            renderers = GetComponentsInChildren<Renderer>(); ownColliders = GetComponentsInChildren<Collider>();   // cached here: hitboxes are built in Awake order-independently
            Current = v; Nearby = null;
            pc.InputLocked = true; cc.enabled = false;
            foreach (var r in renderers) r.enabled = false;
            foreach (var c in ownColliders) c.enabled = false;
            v.SetControlled(true);
            if (playerCamera) playerCamera.SetVehicle(v.transform);
        }

        System.Collections.IEnumerator IgnoreCar(VehicleController v, float seconds)
        {
            var mine = GetComponentsInChildren<Collider>(true); var theirs = v ? v.GetComponentsInChildren<Collider>(true) : new Collider[0];
            foreach (var a in mine) foreach (var b in theirs) if (a && b) Physics.IgnoreCollision(a, b, true);
            yield return new WaitForSeconds(seconds);
            foreach (var a in mine) foreach (var b in theirs) if (a && b) Physics.IgnoreCollision(a, b, false);
        }

        public void Exit()
        {
            var v = Current; Current = null;
            v.SetControlled(false);
            Vector3 side = -v.transform.right * (v.width * 0.5f + 0.8f);
            Vector3 pos = v.transform.position + side + Vector3.up * 0.2f;
            // make sure we don't pop out inside a wall: fall back to the other side / behind
            if (Physics.CheckCapsule(pos + Vector3.up * 0.4f, pos + Vector3.up * 1.5f, 0.3f, ~0, QueryTriggerInteraction.Ignore))
                pos = v.transform.position - side + Vector3.up * 0.2f;
            if (Physics.CheckCapsule(pos + Vector3.up * 0.4f, pos + Vector3.up * 1.5f, 0.3f, ~0, QueryTriggerInteraction.Ignore))
                pos = v.transform.position - v.transform.forward * (v.length * 0.5f + 1f) + Vector3.up * 0.2f;
            foreach (var r in renderers) r.enabled = true;
            foreach (var c in ownColliders) c.enabled = true;
            // the car we just left must not run us over: no contact between it and us for a moment
            StartCoroutine(IgnoreCar(v, 2.5f));
            pc.Teleport(pos, v.transform.eulerAngles.y);
            pc.InputLocked = false;
            if (playerCamera) playerCamera.SetVehicle(null);
        }
    }
}
