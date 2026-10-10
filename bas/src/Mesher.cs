using System;
using System.Collections.Generic;

namespace CraftBlade
{
    /// <summary>A block state as described by Minecraft (sheets/protocol.json StateDef).</summary>
    public sealed class StateDef
    {
        public int id;
        public string name;
        public int shape;          // sheets/shapes.json
        public bool opaque, collide;
        public int tint;           // 0 none, 1 grass, 2 foliage, 3 water
        public string[] tex = new string[6]; // down, up, north, south, west, east (Minecraft Direction ids)
        public float minX, minY, minZ, maxX = 1, maxY = 1, maxZ = 1;

        public static StateDef From(Gen.StateDef m)
        {
            return new StateDef
            {
                id = m.stateId, name = m.name, shape = m.shape, opaque = m.opaque != 0, collide = m.collide != 0, tint = m.tint,
                tex = new[] { m.texDown, m.texUp, m.texNorth, m.texSouth, m.texWest, m.texEast },
                minX = m.minX, minY = m.minY, minZ = m.minZ, maxX = m.maxX, maxY = m.maxY, maxZ = m.maxZ,
            };
        }
    }

    /// <summary>Vertex lists for one section in Unity-local coordinates (x, y, -z): no Unity types, so it is testable outside the game.</summary>
    public sealed class MeshData
    {
        public readonly List<float> pos = new List<float>();      // x,y,z per vertex
        public readonly List<float> uv = new List<float>();       // u,v per vertex
        public readonly List<int> solid = new List<int>();        // cubes, boxes, cross (alpha-clipped)
        public readonly List<int> liquid = new List<int>();       // translucent
        public readonly List<float> colPos = new List<float>();
        public readonly List<int> colTris = new List<int>();
        public int VertexCount { get { return pos.Count / 3; } }
        public bool Empty { get { return solid.Count == 0 && liquid.Count == 0 && colTris.Count == 0; } }
    }

    /// <summary>
    /// Section to faces. Minecraft block (x,y,z) inside the section occupies Unity-local
    /// x..x+1, y..y+1, -(z+1)..-z (Minecraft is right-handed, Unity left-handed: z flips).
    /// </summary>
    public static class Mesher
    {
        /// <summary>Atlas rectangle for a texture name (with optional "#tint") under a tint colour.</summary>
        public delegate void UvLookup(string tex, int tintCategory, SectionData s, out float u0, out float v0, out float u1, out float v1);

        // Minecraft Direction ids and their offsets in Minecraft axes
        static readonly int[] DX = { 0, 0, 0, 0, -1, 1 };
        static readonly int[] DY = { -1, 1, 0, 0, 0, 0 };
        static readonly int[] DZ = { 0, 0, -1, 1, 0, 0 };

        /// <param name="neighbor">state id at section-local coords that may lie outside 0..15; -1 when unknown</param>
        public static MeshData Build(SectionData s, Func<int, StateDef> defs, Func<int, int, int, int> neighbor, UvLookup uvs)
        {
            var m = new MeshData();
            for (int y = 0; y < 16; y++)
                for (int z = 0; z < 16; z++)
                    for (int x = 0; x < 16; x++)
                    {
                        var d = defs(s.StateAt(x, y, z));
                        if (d == null || d.shape == Gen.Tables.SHAPE_AIR) continue;
                        if (d.shape == Gen.Tables.SHAPE_CROSS) { Cross(m, s, d, x, y, z, uvs); continue; }
                        bool liquid = d.shape == Gen.Tables.SHAPE_LIQUID;
                        bool box = d.shape == Gen.Tables.SHAPE_BOX;
                        float x0 = box ? d.minX : 0, y0 = box ? d.minY : 0, z0 = box ? d.minZ : 0;
                        float x1 = box ? d.maxX : 1, y1 = box ? d.maxY : 1, z1 = box ? d.maxZ : 1;
                        if (liquid)
                        {
                            var above = Def(s, defs, neighbor, x, y + 1, z);
                            if (above == null || above.shape != Gen.Tables.SHAPE_LIQUID) y1 = 0.875f;
                        }
                        for (int f = 0; f < 6; f++)
                        {
                            var nd = Def(s, defs, neighbor, x + DX[f], y + DY[f], z + DZ[f]);
                            bool touches = !box || FaceOnBoundary(f, x0, y0, z0, x1, y1, z1);
                            if (touches && nd != null)
                            {
                                if (nd.opaque && nd.shape == Gen.Tables.SHAPE_CUBE) continue;
                                if (liquid && nd.shape == Gen.Tables.SHAPE_LIQUID) continue;
                                if (!liquid && !box && nd.id == d.id && !d.opaque) continue; // glass next to glass, leaves next to leaves
                            }
                            Face(m, s, d, f, x, y, z, x0, y0, z0, x1, y1, z1, liquid ? m.liquid : m.solid, uvs);
                            if (d.collide) Face(m, null, null, f, x, y, z, x0, y0, z0, x1, y1, z1, null, null);
                        }
                    }
            return m;
        }

        static StateDef Def(SectionData s, Func<int, StateDef> defs, Func<int, int, int, int> neighbor, int x, int y, int z)
        {
            int id = (x >= 0 && x < 16 && y >= 0 && y < 16 && z >= 0 && z < 16) ? s.StateAt(x, y, z) : neighbor(x, y, z);
            return id < 0 ? null : defs(id);
        }

