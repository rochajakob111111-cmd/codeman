package gg.craftblade.net;

import gg.craftblade.CraftBlade;
import net.minecraft.network.PacketByteBuf;
import net.minecraft.network.codec.PacketCodec;
import net.minecraft.network.packet.CustomPayload;
import net.minecraft.util.Identifier;

/**
 * Hand offsets of a VR player from their feet, in their body frame (x right, y up, z forward), in blocks.
 * Client -> server (entityId ignored), then server -> everyone tracking that player.
 */
public record ArmsPayload(int entityId, float lx, float ly, float lz, float rx, float ry, float rz) implements CustomPayload {
    public static final Id<ArmsPayload> ID = new Id<>(Identifier.of(CraftBlade.MOD_ID, "arms"));
    public static final PacketCodec<PacketByteBuf, ArmsPayload> CODEC = PacketCodec.of(ArmsPayload::write, ArmsPayload::new);

    private ArmsPayload(PacketByteBuf b) {
        this(b.readVarInt(), b.readFloat(), b.readFloat(), b.readFloat(), b.readFloat(), b.readFloat(), b.readFloat());
    }

    private void write(PacketByteBuf b) {
        b.writeVarInt(entityId);
        b.writeFloat(lx);
        b.writeFloat(ly);
        b.writeFloat(lz);
        b.writeFloat(rx);
        b.writeFloat(ry);
        b.writeFloat(rz);
    }

    public ArmsPayload withEntity(int id) { return new ArmsPayload(id, lx, ly, lz, rx, ry, rz); }

    public boolean sane() {
        float[] v = {lx, ly, lz, rx, ry, rz};
        for (float f : v) if (!Float.isFinite(f) || Math.abs(f) > 3f) return false;
        return true;
    }

    @Override
    public Id<? extends CustomPayload> getId() { return ID; }
}
