# MODLOG: CraftBlade (Minecraft: Java Edition x Blade & Sorcery)

## Idea (from the creator)
- Host: Minecraft. B&S brings its physics combat and its creatures into the Minecraft world.
- First version: the B&S player. They are inside the Minecraft world (blocks rebuilt in B&S with the
  player's own Minecraft textures). Minecraft mobs and players appear as B&S creatures they fight with full VR
  physics. In Minecraft the B&S player moves and swings as they do.
- Multiplayer, both ways (B&S players and MC players in one world). Later: the B&S item spawner book inside
  Minecraft and B&S-style physics melee for desktop players (agreed, deferred by the creator's "first just the
  B&S player").

## Route
Passthrough by state exchange (mashup-mods pattern 2 / geometry transfer), SkyCraft lineage but reversed
roles: the VR player's own Minecraft client is the bridge. It is a normal client in the shared world (so
Minecraft's own server/LAN/e4mc multiplayer and accounts work unchanged) and talks to their B&S on localhost.
- Minecraft: Fabric (Melty installs it via Prism's mmc-pack.json), 1.21.1, Yarn mappings, Fabric API.
- B&S: native script mod (ThunderScript in a DLL under StreamingAssets/Mods). B&S 1.0 loads it by itself:
  no loader Melty has to install. Unity 2021.3 (Mono).

## Environment facts (this build machine)
- Cloud Linux container: neither game installed; no VR.
- Network blocks maven.fabricmc.net, Mojang, Modrinth, GitHub releases, melty.gg. Maven Central, NuGet,
  Gradle plugin portal, apt and git clone from GitHub work.
- So: Fabric code cannot be compiled here (no Minecraft jar / Loom), only syntax-checked with javac.
  The B&S code compiles against Unity 2021.3.33 reference assemblies (NuGet UnityEngine.Modules) plus
  ThunderRoad stand-ins (`bas/stubs`, never shipped).

## Verified here
- `tests/protocol_roundtrip.py`: Java writes all 14 messages, C# decodes, re-encodes byte-identical, all 88
  fields equal.
- `tests/run_cs_tests.py`: mesher (culling, z flip and winding, liquids, cross plants, slabs, section decode).
- `mcs` compile of all of bas/src against Unity references: 0 errors, 0 warnings.
- `javac` on fabric/src: no syntax errors (symbol resolution needs Minecraft).

## Not verified (every `verified: false` row in the sheets)
- All game API calls listed in sheets/hooks.json (Yarn 1.21.1 names, ThunderRoad 1.0 members).
- Pinned versions: Loom 1.9-SNAPSHOT, Yarn 1.21.1+build.3, Fabric API 0.107.0+1.21.1, Loader 0.16.10,
  Prism 9.4, B&S manifest GameVersion "1.3.1" (Melty sees players on B&S 1.3.1).
- Damage scales, creature ids HumanMale/HumanFemale, URP shader names and keywords, collider layer.
- Melty recipe fields (docs/recipe-draft.json), e4mc's chat line format.

## Next steps (on a PC with both games)
1. `cd fabric && gradlew build`; fix any API name mismatches; `gradlew runClient` and open a world.
2. `dotnet build bas -p:BasDir=...`; copy bas/mod + DLL into Mods/CraftBlade; start B&S in VR; check
   Player.log for "[CraftBlade] Connected to Minecraft".
3. Slice order: one stone cube visible -> standing on blocks -> textures -> zombie proxy -> hit kills it in MC.
4. Two copies of Minecraft (host + joiner) for the multiplayer test.

## Melty (connected)
- Draft created: CraftBlade, modId adc4e57f-52f4-4ebf-a84e-a0daf5e6098a, linked to this repo.
  Studio: https://melty.gg/studio/adc4e57f-52f4-4ebf-a84e-a0daf5e6098a
- docs/recipe-draft.json passes validate_recipe; one_click: yes (Melty runs Prism's first start and signs it
  in with the player's Minecraft account; B&S starts alongside). Layout copied from SkyCraft's live recipe.
- setup_multiplayer accepted the block: CRAFTBLADE_LAN_PORT={port} on every Play, address from latest.log
  after "Domain assigned: " (e4mc), join via melty.properties join=. Not tested: needs two Minecraft copies.
- Closest existing: "Blocks & Sorcery" (B&S primary, MC blocks in B&S arenas, single player).
