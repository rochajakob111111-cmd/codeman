package gg.craftblade.mixin;

import gg.craftblade.client.WorldStreamer;
import net.minecraft.block.BlockState;
import net.minecraft.client.world.ClientWorld;
import net.minecraft.util.math.BlockPos;
import net.minecraft.world.World;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** Block edits in the client world go to B&S as they happen. */
@Mixin(World.class)
public abstract class WorldMixin {
    @Inject(method = "onBlockChanged", at = @At("TAIL"))
    private void craftblade$changed(BlockPos pos, BlockState oldBlock, BlockState newBlock, CallbackInfo ci) {
        if ((Object) this instanceof ClientWorld) WorldStreamer.onBlockChanged(pos.toImmutable(), newBlock);
    }
}
