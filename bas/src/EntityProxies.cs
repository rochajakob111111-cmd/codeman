using System.Collections.Generic;
using ThunderRoad;
using UnityEngine;

namespace CraftBlade
{
    /// <summary>
    /// Minecraft mobs and players near the VR player, inside Blade & Sorcery. Humanoids are real B&S creatures
    /// from the player's own catalog (brain stopped, moved by Minecraft, ragdolled when Minecraft says they died);
    /// everything else is a box. Physical hits on them go to Minecraft, which decides the damage.
    /// </summary>
    public sealed class EntityProxies
    {
        public static EntityProxies Instance;

        sealed class Proxy
        {
            public int id;
            public string kind;
            public Gen.EntityState last;
            public Vector3 target;
            public float yaw;
            public Creature creature;
            public bool spawning, dead;
            public GameObject box;
            public float removeAt = -1;
        }

        readonly BridgeClient bridge;
        readonly Dictionary<int, Proxy> proxies = new Dictionary<int, Proxy>();
        readonly Dictionary<Creature, Proxy> byCreature = new Dictionary<Creature, Proxy>();
        readonly Dictionary<int, float> lastHit = new Dictionary<int, float>();
        Material boxMat;
        int creatureCount;

        public EntityProxies(BridgeClient bridge)
        {
            this.bridge = bridge;
            Instance = this;
            EventManager.onCreatureHit += OnCreatureHit;
        }

        public void Dispose()
        {
            EventManager.onCreatureHit -= OnCreatureHit;
            Clear();
            if (Instance == this) Instance = null;
        }

        public void Clear()
        {
            foreach (var p in new List<Proxy>(proxies.Values)) Destroy(p);
            proxies.Clear();
            byCreature.Clear();
            creatureCount = 0;
        }

        static Gen.Tables.EntityRule Rule(string kind)
        {
            foreach (var r in Gen.Tables.Entities) if (r.mcType == kind || r.mcType == "*") return r;
            return Gen.Tables.Entities[Gen.Tables.Entities.Length - 1];
        }

        public void OnState(Gen.EntityState m)
        {
            Proxy p;
            if (!proxies.TryGetValue(m.entityId, out p))
            {
                if ((m.flags & 1) == 0) return; // already dead when first seen
                p = new Proxy { id = m.entityId, kind = m.kind };
                proxies[m.entityId] = p;
                Spawn(p, m);
            }
            p.last = m;
            p.target = Coords.ToUnity(m.x, m.y, m.z);
            p.yaw = Coords.YawToUnity(m.yaw);
            if ((m.flags & 1) == 0 && !p.dead) Die(p);
        }

        public void OnRemove(Gen.EntityRemove m)
        {
            Proxy p;
            if (!proxies.TryGetValue(m.entityId, out p)) return;
            if (p.dead) p.removeAt = Time.time + 5f; // let the ragdoll lie for a moment
            else { Destroy(p); proxies.Remove(m.entityId); }
        }

        void Spawn(Proxy p, Gen.EntityState m)
        {
            var rule = Rule(m.kind);
            if (rule.creature && creatureCount < Gen.Tables.DEFAULT_MAX_CREATURE_PROXIES)
            {
                var data = Catalog.GetData<CreatureData>(rule.basCreature, true);
                if (data != null)
                {
                    p.spawning = true;
                    creatureCount++;
                    data.SpawnAsync(Coords.ToUnity(m.x, m.y, m.z), Coords.YawToUnity(m.yaw), null, false, c =>
                    {
                        p.spawning = false;
                        if (!proxies.ContainsKey(p.id) || p.removeAt == -2) { c.Despawn(); creatureCount--; return; }
                        p.creature = c;
                        byCreature[c] = p;
                        if (c.brain != null) c.brain.Stop();
                        c.maxHealth = Gen.Tables.DEFAULT_PROXY_HEALTH;
                        c.currentHealth = Gen.Tables.DEFAULT_PROXY_HEALTH;
                        if (p.dead) c.Kill();
                    });
                    return;
                }
                Log.Warn("Creature " + rule.basCreature + " is not in this Blade & Sorcery catalog; using a box");
            }
            MakeBox(p, m);
        }

        void MakeBox(Proxy p, Gen.EntityState m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Minecraft " + m.kind + " #" + m.entityId;
            go.transform.localScale = new Vector3(Mathf.Max(0.2f, m.width), Mathf.Max(0.2f, m.height), Mathf.Max(0.2f, m.width));
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            go.AddComponent<ProxyBox>().entityId = m.entityId;
            var r = go.GetComponent<MeshRenderer>();
            if (boxMat == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                boxMat = new Material(sh) { name = "CraftBlade mob box" };
            }
            r.sharedMaterial = boxMat;
            var block = new MaterialPropertyBlock();
            var hue = (m.kind.GetHashCode() & 0xFFFF) / 65535f;
            var col = Color.HSVToRGB(hue, 0.6f, 0.8f);
            block.SetColor("_BaseColor", col);
            block.SetColor("_Color", col);
            r.SetPropertyBlock(block);
            p.box = go;
        }

