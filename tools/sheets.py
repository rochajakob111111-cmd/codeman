"""Shared loader for the design sheets in sheets/*.json."""
import json
import pathlib

ROOT = pathlib.Path(__file__).resolve().parent.parent
SHEETS = ROOT / "sheets"


def load(name):
    with open(SHEETS / f"{name}.json", encoding="utf-8") as f:
        return json.load(f)


def all_sheets():
    return {p.stem: load(p.stem) for p in sorted(SHEETS.glob("*.json"))}
