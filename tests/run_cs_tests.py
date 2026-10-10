"""Compile and run the pure C# tests of the B&S side with mono."""
import pathlib
import subprocess
import sys
import tempfile

ROOT = pathlib.Path(__file__).resolve().parent.parent
src = ROOT / "bas/src"
with tempfile.TemporaryDirectory() as d:
    exe = pathlib.Path(d) / "t.exe"
    files = [ROOT / "tests/MesherTests.cs", src / "Wire.cs", src / "Section.cs", src / "Mesher.cs", src / "Gen/Msg.cs", src / "Gen/Tables.cs"]
    subprocess.run(["mcs", "-nologo", "-out:" + str(exe)] + [str(f) for f in files], check=True)
    sys.exit(subprocess.run(["mono", str(exe)]).returncode)
