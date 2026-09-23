#!/usr/bin/env python3
"""Rebuild every circuit and report whether each still passes its gates.

Run after touching anything in trackgen. Overpass and elevation responses are
cached, so a clean repeat run needs no network.
"""

import json
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).parent
PYTHON = ROOT / ".venv" / "bin" / "python"


def main():
    circuits = [k for k in json.loads((ROOT / "circuits.json").read_text())
                if not k.startswith("_")]
    rows, failed = [], []

    for name in circuits:
        cmd = [str(PYTHON), str(ROOT / "build_track.py"), name, "--no-plot"]
        if name == "testcircuit":
            cmd.append("--no-elevation")
        done = subprocess.run(cmd, capture_output=True, text=True)
        out = done.stdout + done.stderr
        if done.returncode != 0:
            failed.append(name)
            rows.append((name, "FAIL", "-", "-", "-", "-"))
            print(f"--- {name} failed ---\n{out.strip()[-900:]}\n")
            continue

        track = json.loads((ROOT.parent / "Tracks_Data" / f"{name}.json").read_text())
        lap = track["estimated_lap_time_s"]
        rows.append((
            name, "ok",
            f"{track['length_m']:.0f} m",
            f"{track['length_error_pct']:+.2f}%",
            f"{track['tightest_centerline_radius_m']:.0f} m",
            f"{int(lap // 60)}:{lap % 60:06.3f}",
        ))

    head = ("circuit", "state", "length", "vs official", "tightest", "GT3 est")
    widths = [max(len(str(r[i])) for r in rows + [head]) for i in range(6)]
    line = "  ".join(h.ljust(w) for h, w in zip(head, widths))
    print(f"\n{line}\n{'-' * len(line)}")
    for r in rows:
        print("  ".join(str(c).ljust(w) for c, w in zip(r, widths)))

    print(f"\n{len(rows) - len(failed)} of {len(rows)} circuits pass.")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
