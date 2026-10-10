package gg.craftblade;

import gg.craftblade.net.ArmsPayload;
import gg.craftblade.net.HitPayload;
import net.fabricmc.api.ModInitializer;
import net.fabricmc.fabric.api.networking.v1.PayloadTypeRegistry;
import net.fabricmc.fabric.api.networking.v1.PlayerLookup;
import net.fabricmc.fabric.api.networking.v1.ServerPlayNetworking;
import net.minecraft.server.network.ServerPlayerEntity;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

/** Common entry (runs on the host's integrated server and on dedicated servers): hits and arm relay. */
public class CraftBlade implements ModInitializer {
    public static final String MOD_ID = "craftblade";
    public static final Logger LOG = LoggerFactory.getLogger("CraftBlade");

    @Override
    public void onInitialize() {
        Config.load();
        PayloadTypeRegistry.playC2S().register(HitPayload.ID, HitPayload.CODEC);
        PayloadTypeRegistry.playC2S().register(ArmsPayload.ID, ArmsPayload.CODEC);
        PayloadTypeRegistry.playS2C().register(ArmsPayload.ID, ArmsPayload.CODEC);

        ServerPlayNetworking.registerGlobalReceiver(HitPayload.ID, (payload, ctx) -> HitHandler.handle(ctx.player(), payload));
        ServerPlayNetworking.registerGlobalReceiver(ArmsPayload.ID, (payload, ctx) -> {
            if (!payload.sane()) return;
            ServerPlayerEntity sender = ctx.player();
            ArmsPayload out = payload.withEntity(sender.getId());
            for (ServerPlayerEntity other : PlayerLookup.tracking(sender)) {
                if (other != sender && ServerPlayNetworking.canSend(other, ArmsPayload.ID)) {
                    ServerPlayNetworking.send(other, out);
                }
            }
        });
        LOG.info("CraftBlade loaded (bridge protocol {})", gg.craftblade.gen.Msg.PROTOCOL);
    }
}
