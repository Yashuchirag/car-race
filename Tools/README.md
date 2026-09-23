# Track pipeline

Turns an OpenStreetMap circuit layout into engine-ready track data: a smoothed
centreline with curvature and elevation, a minimum-curvature racing line, a
speed profile, corner list and timing reference.

Built first, before any engine work, because it is engine agnostic, it runs
under WSL, and it is the piece that decides whether "real circuit layouts" is
actually achievable. It is.

## Setup

```bash
python3 -m venv Tools/.venv
Tools/.venv/bin/python -m pip install -r Tools/requirements.txt
```

## Use

```bash
Tools/.venv/bin/python Tools/build_track.py testcircuit --no-elevation
Tools/.venv/bin/python Tools/build_track.py spa
Tools/.venv/bin/python Tools/build_track.py monza --car f1
Tools/.venv/bin/python Tools/verify_all.py          # regression check
```

Writes `Tracks_Data/<circuit>.json` and a verification plot in `Tools/out/`.
**Look at the plot before importing anything.** A wrong loop, over-smoothed
corners or a nonsense speed profile are obvious there and expensive later.

Overpass responses and elevation samples are cached under `Tools/out/cache/`,
so re-runs are free and offline.

Useful flags: `--car {gt3,f1,road}`, `--spacing`, `--no-elevation`,
`--start-offset-m`, `--noise`, `--solve-spacing`, `--force`.

## Output format

Parallel arrays, not an array of objects, so the file stays small and parses
fast. `centerline` and `racing_line` are each sampled at `sample_spacing_m`
along the lap, starting at the start/finish line.

Coordinates are **ENU metres, X east, Y north, Z up, right handed**, with the
origin at the circuit's catalogue latitude and longitude. Unity is left handed
and Y up, so its importer maps `(x, y, z)` to `(x, z, y)` and must reverse the
sample order if the resulting lap runs the wrong way.

`width_left`, `width_right`, `camber` and `banking` are authoring fields. They
ship flat and are meant to be edited per section once a circuit matters.

## Adding a circuit

Add an entry to `circuits.json` with a centre latitude and longitude, a search
radius, and `official_length_m`. That published length is not decoration: it
selects the right loop and then validates the result.

If the venue has layouts of similar length, add `name_hint` to restrict the
search to ways whose name contains that string.

## How it decides things

**Which loop.** A venue is rarely one circuit. Silverstone, Monza and Suzuka all
map several layouts that share tarmac. Picking the longest is also wrong:
Bahrain's Endurance layout is longer than its Grand Prix one. So every closed
loop in the way graph is enumerated by depth-first search and the one closest to
`official_length_m` wins. Rejected loops are listed in the run output and in the
exported `stitch.alternatives`, so a wrong pick is visible rather than silent.

**How much smoothing.** Too little and the spline overshoots through the tight
node clusters mappers use in slow corners, inventing radii tighter than the
traced polygon. Too much and it cuts corners and eventually oscillates, which
puts the spikes back. The response is not monotonic, so the tool searches: of
the fits holding the traced length to 0.5%, it takes the one with the largest
minimum radius.

**Racing line.** Bounded least squares over one lateral offset per solve node,
minimising the path's squared second difference. Solved every 8 m and
interpolated back with a periodic cubic spline, which measured within 0.6% of a
full-resolution solve at six times the speed. Linear interpolation is not good
enough: it puts a curvature kink at every node and measured 6.9% slow.

Minimum curvature is not the true fastest line, which trades radius for exit
speed onto long straights and depends on the car. It is close, and it needs no
vehicle model.

## What is approximate

**Lap times are a sanity check, not a simulation.** The estimate runs about 10
to 20% slower than real times for the same class. The geometry is hand traced,
the line is not lap-time optimal, the car is a point mass, and track width is a
flat guess. Use it to catch a broken layout, not to predict a lap.

**Elevation is SRTM at 30 m horizontal and roughly 5 m vertical.** Good enough
for a base profile. Spa's 104 m range comes through correctly, but a crest like
Eau Rouge needs sculpting by hand afterwards.

**Slow corners trace tight.** Mappers draw hairpins tighter than they are, so
corners under 12 m radius are flagged in the run output with their position
along the lap. Widen them by hand.

**Camber and banking are not in any open dataset** and ship as zero.

## Licensing

Layout geometry comes from OpenStreetMap, © OpenStreetMap contributors, under
ODbL 1.0, which requires attribution. That attribution string is written into
every exported file.

A circuit's geometry is factual and fine to recreate. Its name, logos, sponsor
boards and liveries are not. Every catalogue entry therefore carries an `alias`,
the name to use in game, alongside the real layout it derives from.
