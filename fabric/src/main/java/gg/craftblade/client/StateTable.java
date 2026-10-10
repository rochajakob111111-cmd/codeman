package gg.craftblade.client;

import gg.craftblade.gen.Msg;
import gg.craftblade.gen.Tables;
import net.minecraft.block.Block;
import net.minecraft.block.BlockState;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.render.model.BakedModel;
import net.minecraft.client.render.model.BakedQuad;
import net.minecraft.fluid.FluidState;
import net.minecraft.registry.Registries;
import net.minecraft.registry.tag.BlockTags;
import net.minecraft.registry.tag.FluidTags;
import net.minecraft.util.math.BlockPos;
import net.minecraft.util.math.Box;
import net.minecraft.util.math.Direction;
import net.minecraft.util.math.random.Random;
import net.minecraft.util.shape.VoxelShape;
import net.minecraft.world.EmptyBlockView;

import java.util.BitSet;
import java.util.List;

/**
 * Describes block states to B&S (sheets/protocol.json StateDef, sheets/shapes.json) from the player's own
 * Minecraft: the baked block models give the face textures, the shapes give collision.
 */
public final class StateTable {
    private static final BitSet sent = new BitSet();
    /** tint categories, matching the Section message's grass/foliage/water colours */
    public static final int TINT_NONE = 0, TINT_GRASS = 1, TINT_FOLIAGE = 2, TINT_WATER = 3;

    private StateTable() {}

    public static void reset() { sent.clear(); }

    public static int id(BlockState s) { return Block.getRawIdFromState(s); }

    /** Sends the StateDef once per bridge session; must run before any Section or BlockUpdate using it. */
    public static void ensure(MinecraftClient mc, BlockState s) {
        int id = id(s);
        if (sent.get(id)) return;
        sent.set(id);
        CraftBladeClient.bridge.send(describe(mc, s, id));
    }

    /** "minecraft:block/oak_leaves", with "#tint" when Minecraft colours that face by biome. */
    private static String spriteName(BakedQuad q) {
        return q.getSprite().getContents().getId().toString() + (q.hasColor() ? "#tint" : "");
    }

    static Msg.StateDef describe(MinecraftClient mc, BlockState s, int id) {
        String name = Registries.BLOCK.getId(s.getBlock()).toString();
        String[] tex = new String[6];
        java.util.Arrays.fill(tex, "");
        int shape;
        int tint = TINT_NONE;
        boolean opaque = false, collide;
        float[] box = {0, 0, 0, 1, 1, 1};

        VoxelShape collision = s.getCollisionShape(EmptyBlockView.INSTANCE, BlockPos.ORIGIN);
        collide = !collision.isEmpty();
        FluidState fluid = s.getFluidState();

        if (s.isAir()) {
            shape = Tables.SHAPE_AIR;
        } else if (!fluid.isEmpty() && s.getBlock() instanceof net.minecraft.block.FluidBlock) {
            shape = Tables.SHAPE_LIQUID;
            boolean water = fluid.isIn(FluidTags.WATER);
            java.util.Arrays.fill(tex, water ? "minecraft:block/water_still#tint" : "minecraft:block/lava_still");
            tint = water ? TINT_WATER : TINT_NONE;
            collide = false;
        } else {
            BakedModel model = mc.getBlockRenderManager().getModel(s);
            Random rnd = Random.create(42L);
            boolean anyFace = false, tinted = false;
            List<BakedQuad> general = model.getQuads(s, null, rnd);
            for (Direction d : Direction.values()) {
                List<BakedQuad> q = model.getQuads(s, d, rnd);
                BakedQuad pick = q.isEmpty() ? null : q.get(0);
                if (pick == null) for (BakedQuad g : general) if (g.getFace() == d) { pick = g; break; }
                if (pick != null) {
                    tex[d.getId()] = spriteName(pick);
                    tinted |= pick.hasColor();
                    anyFace |= !q.isEmpty();
                }
            }
            if (!anyFace && !collide && !general.isEmpty()) {
                // flowers, grass, saplings, torches: crossed quads with the first texture
                shape = Tables.SHAPE_CROSS;
                java.util.Arrays.fill(tex, spriteName(general.get(0)));
                tinted = general.get(0).hasColor();
            } else if (general.isEmpty() && !anyFace) {
                shape = Tables.SHAPE_AIR; // invisible blocks (barrier, light, structure void)
            } else {
                VoxelShape outline = s.getOutlineShape(EmptyBlockView.INSTANCE, BlockPos.ORIGIN);
                if (Block.isShapeFullCube(outline) || outline.isEmpty()) {
                    shape = Tables.SHAPE_CUBE;
                    opaque = s.isOpaqueFullCube(EmptyBlockView.INSTANCE, BlockPos.ORIGIN);
                } else {
                    shape = Tables.SHAPE_BOX;
                    Box b = outline.getBoundingBox();
                    box = new float[]{(float) b.minX, (float) b.minY, (float) b.minZ, (float) b.maxX, (float) b.maxY, (float) b.maxZ};
                }
                // any side still missing a texture borrows one we have (keeps meshes complete)
                String fallback = "";
                for (String t : tex) if (!t.isEmpty()) { fallback = t; break; }
                for (int i = 0; i < 6; i++) if (tex[i].isEmpty()) tex[i] = fallback;
            }
            if (tinted) tint = s.isIn(BlockTags.LEAVES) ? TINT_FOLIAGE : TINT_GRASS;
        }
        return new Msg.StateDef(id, name, shape, opaque ? 1 : 0, tint, collide ? 1 : 0,
                tex[0], tex[1], tex[2], tex[3], tex[4], tex[5], box[0], box[1], box[2], box[3], box[4], box[5]);
    }
}
