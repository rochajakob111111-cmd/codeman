package gg.craftblade.client;

import gg.craftblade.CraftBlade;
import net.fabricmc.fabric.api.client.message.v1.ClientReceiveMessageEvents;
import net.fabricmc.fabric.api.client.networking.v1.ClientPlayConnectionEvents;
import net.fabricmc.fabric.api.client.screen.v1.ScreenEvents;
import net.fabricmc.loader.api.FabricLoader;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gui.screen.TitleScreen;
import net.minecraft.client.gui.screen.multiplayer.ConnectScreen;
import net.minecraft.client.gui.screen.world.CreateWorldScreen;
import net.minecraft.client.network.ServerAddress;
import net.minecraft.client.network.ServerInfo;
import net.minecraft.server.integrated.IntegratedServer;
import net.minecraft.world.GameMode;
import net.minecraft.world.level.storage.LevelSummary;

import java.io.IOException;
import java.io.Reader;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardOpenOption;
import java.util.Comparator;
import java.util.List;
import java.util.Properties;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/**
 * Melty's play-together contract (config/craftblade/melty.properties, config/craftblade/melty.log):
 *  - join=<address> set: connect straight to the host and log "Melty joined: <address>".
 *  - join empty: load the newest world (or open Create World); with CRAFTBLADE_LAN_PORT set (Melty sets it on
 *    every Play) also open it to others on that port. e4mc then gives it a public address and logs
 *    "Domain assigned: <address>" to latest.log, which is where Melty reads it. Without e4mc we log the LAN
 *    address to melty.log instead.
 */
public final class MeltyLink {
    private static final Pattern E4MC = Pattern.compile("([a-z0-9-]+(?:\\.[a-z0-9-]+)*\\.e4mc\\.link)");
    private static final Path DIR = FabricLoader.getInstance().getConfigDir().resolve("craftblade");
    private static String join = "";
    private static boolean started, opened, announced;
    private static int openedTicks;
    private static int lanPort;

    private MeltyLink() {}

    static void init() {
        Properties p = new Properties();
        Path file = DIR.resolve("melty.properties");
        if (Files.exists(file)) {
            try (Reader r = Files.newBufferedReader(file, StandardCharsets.UTF_8)) {
                p.load(r);
            } catch (IOException e) {
                CraftBlade.LOG.warn("melty.properties unreadable: {}", e.toString());
            }
        }
        join = p.getProperty("join", "").trim();

        ScreenEvents.AFTER_INIT.register((client, screen, w, h) -> {
            if (started || !(screen instanceof TitleScreen) || !meltyLaunch()) return;
            started = true;
            client.execute(() -> begin(client, screen));
        });
        ClientPlayConnectionEvents.JOIN.register((handler, sender, client) -> {
            if (!join.isEmpty()) log("Melty joined: " + join);
        });
        ClientReceiveMessageEvents.GAME.register((message, overlay) -> {
            if (!opened || announced) return;
            Matcher m = E4MC.matcher(message.getString());
            if (m.find()) announce(m.group(1)); // e4mc also logs "Domain assigned: ..." itself
        });
    }

    /** Only take over the title screen when Melty started us (it writes melty.properties). */
    private static boolean meltyLaunch() {
        return Files.exists(DIR.resolve("melty.properties"));
    }

    private static int hostPort() {
        try {
            return Integer.parseInt(System.getenv().getOrDefault("CRAFTBLADE_LAN_PORT", "").trim());
        } catch (NumberFormatException e) {
            return -1;
        }
    }

    private static boolean hosting() {
        return join.isEmpty() && hostPort() > 0;
    }

    private static void begin(MinecraftClient mc, net.minecraft.client.gui.screen.Screen title) {
        if (!join.isEmpty()) {
            ServerAddress addr = ServerAddress.parse(join);
            ServerInfo info = new ServerInfo("Melty host", join, ServerInfo.ServerType.OTHER);
            ConnectScreen.connect(title, mc, addr, info, false, null);
            return;
        }
        mc.getLevelStorage().loadSummaries(mc.getLevelStorage().getLevelList()).thenAccept(list -> mc.execute(() -> {
            List<LevelSummary> worlds = list.stream().filter(s -> !s.isLocked() && !s.requiresConversion()).sorted(Comparator.comparingLong(LevelSummary::getLastPlayed).reversed()).toList();
            if (worlds.isEmpty()) {
                CreateWorldScreen.create(mc, title);
            } else {
                mc.createIntegratedServerLoader().start(worlds.get(0).getName(), () -> mc.setScreen(title));
            }
        }));
    }

    static void tick(MinecraftClient mc) {
        if (!hosting() || opened) {
            if (opened && !announced && ++openedTicks == 300) {
                // no relay address appeared within 15 s: give the LAN address (reachable on the same network only)
                announce(localAddress() + ":" + lanPort);
            }
            return;
        }
        IntegratedServer server = mc.getServer();
        if (server == null || mc.player == null || server.isRemote()) return;
        lanPort = hostPort();
        GameMode mode = mc.interactionManager != null ? mc.interactionManager.getCurrentGameMode() : GameMode.SURVIVAL;
        if (server.openToLan(mode, false, lanPort)) {
            opened = true;
            CraftBlade.LOG.info("Opened to others on port {}", lanPort);
        }
    }

    private static String localAddress() {
        try {
            return java.net.InetAddress.getLocalHost().getHostAddress();
        } catch (IOException e) {
            return "127.0.0.1";
        }
    }

    private static void announce(String address) {
        announced = true;
        log("Melty join address: " + address);
    }

    private static void log(String line) {
        CraftBlade.LOG.info(line);
        try {
            Files.createDirectories(DIR);
            Files.writeString(DIR.resolve("melty.log"), line + System.lineSeparator(), StandardCharsets.UTF_8,
                    StandardOpenOption.CREATE, StandardOpenOption.APPEND);
        } catch (IOException e) {
            CraftBlade.LOG.warn("melty.log unwritable: {}", e.toString());
        }
    }
}
