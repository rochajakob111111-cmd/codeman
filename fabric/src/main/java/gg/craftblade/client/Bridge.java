package gg.craftblade.client;

import gg.craftblade.CraftBlade;
import gg.craftblade.gen.Msg;

import java.io.*;
import java.net.InetAddress;
import java.net.ServerSocket;
import java.net.Socket;
import java.util.concurrent.ConcurrentLinkedQueue;
import java.util.concurrent.LinkedBlockingQueue;
import java.util.concurrent.TimeUnit;

/**
 * Localhost TCP link to the Blade & Sorcery mod on the same PC. One connection at a time.
 * Frame: u32 length | u8 id | fields (big-endian), see sheets/protocol.json.
 * Reading and writing happen on their own threads; the game thread only touches the queues.
 */
public final class Bridge {
    private final int port;
    private final ConcurrentLinkedQueue<Msg> inbox = new ConcurrentLinkedQueue<>();
    private final LinkedBlockingQueue<byte[]> outbox = new LinkedBlockingQueue<>();
    private volatile Socket socket;
    private volatile int generation;

    public Bridge(int port) { this.port = port; }

    public void start() {
        Thread t = new Thread(this::acceptLoop, "CraftBlade bridge");
        t.setDaemon(true);
        t.start();
    }

    public boolean connected() { return socket != null; }

    /** Changes each time B&S (re)connects, so the game thread knows to start over. */
    public int generation() { return generation; }

    public Msg poll() { return inbox.poll(); }

    public void send(Msg m) {
        if (socket == null) return;
        try {
            ByteArrayOutputStream bytes = new ByteArrayOutputStream(64);
            DataOutputStream o = new DataOutputStream(bytes);
            o.writeInt(0);
            o.writeByte(m.id());
            m.write(o);
            byte[] frame = bytes.toByteArray();
            int len = frame.length - 4;
            frame[0] = (byte) (len >>> 24); frame[1] = (byte) (len >>> 16); frame[2] = (byte) (len >>> 8); frame[3] = (byte) len;
            outbox.add(frame);
        } catch (IOException e) {
            CraftBlade.LOG.warn("bridge encode failed: {}", e.toString());
        }
    }

    /** Bytes waiting to go out; streamers back off when B&S cannot keep up. */
    public int backlog() { return outbox.size(); }

    private void acceptLoop() {
        try (ServerSocket server = new ServerSocket(port, 1, InetAddress.getLoopbackAddress())) {
            CraftBlade.LOG.info("CraftBlade bridge listening on 127.0.0.1:{}", port);
            while (true) {
                Socket s = server.accept();
                s.setTcpNoDelay(true);
                outbox.clear();
                inbox.clear();
                socket = s;
                generation++;
                CraftBlade.LOG.info("Blade & Sorcery connected");
                Thread w = new Thread(() -> writeLoop(s), "CraftBlade bridge writer");
                w.setDaemon(true);
                w.start();
                readLoop(s);
                socket = null;
                try { s.close(); } catch (IOException ignored) {}
                CraftBlade.LOG.info("Blade & Sorcery disconnected");
            }
        } catch (IOException e) {
            CraftBlade.LOG.error("CraftBlade bridge could not listen on port {}: {}", port, e.toString());
        }
    }

    private void readLoop(Socket s) {
        try (DataInputStream in = new DataInputStream(new BufferedInputStream(s.getInputStream()))) {
            while (true) {
                int len = in.readInt();
                if (len < 1 || len > (16 << 20)) throw new IOException("bad frame length " + len);
                byte[] body = new byte[len];
                in.readFully(body);
                DataInputStream b = new DataInputStream(new ByteArrayInputStream(body));
                inbox.add(Msg.read(b.readUnsignedByte(), b));
            }
        } catch (IOException e) {
            if (!(e instanceof EOFException)) CraftBlade.LOG.info("bridge read ended: {}", e.toString());
        }
    }

    private void writeLoop(Socket s) {
        try (OutputStream out = new BufferedOutputStream(s.getOutputStream(), 1 << 16)) {
            while (socket == s) {
                byte[] f = outbox.poll(100, TimeUnit.MILLISECONDS);
                if (f == null) { out.flush(); continue; }
                out.write(f);
                if (outbox.isEmpty()) out.flush();
            }
        } catch (IOException | InterruptedException e) {
            try { s.close(); } catch (IOException ignored) {}
        }
    }
}
