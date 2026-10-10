"""Preflight: lay every sheet's rows and columns over each other and list what is unfilled,
what does not resolve, and what is not yet verified in the running game.
Exit 1 when anything is unfilled or unresolved (the build must not run); unverified cells are
listed as remaining work."""
import sys

from sheets import ROOT, all_sheets
import gen

WIRE_TYPES = None
problems, unverified = [], []


def need(cond, msg):
    if not cond:
        problems.append(msg)


def filled(v):
    return v is not None and v != "" and v != []


def main():
    global WIRE_TYPES
    s = all_sheets()
    WIRE_TYPES = set(s["protocol"]["_types"])

    for name, sheet in s.items():
        cols = sheet.get("columns")
        need(cols, f"{name}: no columns")
        for i, row in enumerate(sheet.get("rows", [])):
            label = f"{name}[{i}:{row.get(cols[0]) if cols else '?'}]"
            for c in cols or []:
                need(c in row and filled(row[c]), f"{label}.{c} is unfilled")
            for c in row:
                need(c in cols, f"{label} has column '{c}' not in the sheet header")
            if "verified" in (cols or []) and row.get("verified") is not True:
                unverified.append(label)

    # protocol
    ids = [r["id"] for r in s["protocol"]["rows"]]
    need(len(ids) == len(set(ids)), "protocol: duplicate message ids")
    for r in s["protocol"]["rows"]:
        need(r["direction"] in ("mc>bas", "bas>mc"), f"protocol.{r['name']}: bad direction")
        names = set()
        for f in r["fields"]:
            n, _, t = f.partition(":")
            need(t in WIRE_TYPES, f"protocol.{r['name']}.{n}: unknown type '{t}'")
            need(n not in names, f"protocol.{r['name']}: duplicate field {n}")
            names.add(n)
    # shapes
    sids = [r["id"] for r in s["shapes"]["rows"]]
    need(sids == list(range(len(sids))), "shapes: ids must be 0..n-1")
    # damage
    dids = [r["id"] for r in s["damage"]["rows"]]
    need(dids == list(range(len(dids))), "damage: ids must be 0..n-1")
    for r in s["damage"]["rows"]:
        need(r["mcSource"] in ("playerAttack",), f"damage.{r['basType']}: unknown mcSource")
    # entities -> creatures
    creatures = {r["id"] for r in s["creatures"]["rows"]}
    rows = s["entities"]["rows"]
    need(rows and rows[-1]["mcType"] == "*", "entities: last row must be the '*' fallback")
    for r in rows:
        need(r["representation"] in ("creature", "box"), f"entities.{r['mcType']}: bad representation")
        if r["representation"] == "creature":
            need(r["basCreature"] in creatures, f"entities.{r['mcType']}: creature '{r['basCreature']}' not in creatures sheet")
    # config
    for r in s["config"]["rows"]:
        need(r["side"] in ("mc", "bas", "both"), f"config.{r['key']}: bad side")
        need(r["type"] in ("int", "double", "float", "string"), f"config.{r['key']}: bad type")
    # hooks -> files exist
    for r in s["hooks"]["rows"]:
        need(r["game"] in ("minecraft-java", "blade-and-sorcery"), f"hooks.{r['id']}: unknown game")
        need((ROOT / r["file"]).exists(), f"hooks.{r['id']}: file {r['file']} does not exist")
    # generated code is current
    for path, text in gen.outputs(s).items():
        p = ROOT / path
        need(p.exists() and p.read_text(encoding="utf-8") == text, f"generated {path} is stale: run tools/gen.py")

    print(f"Preflight: {len(problems)} problem(s), {len(unverified)} row(s) not yet verified in game")
    for p in problems:
        print("  PROBLEM", p)
    if "-v" in sys.argv:
        for u in unverified:
            print("  unverified", u)
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
