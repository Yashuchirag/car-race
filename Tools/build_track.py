#!/usr/bin/env python3
"""Build engine-ready track data from an OpenStreetMap circuit layout.

    python build_track.py bahrain
    python build_track.py testcircuit --no-elevation
    python build_track.py spa --car f1 --spacing 2.0

Writes a JSON track definition plus a verification plot. Nothing is exported
unless the geometry passes its validation gates, because a silently wrong
centreline is far more expensive to discover later in the engine.
"""

import argparse
import json
import sys
from datetime import date
from pathlib import Path

import numpy as np
from shapely.geometry import LineString

sys.path.insert(0, str(Path(__file__).parent))
from trackgen import centerline as cl  # noqa: E402
from trackgen import elevation as elev  # noqa: E402
from trackgen import geo, osm  # noqa: E402
from trackgen import racing_line as rl  # noqa: E402

ROOT = Path(__file__).parent
ATTRIBUTION = ("Layout geometry derived from OpenStreetMap, (c) OpenStreetMap "
               "contributors, ODbL 1.0. Elevation from SRTM via OpenTopoData.")


# A hand-authored club circuit: main straight, fast right onto a north chute, a
# 20 m hairpin, a descending link, a west straight, a chicane and a long left
# loop back onto the straight. It exercises the whole speed range offline and
# deterministically, which makes it the right fixture to test changes against.
#
# The list starts mid-straight on purpose. A closed layout whose first and last
# waypoints meet at an angle puts a curvature spike on the seam, and that spike
# then dominates every curvature-derived result.
TEST_CIRCUIT_WAYPOINTS = [
    (450, 0), (600, 0), (700, 0), (750, 0),
    (850, 40), (890, 130), (898, 230), (900, 320),
    (900, 340), (895, 368), (878, 388), (852, 392), (834, 376), (830, 350),
    (824, 300), (808, 250),
    (762, 192), (692, 152), (605, 130),
    (520, 120), (430, 118), (330, 125),
    (262, 152), (200, 128),
    (130, 115), (75, 85), (40, 45), (30, 0),
    (45, -40), (90, -62), (150, -62),
    (220, -45), (280, -25), (340, -8), (400, 0),
]


def synthetic_layout(step=10.0):
    """Resample the authored waypoints evenly so the smoothing spline rounds
    the polygon corners into arcs instead of chasing uneven control points."""
    xy = np.array(TEST_CIRCUIT_WAYPOINTS, dtype=float)
    xy = np.vstack([xy, xy[0]])
    seg = np.hypot(*np.diff(xy, axis=0).T)
    s = np.concatenate(([0.0], np.cumsum(seg)))
    t = np.arange(0.0, s[-1], step)
    dense = np.stack([np.interp(t, s, xy[:, 0]), np.interp(t, s, xy[:, 1])], axis=1)
    return np.vstack([dense, dense[0]])


