package gg.craftblade.net;

import gg.craftblade.CraftBlade;
import net.minecraft.network.PacketByteBuf;
import net.minecraft.network.codec.PacketCodec;
import net.minecraft.network.packet.CustomPayload;
import net.minecraft.util.Identifier;

/** Client -> server: a Blade & Sorcery weapon hit a Minecraft entity (see sheets/protocol.json Hit). */
public record HitPayload(int entityId, float damage, int dmgType, float vx, float vy, float vz, boolean thrown) implements CustomPayload {
    public static final Id<HitPayload> ID = new Id<>(Identifier.of(CraftBlade.MOD_ID, "hit"));
    public static final PacketCodec<PacketByteBuf, HitPayload> CODEC = PacketCodec.of(HitPayload::write, HitPayload::new);

    private HitPayload(PacketByteBuf b) {
        this(b.readVarInt(), b.readFloat(), b.readByte(), b.readFloat(), b.readFloat(), b.readFloat(), b.readBoolean());
    }

    private void write(PacketByteBuf b) {
        b.writeVarInt(entityId);
        b.writeFloat(damage);
        b.writeByte(dmgType);
        b.writeFloat(vx);
        b.writeFloat(vy);
        b.writeFloat(vz);
        b.writeBoolean(thrown);
    }

    @Override
    public Id<? extends CustomPayload> getId() { return ID; }
}
