// COMPILE-CHECK ONLY. Stand-ins for the members of Blade & Sorcery's ThunderRoad.dll this mod calls, so the
// mod can be type-checked on a machine without the game. The real build (CraftBlade.csproj) references the
// game's own ThunderRoad.dll instead; never ship this file. Every member here is listed in sheets/hooks.json.
using System;
using UnityEngine;

namespace ThunderRoad
{
    public class ModManager { public class ModData { public string fullPath; } }

    public abstract class ThunderScript
    {
        public virtual void ScriptLoaded(ModManager.ModData modData) { }
        public virtual void ScriptUpdate() { }
        public virtual void ScriptDisable() { }
    }

    public class PlayerHead : MonoBehaviour { }
    public class PlayerLocomotion : MonoBehaviour { public bool isGrounded; }

    public class Player : MonoBehaviour
    {
        public static Player local;
        public Creature creature;
        public PlayerHead head;
        public PlayerLocomotion locomotion;
        public void Teleport(Vector3 position, Quaternion rotation) { }
    }

    public class Brain : MonoBehaviour { public void Stop() { } }
    public class RagdollHand : RagdollPart { }
    public class Ragdoll : MonoBehaviour { public Creature creature; }
    public class RagdollPart : MonoBehaviour { public Ragdoll ragdoll; }

    public class Creature : MonoBehaviour
    {
        public float currentHealth, maxHealth;
        public bool isKilled, isPlayer;
        public Brain brain;
        public RagdollHand handLeft, handRight;
        public void Kill() { }
        public void Despawn() { }
        public void Teleport(Vector3 position, Quaternion rotation) { }
    }

    public class CatalogData { }
    public class CreatureData : CatalogData
    {
        public void SpawnAsync(Vector3 position, float rotation, Transform parent, bool pooled, Action<Creature> callback) { }
    }

    public static class Catalog
    {
        public static T GetData<T>(string id, bool logWarning = true) where T : CatalogData { return null; }
    }

    public class Item : MonoBehaviour { public RagdollHand mainHandler; }
    public class CollisionHandler : MonoBehaviour { public Item item; }
    public class ColliderGroup : MonoBehaviour { public CollisionHandler collisionHandler; }

    public enum DamageType { Unknown, Pierce, Slash, Blunt, Energy }
    public struct DamageStruct { public float damage; public DamageType damageType; }

    public class CollisionInstance
    {
        public DamageStruct damageStruct;
        public Vector3 impactVelocity;
        public ColliderGroup sourceColliderGroup;
    }

    public enum EventTime { OnStart, OnEnd }

    public static class EventManager
    {
        public delegate void CreatureHitEvent(Creature creature, CollisionInstance collisionInstance, EventTime eventTime);
        public static event CreatureHitEvent onCreatureHit;
        static void Unused() { onCreatureHit?.Invoke(null, null, EventTime.OnStart); }
    }
}
