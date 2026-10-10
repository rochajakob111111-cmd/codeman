using System;
using System.IO;
using System.Text;

namespace CraftBlade
{
    /// <summary>Big-endian encoding matching Java's DataOutputStream (sheets/protocol.json).</summary>
    public static class Wire
    {
        public sealed class Reader
        {
            readonly byte[] b;
            int p;
            public Reader(byte[] bytes, int offset) { b = bytes; p = offset; }
            public int Position { get { return p; } }

            void Need(int n) { if (p + n > b.Length) throw new IOException("message truncated"); }
            public byte U8() { Need(1); return b[p++]; }
            public int I32() { Need(4); int v = (b[p] << 24) | (b[p + 1] << 16) | (b[p + 2] << 8) | b[p + 3]; p += 4; return v; }
            public short I16() { Need(2); short v = (short)((b[p] << 8) | b[p + 1]); p += 2; return v; }
            public long I64() { long hi = (uint)I32(); long lo = (uint)I32(); return (hi << 32) | lo; }
            public float F32() { return BitConverter.ToSingle(BitConverter.GetBytes(I32()), 0); }
            public double F64() { return BitConverter.Int64BitsToDouble(I64()); }
            public string Str()
            {
                Need(2);
                int n = (b[p] << 8) | b[p + 1]; p += 2;
                Need(n);
                // Java writeUTF is modified UTF-8; identical to UTF-8 for the names we send (no NUL, no surrogates).
                string s = Encoding.UTF8.GetString(b, p, n); p += n;
                return s;
            }
            public byte[] Bytes()
            {
                int n = I32();
                if (n < 0 || n > (64 << 20)) throw new IOException("bad length " + n);
                Need(n);
                var r = new byte[n];
                Buffer.BlockCopy(b, p, r, 0, n); p += n;
                return r;
            }
        }

        public sealed class Writer
        {
            readonly MemoryStream m = new MemoryStream(64);
            public void U8(byte v) { m.WriteByte(v); }
            public void I32(int v) { m.WriteByte((byte)(v >> 24)); m.WriteByte((byte)(v >> 16)); m.WriteByte((byte)(v >> 8)); m.WriteByte((byte)v); }
            public void I64(long v) { I32((int)(v >> 32)); I32((int)v); }
            public void F32(float v) { I32(BitConverter.ToInt32(BitConverter.GetBytes(v), 0)); }
            public void F64(double v) { I64(BitConverter.DoubleToInt64Bits(v)); }
            public void Str(string s)
            {
                var bytes = Encoding.UTF8.GetBytes(s ?? "");
                if (bytes.Length > 65535) throw new IOException("string too long");
                m.WriteByte((byte)(bytes.Length >> 8)); m.WriteByte((byte)bytes.Length);
                m.Write(bytes, 0, bytes.Length);
            }
            public void Bytes(byte[] v) { v = v ?? new byte[0]; I32(v.Length); m.Write(v, 0, v.Length); }
            public byte[] ToArray() { return m.ToArray(); }
        }

        /// <summary>u32 length | u8 id | fields.</summary>
        public static byte[] Frame(Gen.Msg msg)
        {
            var w = new Writer();
            w.I32(0);
            w.U8((byte)msg.Id);
            msg.Write(w);
            var f = w.ToArray();
            int len = f.Length - 4;
            f[0] = (byte)(len >> 24); f[1] = (byte)(len >> 16); f[2] = (byte)(len >> 8); f[3] = (byte)len;
            return f;
        }

        /// <summary>Decodes one frame body (id + fields).</summary>
        public static Gen.Msg Decode(byte[] body)
        {
            var r = new Reader(body, 1);
            return Gen.Msg.Read(body[0], r);
        }
    }
}
