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
        static readonly Collider[] hits = new Collider[16];

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
            int n = Physics.OverlapSphereNonAlloc(transform.position + Vector3.up, enterRange, hits, ~0, QueryTriggerInteraction.Ignore);
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
            pc.Teleport(pos, v.transform.eulerAngles.y);
            pc.InputLocked = false;
            if (playerCamera) playerCamera.SetVehicle(null);
        }
    }
}
