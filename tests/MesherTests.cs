// Unit tests for the B&S-side mesher and section decoding (pure C#, run with mono: python3 tests/run_cs_tests.py).
using System;
using System.Collections.Generic;
using CraftBlade;

public static class MesherTests
{
    static int failures;
    static void Expect(bool ok, string what) { if (!ok) { failures++; Console.WriteLine("  FAIL " + what); } }

    static readonly Dictionary<int, StateDef> Defs = new Dictionary<int, StateDef>
    {
        { 0, new StateDef { id = 0, name = "minecraft:air", shape = 0 } },
        { 1, Cube(1, "minecraft:stone", true) },
        { 2, Cube(2, "minecraft:glass", false) },
        { 3, new StateDef { id = 3, name = "minecraft:water", shape = 3, tex = Tex("minecraft:block/water_still#tint"), tint = 3 } },
        { 4, new StateDef { id = 4, name = "minecraft:poppy", shape = 2, tex = Tex("minecraft:block/poppy") } },
        { 5, new StateDef { id = 5, name = "minecraft:stone_slab", shape = 4, collide = true, tex = Tex("minecraft:block/stone"), maxY = 0.5f } },
    };

    static string[] Tex(string t) { return new[] { t, t, t, t, t, t }; }
    static StateDef Cube(int id, string n, bool opaque) { return new StateDef { id = id, name = n, shape = 1, opaque = opaque, collide = true, tex = Tex(n.Replace(":", ":block/")) }; }

    static SectionData Empty() { return new SectionData { palette = new[] { 0 } }; }

    static MeshData Mesh(SectionData s, int outside = 0)
    {
        return Mesher.Build(s, id => Defs.ContainsKey(id) ? Defs[id] : null, (x, y, z) => outside,
            (string t, int c, SectionData sd, out float u0, out float v0, out float u1, out float v1) => { u0 = 0; v0 = 0; u1 = 1; v1 = 1; });
    }

    /// Every triangle's front (Unity: cross(b-a, c-a)) must point away from the block centre.
    static bool Outward(List<float> pos, List<int> tris, float cx, float cy, float cz)
    {
        for (int i = 0; i < tris.Count; i += 3)
        {
            int a = tris[i] * 3, b = tris[i + 1] * 3, c = tris[i + 2] * 3;
            float e1x = pos[b] - pos[a], e1y = pos[b + 1] - pos[a + 1], e1z = pos[b + 2] - pos[a + 2];
            float e2x = pos[c] - pos[a], e2y = pos[c + 1] - pos[a + 1], e2z = pos[c + 2] - pos[a + 2];
            float nx = e1y * e2z - e1z * e2y, ny = e1z * e2x - e1x * e2z, nz = e1x * e2y - e1y * e2x;
            float mx = (pos[a] + pos[b] + pos[c]) / 3 - cx, my = (pos[a + 1] + pos[b + 1] + pos[c + 1]) / 3 - cy, mz = (pos[a + 2] + pos[b + 2] + pos[c + 2]) / 3 - cz;
            if (nx * mx + ny * my + nz * mz <= 0) return false;
        }
        return true;
    }

