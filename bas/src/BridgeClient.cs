using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace CraftBlade
{
    /// <summary>
    /// Connects to the player's own Minecraft client (127.0.0.1:bridgePort) and keeps retrying.
    /// Socket work runs on background threads; the game thread only touches the queues.
    /// </summary>
    public sealed class BridgeClient
    {
        readonly int port;
        readonly ConcurrentQueue<Gen.Msg> inbox = new ConcurrentQueue<Gen.Msg>();
        readonly BlockingCollection<byte[]> outbox = new BlockingCollection<byte[]>();
        volatile TcpClient client;
        volatile bool running = true;
        int generation;

        public BridgeClient(int port) { this.port = port; }

        public bool Connected { get { return client != null; } }
        public int Generation { get { return generation; } }

        public void Start()
        {
            var t = new Thread(ConnectLoop) { IsBackground = true, Name = "CraftBlade bridge" };
            t.Start();
        }

        public void Stop()
        {
            running = false;
            var c = client;
            if (c != null) c.Close();
        }

        public bool TryReceive(out Gen.Msg m) { return inbox.TryDequeue(out m); }

        public void Send(Gen.Msg m)
        {
            if (client != null) outbox.Add(Wire.Frame(m));
        }

        void ConnectLoop()
        {
            while (running)
            {
                TcpClient c = null;
                try
                {
                    c = new TcpClient();
                    c.NoDelay = true;
                    c.Connect("127.0.0.1", port);
                    while (outbox.Count > 0) { byte[] drop; outbox.TryTake(out drop); }
                    client = c;
                    Interlocked.Increment(ref generation);
                    Log.Info("Connected to Minecraft on port " + port);
                    var stream = c.GetStream();
                    var writer = new Thread(() => WriteLoop(c, stream)) { IsBackground = true, Name = "CraftBlade bridge writer" };
                    writer.Start();
                    Send(new Gen.Hello { protocol = Gen.Msg.PROTOCOL, basVersion = Version.Text });
                    ReadLoop(stream);
                }
                catch (Exception e)
                {
                    if (client != null) Log.Info("Minecraft link ended: " + e.Message);
                }
                finally
                {
                    client = null;
                    if (c != null) c.Close();
                }
                if (running) Thread.Sleep(2000); // Minecraft not started yet, or it closed
            }
        }

        void ReadLoop(NetworkStream s)
        {
            var head = new byte[4];
            while (running)
            {
                ReadFully(s, head, 4);
                int len = (head[0] << 24) | (head[1] << 16) | (head[2] << 8) | head[3];
                if (len < 1 || len > (64 << 20)) throw new IOException("bad frame length " + len);
                var body = new byte[len];
                ReadFully(s, body, len);
                inbox.Enqueue(Wire.Decode(body));
            }
        }

        static void ReadFully(Stream s, byte[] b, int n)
        {
            int got = 0;
            while (got < n)
            {
                int r = s.Read(b, got, n - got);
                if (r <= 0) throw new EndOfStreamException("Minecraft closed the link");
                got += r;
            }
        }

        void WriteLoop(TcpClient c, NetworkStream s)
        {
            try
            {
                while (running && client == c)
                {
                    byte[] f;
                    if (outbox.TryTake(out f, 100)) s.Write(f, 0, f.Length);
                }
            }
            catch (Exception)
            {
                c.Close();
            }
        }
    }
}
