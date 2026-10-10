package gg.craftblade.mixin;

import gg.craftblade.client.PoseDriver;
import net.minecraft.client.network.ClientPlayNetworkHandler;
import net.minecraft.network.packet.s2c.play.PlayerPositionLookS2CPacket;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** The server moved the player (respawn, /tp, correction): B&S has to follow before its poses count again. */
@Mixin(ClientPlayNetworkHandler.class)
public abstract class NetworkHandlerMixin {
    @Inject(method = "onPlayerPositionLook", at = @At("TAIL"))
    private void craftblade$teleported(PlayerPositionLookS2CPacket packet, CallbackInfo ci) {
        PoseDriver.onServerTeleport();
    }
}
