using System.IO;
using ThunderRoad;
using UnityEngine;

namespace CraftBlade
{
    [System.Serializable]
    public class Settings
    {
        public int bridgePort = Gen.Tables.DEFAULT_BRIDGE_PORT;
        public float anchorY = Gen.Tables.DEFAULT_ANCHOR_Y;
    }

    /// <summary>
    /// Entry point. Blade & Sorcery loads this DLL from Mods/CraftBlade by itself and runs every ThunderScript.
    /// Waits for the player's Minecraft client (127.0.0.1), then mirrors its world in whatever level the player is in.
    /// </summary>
    public class CraftBladeScript : ThunderScript
    {
        BridgeClient bridge;
        Atlas atlas;
        BlockWorld world;
        PlayerLink player;
        EntityProxies entities;
        int seenGeneration = -1;
        bool inSession;

        public override void ScriptLoaded(ModManager.ModData modData)
        {
            base.ScriptLoaded(modData);
            var settings = new Settings();
            try
            {
                var file = Path.Combine(modData.fullPath, "craftblade.json");
                if (File.Exists(file)) JsonUtility.FromJsonOverwrite(File.ReadAllText(file), settings);
            }
            catch (System.Exception e) { Log.Warn("craftblade.json unreadable: " + e.Message); }
            Coords.anchorY = settings.anchorY;
            bridge = new BridgeClient(settings.bridgePort);
            bridge.Start();
            Log.Info("Loaded " + Version.Text + "; waiting for Minecraft on 127.0.0.1:" + settings.bridgePort);
        }

        public override void ScriptUpdate()
        {
            base.ScriptUpdate();
            if (bridge == null || Player.local == null || Player.local.creature == null) return;
            if (world == null)
            {
                atlas = new Atlas(name => bridge.Send(new Gen.TextureRequest { name = name }));
                world = new BlockWorld(atlas);
                player = new PlayerLink(bridge, world);
                entities = new EntityProxies(bridge);
            }
            if (bridge.Generation != seenGeneration) { seenGeneration = bridge.Generation; inSession = false; }

            var deadline = Time.realtimeSinceStartup + 0.006f; // keep frames smooth in VR
            Gen.Msg m;
            while (Time.realtimeSinceStartup < deadline && bridge.TryReceive(out m)) Handle(m);

            if (!inSession) return;
            player.Update();
            entities.Update();
            world.Update(player.PlayerUnity);
        }

        void Handle(Gen.Msg m)
        {
            switch (m.Id)
            {
                case Gen.Welcome.ID:
                    var w = (Gen.Welcome)m;
                    if (w.protocol != Gen.Msg.PROTOCOL) { Log.Warn("Minecraft mod speaks protocol " + w.protocol + ", this mod " + Gen.Msg.PROTOCOL + ": update both"); return; }
                    entities.Clear();
                    world.Reset();
                    player.OnWelcome(w);
                    inSession = true;
                    break;
                case Gen.StateDef.ID: world.OnStateDef((Gen.StateDef)m); break;
                case Gen.Texture.ID: var t = (Gen.Texture)m; atlas.OnTexture(t.name, t.png); break;
                case Gen.Section.ID: world.OnSection((Gen.Section)m); break;
                case Gen.SectionClear.ID: world.OnSectionClear((Gen.SectionClear)m); break;
                case Gen.BlockUpdate.ID: world.OnBlockUpdate((Gen.BlockUpdate)m); break;
                case Gen.EntityState.ID: if (inSession) entities.OnState((Gen.EntityState)m); break;
                case Gen.EntityRemove.ID: entities.OnRemove((Gen.EntityRemove)m); break;
                case Gen.SelfState.ID: player.OnSelfState((Gen.SelfState)m); break;
                case Gen.Hurt.ID: player.OnHurt((Gen.Hurt)m); break;
            }
        }

        public override void ScriptDisable()
        {
            base.ScriptDisable();
            if (entities != null) entities.Dispose();
            if (bridge != null) bridge.Stop();
        }
    }
}
