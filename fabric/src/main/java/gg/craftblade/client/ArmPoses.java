package gg.craftblade.client;

import gg.craftblade.net.ArmsPayload;

import java.util.HashMap;
import java.util.Map;

/** Latest hand offsets per player entity id, for drawing VR players' arms. Game thread only. */
public final class ArmPoses {
    public record Entry(ArmsPayload arms, long time) {}

    private static final Map<Integer, Entry> poses = new HashMap<>();

    private ArmPoses() {}

    public static void put(int entityId, ArmsPayload p) {
        if (p.sane()) poses.put(entityId, new Entry(p, System.currentTimeMillis()));
    }

    /** The pose if it is fresh (a VR player that stops sending goes back to normal arms after a second). */
    public static ArmsPayload get(int entityId) {
        Entry e = poses.get(entityId);
        if (e == null) return null;
        if (System.currentTimeMillis() - e.time() > 1000) { poses.remove(entityId); return null; }
        return e.arms();
    }

    public static void clear() { poses.clear(); }
}
