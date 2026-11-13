using UnityEngine;

namespace MafiaSandbox.Economy
{
    /// <summary>An item lying in the world (shop shelf, vault, dropped, loot). The model is an Esya_* prop instance under this object.</summary>
    public class Pickable : MonoBehaviour
    {
        public string id; public ItemDef def; public Shop shop; public bool vault; public int cashAmount;
        public bool taken; public float respawnAt = -1f; public bool respawns = true;
        Vector3 homePos; Quaternion homeRot;

        public void Setup(string id, Shop shop, bool vault)
        {
            this.id = id; def = Items.Get(id); this.shop = shop; this.vault = vault; homePos = transform.position; homeRot = transform.rotation;
            cashAmount = id == "Para" ? 80 + Random.Range(0, 220) : 0;
        }

        public void Take()
        {
            taken = true; gameObject.SetActive(false);
            if (respawns) respawnAt = Time.time + 240f; else Destroy(gameObject, 0.1f);
        }

        public void Respawn()
        {
            taken = false; transform.SetPositionAndRotation(homePos, homeRot); gameObject.SetActive(true); respawnAt = -1f;
            if (id == "Para") cashAmount = 80 + Random.Range(0, 220);
        }
    }
}
