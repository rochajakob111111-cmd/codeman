package gg.craftblade.client;

import gg.craftblade.gen.Msg;
import gg.craftblade.net.ArmsPayload;
import net.fabricmc.fabric.api.client.networking.v1.ClientPlayNetworking;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.input.Input;
import net.minecraft.client.input.KeyboardInput;
import net.minecraft.client.network.ClientPlayerEntity;
import net.minecraft.entity.LivingEntity;
import net.minecraft.util.math.Box;
import net.minecraft.util.math.Vec3d;

/**
 * The VR player's B&S body drives their Minecraft body (position, look, grounded, hands);
 * Minecraft stays the authority on their health, deaths and server teleports.
 */
public final class PoseDriver {
    static boolean helloReceived;
    private static Msg.Pose pose;
    private static Msg.Pose lastApplied;
    private static int teleportSeq;
    private static float lastHealth = -1;
    private static int deadTicks;
    private static boolean driving;

    private PoseDriver() {}

    public static void reset() {
        helloReceived = false;
        resetBody();
    }

    static void resetBody() {
        pose = null;
        lastApplied = null;
        lastHealth = -1;
        deadTicks = 0;
        release();
    }

    static void onPose(Msg.Pose p) {
        if (!Double.isFinite(p.x()) || !Double.isFinite(p.y()) || !Double.isFinite(p.z())) return;
        pose = p;
    }

    /** Called from the server-teleport mixin: B&S must move the player there before poses count again. */
    public static void onServerTeleport() { teleportSeq++; }

    /** Used by the movement-packet mixin. */
    public static boolean driving() { return driving && CraftBladeClient.active(); }

    public static Msg.Pose current() { return lastApplied; }

    static void startTick(MinecraftClient mc) {
        ClientPlayerEntity p = mc.player;
        if (pose == null || pose.teleportSeq() != teleportSeq || p.isDead()) return;
        driving = true;
        // Checked every tick: a respawn creates a new player with keyboard input again.
        if (p.input.getClass() != Input.class) p.input = new Input();
        if (!p.hasNoGravity()) p.setNoGravity(true);
        apply(p, pose);
    }

    /** Puts the player exactly where B&S says; also called right before each movement packet. */
    public static void apply(ClientPlayerEntity p, Msg.Pose q) {
        Vec3d before = p.getPos();
        p.setPosition(q.x(), q.y(), q.z());
        p.setVelocity(Vec3d.ZERO);
        p.setYaw(q.yaw());
        p.setHeadYaw(q.yaw());
        p.setBodyYaw(q.yaw());
        p.setPitch(q.pitch());
        p.setOnGround(q.grounded() != 0);
        if (q.grounded() != 0) p.fallDistance = 0; else if (q.y() < before.y) p.fallDistance += (float) (before.y - q.y());
        lastApplied = q;
    }

    static void endTick(MinecraftClient mc) {
        ClientPlayerEntity p = mc.player;
        Bridge b = CraftBladeClient.bridge;

        // Health and damage: Minecraft decides, B&S shows it.
        float hp = p.getHealth();
        if (lastHealth >= 0 && hp < lastHealth - 0.01f) {
            Vec3d from = attackerPos(mc, p);
            b.send(new Msg.Hurt(lastHealth - hp, from.x, from.y, from.z));
        }
        lastHealth = hp;
        b.send(new Msg.SelfState(p.getX(), p.getY(), p.getZ(), hp, p.getMaxHealth(), (byte) (p.isDead() ? 1 : 0), teleportSeq));

        // Respawn on our own while the player is in VR and cannot click the death screen.
        if (p.isDead()) {
            if (++deadTicks == 60) {
                p.requestRespawn();
                mc.setScreen(null);
            }
        } else {
            deadTicks = 0;
        }

        // Arms for everyone else (and our own third-person view).
        Msg.Pose q = lastApplied;
        if (q != null) {
            ArmsPayload arms = new ArmsPayload(p.getId(), q.lhX(), q.lhY(), q.lhZ(), q.rhX(), q.rhY(), q.rhZ());
            ArmPoses.put(p.getId(), arms);
            if (ClientPlayNetworking.canSend(ArmsPayload.ID)) ClientPlayNetworking.send(arms);
        }
    }

    private static Vec3d attackerPos(MinecraftClient mc, ClientPlayerEntity p) {
        LivingEntity best = null;
        double bestD = 25;
        for (LivingEntity e : mc.world.getEntitiesByClass(LivingEntity.class, new Box(p.getBlockPos()).expand(5), e -> e != p && e.isAlive())) {
            double d = e.squaredDistanceTo(p);
            if (d < bestD) { bestD = d; best = e; }
        }
        return best != null ? best.getPos() : p.getPos();
    }

    /** Give the keyboard back when B&S goes away. */
    private static void release() {
        if (!driving) return;
        driving = false;
        MinecraftClient mc = MinecraftClient.getInstance();
        if (mc.player != null) {
            mc.player.input = new KeyboardInput(mc.options);
            mc.player.setNoGravity(false);
        }
    }
}
