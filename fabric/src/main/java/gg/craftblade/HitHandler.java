package gg.craftblade;

import gg.craftblade.gen.Tables;
import gg.craftblade.net.HitPayload;
import net.minecraft.entity.Entity;
import net.minecraft.entity.LivingEntity;
import net.minecraft.entity.damage.DamageSource;
import net.minecraft.server.network.ServerPlayerEntity;

import java.util.HashMap;
import java.util.Map;

/** Server side of a B&S hit: checks reach and cooldown, scales by sheets/damage.json, applies damage and knockback. */
public final class HitHandler {
    private static final Map<Long, Long> lastHit = new HashMap<>();

    private HitHandler() {}

    public static void handle(ServerPlayerEntity player, HitPayload p) {
        if (!player.isAlive() || p.dmgType() < 0 || p.dmgType() >= Tables.DAMAGE.length) return;
        if (!Float.isFinite(p.damage()) || !Float.isFinite(p.vx()) || !Float.isFinite(p.vy()) || !Float.isFinite(p.vz())) return;
        Entity e = player.getServerWorld().getEntityById(p.entityId());
        if (!(e instanceof LivingEntity target) || target == player || !target.isAlive()) return;

        double reach = p.thrown() ? Config.maxThrowReach : Config.maxHitReach;
        if (player.squaredDistanceTo(target) > reach * reach) return;

        long key = ((long) player.getId() << 32) ^ (target.getId() & 0xffffffffL);
        long now = System.currentTimeMillis();
        Long prev = lastHit.get(key);
        if (prev != null && now - prev < Config.damageCooldownMs) return;

        Tables.Damage d = Tables.DAMAGE[p.dmgType()];
        float speed = (float) Math.sqrt(p.vx() * p.vx() + p.vy() * p.vy() + p.vz() * p.vz());
        if (speed < d.minSpeed()) return;
        float amount = Math.min(d.maxMc(), Math.max(0f, p.damage()) * d.scale());
        if (amount <= 0.01f) return;

        lastHit.put(key, now);
        if (lastHit.size() > 4096) lastHit.clear();

        DamageSource src = player.getDamageSources().playerAttack(player);
        // A fresh hit from a different weapon swing should land even inside vanilla's 10-tick immunity.
        target.timeUntilRegen = 0;
        if (target.damage(src, amount)) {
            double h = Math.sqrt(p.vx() * p.vx() + p.vz() * p.vz());
            double kb = Math.min(2.0, speed * d.knockbackPerMs());
            if (h > 1e-3 && kb > 0) target.takeKnockback(kb, -p.vx() / h, -p.vz() / h);
            player.onAttacking(target);
        }
    }
}
