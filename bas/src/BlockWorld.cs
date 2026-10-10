using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CraftBlade
{
    /// <summary>
    /// The Minecraft world around the VR player, built inside Blade & Sorcery: one GameObject per 16^3 section
    /// with a mesh (atlas-textured) and a MeshCollider so the player, items and ragdolls stand on the blocks.
    /// </summary>
    public sealed class BlockWorld
    {
        readonly Dictionary<int, StateDef> defs = new Dictionary<int, StateDef>();
        readonly Dictionary<long, SectionData> sections = new Dictionary<long, SectionData>();
        readonly Dictionary<long, GameObject> objects = new Dictionary<long, GameObject>();
        readonly HashSet<long> dirty = new HashSet<long>();
        readonly HashSet<long> built = new HashSet<long>();
        readonly Atlas atlas;
        readonly Material solidMat, liquidMat;
        GameObject root;
        public int RebuildsPerFrame = 4;

        public BlockWorld(Atlas atlas)
        {
            this.atlas = atlas;
            solidMat = MakeMaterial(false);
            liquidMat = MakeMaterial(true);
        }

        Material MakeMaterial(bool translucent)
        {
            string[] names = { "Universal Render Pipeline/Lit", "Universal Render Pipeline/Simple Lit", "Universal Render Pipeline/Unlit", "Standard" };
            Shader sh = null;
            foreach (var n in names) { sh = Shader.Find(n); if (sh != null) break; }
            if (sh == null) Log.Warn("No usable shader found; blocks will not be visible");
            var m = new Material(sh) { name = translucent ? "CraftBlade liquid" : "CraftBlade blocks", mainTexture = atlas.texture };
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", atlas.texture);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0f);
            if (translucent)
            {
                m.SetFloat("_Surface", 1f);
                m.SetOverrideTag("RenderType", "Transparent");
                m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)RenderQueue.Transparent;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", new Color(1, 1, 1, 0.75f));
            }
            else
            {
                // leaves, glass, flowers: cut out by the texture's alpha
                m.SetFloat("_AlphaClip", 1f);
                m.SetFloat("_Cutoff", 0.5f);
                m.EnableKeyword("_ALPHATEST_ON");
                m.renderQueue = (int)RenderQueue.AlphaTest;
            }
            return m;
        }

        public void Reset()
        {
            if (root != null) Object.Destroy(root);
            root = new GameObject("CraftBlade World");
            defs.Clear(); sections.Clear(); objects.Clear(); dirty.Clear(); built.Clear();
            atlas.Clear();
        }

        public void OnStateDef(Gen.StateDef m) { defs[m.stateId] = StateDef.From(m); }

        public void OnSection(Gen.Section m)
        {
            var s = SectionData.From(m);
            long k = SectionData.Key(s.sx, s.sy, s.sz);
            sections[k] = s;
            MarkWithNeighbours(s.sx, s.sy, s.sz);
        }

        public void OnSectionClear(Gen.SectionClear m)
        {
            long k = SectionData.Key(m.sx, m.sy, m.sz);
            sections.Remove(k);
            dirty.Remove(k);
            built.Remove(k);
            GameObject go;
            if (objects.TryGetValue(k, out go)) { Object.Destroy(go); objects.Remove(k); }
        }

        public void OnBlockUpdate(Gen.BlockUpdate m)
        {
            int sx = m.x >> 4, sy = m.y >> 4, sz = m.z >> 4;
            SectionData s;
            if (!sections.TryGetValue(SectionData.Key(sx, sy, sz), out s)) return;
            s.Set(m.x & 15, m.y & 15, m.z & 15, m.stateId);
            dirty.Add(SectionData.Key(sx, sy, sz));
            // an edit on a section border changes the neighbour's culled faces too
            int lx = m.x & 15, ly = m.y & 15, lz = m.z & 15;
            if (lx == 0) Mark(sx - 1, sy, sz); if (lx == 15) Mark(sx + 1, sy, sz);
            if (ly == 0) Mark(sx, sy - 1, sz); if (ly == 15) Mark(sx, sy + 1, sz);
            if (lz == 0) Mark(sx, sy, sz - 1); if (lz == 15) Mark(sx, sy, sz + 1);
        }

        void MarkWithNeighbours(int sx, int sy, int sz)
        {
            dirty.Add(SectionData.Key(sx, sy, sz));
            Mark(sx - 1, sy, sz); Mark(sx + 1, sy, sz); Mark(sx, sy - 1, sz); Mark(sx, sy + 1, sz); Mark(sx, sy, sz - 1); Mark(sx, sy, sz + 1);
        }

        void Mark(int sx, int sy, int sz)
        {
            long k = SectionData.Key(sx, sy, sz);
            if (sections.ContainsKey(k)) dirty.Add(k);
        }

        /// <summary>State id at absolute Minecraft block coordinates; -1 if that section has not arrived.</summary>
        public int StateAt(int x, int y, int z)
        {
            SectionData s;
            if (!sections.TryGetValue(SectionData.Key(x >> 4, y >> 4, z >> 4), out s)) return -1;
            return s.StateAt(x & 15, y & 15, z & 15);
        }

        /// <summary>True once the section holding this block has a built mesh and collider.</summary>
        public bool ReadyAt(double x, double y, double z)
        {
            return built.Contains(SectionData.Key((int)System.Math.Floor(x) >> 4, (int)System.Math.Floor(y) >> 4, (int)System.Math.Floor(z) >> 4));
        }

        /// <summary>Rebuild a few dirty sections, nearest to the player first.</summary>
        public void Update(Vector3 playerUnity)
        {
            if (dirty.Count > 0)
            {
                double px, py, pz;
                Coords.ToMinecraft(playerUnity, out px, out py, out pz);
                int cx = (int)System.Math.Floor(px) >> 4, cy = (int)System.Math.Floor(py) >> 4, cz = (int)System.Math.Floor(pz) >> 4;
                var order = new List<long>(dirty);
                order.Sort((a, b) => Dist(a, cx, cy, cz).CompareTo(Dist(b, cx, cy, cz)));
                for (int i = 0; i < order.Count && i < RebuildsPerFrame; i++)
                {
                    dirty.Remove(order[i]);
                    SectionData s;
                    if (sections.TryGetValue(order[i], out s)) Build(order[i], s);
                }
            }
            atlas.Flush();
        }

        int Dist(long k, int cx, int cy, int cz)
        {
            var s = sections[k];
            int dx = s.sx - cx, dy = s.sy - cy, dz = s.sz - cz;
            return dx * dx + 2 * dy * dy + dz * dz;
        }

        void Build(long k, SectionData s)
        {
            int bx = s.sx * 16, by = s.sy * 16, bz = s.sz * 16;
            var md = Mesher.Build(s, id => { StateDef d; return defs.TryGetValue(id, out d) ? d : null; },
                (x, y, z) => StateAt(bx + x, by + y, bz + z), atlas.Uv);

            GameObject go;
            objects.TryGetValue(k, out go);
            if (md.Empty)
            {
                if (go != null) { Object.Destroy(go); objects.Remove(k); }
                built.Add(k);
                return;
            }
            if (go == null)
            {
                go = new GameObject("Section " + s.sx + "," + s.sy + "," + s.sz);
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>();
                go.AddComponent<MeshRenderer>().sharedMaterials = new[] { solidMat, liquidMat };
                objects[k] = go;
            }
            go.transform.position = Coords.ToUnity(bx, by, bz);

            var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            if (mesh == null) { mesh = new Mesh { indexFormat = IndexFormat.UInt32 }; go.GetComponent<MeshFilter>().sharedMesh = mesh; }
            mesh.Clear();
            mesh.SetVertices(ToVectors(md.pos));
            mesh.SetUVs(0, ToUv(md.uv));
            mesh.subMeshCount = 2;
            mesh.SetTriangles(md.solid, 0);
            mesh.SetTriangles(md.liquid, 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var col = go.GetComponent<MeshCollider>();
            if (md.colTris.Count == 0)
            {
                if (col != null) Object.Destroy(col);
            }
            else
            {
                if (col == null) col = go.AddComponent<MeshCollider>();
                var cm = col.sharedMesh;
                if (cm == null) cm = new Mesh { indexFormat = IndexFormat.UInt32 };
                cm.Clear();
                cm.SetVertices(ToVectors(md.colPos));
                cm.SetTriangles(md.colTris, 0);
                cm.RecalculateBounds();
                col.sharedMesh = null; // re-assigning forces the physics mesh to rebuild
                col.sharedMesh = cm;
            }
            built.Add(k);
        }

        /// <summary>After the floating origin moved: put every section where it now belongs.</summary>
        public void Reposition()
        {
            foreach (var kv in objects)
            {
                var s = sections.ContainsKey(kv.Key) ? sections[kv.Key] : null;
                if (s != null) kv.Value.transform.position = Coords.ToUnity(s.sx * 16, s.sy * 16, s.sz * 16);
            }
        }

        static List<Vector3> ToVectors(List<float> f)
        {
            var l = new List<Vector3>(f.Count / 3);
            for (int i = 0; i < f.Count; i += 3) l.Add(new Vector3(f[i], f[i + 1], f[i + 2]));
            return l;
        }

        static List<Vector2> ToUv(List<float> f)
        {
            var l = new List<Vector2>(f.Count / 2);
            for (int i = 0; i < f.Count; i += 2) l.Add(new Vector2(f[i], f[i + 1]));
            return l;
        }
    }
}
