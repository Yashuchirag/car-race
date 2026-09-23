"""Fetch circuit geometry from OpenStreetMap and stitch it into one closed ring.

Source data is ODbL licensed, so anything shipped from it needs attribution.
Circuits are mapped as `highway=raceway` ways. A circuit is sometimes one closed
way, but more often several ways meeting at pit entry and exit junctions, so the
ways have to be walked into a single loop.
"""

import hashlib
import json
import time
from collections import defaultdict
from pathlib import Path

import numpy as np
import requests

ENDPOINTS = [
    "https://overpass-api.de/api/interpreter",
    "https://overpass.kumi.systems/api/interpreter",
    "https://overpass.osm.ch/api/interpreter",
    "https://overpass.private.coffee/api/interpreter",
]

# Overpass rejects the default requests user agent with a bare 406, and its
# usage policy asks callers to identify themselves, so send a real one.
HEADERS = {"User-Agent": "car_race-trackgen/0.1 (circuit geometry pipeline)"}

# Tag values that mark a way as something other than the racing surface.
# OpenStreetMap spells the pit lane raceway=pit_lane, with the underscore.
EXCLUDED_RACEWAY = {"pit_lane", "pitlane", "pit", "paddock", "service",
                    "karting", "dragstrip", "grandstand"}
# Loose surfaces belong to rally and motocross tracks, which share these tags
# with circuits and are often the longest closed way in a venue's bounding box.
EXCLUDED_SURFACE = {"dirt", "ground", "earth", "gravel", "sand", "grass", "mud"}
EXCLUDED_SPORT = {"motocross", "karting", "cyclING"}


def fetch_raceway(bbox, cache_dir=None, timeout=120):
    """Return (nodes, ways) for every raceway way inside `bbox`.

    nodes maps node id to (lon, lat). ways is a list of dicts with id, tags and
    an ordered list of node ids.
    """
    south, west, north, east = bbox
    query = (
        "[out:json][timeout:120];\n"
        f'(way["highway"="raceway"]({south:.6f},{west:.6f},{north:.6f},{east:.6f}););\n'
        "(._;>;);\nout body;"
    )

    payload = _request(query, cache_dir, timeout)

    nodes = {}
    ways = []
    for el in payload.get("elements", []):
        if el["type"] == "node":
            nodes[el["id"]] = (el["lon"], el["lat"])
        elif el["type"] == "way":
            tags = el.get("tags", {})
            if tags.get("area") == "yes":
                continue
            if tags.get("raceway") in EXCLUDED_RACEWAY:
                continue
            if tags.get("surface") in EXCLUDED_SURFACE:
                continue
            sports = {p.strip().lower() for p in tags.get("sport", "").split(";")}
            if sports & {s.lower() for s in EXCLUDED_SPORT}:
                continue
            ways.append({"id": el["id"], "tags": tags, "nodes": el["nodes"]})

    return nodes, ways


def _request(query, cache_dir, timeout):
    """Post the query to Overpass, caching the raw response on disk."""
    if cache_dir:
        cache_dir = Path(cache_dir)
        cache_dir.mkdir(parents=True, exist_ok=True)
        key = hashlib.sha1(query.encode()).hexdigest()[:16]
        cached = cache_dir / f"overpass_{key}.json"
        if cached.exists():
            print(f"  overpass: cache hit ({cached.name})")
            return json.loads(cached.read_text())

    last_error = None
    for url in ENDPOINTS:
        # Mirrors are shared and commonly rate limit, so back off before moving on.
        for attempt in range(3):
            try:
                print(f"  overpass: querying {url}")
                resp = requests.post(url, data={"data": query},
                                     headers=HEADERS, timeout=timeout)
                if resp.status_code in (429, 504):
                    wait = 5 * (attempt + 1)
                    print(f"  overpass: {resp.status_code}, retrying in {wait}s")
                    time.sleep(wait)
                    continue
                resp.raise_for_status()
                payload = resp.json()
                if cache_dir:
                    cached.write_text(json.dumps(payload))
                return payload
            except Exception as exc:  # noqa: BLE001 - fall through to the next mirror
                last_error = exc
                print(f"  overpass: {type(exc).__name__}: {exc}")
                break

    raise RuntimeError(
        f"all {len(ENDPOINTS)} Overpass endpoints failed; last error: {last_error}")


def _way_length(way, nodes):
    """Approximate way length in metres, good enough for ranking candidates."""
    pts = np.array([nodes[n] for n in way["nodes"] if n in nodes])
    if len(pts) < 2:
        return 0.0
    lat = np.radians(pts[:, 1].mean())
    dx = np.diff(pts[:, 0]) * 111_320.0 * np.cos(lat)
    dy = np.diff(pts[:, 1]) * 111_320.0
    return float(np.hypot(dx, dy).sum())


def _ring_length(ring, nodes):
    """Length in metres of a ring expressed as node ids."""
    pts = np.array([nodes[n] for n in ring if n in nodes], dtype=float)
    if len(pts) < 2:
        return 0.0
    lat = np.radians(pts[:, 1].mean())
    dx = np.diff(pts[:, 0]) * 111_320.0 * np.cos(lat)
    dy = np.diff(pts[:, 1]) * 111_320.0
    return float(np.hypot(dx, dy).sum())


