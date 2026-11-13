using UnityEngine;
using MafiaSandbox.Player;
using MafiaSandbox.Vehicles;

namespace MafiaSandbox.Core
{
    /// <summary>Scene-level glue: references, respawn at home (R), cursor handling. Economy, wanted level and vitals plug in here later.</summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager I { get; private set; }
        public PlayerController player;
        public PlayerCamera playerCamera;
        public VehicleController playerCar;
        public Vector3 homePosition;
        public float homeYaw;

        void Awake()
        {
            I = this;
            Application.targetFrameRate = -1;
            if (!player) player = FindAnyObjectByType<PlayerController>();
            if (!playerCamera) playerCamera = FindAnyObjectByType<PlayerCamera>();
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.R) && player && !player.InputLocked) Respawn();
        }

        public void Respawn()
        {
            player.Teleport(homePosition, homeYaw);
        }
    }
}
