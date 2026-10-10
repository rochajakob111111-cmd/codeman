package gg.craftblade;

import gg.craftblade.gen.Tables;
import net.fabricmc.loader.api.FabricLoader;

import java.io.IOException;
import java.io.Reader;
import java.io.Writer;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Properties;

/** config/craftblade.properties; defaults come from sheets/config.json. */
public final class Config {
    public static int bridgePort = Tables.DEFAULT_BRIDGE_PORT;
    public static int viewRadius = Tables.DEFAULT_VIEW_RADIUS;
    public static int sectionsPerTick = Tables.DEFAULT_SECTIONS_PER_TICK;
    public static double entityRadius = Tables.DEFAULT_ENTITY_RADIUS;
    public static double maxHitReach = Tables.DEFAULT_MAX_HIT_REACH;
    public static double maxThrowReach = Tables.DEFAULT_MAX_THROW_REACH;
    public static int damageCooldownMs = Tables.DEFAULT_DAMAGE_COOLDOWN_MS;

    private Config() {}

    public static void load() {
        Path file = FabricLoader.getInstance().getConfigDir().resolve("craftblade.properties");
        Properties p = new Properties();
        if (Files.exists(file)) {
            try (Reader r = Files.newBufferedReader(file)) {
                p.load(r);
            } catch (IOException e) {
                CraftBlade.LOG.warn("Could not read {}: {}", file, e.toString());
            }
        }
        bridgePort = intOf(p, "bridgePort", bridgePort);
        viewRadius = Math.max(1, Math.min(8, intOf(p, "viewRadius", viewRadius)));
        sectionsPerTick = Math.max(1, intOf(p, "sectionsPerTick", sectionsPerTick));
        entityRadius = doubleOf(p, "entityRadius", entityRadius);
        maxHitReach = doubleOf(p, "maxHitReach", maxHitReach);
        maxThrowReach = doubleOf(p, "maxThrowReach", maxThrowReach);
        damageCooldownMs = intOf(p, "damageCooldownMs", damageCooldownMs);
        if (!Files.exists(file)) {
            p.setProperty("bridgePort", Integer.toString(bridgePort));
            p.setProperty("viewRadius", Integer.toString(viewRadius));
            p.setProperty("sectionsPerTick", Integer.toString(sectionsPerTick));
            p.setProperty("entityRadius", Double.toString(entityRadius));
            p.setProperty("maxHitReach", Double.toString(maxHitReach));
            p.setProperty("maxThrowReach", Double.toString(maxThrowReach));
            p.setProperty("damageCooldownMs", Integer.toString(damageCooldownMs));
            try (Writer w = Files.newBufferedWriter(file)) {
                p.store(w, "CraftBlade (Minecraft x Blade & Sorcery)");
            } catch (IOException e) {
                CraftBlade.LOG.warn("Could not write {}: {}", file, e.toString());
            }
        }
    }

    private static int intOf(Properties p, String k, int d) {
        try { return Integer.parseInt(p.getProperty(k, Integer.toString(d)).trim()); } catch (NumberFormatException e) { return d; }
    }

    private static double doubleOf(Properties p, String k, double d) {
        try { return Double.parseDouble(p.getProperty(k, Double.toString(d)).trim()); } catch (NumberFormatException e) { return d; }
    }
}
