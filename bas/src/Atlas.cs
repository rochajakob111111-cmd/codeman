using System;
using System.Collections.Generic;
using UnityEngine;

namespace CraftBlade
{
    /// <summary>
    /// One 1024x1024 point-filtered atlas of 16x16 slots, filled at runtime with the player's own Minecraft
    /// textures (sent by their Minecraft client). A slot is one texture under one tint colour, so meshes can be
    /// built before a texture arrives: the slot fills in later without remeshing.
    /// </summary>
    public sealed class Atlas
    {
        const int Size = 1024, Tile = 16, PerRow = Size / Tile;
        public readonly Texture2D texture;
        readonly Dictionary<string, int> slots = new Dictionary<string, int>();
        readonly Dictionary<string, Color32[]> pixels = new Dictionary<string, Color32[]>();
        readonly Dictionary<string, List<KeyValuePair<int, int>>> waiting = new Dictionary<string, List<KeyValuePair<int, int>>>(); // name -> (slot, tint)
        readonly HashSet<string> requested = new HashSet<string>();
        readonly Action<string> request;
        bool dirty;

        public Atlas(Action<string> request)
        {
            this.request = request;
            texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "CraftBlade atlas" };
            var magenta = new Color32[Size * Size];
            for (int i = 0; i < magenta.Length; i++) magenta[i] = new Color32(255, 0, 255, 255);
            texture.SetPixels32(magenta);
            texture.Apply(false);
        }

        public void Clear()
        {
            slots.Clear();
            waiting.Clear();
            requested.Clear(); // a new session may come from another Minecraft with other resource packs
            pixels.Clear();
        }

        /// <summary>Mesher.UvLookup: texture name (maybe "#tint") + tint category + section colours.</summary>
        public void Uv(string tex, int tintCategory, SectionData s, out float u0, out float v0, out float u1, out float v1)
        {
            bool tinted = tex.EndsWith("#tint", StringComparison.Ordinal);
            string name = tinted ? tex.Substring(0, tex.Length - 5) : tex;
            int tint = 0xFFFFFF;
            if (tinted && s != null)
            {
                tint = tintCategory == 2 ? s.foliage : tintCategory == 3 ? s.water : s.grass;
                tint &= 0xF0F0F0; // quantise so biome blends don't use up the atlas
            }
            string key = name + "|" + tint;
            int slot;
            if (!slots.TryGetValue(key, out slot))
            {
                slot = slots.Count;
                if (slot >= PerRow * PerRow) slot = 0; // atlas full: reuse the first slot rather than fail
                else
                {
                    slots[key] = slot;
                    Color32[] px;
                    if (pixels.TryGetValue(name, out px)) Fill(slot, px, tint);
                    else
                    {
                        List<KeyValuePair<int, int>> l;
                        if (!waiting.TryGetValue(name, out l)) waiting[name] = l = new List<KeyValuePair<int, int>>();
                        l.Add(new KeyValuePair<int, int>(slot, tint));
                        if (requested.Add(name)) request(name);
                    }
                }
            }
            int col = slot % PerRow, row = slot / PerRow;
            const float inset = 0.02f;
            u0 = (col * Tile + inset) / Size; u1 = ((col + 1) * Tile - inset) / Size;
            v0 = (row * Tile + inset) / Size; v1 = ((row + 1) * Tile - inset) / Size;
        }

        /// <summary>PNG from Minecraft: take the top square (animated textures are vertical strips) and fit it to 16x16.</summary>
        public void OnTexture(string name, byte[] png)
        {
            var px = new Color32[Tile * Tile];
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (png != null && png.Length > 0 && tex.LoadImage(png))
            {
                int w = tex.width, h = tex.height, side = Math.Min(w, h);
                var src = tex.GetPixels32();
                for (int y = 0; y < Tile; y++)
                    for (int x = 0; x < Tile; x++)
                    {
                        int sx = x * side / Tile, sy = (h - side) + y * side / Tile; // Unity rows start at the bottom: top frame = highest rows
                        px[y * Tile + x] = src[sy * w + sx];
                    }
            }
            else
            {
                for (int i = 0; i < px.Length; i++) px[i] = ((i / Tile + i) & 1) == 0 ? new Color32(255, 0, 255, 255) : new Color32(0, 0, 0, 255);
                Log.Warn("Minecraft had no texture " + name);
            }
            UnityEngine.Object.Destroy(tex);
            pixels[name] = px;
            List<KeyValuePair<int, int>> l;
            if (waiting.TryGetValue(name, out l))
            {
                foreach (var kv in l) Fill(kv.Key, px, kv.Value);
                waiting.Remove(name);
            }
        }

        void Fill(int slot, Color32[] px, int tint)
        {
            byte tr = (byte)(tint >> 16), tg = (byte)(tint >> 8), tb = (byte)tint;
            var o = new Color32[px.Length];
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                o[i] = new Color32((byte)(c.r * tr / 255), (byte)(c.g * tg / 255), (byte)(c.b * tb / 255), c.a);
            }
            texture.SetPixels32((slot % PerRow) * Tile, (slot / PerRow) * Tile, Tile, Tile, o);
            dirty = true;
        }

        public void Flush()
        {
            if (!dirty) return;
            texture.Apply(false);
            dirty = false;
        }
    }
}