        static bool FaceOnBoundary(int f, float x0, float y0, float z0, float x1, float y1, float z1)
        {
            switch (f)
            {
                case 0: return y0 <= 0f; case 1: return y1 >= 1f;
                case 2: return z0 <= 0f; case 3: return z1 >= 1f;
                case 4: return x0 <= 0f; default: return x1 >= 1f;
            }
        }

        /// <summary>One face of a box. With tris == null it goes to the collision mesh instead.</summary>
        static void Face(MeshData m, SectionData s, StateDef d, int f, int bx, int by, int bz,
                         float x0, float y0, float z0, float x1, float y1, float z1, List<int> tris, UvLookup uvs)
        {
            // corners in Minecraft-local coords, ordered around the face
            float[][] c;
            switch (f)
            {
                case 0: c = new[] { V(x0, y0, z0), V(x1, y0, z0), V(x1, y0, z1), V(x0, y0, z1) }; break;
                case 1: c = new[] { V(x0, y1, z1), V(x1, y1, z1), V(x1, y1, z0), V(x0, y1, z0) }; break;
                case 2: c = new[] { V(x1, y0, z0), V(x0, y0, z0), V(x0, y1, z0), V(x1, y1, z0) }; break;
                case 3: c = new[] { V(x0, y0, z1), V(x1, y0, z1), V(x1, y1, z1), V(x0, y1, z1) }; break;
                case 4: c = new[] { V(x0, y0, z0), V(x0, y0, z1), V(x0, y1, z1), V(x0, y1, z0) }; break;
                default: c = new[] { V(x1, y0, z1), V(x1, y0, z0), V(x1, y1, z0), V(x1, y1, z1) }; break;
            }
            // outward normal in Unity axes
            float nx = DX[f], ny = DY[f], nz = -DZ[f];
            var pos = tris == null ? m.colPos : m.pos;
            var idx = tris ?? m.colTris;
            int baseV = pos.Count / 3;
            for (int i = 0; i < 4; i++)
            {
                pos.Add(bx + c[i][0]); pos.Add(by + c[i][1]); pos.Add(-(bz + c[i][2]));
            }
            if (tris != null)
            {
                float u0, v0, u1, v1;
                uvs(d.tex[f], d.tint, s, out u0, out v0, out u1, out v1);
                // corners 0,1 are the bottom edge (or the north edge for up/down faces)
                m.uv.Add(u0); m.uv.Add(v0); m.uv.Add(u1); m.uv.Add(v0); m.uv.Add(u1); m.uv.Add(v1); m.uv.Add(u0); m.uv.Add(v1);
            }
            AddQuad(pos, idx, baseV, nx, ny, nz);
        }

        static void Cross(MeshData m, SectionData s, StateDef d, int bx, int by, int bz, UvLookup uvs)
        {
            float u0, v0, u1, v1;
            uvs(d.tex[0], d.tint, s, out u0, out v0, out u1, out v1);
            const float a = 0.15f, b = 0.85f;
            float[][][] planes =
            {
                new[] { V(a, 0, a), V(b, 0, b), V(b, 1, b), V(a, 1, a) },
                new[] { V(a, 0, b), V(b, 0, a), V(b, 1, a), V(a, 1, b) },
            };
            foreach (var c in planes)
            {
                for (int side = 0; side < 2; side++) // both sides visible
                {
                    int baseV = m.pos.Count / 3;
                    for (int i = 0; i < 4; i++) { m.pos.Add(bx + c[i][0]); m.pos.Add(by + c[i][1]); m.pos.Add(-(bz + c[i][2])); }
                    m.uv.Add(u0); m.uv.Add(v0); m.uv.Add(u1); m.uv.Add(v0); m.uv.Add(u1); m.uv.Add(v1); m.uv.Add(u0); m.uv.Add(v1);
                    if (side == 0) { m.solid.Add(baseV); m.solid.Add(baseV + 2); m.solid.Add(baseV + 1); m.solid.Add(baseV); m.solid.Add(baseV + 3); m.solid.Add(baseV + 2); }
                    else { m.solid.Add(baseV); m.solid.Add(baseV + 1); m.solid.Add(baseV + 2); m.solid.Add(baseV); m.solid.Add(baseV + 2); m.solid.Add(baseV + 3); }
                }
            }
        }

        /// <summary>Two triangles wound so Unity treats the outward side as the front (cross(b-a, c-a) along the normal).</summary>
        static void AddQuad(List<float> pos, List<int> idx, int baseV, float nx, float ny, float nz)
        {
            int a = baseV * 3, b = (baseV + 1) * 3, c = (baseV + 2) * 3;
            float e1x = pos[b] - pos[a], e1y = pos[b + 1] - pos[a + 1], e1z = pos[b + 2] - pos[a + 2];
            float e2x = pos[c] - pos[a], e2y = pos[c + 1] - pos[a + 1], e2z = pos[c + 2] - pos[a + 2];
            float cx = e1y * e2z - e1z * e2y, cy = e1z * e2x - e1x * e2z, cz = e1x * e2y - e1y * e2x;
            if (cx * nx + cy * ny + cz * nz >= 0)
            {
                idx.Add(baseV); idx.Add(baseV + 1); idx.Add(baseV + 2); idx.Add(baseV); idx.Add(baseV + 2); idx.Add(baseV + 3);
            }
            else
            {
                idx.Add(baseV); idx.Add(baseV + 2); idx.Add(baseV + 1); idx.Add(baseV); idx.Add(baseV + 3); idx.Add(baseV + 2);
            }
        }

        static float[] V(float x, float y, float z) { return new[] { x, y, z }; }
    }
}