        void Die(Proxy p)
        {
            p.dead = true;
            if (p.creature != null && !p.creature.isKilled) p.creature.Kill();
            if (p.box != null) { Object.Destroy(p.box); p.box = null; }
        }

        void Destroy(Proxy p)
        {
            if (p.creature != null)
            {
                byCreature.Remove(p.creature);
                p.creature.Despawn();
                p.creature = null;
                creatureCount--;
            }
            else if (p.spawning) p.removeAt = -2; // despawn when the spawn completes
            if (p.box != null) Object.Destroy(p.box);
        }

        /// <summary>Move every proxy toward its Minecraft position (20 Hz data, smoothed per frame).</summary>
        public void Update()
        {
            var gone = new List<int>();
            foreach (var p in proxies.Values)
            {
                if (p.removeAt > 0 && Time.time > p.removeAt) { Destroy(p); gone.Add(p.id); continue; }
                if (p.dead) continue;
                var rot = Quaternion.Euler(0f, p.yaw, 0f);
                if (p.creature != null)
                {
                    var c = p.creature;
                    if (c.isKilled)
                    {
                        // B&S killed it (beheaded, sliced) but Minecraft says it lives: bring a fresh one
                        byCreature.Remove(c);
                        c.Despawn();
                        p.creature = null;
                        creatureCount--;
                        Spawn(p, p.last);
                        continue;
                    }
                    c.currentHealth = Gen.Tables.DEFAULT_PROXY_HEALTH;
                    var pos = c.transform.position;
                    if ((pos - p.target).sqrMagnitude > 4f) c.Teleport(p.target, rot);
                    else
                    {
                        c.transform.position = Vector3.Lerp(pos, p.target, Mathf.Min(1f, Time.deltaTime * 12f));
                        c.transform.rotation = Quaternion.Slerp(c.transform.rotation, rot, Mathf.Min(1f, Time.deltaTime * 10f));
                    }
                }
                else if (p.box != null)
                {
                    var center = p.target + Vector3.up * (p.last.height * 0.5f);
                    var rb = p.box.GetComponent<Rigidbody>();
                    rb.MovePosition(Vector3.Lerp(p.box.transform.position, center, Mathf.Min(1f, Time.deltaTime * 12f)));
                    rb.MoveRotation(rot);
                }
            }
            foreach (var id in gone) proxies.Remove(id);
        }

        public void Reposition()
        {
            foreach (var p in proxies.Values)
            {
                if (p.last == null) continue;
                p.target = Coords.ToUnity(p.last.x, p.last.y, p.last.z);
                if (p.creature != null) p.creature.Teleport(p.target, Quaternion.Euler(0f, p.yaw, 0f));
                if (p.box != null) p.box.transform.position = p.target + Vector3.up * (p.last.height * 0.5f);
            }
        }

        void OnCreatureHit(Creature creature, CollisionInstance hit, EventTime time)
        {
            if (time != EventTime.OnEnd || creature == null || hit == null) return;
            Proxy p;
            if (!byCreature.TryGetValue(creature, out p) || p.dead) return;
            var ds = hit.damageStruct;
            int type = Gen.Tables.DamageId(ds.damageType.ToString());
            Item item = hit.sourceColliderGroup != null && hit.sourceColliderGroup.collisionHandler != null ? hit.sourceColliderGroup.collisionHandler.item : null;
            bool thrown = item != null && item.mainHandler == null;
            SendHit(p.id, ds.damage, type, hit.impactVelocity, thrown);
            creature.currentHealth = Gen.Tables.DEFAULT_PROXY_HEALTH;
        }

        public void SendHit(int entityId, float damage, int type, Vector3 unityVelocity, bool thrown)
        {
            float prev;
            if (lastHit.TryGetValue(entityId, out prev) && Time.time - prev < Gen.Tables.DEFAULT_DAMAGE_COOLDOWN_MS / 1000f) return;
            lastHit[entityId] = Time.time;
            var v = Coords.DirToMinecraft(unityVelocity);
            bridge.Send(new Gen.Hit { entityId = entityId, damage = damage, dmgType = (byte)type, vx = v.x, vy = v.y, vz = v.z, thrown = (byte)(thrown ? 1 : 0) });
        }
    }
}