def find_longest_straight(curvature, max_curv=0.002):
    """Index of the middle of the longest straight, wrapping across index 0.

    Start/finish lines nearly always sit on the longest straight, so this is a
    far better default than whichever node OpenStreetMap happened to list first.
    """
    n = len(curvature)
    straight = np.abs(curvature) < max_curv
    if not straight.any():
        return 0
    doubled = np.concatenate([straight, straight])
    best_len, best_start, run = 0, 0, 0
    for i in range(2 * n):
        if doubled[i]:
            run += 1
            if run > best_len:
                best_len, best_start = run, i - run + 1
        else:
            run = 0
    best_len = min(best_len, n)
    return (best_start + best_len // 2) % n


def build(args):
    catalogue = json.loads((ROOT / "circuits.json").read_text())
    if args.circuit not in catalogue or args.circuit.startswith("_"):
        sys.exit(f"unknown circuit '{args.circuit}'. "
                 f"Known: {[k for k in catalogue if not k.startswith('_')]}")
    spec = catalogue[args.circuit]
    print(f"\n=== {spec['display_name']} ===")

    cache = ROOT / "out" / "cache"

    # --- source geometry -------------------------------------------------
    if spec.get("synthetic"):
        xy_raw = synthetic_layout()
        frame = geo.LocalFrame(0.0, 0.0)
        stitch_report = {"strategy": "synthetic", "ways_used": 0, "ways_available": 0}
        closure_gap = 0.0
        print(f"  source: synthetic layout, {len(xy_raw)} points")
    else:
        bbox = geo.bbox_around(spec["lat"], spec["lon"], spec["radius_m"])
        nodes, ways = osm.fetch_raceway(bbox, cache_dir=cache)
        print(f"  source: {len(ways)} raceway ways, {len(nodes)} nodes")
        ring, stitch_report = osm.stitch_loop(
            nodes, ways,
            target_length_m=spec["official_length_m"],
            name_hint=spec.get("name_hint"))
        lonlat = osm.ring_to_lonlat(ring, nodes)
        frame = geo.LocalFrame(spec["lat"], spec["lon"])
        x, y = frame.to_local(lonlat[:, 0], lonlat[:, 1])
        xy_raw = np.stack([x, y], axis=1)
        xy_raw, closure_gap = cl.close_ring(xy_raw)
        print(f"  closure gap before joining: {closure_gap:.1f} m")

    raw_length = cl.polyline_length(xy_raw)

    # --- smooth and resample ---------------------------------------------
    forced_noise = spec.get("noise_m", args.noise)
    if forced_noise is not None:
        tck = cl.fit_periodic_spline(xy_raw, noise_m=forced_noise)
        geom = cl.sample_uniform(tck, spacing=args.spacing)
        chosen_noise, smoothing_trials = forced_noise, None
    else:
        tck, geom, chosen_noise, smoothing_trials = cl.fit_best_spline(
            xy_raw, raw_length, spacing=args.spacing,
            min_radius_floor_m=args.min_radius_floor)
    n = len(geom["x"])
    print(f"  centreline: {geom['length']:.1f} m over {n} samples "
          f"at {geom['spacing']:.2f} m spacing")

    # --- place the start line on the longest straight ---------------------
    shift = find_longest_straight(geom["curvature"])
    if args.start_offset_m:
        shift = (shift + int(round(args.start_offset_m / geom["spacing"]))) % n
    for key in ("x", "y", "heading", "curvature", "normal_x", "normal_y"):
        geom[key] = np.roll(geom[key], -shift)
    geom["s"] = np.arange(n) * geom["spacing"]

    cx, cy = geom["x"], geom["y"]
    nx, ny = geom["normal_x"], geom["normal_y"]

    half = spec["width_m"] / 2.0
    width_left = np.full(n, half)
    width_right = np.full(n, half)

    # --- elevation --------------------------------------------------------
    if args.no_elevation or spec.get("synthetic"):
        cz = np.zeros(n)
        elevation_source = "flat (not sampled)"
    else:
        cz = elev.profile_for_centerline(
            frame, cx, cy, geom["spacing"],
            cache_path=cache / f"elev_{args.circuit}.json")
        elevation_source = "SRTM 30 m via OpenTopoData, smoothed"
        print(f"  elevation: {cz.min():.1f} m to {cz.max():.1f} m "
              f"(range {cz.max() - cz.min():.1f} m)")

    # --- racing line and speed profile ------------------------------------
    car = rl.car_presets()[args.car]
    alpha, line_report = rl.optimise(cx, cy, nx, ny, width_left, width_right, car,
                                     solve_spacing=args.solve_spacing,
                                     sample_spacing=geom["spacing"])
    rx, ry = cx + alpha * nx, cy + alpha * ny
    # Camber is authored as zero, so the surface is level across the track and
    # the racing line sits at the centreline height for the same station.
    rz = cz.copy()
    stencil = max(int(round(args.curvature_baseline / geom["spacing"])), 1)
    r_kappa = rl.menger_curvature(rx, ry, stride=stencil)
    r_ds = np.hypot(np.roll(rx, -1) - rx, np.roll(ry, -1) - ry)
    speed, lap_time = rl.speed_profile(r_ds, r_kappa, car)
    print(f"  {car.name}: estimated lap {lap_time // 60:.0f}:{lap_time % 60:06.3f}, "
          f"top {speed.max() * 3.6:.0f} km/h, min {speed.min() * 3.6:.0f} km/h")

    corners = cl.detect_corners(geom["s"], geom["curvature"])
    turn_raw = cl.total_turning(xy_raw)
    turn_fit = cl.total_turning(np.stack([cx, cy], axis=1))
    turn_kept = 100.0 * turn_fit / max(turn_raw, 1e-9)
    print(f"  corners detected: {len(corners)}   "
          f"turning kept after smoothing: {turn_kept:.0f}% of the traced polyline")

    # --- validation gates --------------------------------------------------
    official = spec["official_length_m"]
    length_err = 100.0 * (geom["length"] - official) / official
    min_radius = 1.0 / max(np.max(np.abs(geom["curvature"])), 1e-9)
    simple = LineString(np.stack([cx, cy], axis=1)).is_simple
    expect_crossing = spec.get("self_intersects", False)

    checks = [
        ("ring closes", closure_gap < 25.0, f"{closure_gap:.1f} m gap"),
        ("length within 2% of official",
         abs(length_err) < 2.0, f"{geom['length']:.0f} m vs {official} m ({length_err:+.2f}%)"),
        ("smoothing preserved length",
         abs(geom["length"] - raw_length) / raw_length < 0.01,
         f"raw {raw_length:.0f} m, smoothed {geom['length']:.0f} m"),
        # A metre-scale radius means a bad stitch or a digitising blunder. A
        # tight-but-real hairpin is reported separately below rather than
        # blocking the export, because hand-traced slow corners often come out
        # tighter than the real circuit and that is the data, not a defect.
        ("no geometry breakage", min_radius > 5.0, f"tightest radius {min_radius:.1f} m"),
        ("self-intersection as expected",
         simple != expect_crossing if expect_crossing else simple,
         "crosses itself" if not simple else "simple loop"),
        ("lap time plausible", 40.0 < lap_time < 400.0, f"{lap_time:.1f} s"),
    ]

    tight = [c for c in corners if c["min_radius_m"] < 12.0]
    if tight:
        print(f"\n  note: {len(tight)} corner(s) traced tighter than 12 m radius. "
              "Hand-traced\n        slow corners usually come out tighter than the real "
              "circuit, which\n        costs lap time. Worth checking against the plot "
              "and widening by hand:")
        for c in tight:
            print(f"        turn {c['number']:2d} at {c['apex_s']:7.1f} m "
                  f"({100 * c['apex_s'] / geom['length']:4.1f}% of lap): "
                  f"{c['min_radius_m']:.1f} m {c['direction']}")

    print("\n  validation")
    all_passed = True
    for name, ok, detail in checks:
        print(f"    [{'PASS' if ok else 'FAIL'}] {name:34s} {detail}")
        all_passed &= ok

    if not all_passed and not args.force:
        sys.exit("\n  export blocked: validation failed. "
                 "Inspect the plot, adjust the catalogue, or pass --force.")

    # --- export -------------------------------------------------------------
    def r3(a):
        return [round(float(v), 3) for v in a]

    track = {
        "name": spec["alias"],
        "source_layout": spec["display_name"],
        "generated": date.today().isoformat(),
        "attribution": ATTRIBUTION,
        "coordinate_system": ("ENU metres, X=east Y=north Z=up, right handed. "
                              "Unity import maps (x, y, z) to (x, z, y)."),
        "origin": {"lat": getattr(frame, "lat0", 0.0), "lon": getattr(frame, "lon0", 0.0)},
        "length_m": round(geom["length"], 2),
        "official_length_m": official,
        "length_error_pct": round(length_err, 3),
        "sample_spacing_m": round(geom["spacing"], 4),
        "smoothing_noise_m": chosen_noise,
        "smoothing_trials": smoothing_trials,
        "sample_count": n,
        "elevation_source": elevation_source,
        "stitch": stitch_report,
        "racing_line_solve": line_report,
        "reference_car": rl.car_to_dict(car),
        "estimated_lap_time_s": round(lap_time, 3),
        "curvature_baseline_m": args.curvature_baseline,
        "tightest_centerline_radius_m": round(float(min_radius), 2),
        "turning_kept_pct": round(turn_kept, 1),
        "corners_under_12m_radius": [c["number"] for c in tight],
        "centerline": {
            "s": r3(geom["s"]), "x": r3(cx), "y": r3(cy), "z": r3(cz),
            "heading": r3(geom["heading"]), "curvature": [round(float(v), 6) for v in geom["curvature"]],
            "width_left": r3(width_left), "width_right": r3(width_right),
            "camber": r3(np.zeros(n)), "banking": r3(np.zeros(n)),
        },
        "racing_line": {
            "x": r3(rx), "y": r3(ry), "z": r3(rz),
            "offset": r3(alpha),
            "curvature": [round(float(v), 6) for v in r_kappa],
            "speed_ms": r3(speed),
        },
        "corners": corners,
    }

    out_dir = Path(args.out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)
    out_path = out_dir / f"{args.circuit}.json"
    out_path.write_text(json.dumps(track, indent=1))
    size_mb = out_path.stat().st_size / 1e6
    print(f"\n  exported {out_path} ({size_mb:.2f} MB)")

    if not args.no_plot:
        from visualize import plot_track
        png = ROOT / "out" / f"{args.circuit}.png"
        plot_track(track, png)
        print(f"  plotted   {png}")

    return 0 if all_passed else 1


def main():
    p = argparse.ArgumentParser(description=__doc__,
                                formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("circuit")
    p.add_argument("--spacing", type=float, default=2.0,
                   help="centreline sample spacing in metres (default 2.0)")
    p.add_argument("--noise", type=float, default=None,
                   help="fix the assumed digitising noise in metres; "
                        "omitted, the best smoothing level is searched for")
    p.add_argument("--min-radius-floor", type=float, default=8.0,
                   help="smooth only until the tightest radius clears this, in "
                        "metres (default 8.0); higher erases real corners")
    p.add_argument("--curvature-baseline", type=float, default=6.0,
                   help="baseline in metres for racing line curvature (default 6.0)")
    p.add_argument("--solve-spacing", type=float, default=8.0,
                   help="racing line solve node spacing in metres (default 8.0; "
                        "set equal to --spacing for an exact full-resolution solve)")
    p.add_argument("--car", default="gt3", choices=list(rl.car_presets()),
                   help="reference car for the speed profile (default gt3)")
    p.add_argument("--start-offset-m", type=float, default=0.0,
                   help="shift the start line along the lap")
    p.add_argument("--no-elevation", action="store_true")
    p.add_argument("--no-plot", action="store_true")
    p.add_argument("--force", action="store_true", help="export even if validation fails")
    p.add_argument("--out-dir", default=str(ROOT.parent / "Tracks_Data"))
    sys.exit(build(p.parse_args()))


if __name__ == "__main__":
    main()
