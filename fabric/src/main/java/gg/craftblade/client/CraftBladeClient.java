package gg.craftblade.client;

import gg.craftblade.Config;
import gg.craftblade.gen.Msg;
import gg.craftblade.net.ArmsPayload;
import gg.craftblade.net.HitPayload;
import net.fabricmc.api.ClientModInitializer;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientTickEvents;
import net.fabricmc.fabric.api.client.networking.v1.ClientPlayConnectionEvents;
import net.fabricmc.fabric.api.client.networking.v1.ClientPlayNetworking;
import net.minecraft.SharedConstants;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.world.ClientWorld;

/** Client entry: the VR player's own Minecraft client is the bridge between their B&S and the shared world. */
public class CraftBladeClient implements ClientModInitializer {
    public static Bridge bridge;
    private static int seenGeneration = -1;
    private static boolean welcomed;
    private static ClientWorld welcomedWorld;

    @Override
    public void onInitializeClient() {
        bridge = new Bridge(Config.bridgePort);
        bridge.start();
        ClientTickEvents.START_CLIENT_TICK.register(CraftBladeClient::startTick);
        ClientTickEvents.END_CLIENT_TICK.register(CraftBladeClient::endTick);
        ClientPlayNetworking.registerGlobalReceiver(ArmsPayload.ID, (payload, ctx) -> ArmPoses.put(payload.entityId(), payload));
        ClientPlayConnectionEvents.DISCONNECT.register((handler, client) -> client.execute(CraftBladeClient::leftWorld));
        MeltyLink.init();
    }

    /** True while a B&S player is driving this client. */
    public static boolean active() {
        return bridge != null && bridge.connected() && welcomed;
    }

    private static void resetSession() {
        welcomed = false;
        welcomedWorld = null;
        WorldStreamer.reset();
        EntityStreamer.reset();
        PoseDriver.reset();
        ArmPoses.clear();
    }

    private static void startTick(MinecraftClient mc) {
        if (bridge.generation() != seenGeneration) {
            seenGeneration = bridge.generation();
            resetSession();
        }
        if (!bridge.connected() && welcomed) resetSession();

        Msg m;
        while ((m = bridge.poll()) != null) {
            switch (m) {
                case Msg.Hello h -> {
                    if (h.protocol() != Msg.PROTOCOL) {
                        gg.craftblade.CraftBlade.LOG.error("B&S mod speaks bridge protocol {}, this Minecraft mod {}: update both", h.protocol(), Msg.PROTOCOL);
                    } else {
                        welcomed = false; // sent below once a world is loaded
                        welcomedWorld = null;
                        PoseDriver.helloReceived = true;
                    }
                }
                case Msg.Pose p -> PoseDriver.onPose(p);
                case Msg.Hit hit -> {
                    if (mc.player != null && ClientPlayNetworking.canSend(HitPayload.ID)) {
                        ClientPlayNetworking.send(new HitPayload(hit.entityId(), hit.damage(), hit.dmgType(), hit.vx(), hit.vy(), hit.vz(), hit.thrown() != 0));
                    }
                }
                case Msg.TextureRequest t -> WorldStreamer.sendTexture(mc, t.name());
                default -> { }
            }
        }

        if (PoseDriver.helloReceived && mc.world != null && mc.player != null && mc.world != welcomedWorld) {
            resetSessionKeepHello();
            var pos = mc.player.getBlockPos();
            bridge.send(new Msg.Welcome(Msg.PROTOCOL, SharedConstants.getGameVersion().getName(), mc.player.getGameProfile().getName(), pos.getX(), pos.getY(), pos.getZ()));
            welcomed = true;
            welcomedWorld = mc.world;
        }
        if (active() && mc.player != null) PoseDriver.startTick(mc);
    }

    /** Left the Minecraft world while B&S stays connected: the next world sends a new Welcome. */
    private static void leftWorld() {
        welcomed = false;
        welcomedWorld = null;
        resetSessionKeepHello();
        ArmPoses.clear();
    }

    private static void resetSessionKeepHello() {
        WorldStreamer.reset();
        EntityStreamer.reset();
        PoseDriver.resetBody();
    }

    private static void endTick(MinecraftClient mc) {
        MeltyLink.tick(mc);
        if (!active() || mc.world == null || mc.player == null) return;
        PoseDriver.endTick(mc);
        WorldStreamer.tick(mc);
        EntityStreamer.tick(mc);
    }
}
