package gg.craftblade.client;

import gg.craftblade.Config;
import gg.craftblade.gen.Msg;
import it.unimi.dsi.fastutil.ints.IntOpenHashSet;
import net.minecraft.client.MinecraftClient;
import net.minecraft.entity.LivingEntity;
import net.minecraft.entity.player.PlayerEntity;
import net.minecraft.registry.Registries;

import java.util.List;

/** Sends every living entity near the VR player (mobs and other players) to B&S each tick. */
public final class EntityStreamer {
    private static IntOpenHashSet present = new IntOpenHashSet();

    private EntityStreamer() {}

    public static void reset() { present = new IntOpenHashSet(); }

    static void tick(MinecraftClient mc) {
        Bridge b = CraftBladeClient.bridge;
        IntOpenHashSet now = new IntOpenHashSet();
        List<LivingEntity> list = mc.world.getEntitiesByClass(LivingEntity.class,
                mc.player.getBoundingBox().expand(Config.entityRadius), e -> e != mc.player && !e.isSpectator() && !e.isInvisible());
        for (LivingEntity e : list) {
            now.add(e.getId());
            int flags = 0;
            if (e.isAlive()) flags |= 1;
            if (e instanceof PlayerEntity) flags |= 2;
            if (e.hurtTime > 0 && e.hurtTime == e.maxHurtTime) flags |= 4;
            if (e.handSwinging) flags |= 8;
            b.send(new Msg.EntityState(e.getId(), Registries.ENTITY_TYPE.getId(e.getType()).toString(), e.getName().getString(),
                    e.getX(), e.getY(), e.getZ(), e.getBodyYaw(), e.getPitch(), e.getWidth(), e.getHeight(),
                    e.getHealth(), e.getMaxHealth(), flags));
        }
        for (int id : present) if (!now.contains(id)) b.send(new Msg.EntityRemove(id));
        present = now;
    }
}
