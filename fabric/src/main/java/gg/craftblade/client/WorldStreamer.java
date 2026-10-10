package gg.craftblade.client;

import gg.craftblade.CraftBlade;
import gg.craftblade.Config;
import gg.craftblade.gen.Msg;
import it.unimi.dsi.fastutil.ints.Int2IntOpenHashMap;
import it.unimi.dsi.fastutil.longs.LongOpenHashSet;
import net.minecraft.block.BlockState;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.world.ClientWorld;
import net.minecraft.resource.Resource;
import net.minecraft.util.Identifier;
import net.minecraft.util.math.BlockPos;
import net.minecraft.util.math.ChunkSectionPos;
import net.minecraft.world.biome.BiomeColors;
import net.minecraft.world.chunk.ChunkSection;
import net.minecraft.world.chunk.WorldChunk;

import java.io.ByteArrayOutputStream;
import java.io.DataOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.util.ArrayList;
import java.util.List;
import java.util.Optional;

/** Streams the blocks around the VR player to B&S, nearest sections first, plus live block edits. */
public final class WorldStreamer {
    private static final LongOpenHashSet sentSections = new LongOpenHashSet();
    private static long[] order = new long[0];
    private static int orderRadius = -1;

    private WorldStreamer() {}

    public static void reset() {
        sentSections.clear();
        StateTable.reset();
    }

    static void tick(MinecraftClient mc) {
        ClientWorld w = mc.world;
        Bridge b = CraftBladeClient.bridge;
        int r = Config.viewRadius;
        if (orderRadius != r) buildOrder(r);
        ChunkSectionPos center = ChunkSectionPos.from(mc.player);

        // drop sections that left the radius (with one section of slack so edges don't flicker)
        List<Long> drop = new ArrayList<>();
        for (long key : sentSections) {
            int sx = ChunkSectionPos.unpackX(key), sy = ChunkSectionPos.unpackY(key), sz = ChunkSectionPos.unpackZ(key);
            if (Math.abs(sx - center.getSectionX()) > r + 1 || Math.abs(sy - center.getSectionY()) > r + 1 || Math.abs(sz - center.getSectionZ()) > r + 1) {
                drop.add(key);
            }
        }
        for (long key : drop) {
            sentSections.remove(key);
            b.send(new Msg.SectionClear(ChunkSectionPos.unpackX(key), ChunkSectionPos.unpackY(key), ChunkSectionPos.unpackZ(key)));
        }

        if (b.backlog() > 256) return; // B&S is still digesting
        int budget = Config.sectionsPerTick;
        for (long offset : order) {
            if (budget <= 0) break;
            int sx = center.getSectionX() + ChunkSectionPos.unpackX(offset);
            int sy = center.getSectionY() + ChunkSectionPos.unpackY(offset);
            int sz = center.getSectionZ() + ChunkSectionPos.unpackZ(offset);
            if (sy < w.getBottomSectionCoord() || sy >= w.getTopSectionCoord()) continue;
            long key = ChunkSectionPos.asLong(sx, sy, sz);
            if (sentSections.contains(key)) continue;
            if (!w.getChunkManager().isChunkLoaded(sx, sz)) continue;
            WorldChunk chunk = w.getChunk(sx, sz);
            ChunkSection section = chunk.getSection(w.sectionCoordToIndex(sy));
            b.send(encode(mc, w, section, sx, sy, sz));
            sentSections.add(key);
            budget--;
        }
    }

    /** Offsets in the cube of the radius sorted by distance from the centre section. */
    private static void buildOrder(int r) {
        List<long[]> l = new ArrayList<>();
        for (int x = -r; x <= r; x++) for (int y = -r; y <= r; y++) for (int z = -r; z <= r; z++) {
            l.add(new long[]{ChunkSectionPos.asLong(x, y, z), (long) x * x + 2L * y * y + (long) z * z});
        }
        l.sort((a, c) -> Long.compare(a[1], c[1]));
        order = new long[l.size()];
        for (int i = 0; i < order.length; i++) order[i] = l.get(i)[0];
        orderRadius = r;
    }

    private static Msg.Section encode(MinecraftClient mc, ClientWorld w, ChunkSection section, int sx, int sy, int sz) {
        Int2IntOpenHashMap palette = new Int2IntOpenHashMap();
        palette.defaultReturnValue(-1);
        List<Integer> ids = new ArrayList<>();
        short[] cells = new short[4096];
        for (int y = 0; y < 16; y++) for (int z = 0; z < 16; z++) for (int x = 0; x < 16; x++) {
            BlockState s = section.getBlockState(x, y, z);
            int id = StateTable.id(s);
            int idx = palette.get(id);
            if (idx < 0) {
                StateTable.ensure(mc, s);
                idx = ids.size();
                palette.put(id, idx);
                ids.add(id);
            }
            cells[y * 256 + z * 16 + x] = (short) idx;
        }
        ByteArrayOutputStream bytes = new ByteArrayOutputStream(ids.size() == 1 ? 8 : 8300);
        try (DataOutputStream o = new DataOutputStream(bytes)) {
            o.writeShort(ids.size());
            for (int id : ids) o.writeInt(id);
            if (ids.size() > 1) for (short c : cells) o.writeShort(c); // a one-state section has no cell array
        } catch (IOException e) {
            throw new IllegalStateException(e);
        }
        BlockPos mid = new BlockPos(sx * 16 + 8, sy * 16 + 8, sz * 16 + 8);
        return new Msg.Section(sx, sy, sz,
                BiomeColors.getGrassColor(w, mid), BiomeColors.getFoliageColor(w, mid), BiomeColors.getWaterColor(w, mid),
                bytes.toByteArray());
    }

    /** From the block-change mixin (client thread). */
    public static void onBlockChanged(BlockPos pos, BlockState now) {
        if (!CraftBladeClient.active()) return;
        long key = ChunkSectionPos.asLong(pos.getX() >> 4, pos.getY() >> 4, pos.getZ() >> 4);
        if (!sentSections.contains(key)) return; // it will be sent whole later
        MinecraftClient mc = MinecraftClient.getInstance();
        StateTable.ensure(mc, now);
        CraftBladeClient.bridge.send(new Msg.BlockUpdate(pos.getX(), pos.getY(), pos.getZ(), StateTable.id(now)));
    }

    /** "minecraft:block/stone" -> PNG bytes from the player's own game and resource packs (empty if missing). */
    static void sendTexture(MinecraftClient mc, String name) {
        byte[] png = new byte[0];
        Identifier sprite = Identifier.tryParse(name);
        if (sprite != null) {
            Identifier file = Identifier.of(sprite.getNamespace(), "textures/" + sprite.getPath() + ".png");
            Optional<Resource> res = mc.getResourceManager().getResource(file);
            if (res.isPresent()) {
                try (InputStream in = res.get().getInputStream()) {
                    png = in.readAllBytes();
                } catch (IOException e) {
                    CraftBlade.LOG.warn("texture {} unreadable: {}", file, e.toString());
                }
            }
        }
        CraftBladeClient.bridge.send(new Msg.Texture(name, png));
    }
}
