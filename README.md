# CraftBlade: Minecraft x Blade & Sorcery

Step into your Minecraft world in VR with Blade & Sorcery's physics combat.

A Blade & Sorcery player joins a Minecraft world and stands inside it: the blocks around them are rebuilt in
B&S from their own Minecraft's textures, and they walk, climb and fall on them with B&S physics. Zombies,
skeletons, pillagers, villagers and other players appear as real B&S creatures. Slash, stab, smash or throw
your B&S weapons at them, and Minecraft takes the damage: they flinch, get knocked back and die as B&S
ragdolls. In Minecraft, everyone else sees the VR player's body moving and their arms following their real
hands.

## What players need
- **Minecraft: Java Edition** (the host game; the release brings a portable Prism Launcher with Minecraft
  1.21.1 + Fabric, and the player signs in once with their own account).
- **Blade & Sorcery** (PC VR, 1.0+) and a VR headset, for the player who goes in as the B&S fighter. The mod
  runs from the player's own B&S install and needs no extra loader: B&S loads script mods from its Mods folder.
- Both games run at the same time on the VR player's PC.

## Playing together
One player hosts (their Minecraft world opens to others, with a public address from e4mc so nobody needs to
forward ports) and friends join through Melty's join link. Up to 8 players (Minecraft's limit for an opened
world). In this first version every player who joins through Melty plays as a B&S fighter. Plain Minecraft
players can join the same world from Minecraft with the CraftBlade mod installed.

## How it works
| Part | Where | What it does |
|---|---|---|
| `fabric/` | Fabric mod in the Minecraft instance | Streams nearby blocks, textures and entities to B&S over `127.0.0.1`; lets the B&S body drive the Minecraft body; server applies B&S hits; relays VR arms; Melty host/join |
| `bas/` | B&S script mod (`Mods/CraftBlade`) | Builds the blocks as meshes + colliders, places the player, sends their pose, spawns creature/box proxies, sends hits |
| `sheets/` | design | The source of truth: protocol, shapes, damage, entity mapping, config, hooks, package |
| `tools/` | | `gen.py` (code from sheets), `preflight.py` (checks the sheets), `package.py` (builds a release) |
| `tests/` | | Java/C# protocol roundtrip, mesher unit tests |

Nothing from either game is copied: Minecraft textures are read live from the player's own Minecraft client,
B&S creatures come from the player's own B&S catalog.

## Build
```
python3 tools/gen.py && python3 tools/preflight.py -v
python3 tests/protocol_roundtrip.py && python3 tests/run_cs_tests.py
python tools/package.py --bas "C:/Program Files (x86)/Steam/steamapps/common/Blade & Sorcery"
```

## Status
Source written and checked as far as possible without the games (see `MODLOG.md`): the protocol passes the
Java<->C# roundtrip test, the mesher passes its unit tests, the B&S mod compiles against Unity 2021.3, and the
Fabric code passes a syntax check. **Not yet built or run in either game.**
