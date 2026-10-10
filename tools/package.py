"""Build and assemble a CraftBlade release (run on a PC with network access and Blade & Sorcery installed).

    python tools/package.py --bas "C:/Program Files (x86)/Steam/steamapps/common/Blade & Sorcery"

Produces dist/craftblade-minecraft-<ver>.zip (portable Prism Launcher + the CraftBlade instance: Minecraft
1.21.1, Fabric Loader named in mmc-pack.json so Prism installs it, our mod, Fabric API, e4mc) and
dist/craftblade-bas-<ver>.zip (the B&S script mod folder). Prints byte size and SHA-256 of each for upload.
Downloads come only from official sources (Prism's GitHub releases, Modrinth). No game files are included:
the B&S build only *references* the game's DLLs."""
import argparse
import hashlib
import io
import json
import pathlib
import shutil
import subprocess
import sys
import urllib.parse
import urllib.request
import zipfile

ROOT = pathlib.Path(__file__).resolve().parent.parent
VERSION = "0.1.0"
MC = "1.21.1"
FABRIC_LOADER = "0.16.10"
FABRIC_API = "0.107.0+1.21.1"
PRISM = "9.4"
PRISM_ZIP = f"https://github.com/PrismLauncher/PrismLauncher/releases/download/{PRISM}/PrismLauncher-Windows-MSVC-Portable-{PRISM}.zip"
UA = {"User-Agent": "craftblade-packager/0.1 (github.com/rochajakob111111-cmd/codeman)"}


def get(url):
    with urllib.request.urlopen(urllib.request.Request(url, headers=UA)) as r:
        return r.read()


def modrinth_jar(project, version_number=None):
    q = urllib.parse.urlencode({"game_versions": json.dumps([MC]), "loaders": json.dumps(["fabric"])})
    versions = json.loads(get(f"https://api.modrinth.com/v2/project/{project}/version?{q}"))
    if version_number:
        versions = [v for v in versions if v["version_number"] == version_number]
    if not versions:
        sys.exit(f"Modrinth has no {project} {version_number or ''} for Fabric {MC}")
    f = next(f for f in versions[0]["files"] if f["primary"]) if any(f["primary"] for f in versions[0]["files"]) else versions[0]["files"][0]
    data = get(f["url"])
    if hashlib.sha512(data).hexdigest() != f["hashes"]["sha512"]:
        sys.exit(f"{f['filename']}: checksum mismatch")
    print(f"  {project} {versions[0]['version_number']} -> {f['filename']}")
    return f["filename"], data


def run(cmd, cwd):
    print("  $", " ".join(cmd))
    subprocess.run(cmd, cwd=cwd, check=True, shell=(sys.platform == "win32"))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--bas", required=True, help="Blade & Sorcery install folder (for compile references only)")
    a = ap.parse_args()

    print("preflight")
    if subprocess.run([sys.executable, str(ROOT / "tools/preflight.py")]).returncode:
        sys.exit("preflight failed: fix the sheets first")

    print("build Minecraft mod")
    gradlew = "gradlew.bat" if sys.platform == "win32" else "./gradlew"
    run([gradlew, "build"], ROOT / "fabric")
    jar = next(p for p in (ROOT / "fabric/build/libs").glob("craftblade-*.jar") if "sources" not in p.name)

    print("build Blade & Sorcery mod")
    run(["dotnet", "build", "-c", "Release", f"-p:BasDir={a.bas}"], ROOT / "bas")
    dll = ROOT / "bas/bin/Release/net472/CraftBlade.dll"

    dist = ROOT / "dist"
    shutil.rmtree(dist, ignore_errors=True)
    dist.mkdir()

    print("download dependencies")
    fapi_name, fapi = modrinth_jar("fabric-api", FABRIC_API)
    e4mc_name, e4mc = modrinth_jar("e4mc")
    prism = zipfile.ZipFile(io.BytesIO(get(PRISM_ZIP)))

    mc_zip = dist / f"craftblade-minecraft-{VERSION}.zip"
    inst = "PrismLauncher/instances/CraftBlade"
    with zipfile.ZipFile(mc_zip, "w", zipfile.ZIP_DEFLATED) as z:
        for info in prism.infolist():
            if not info.is_dir():
                z.writestr("PrismLauncher/" + info.filename, prism.read(info))
        z.writestr("PrismLauncher/portable.txt", "")
        z.writestr(f"{inst}/instance.cfg", "InstanceType=OneSix\nname=CraftBlade\niconKey=default\n")
        z.writestr(f"{inst}/mmc-pack.json", json.dumps({"formatVersion": 1, "components": [
            {"uid": "net.minecraft", "version": MC, "important": True},
            {"uid": "net.fabricmc.intermediary", "version": MC, "dependencyOnly": True},
            {"uid": "net.fabricmc.fabric-loader", "version": FABRIC_LOADER},
        ]}, indent=2))
        z.write(jar, f"{inst}/.minecraft/mods/{jar.name}")
        z.writestr(f"{inst}/.minecraft/mods/{fapi_name}", fapi)
        z.writestr(f"{inst}/.minecraft/mods/{e4mc_name}", e4mc)
        z.writestr(f"{inst}/.minecraft/config/craftblade/melty.properties", "join=\n")
        z.write(ROOT / "LICENSES-THIRD-PARTY.md", "PrismLauncher/CRAFTBLADE-THIRD-PARTY.md")

    bas_zip = dist / f"craftblade-bas-{VERSION}.zip"
    with zipfile.ZipFile(bas_zip, "w", zipfile.ZIP_DEFLATED) as z:
        z.write(ROOT / "bas/mod/manifest.json", "CraftBlade/manifest.json")
        z.write(ROOT / "bas/mod/craftblade.json", "CraftBlade/craftblade.json")
        z.write(dll, "CraftBlade/CraftBlade.dll")

    for f in (mc_zip, bas_zip):
        print(f"{f.name}: {f.stat().st_size} bytes, sha256 {hashlib.sha256(f.read_bytes()).hexdigest()}")


if __name__ == "__main__":
    main()
