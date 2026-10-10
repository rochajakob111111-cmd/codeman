using ThunderRoad;
using UnityEngine;

namespace CraftBlade
{
    /// <summary>
    /// The VR player's side of the link: puts them into the block world, sends their pose to their Minecraft
    /// body 20 times a second, and mirrors Minecraft's verdict on their health, deaths and teleports.
    /// </summary>
    public sealed class PlayerLink
    {
        readonly BridgeClient bridge;
        readonly BlockWorld world;
        bool placed;
        double startX, startY, startZ;
        int appliedTeleport = -1;
        float nextPose;
        float mcHealth = 20, mcMax = 20;
        bool mcDead;

        public PlayerLink(BridgeClient bridge, BlockWorld world) { this.bridge = bridge; this.world = world; }

        public void OnWelcome(Gen.Welcome m)
        {
            Coords.ox = m.originX; Coords.oy = m.originY; Coords.oz = m.originZ;
            startX = m.originX + 0.5; startY = m.originY; startZ = m.originZ + 0.5;
            placed = false;
            appliedTeleport = -1;
            Log.Info("Joined Minecraft " + m.mcVersion + " as " + m.playerName + " at " + m.originX + " " + m.originY + " " + m.originZ);
        }

        public void OnSelfState(Gen.SelfState m)
        {
            mcHealth = m.health; mcMax = Mathf.Max(1f, m.maxHealth);
            mcDead = m.dead != 0;
            if (m.teleportSeq != appliedTeleport)
            {
                // Minecraft moved us (respawn, /tp, server correction): follow, then poses count again
                appliedTeleport = m.teleportSeq;
                startX = m.x; startY = m.y; startZ = m.z;
                placed = false;
            }
        }

        public void OnHurt(Gen.Hurt m)
        {
            Log.Info("Hit in Minecraft for " + m.amount.ToString("0.0"));
        }

        static Creature Body { get { return Player.local != null ? Player.local.creature : null; } }

        public void Update()
        {
            var body = Body;
            if (body == null) return;

            if (!placed)
            {
                // Hold the player at the spot until the blocks under them exist, then let go.
                if (FarFromOrigin(startX, startZ)) Recenter(startX, startY, startZ);
                Player.local.Teleport(Coords.ToUnity(startX, startY + 0.05, startZ), Player.local.transform.rotation);
                if (world.ReadyAt(startX, startY - 1, startZ) && world.ReadyAt(startX, startY, startZ)) placed = true;
                else return;
            }

            // Minecraft owns health; B&S shows it and never kills the player on its own.
            body.maxHealth = 100f;
            body.currentHealth = mcDead ? 1f : Mathf.Max(1f, 100f * mcHealth / mcMax);

            double x, y, z;
            Coords.ToMinecraft(body.transform.position, out x, out y, out z);
            if (FarFromOrigin(x, z)) Recenter(x, y, z);

            if (Time.unscaledTime < nextPose) return;
            nextPose = Time.unscaledTime + 0.05f;

            Transform head = Player.local.head != null ? Player.local.head.transform : (Camera.main != null ? Camera.main.transform : body.transform);
            float yawU = head.eulerAngles.y;
            float pitch = Mathf.DeltaAngle(0f, head.eulerAngles.x);
            var feet = body.transform.position;
            var inv = Quaternion.Inverse(Quaternion.Euler(0f, yawU, 0f));
            var lh = inv * (body.handLeft.transform.position - feet);
            var rh = inv * (body.handRight.transform.position - feet);
            bridge.Send(new Gen.Pose
            {
                x = x, y = y, z = z,
                yaw = Coords.YawToMinecraft(yawU), pitch = Mathf.Clamp(pitch, -90f, 90f),
                grounded = (byte)(Player.local.locomotion != null && Player.local.locomotion.isGrounded ? 1 : 0),
                lhX = lh.x, lhY = lh.y, lhZ = lh.z, rhX = rh.x, rhY = rh.y, rhZ = rh.z,
                teleportSeq = appliedTeleport,
            });
        }

        static bool FarFromOrigin(double x, double z)
        {
            return System.Math.Abs(x - Coords.ox) > 256 || System.Math.Abs(z - Coords.oz) > 256;
        }

        /// <summary>Floating origin: keep Unity coordinates small however far the player walks in Minecraft.</summary>
        void Recenter(double x, double y, double z)
        {
            var body = Body;
            double px = 0, py = 0, pz = 0;
            if (body != null) Coords.ToMinecraft(body.transform.position, out px, out py, out pz);
            Coords.ox = System.Math.Floor(x); Coords.oy = System.Math.Floor(y); Coords.oz = System.Math.Floor(z);
            world.Reposition();
            EntityProxies.Instance?.Reposition();
            if (body != null && placed) Player.local.Teleport(Coords.ToUnity(px, py, pz), Player.local.transform.rotation);
        }

        public Vector3 PlayerUnity { get { var b = Body; return b != null ? b.transform.position : Vector3.zero; } }
    }
}