    public static int Main()
    {
        // one stone block at (0,0,0): 6 faces, outward, solid collision; Unity z is flipped
        var s = Empty(); s.Set(0, 0, 0, 1);
        var m = Mesh(s);
        Expect(m.solid.Count == 36 && m.VertexCount == 24, "single cube has 6 faces (got " + m.solid.Count / 6 + ")");
        Expect(m.colTris.Count == 36, "single cube has 6 collision faces");
        Expect(Outward(m.pos, m.solid, 0.5f, 0.5f, -0.5f), "cube faces point outward in Unity (z flipped)");
        Expect(Outward(m.colPos, m.colTris, 0.5f, 0.5f, -0.5f), "collision faces point outward");
        float minZ = float.MaxValue, maxZ = float.MinValue;
        for (int i = 2; i < m.pos.Count; i += 3) { minZ = Math.Min(minZ, m.pos[i]); maxZ = Math.Max(maxZ, m.pos[i]); }
        Expect(minZ == -1 && maxZ == 0, "Minecraft z 0..1 maps to Unity z -1..0");

        // two stones side by side share a hidden face pair
        s.Set(1, 0, 0, 1);
        Expect(Mesh(s).solid.Count == 10 * 6, "two touching cubes cull the shared faces");

        // glass next to glass culls, glass next to stone keeps the glass face hidden by stone
        var g = Empty(); g.Set(0, 0, 0, 2); g.Set(1, 0, 0, 2);
        Expect(Mesh(g).solid.Count == 10 * 6, "glass culls against glass");

        // a block at the section edge is culled by an opaque neighbour section, drawn when the neighbour is unknown
        var e = Empty(); e.Set(15, 0, 0, 1);
        Expect(Mesh(e, 1).solid.Count == 3 * 6, "the 3 faces against an opaque neighbour section are culled, the 3 against air stay (got " + Mesh(e, 1).solid.Count / 6 + ")");
        Expect(Mesh(e, -1).solid.Count == 36, "faces against unknown neighbours are drawn");

        // water: top at 0.875, translucent list, no collision
        var w = Empty(); w.Set(3, 3, 3, 3);
        var wm = Mesh(w);
        Expect(wm.liquid.Count == 36 && wm.solid.Count == 0 && wm.colTris.Count == 0, "water goes to the liquid list without collision");
        float topY = 0; for (int i = 1; i < wm.pos.Count; i += 3) topY = Math.Max(topY, wm.pos[i]);
        Expect(Math.Abs(topY - 3.875f) < 1e-5, "water surface sits at 0.875 (got " + topY + ")");
        w.Set(3, 4, 3, 3);
        Expect(Mesh(w).liquid.Count == 10 * 6, "stacked water culls the shared faces and the lower block fills to 1");

        // flower: 2 planes x 2 sides, no collision
        var f = Empty(); f.Set(2, 2, 2, 4);
        var fm = Mesh(f);
        Expect(fm.solid.Count == 4 * 6 && fm.colTris.Count == 0, "cross plant is 4 quads without collision");

        // slab: 6 faces, top at 0.5, collides; a stone above does not hide the slab's top
        var b = Empty(); b.Set(0, 0, 0, 5); b.Set(0, 1, 0, 1);
        var bm = Mesh(b);
        Expect(bm.colTris.Count >= 36, "slab collides");
        Expect(Outward(bm.pos, bm.solid.GetRange(0, 36), 0.5f, 0.25f, -0.5f), "slab faces point outward");

        // section decoding: palette + cells, uniform sections have no cell array
        var states = new Wire.Writer(); states.U8(0); states.U8(2); states.I32(0); states.I32(1);
        for (int i = 0; i < 4096; i++) { states.U8(0); states.U8((byte)(i == 4095 ? 1 : 0)); }
        var sec = SectionData.From(new CraftBlade.Gen.Section { sx = 1, sy = -4, sz = 2, states = states.ToArray() });
        Expect(sec.StateAt(15, 15, 15) == 1 && sec.StateAt(0, 0, 0) == 0, "section cells decode in y*256+z*16+x order");
        var uni = new Wire.Writer(); uni.U8(0); uni.U8(1); uni.I32(1);
        var us = SectionData.From(new CraftBlade.Gen.Section { states = uni.ToArray() });
        Expect(us.cells == null && us.StateAt(7, 7, 7) == 1, "uniform section decodes without cells");
        us.Set(1, 2, 3, 0);
        Expect(us.StateAt(1, 2, 3) == 0 && us.StateAt(0, 0, 0) == 1, "editing a uniform section expands it");

        Console.WriteLine("mesher tests: " + (failures == 0 ? "all passed" : failures + " failed"));
        return failures == 0 ? 0 : 1;
    }
}
