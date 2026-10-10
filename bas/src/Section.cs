using System.IO;

namespace CraftBlade
{
    /// <summary>One decoded 16x16x16 Minecraft section (sheets/protocol.json Section).</summary>
    public sealed class SectionData
    {
        public int sx, sy, sz;
        public int grass, foliage, water;
        public int[] palette;     // local index -> Minecraft state id
        public ushort[] cells;    // y*256+z*16+x -> local index; null = every cell is palette[0]

        public int StateAt(int x, int y, int z)
        {
            return cells == null ? palette[0] : palette[cells[y * 256 + z * 16 + x]];
        }

        public void Set(int x, int y, int z, int stateId)
        {
            int idx = -1;
            for (int i = 0; i < palette.Length; i++) if (palette[i] == stateId) { idx = i; break; }
            if (idx < 0)
            {
                var np = new int[palette.Length + 1];
                System.Array.Copy(palette, np, palette.Length);
                np[palette.Length] = stateId;
                idx = palette.Length;
                palette = np;
            }
            if (cells == null)
            {
                if (idx == 0) return;
                cells = new ushort[4096];
            }
            cells[y * 256 + z * 16 + x] = (ushort)idx;
        }

        public static SectionData From(Gen.Section m)
        {
            var s = new SectionData { sx = m.sx, sy = m.sy, sz = m.sz, grass = m.grass, foliage = m.foliage, water = m.water };
            var r = new Wire.Reader(m.states, 0);
            int n = (ushort)r.I16();
            if (n < 1) throw new IOException("empty palette");
            s.palette = new int[n];
            for (int i = 0; i < n; i++) s.palette[i] = r.I32();
            if (n > 1)
            {
                s.cells = new ushort[4096];
                for (int i = 0; i < 4096; i++)
                {
                    int v = (ushort)r.I16();
                    if (v >= n) throw new IOException("palette index out of range");
                    s.cells[i] = (ushort)v;
                }
            }
            return s;
        }

        public static long Key(int sx, int sy, int sz)
        {
            return ((long)(uint)(sx & 0x3FFFFF) << 42) | ((long)(uint)(sz & 0x3FFFFF) << 20) | (long)(uint)(sy & 0xFFFFF);
        }
    }
}
