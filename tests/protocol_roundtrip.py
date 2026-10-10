"""Cross-language check of the bridge protocol: Java (the Minecraft side's generated Msg) writes one frame of
every message in sheets/protocol.json, C# (the B&S side's generated Msg) decodes them, re-encodes them and
prints every field. Passes when the bytes are identical both ways and every value survived."""
import json
import pathlib
import subprocess
import sys
import tempfile

ROOT = pathlib.Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "tools"))
from sheets import load  # noqa: E402

proto = load("protocol")


def value(t, i):
    return {"u8": 200 - i, "i32": -123456 + 1000 * i, "f32": 1.5 + i, "f64": -2.25e6 + i,
            "str": f"minecraft:block/stone_{i}é", "bytes": [i, 255, 0, 7]}[t]


def java_lit(t, v):
    if t == "str": return json.dumps(v)
    if t == "bytes": return "new byte[]{" + ",".join(f"(byte){b}" for b in v) + "}"
    if t == "f32": return f"{v}f"
    return str(v)


with tempfile.TemporaryDirectory() as d:
    d = pathlib.Path(d)
    lines = []
    for r in proto["rows"]:
        fs = [f.split(":") for f in r["fields"]]
        args = ", ".join(java_lit(t, value(t, i)) for i, (_, t) in enumerate(fs))
        lines.append(f"        write(o, new Msg.{r['name']}({args}));")
    (d / "Dump.java").write_text(f"""
import gg.craftblade.gen.Msg;
import java.io.*;
public class Dump {{
    static void write(DataOutputStream out, Msg m) throws IOException {{
        ByteArrayOutputStream b = new ByteArrayOutputStream();
        DataOutputStream o = new DataOutputStream(b);
        o.writeByte(m.id()); m.write(o);
        out.writeInt(b.size()); out.write(b.toByteArray());
    }}
    public static void main(String[] a) throws IOException {{
        try (DataOutputStream o = new DataOutputStream(new FileOutputStream(a[0]))) {{
{chr(10).join(lines)}
        }}
    }}
}}
""", encoding="utf-8")
    subprocess.run(["javac", "-encoding", "UTF-8", "-d", str(d), str(ROOT / "fabric/src/main/java/gg/craftblade/gen/Msg.java"), str(d / "Dump.java")], check=True, capture_output=True)
    subprocess.run(["java", "-cp", str(d), "Dump", str(d / "frames.bin")], check=True, capture_output=True)

    (d / "Check.cs").write_text("""
using System; using System.IO; using System.Reflection; using System.Text;
public static class Check {
    public static void Main(string[] a) {
        Console.OutputEncoding = new UTF8Encoding(false); var all = File.ReadAllBytes(a[0]); int p = 0; var outp = new MemoryStream();
        while (p < all.Length) {
            int len = (all[p] << 24) | (all[p+1] << 16) | (all[p+2] << 8) | all[p+3];
            var body = new byte[len]; Buffer.BlockCopy(all, p + 4, body, 0, len); p += 4 + len;
            var m = CraftBlade.Wire.Decode(body);
            var f = CraftBlade.Wire.Frame(m); outp.Write(f, 0, f.Length);
            var sb = new StringBuilder("{\\"_type\\":\\"" + m.GetType().Name + "\\"");
            foreach (var fi in m.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance)) {
                object v = fi.GetValue(m); string js;
                if (v is byte[]) js = "[" + string.Join(",", Array.ConvertAll((byte[])v, x => x.ToString())) + "]";
                else if (v is string) js = "\\"" + ((string)v).Replace("\\\\", "\\\\\\\\").Replace("\\"", "\\\\\\"") + "\\"";
                else if (v is float) js = ((float)v).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                else if (v is double) js = ((double)v).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                else js = Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
                sb.Append(",\\"" + fi.Name + "\\":" + js);
            }
            Console.WriteLine(sb.Append("}").ToString());
        }
        File.WriteAllBytes(a[1], outp.ToArray());
    }
}
""", encoding="utf-8")
    src = ROOT / "bas/src"
    subprocess.run(["mcs", "-nologo", "-out:" + str(d / "check.exe"), str(d / "Check.cs"), str(src / "Wire.cs"), str(src / "Gen/Msg.cs")], check=True)
    res = subprocess.run(["mono", str(d / "check.exe"), str(d / "frames.bin"), str(d / "back.bin")], check=True, capture_output=True)
    out = [json.loads(l) for l in res.stdout.decode("utf-8").splitlines()]

    fails = []
    if (d / "frames.bin").read_bytes() != (d / "back.bin").read_bytes():
        fails.append("C# re-encoding differs from Java bytes")
    for r, got in zip(proto["rows"], out):
        if got["_type"] != r["name"]:
            fails.append(f"{r['name']}: decoded as {got["_type"]}")
            continue
        for i, (n, t) in enumerate(f.split(":") for f in r["fields"]):
            want = value(t, i)
            if t == "f32": ok = abs(got[n] - want) < 1e-6
            else: ok = got[n] == want
            if not ok: fails.append(f"{r['name']}.{n}: sent {want!r}, got {got[n]!r}")
    if len(out) != len(proto["rows"]):
        fails.append(f"decoded {len(out)} of {len(proto['rows'])} messages")
    print(f"protocol roundtrip: {len(out)} messages, {sum(len(r['fields']) for r in proto['rows'])} fields, {len(fails)} failures")
    for f in fails: print("  FAIL", f)
    sys.exit(1 if fails else 0)
