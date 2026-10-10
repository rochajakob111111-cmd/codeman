package gg.craftblade.mixin;

import gg.craftblade.client.PoseDriver;
import gg.craftblade.gen.Msg;
import net.minecraft.client.network.ClientPlayerEntity;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** Movement packets carry exactly the B&S position and grounded flag, so server fall damage stays right. */
@Mixin(ClientPlayerEntity.class)
public abstract class ClientPlayerMixin {
    @Inject(method = "sendMovementPackets", at = @At("HEAD"))
    private void craftblade$beforeMove(CallbackInfo ci) {
        Msg.Pose q = PoseDriver.current();
        if (q != null && PoseDriver.driving()) PoseDriver.apply((ClientPlayerEntity) (Object) this, q);
    }
}
