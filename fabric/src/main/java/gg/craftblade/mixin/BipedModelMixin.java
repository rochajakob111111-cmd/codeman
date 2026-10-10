package gg.craftblade.mixin;

import gg.craftblade.client.ArmPoses;
import gg.craftblade.net.ArmsPayload;
import net.minecraft.client.model.ModelPart;
import net.minecraft.client.render.entity.model.BipedEntityModel;
import net.minecraft.entity.LivingEntity;
import net.minecraft.entity.player.PlayerEntity;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** A VR player's Minecraft arms point at their real hands (PlayerEntityModel copies these to the sleeves). */
@Mixin(BipedEntityModel.class)
public abstract class BipedModelMixin {
    @Shadow @Final public ModelPart rightArm;
    @Shadow @Final public ModelPart leftArm;

    /** Shoulder height above the feet and half shoulder width, in blocks. */
    private static final float SHOULDER_Y = 1.375f, SHOULDER_X = 0.3125f;

    @Inject(method = "setAngles(Lnet/minecraft/entity/LivingEntity;FFFFF)V", at = @At("TAIL"))
    private void craftblade$vrArms(LivingEntity entity, float limbAngle, float limbDistance, float age, float headYaw, float headPitch, CallbackInfo ci) {
        if (!(entity instanceof PlayerEntity)) return;
        ArmsPayload a = ArmPoses.get(entity.getId());
        if (a == null) return;
        aim(rightArm, a.rx() - SHOULDER_X, a.ry() - SHOULDER_Y, a.rz(), 1f);
        aim(leftArm, a.lx() + SHOULDER_X, a.ly() - SHOULDER_Y, a.lz(), -1f);
    }

    /** dx right, dy up, dz forward from the shoulder; side = +1 right arm, -1 left arm. */
    private static void aim(ModelPart arm, float dx, float dy, float dz, float side) {
        float len = (float) Math.sqrt(dx * dx + dy * dy + dz * dz);
        if (len < 1e-3f) return;
        dx /= len; dy /= len; dz /= len;
        arm.pitch = (float) -Math.atan2(dz, -dy);
        arm.yaw = 0f;
        arm.roll = (float) Math.asin(Math.max(-1f, Math.min(1f, dx * side))) * side;
    }
}
