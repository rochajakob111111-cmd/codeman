using ThunderRoad;
using UnityEngine;

namespace CraftBlade
{
    /// <summary>
    /// A Minecraft mob without a human shape (spider, creeper, pig...): a kinematic box the size of its
    /// Minecraft hitbox. Physical hits from B&S items and fists become Minecraft damage.
    /// </summary>
    public sealed class ProxyBox : MonoBehaviour
    {
        public int entityId;
        float lastHit;

        void OnCollisionEnter(Collision c)
        {
            if (Time.time - lastHit < Gen.Tables.DEFAULT_DAMAGE_COOLDOWN_MS / 1000f) return;
            float speed = c.relativeVelocity.magnitude;
            var item = c.collider.GetComponentInParent<Item>();
            var part = item == null ? c.collider.GetComponentInParent<RagdollPart>() : null;
            if (item == null && (part == null || part.ragdoll == null || part.ragdoll.creature == null || !part.ragdoll.creature.isPlayer)) return;
            float mass = c.rigidbody != null ? c.rigidbody.mass : 1f;
            int type = Gen.Tables.DamageId("Blunt");
            if (speed < Gen.Tables.DamageMinSpeed[type]) return;
            lastHit = Time.time;
            bool thrown = item != null && item.mainHandler == null;
            EntityProxies.Instance?.SendHit(entityId, mass * speed * 4f, type, -c.relativeVelocity, thrown);
        }
    }
}