def _find_cycles(ways, endpoints, lengths, max_length, max_cycles=300,
                 budget=400_000):
    """Enumerate distinct closed loops in the way graph, depth first.

    A greedy walk cannot do this. Silverstone maps its Grand Prix circuit as
    individually named segments ("Hangar Straight", "Vale"), so no name or
    length heuristic points the right way at a junction; the branch only proves
    wrong several ways later. Backtracking handles that, and most junction nodes
    join exactly two ways, so the search stays small in practice.
    """
    cycles = []
    seen = set()
    expansions = 0

    for seed in range(len(ways)):
        start_node = ways[seed]["nodes"][0]
        stack = [(ways[seed]["nodes"][-1], (seed,), lengths[seed])]
        while stack:
            if expansions > budget or len(cycles) >= max_cycles:
                return cycles
            cursor, used, length = stack.pop()
            expansions += 1

            if cursor == start_node:
                key = frozenset(used)
                if key not in seen:
                    seen.add(key)
                    cycles.append((list(used), length))
                continue
            if length > max_length:
                continue

            for cand in endpoints.get(cursor, ()):
                if cand in used:
                    continue
                seq = ways[cand]["nodes"]
                if cursor == seq[0]:
                    nxt = seq[-1]
                elif cursor == seq[-1]:
                    nxt = seq[0]
                else:
                    continue
                stack.append((nxt, used + (cand,), length + lengths[cand]))

    return cycles


def _assemble(ways, order):
    """Join an ordered list of way indices into one ring of node ids."""
    ring = list(ways[order[0]]["nodes"])
    for idx in order[1:]:
        seq = list(ways[idx]["nodes"])
        if seq[-1] == ring[-1]:
            seq.reverse()
        ring.extend(seq[1:])
    return ring


def stitch_loop(nodes, ways, target_length_m=None, name_hint=None,
                min_length_m=400.0, verbose=True):
    """Walk the ways into a single closed ring of node ids.

    A venue is rarely one circuit. Silverstone, Monza and Suzuka all publish
    several layouts that share tarmac, and OpenStreetMap maps each of them as
    raceway, so several valid loops exist in the same bounding box. Picking the
    longest is wrong too, because Bahrain's Endurance layout is longer than its
    Grand Prix one.

    So every loop is enumerated and the one closest to `target_length_m` wins.
    Without a target the longest is taken, and the report lists what was passed
    over so a wrong pick is visible rather than silent.

    Returns (ring, report). `ring` is an ordered list of node ids whose first and
    last entries are the same node.
    """
    ways = [w for w in ways if len(w["nodes"]) >= 2]
    if not ways:
        raise RuntimeError("no raceway ways found in the bounding box")

    if name_hint:
        hinted = [w for w in ways
                  if name_hint.lower() in w["tags"].get("name", "").lower()]
        if hinted:
            if verbose:
                print(f"  stitch: {len(hinted)} of {len(ways)} ways match "
                      f"name hint {name_hint!r}")
            ways = hinted

    lengths = {i: _way_length(w, nodes) for i, w in enumerate(ways)}
    endpoints = defaultdict(list)
    for i, w in enumerate(ways):
        endpoints[w["nodes"][0]].append(i)
        endpoints[w["nodes"][-1]].append(i)

    max_length = (target_length_m * 1.6) if target_length_m else 60_000.0
    found = _find_cycles(ways, endpoints, lengths, max_length)

    candidates = []
    for order, _ in found:
        ring = _assemble(ways, order)
        length = _ring_length(ring, nodes)
        if length < min_length_m:
            continue
        named = [ways[i]["tags"].get("name", "") for i in order]
        candidates.append({
            "ring": ring,
            "ways_used": len(order),
            "length_m": round(length, 1),
            "name": next((n for n in named if n), ""),
        })

    if not candidates:
        raise RuntimeError(
            f"could not close a loop from {len(ways)} raceway ways. Inspect the "
            "bounding box, or the circuit may be mapped unusually.")

    if target_length_m:
        candidates.sort(key=lambda c: abs(c["length_m"] - target_length_m))
    else:
        candidates.sort(key=lambda c: -c["length_m"])
    best = candidates[0]

    if verbose:
        why = (f"closest to the {target_length_m:.0f} m target"
               if target_length_m else "longest")
        print(f"  stitch: {len(candidates)} candidate loop(s); took the {why}: "
              f"{best['length_m']:.0f} m from {best['ways_used']} way(s)"
              + (f", incl. {best['name']!r}" if best["name"] else ""))
        for c in candidates[1:4]:
            print(f"          passed over: {c['length_m']:8.0f} m "
                  f"({c['ways_used']} ways) {c['name']!r}")

    report = {
        "ways_used": best["ways_used"],
        "ways_available": len(ways),
        "name": best["name"],
        "length_m": best["length_m"],
        "candidates_considered": len(candidates),
        "alternatives": [{k: c[k] for k in ("length_m", "ways_used", "name")}
                         for c in candidates[1:6]],
    }
    return best["ring"], report


def ring_to_lonlat(ring, nodes):
    """Convert a ring of node ids into an (N, 2) array of longitude and latitude."""
    pts = np.array([nodes[n] for n in ring if n in nodes], dtype=float)
    if len(pts) < 4:
        raise RuntimeError(f"ring has only {len(pts)} resolvable nodes")
    return pts
