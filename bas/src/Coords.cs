using UnityEngine;

namespace CraftBlade
{
    /// <summary>
    /// Minecraft (right-handed, +Z south, yaw 0 = +Z, blocks = metres) to Unity (left-handed): z flips,
    /// and an origin block is subtracted so Unity coordinates stay small (floating origin).
    /// </summary>
    public static class Coords
    {
        public static double ox, oy, oz;
        public static float anchorY = Gen.Tables.DEFAULT_ANCHOR_Y;

        public static Vector3 ToUnity(double x, double y, double z)
        {
            return new Vector3((float)(x - ox), (float)(y - oy) + anchorY, (float)(oz - z));
        }

        public static void ToMinecraft(Vector3 u, out double x, out double y, out double z)
        {
            x = u.x + ox;
            y = u.y - anchorY + oy;
            z = oz - u.z;
        }

        /// <summary>Direction (velocity) from Unity axes to Minecraft axes.</summary>
        public static Vector3 DirToMinecraft(Vector3 d) { return new Vector3(d.x, d.y, -d.z); }

        /// <summary>Minecraft yaw (0 = +Z, 90 = -X) to Unity yaw (0 = +Z, 90 = +X).</summary>
        public static float YawToUnity(float mcYaw) { return 180f + mcYaw; }
        public static float YawToMinecraft(float unityYaw) { return Mathf.DeltaAngle(0f, unityYaw - 180f); }
    }
}
