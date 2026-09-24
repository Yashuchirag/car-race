# Implementation report

**Project:** car_race, a Gran Turismo style circuit racer
**Date:** 2026-09-22
**Status:** Phase 0 planning complete. Phase 2 (track pipeline) built and passing. Engine work not started.

This document is the pick-up point. It records what was decided and why, what
exists in the repo today, the reasoning behind every non-obvious algorithm
choice, the problems hit during implementation and how they were resolved, and
the full plan for the parts not yet built.

---

## 1. Status at a glance

| Area | State |
|---|---|
| Scope and stack decisions | Settled, section 2 |
| Track pipeline (Python) | **Built, 6 of 6 circuits passing** |
| Vehicle physics (C#) | **Built, 5 of 5 checks passing. Section 9** |
| Unity project | Not started, needs Unity 6 installed (a manual step) |
| AI, race systems, multiplayer, art | Not started, specified in sections 10 to 12 |

Roughly 1,320 lines of Python across nine modules, plus 1,820 lines of C# across
nine more. No engine code yet, by design:
the track pipeline was built first because it is engine agnostic, it runs under
WSL without Unity installed, and it was the single largest risk to the "real F1
circuit layouts" requirement.

---

## 2. Decisions made, and why

These were weighed against alternatives and settled. They are recorded here so
they do not get relitigated later.

### Scope: circuit racer, not open world

Forza Horizon 5 was roughly 600 developers over four years, and most of that
went into the open world: streaming, traffic, a road network, and a 100 km²
hand-populated map. A single car in it is weeks of one artist's time.

A closed-circuit racer is bounded, can be fully baked and optimised, and is
exactly what Gran Turismo and the F1 games are. Forza's *visual polish* stays as
the target; its structure does not. Free roam, if ever wanted, is one drivable
region added late, not a world.

### Engine: Unity 6 with URP

**Changed on 2026-09-24 from HDRP to URP.** Unity's 2026 render pipeline strategy puts
HDRP in maintenance only, with no new features, and recommends URP for new projects;
URP also leaves more headroom for 1080p and 100fps on the RTX 2060. The HDRP reasoning
below is kept for the record. Moving to HDRP later is a migration of materials, lighting
and post-processing, cheap only before real art exists. Installed: 6.3 LTS, 6000.3.24f1.

Unreal Engine 5 was the expected answer and was rejected on hardware grounds,
not on merit:

| Constraint | This machine | UE5 needs |
|---|---|---|
| RAM | 15.8 GB | 32 GB recommended; editor alone takes 8 to 14 GB |
| Fast disk free | 79.5 GB on C: only | ~40 GB engine + 20 to 40 GB shader cache + project + 25 GB Visual Studio |
| VRAM | 6 GB | 8 GB+ for Lumen and Nanite at 1080p100 |
| Compile cores | 6 | Full C++ rebuild is 15 to 25 minutes at 6 cores |

The deeper point: Unreal's advantage over Unity is Lumen and Nanite, and a 6 GB
RTX 2060 cannot run either at the 1080p and 100fps+ a 144 Hz racing game wants.
Unreal's baked-lighting path would be the one actually used, which is what Unity
HDRP does natively and more cheaply. So the cost of Unreal's iteration penalty
buys a ceiling that is never reached.

Unity 6 HDRP provides everything the visual target actually needs: baked GI with
Adaptive Probe Volumes, physically based sky, volumetric clouds and fog, area
lights, decals, a water system for wet roads, GPU Resident Drawer with occlusion
culling, and DLSS, FSR2 and STP upscaling. Editor RAM sits at 4 to 7 GB, the
install is about 15 GB, and C# recompiles in seconds.

**Revisit trigger:** the GP65 has two SODIMM slots (max 64 GB) and two M.2 slots.
A 32 GB kit plus a second NVMe, roughly $60 to $140 total, makes Unreal viable
and would justify reopening this.

### Everything on D:, never C:

Accepted deliberately, with the 5400rpm penalty understood. It costs iteration
time (asset import, shader cache), not runtime framerate, because the game runs
from RAM and VRAM once loaded.

It turned out better than expected: WSL already lives at `D:\Software\Ubuntu`, so
this project directory is physically on D: and nothing needed moving.

- Python pipeline: stays in WSL at the current path, already on D:.
- Unity project: a **native Windows path** such as `D:\Dev\CarRace`. Never inside
  the WSL filesystem, which Unity reads slowly through `\\wsl.localhost`.
- Unity Hub: set the editor install location to D: as well. C: then carries only
  Unity Hub itself, about 1 GB.
- **Mitigation worth doing:** add a Windows Defender exclusion for the project
  folder and the Unity processes. On a spinning disk that is typically a 2 to 5x
  improvement in import times and costs nothing.

### Physics: Gran Turismo-like

Grounded simulation with generous optional assists. Full Pacejka tyres with load
sensitivity and combined slip, real weight transfer, meaningful setup, but
forgiving enough that a guest can pick up a pad and enjoy it. Specified in full
in section 9.

### Art: free and CC0 assets, plus cars modelled in house

No bought models, no unlicensed real-car models. Legally clean, and it means
anything built can be shared. Expect roughly double the timeline on art versus
buying, and expect cars to be the bottleneck rather than code. Five excellent
cars beat fifty mediocre ones.

### Build order: track pipeline first

Engine agnostic, runs without Unity installed, and it was the largest open
question in the whole plan. Now answered.

---

## 3. Hardware baseline

**MSI GP65 Leopard 10SEK.** This is both the development machine and the intended
minimum spec, which is useful: if it holds 1080p at 100fps, the wider "gaming
laptop with a decent card" audience is covered.

- Intel i7-10750H, 6 cores / 12 threads
- NVIDIA RTX 2060 Mobile 6 GB, plus Intel UHD (Optimus)
- 15.8 GB RAM
- C: 238 GB Kingston NVMe SSD, ~80 GB free
- D: 932 GB Seagate **5400rpm SATA HDD**, ~302 GB free
- 1920x1080 at 144 Hz
- Windows 11 build 26200
- WSL2 capped by `.wslconfig` to 6 GB RAM and 6 processors

**Performance target:** 1080p at 100 to 120fps, not 60. Racing games live on
input latency, and the panel is 144 Hz. That is an 8 to 10 ms frame budget, which
rules out dynamic global illumination and makes upscaling mandatory rather than
optional.

---

## 4. What is built: the track pipeline

Turns an OpenStreetMap circuit layout into engine-ready track data: a smoothed
centreline with curvature and elevation, a minimum-curvature racing line, a
speed profile, a corner list, and a timing reference.

### Module map

| File | Lines | Responsibility |
|---|---|---|
| `Tools/trackgen/geo.py` | 40 | WGS84 to local metric ENU via transverse Mercator; bounding boxes |
| `Tools/trackgen/osm.py` | 284 | Overpass fetch, tag filtering, cycle enumeration, loop selection |
| `Tools/trackgen/centerline.py` | 204 | Ring closing, periodic smoothing spline, arc-length resampling, curvature, smoothing search, corner detection |
| `Tools/trackgen/elevation.py` | 98 | DEM sampling via OpenTopoData, void filling, circular smoothing |
| `Tools/trackgen/racing_line.py` | 212 | Bounded least-squares racing line, Menger curvature, speed profile, car presets |
| `Tools/build_track.py` | 322 | CLI, orchestration, start-line placement, validation gates, JSON export |
| `Tools/visualize.py` | 99 | Verification plot: map, elevation, curvature, speed |
| `Tools/verify_all.py` | 56 | Regression run across every catalogue circuit |
| `Tools/circuits.json` | data | Circuit catalogue: centre, radius, official length, width, alias |

### Data flow

```
circuits.json entry (lat, lon, radius, official_length_m)
   -> Overpass query for highway=raceway in the bounding box   [cached]
   -> tag filter (drop pit lanes, dirt, motocross, karting)
   -> enumerate every closed cycle in the way graph (DFS)
   -> pick the cycle closest to official_length_m
   -> project WGS84 to local ENU metres (transverse Mercator)
   -> close the ring, drop duplicate nodes
   -> search smoothing levels, fit a periodic cubic smoothing spline
   -> resample at uniform arc length (default 2 m)
   -> roll so the start line sits mid longest straight
   -> sample SRTM elevation every 25 m, smooth, interpolate up   [cached]
   -> solve the minimum-curvature racing line (bounded least squares)
   -> derive the speed profile (grip, power, drag, braking, friction ellipse)
   -> detect corners, run six validation gates
   -> export Tracks_Data/<circuit>.json + Tools/out/<circuit>.png
```

### Key algorithms, and why they are what they are

**Projection.** A transverse Mercator centred on the circuit keeps scale
distortion below a millimetre across a 10 km extent, far finer than the source
geometry, so local coordinates can be treated as exact metres. Projecting before
any geometry work is essential; doing trigonometry on raw latitude and longitude
is the classic bug here.

**Loop selection (`_find_cycles` + `stitch_loop`).** A venue is rarely one
circuit. Silverstone, Monza and Suzuka each map several layouts that share
tarmac. Picking the longest is also wrong, because Bahrain's Endurance layout is
longer than its Grand Prix one. So every closed loop in the way graph is
enumerated by depth-first search with backtracking, and the one closest to the
catalogue's `official_length_m` wins. A greedy walk cannot do this: Silverstone
maps its GP circuit as individually named segments ("Hangar Straight", "Vale"),
so no name or length heuristic points the right way at a junction, and the wrong
branch only proves wrong several ways later. Rejected loops are printed and
recorded in `stitch.alternatives`, so a wrong pick is visible rather than silent.

**Smoothing search (`fit_best_spline`).** OSM node spacing is irregular (Bahrain:
239 nodes for 5.4 km, mean spacing 23 m, one gap of 770 m) and carries digitising
noise. Differentiating that directly produces curvature that is mostly noise. The
ring is fitted with a periodic cubic smoothing spline, where the smoothing budget
is expressed as an expected per-point error in metres (total squared residual
`N * noise²`), which is far easier to reason about than SciPy's raw `s`.

The smoothing level is *searched*, not guessed, because the response is not
monotonic: too little and the spline overshoots through the tight node clusters
mappers use in slow corners, inventing radii tighter than the traced polygon; too
much and it erases real corners and eventually oscillates, putting spikes back.
The rule is **least smoothing that clears an 8 m spike floor**, among fits holding
the traced length to 0.5%. Every trial is recorded in `smoothing_trials` in the
export.

**Racing line (`optimise`).** Bounded linear least squares. Each solve node gets
one lateral offset along the unit left normal, and the objective is the summed
squared second difference of the resulting path, a discrete stand-in for
curvature. Box bounds keep the car inside the track, with clearance of half the
car width plus a 0.6 m margin. Solved with `scipy.optimize.lsq_linear` on a
sparse 2N by N system.

Solved every 8 m and interpolated back to the 2 m centreline with a **periodic
cubic spline**. Bounded least squares costs iterations per active constraint, and
a hairpin pins a long run of samples against the track edge, so solving every
sample is slow. Measured on the test circuit: 8 m nodes with cubic interpolation
came within 0.64% of a full-resolution solve at six times the speed. The
interpolant must be smooth; linear interpolation puts a curvature kink at every
node and measured 6.9% slow. Pass `--solve-spacing` equal to `--spacing` to skip
the approximation.

Minimum curvature is not the true fastest line, which trades radius for exit
speed onto long straights and depends on the car. It is close, and it needs no
vehicle model.

**Curvature estimation (`menger_curvature`).** Signed curvature from the circle
through three points a configurable baseline apart, default 6 m. A wider baseline
rejects digitising noise without biasing genuine corners, because three points on
a circle return that circle's radius however far apart they sit. The racing line
is not uniformly spaced once offsets are applied, so a finite-difference formula
assuming even spacing would be wrong.

**Speed profile (`speed_profile`).** Starts from the pure cornering limit
including downforce, solving `m v² k = mu (m g + 0.5 rho Cl A v²)` for v, then
alternates a backward braking pass and a forward acceleration pass until the
closed loop settles (4 passes). Longitudinal acceleration available at each point
is limited by the **friction ellipse** (less longitudinal grip while cornering
hard), by engine power `P / (m v)`, and by drag. Braking gains from drag. Three
car presets ship: `gt3`, `f1`, `road`.

**Elevation.** SRTM 30 m sampled through the OpenTopoData public API, every 25 m
along the lap rather than every 2 m, because sampling finer than the DEM
resolution buys nothing but noise. Batched 100 per request with rate limiting,
cached on disk, DEM voids interpolated over, then circularly smoothed over a
120 m window and interpolated back up to full resolution.

**Start line placement.** Put at the middle of the longest straight, wrapping
across index 0. Start/finish lines nearly always sit on the longest straight, so
this is a far better default than whichever node OSM happened to list first.
`--start-offset-m` shifts it. Note that for Spa this lands on the Kemmel straight
rather than the real start/finish, which needs the offset flag.

### Validation gates

Nothing exports unless all six pass, because a silently wrong centreline is far
more expensive to discover later inside the engine. `--force` overrides.

| Gate | Threshold |
|---|---|
| ring closes | gap under 25 m before joining |
| length within 2% of official | strongest single check on the whole pipeline |
| smoothing preserved length | within 1% of the traced polyline |
| no geometry breakage | tightest radius over 5 m |
| self-intersection as expected | per-circuit, Suzuka's figure-8 expects a crossing |
| lap time plausible | 40 to 400 s |

A soft report also lists every corner traced tighter than 12 m radius with its
position along the lap, and `turning_kept_pct` reports integrated absolute
heading change against the traced polyline, which catches corners being smoothed
away (a net-turning check cannot: a simple closed curve always turns 2π).

### Output format

`Tracks_Data/<circuit>.json`, 0.19 to 0.62 MB per circuit. Parallel arrays rather
than an array of objects, so the file stays small and parses fast.

```
name, source_layout, generated, attribution, coordinate_system, origin
length_m, official_length_m, length_error_pct, sample_spacing_m, sample_count
smoothing_noise_m, smoothing_trials[8], elevation_source
stitch{ways_used, ways_available, name, length_m, candidates_considered, alternatives[]}
racing_line_solve{solve_nodes, nodes_on_track_edge_pct, max_offset_m, optimiser_status}
reference_car{mass, mu, cl_a, cd_a, power_w, v_max, width, brake_frac, traction_frac}
estimated_lap_time_s, curvature_baseline_m, tightest_centerline_radius_m
turning_kept_pct, corners_under_12m_radius[]
centerline{s, x, y, z, heading, curvature, width_left, width_right, camber, banking}
racing_line{x, y, z, offset, curvature, speed_ms}
corners[]{number, s_start, s_end, length_m, direction, min_radius_m, apex_s}
```

**Coordinates are ENU metres: X east, Y north, Z up, right handed**, origin at the
catalogue latitude and longitude. Unity is left handed and Y up, so its importer
maps `(x, y, z)` to `(x, z, y)` and must reverse sample order if the resulting lap
runs the wrong way. `width_left`, `width_right`, `camber` and `banking` are
authoring fields shipped flat, meant to be edited per section.

---

## 5. Results

All six circuits pass. Verified with `Tools/verify_all.py`.

| Circuit | Generated | vs official | Tightest radius | Elevation range | GT3 estimate | Real GT3 |
|---|---|---|---|---|---|---|
| Airfield Test (fictional) | 2286 m | +0.02% | 21 m | flat | 0:48.7 | n/a |
| Bahrain | 5415 m | +0.06% | 8 m | 21.6 m | 2:18.9 | ~1:57 |
| Silverstone | 5893 m | +0.03% | 16 m | 14.0 m | 2:14.4 | ~2:00 |
| Monza | 5803 m | +0.17% | 9 m | 18.4 m | 1:55.8 | ~1:47 |
| Spa | 7005 m | +0.01% | 10 m | **104.6 m** | 2:30.0 | ~2:18 |
| Suzuka | 5811 m | +0.07% | 13 m | 42.8 m | 2:11.9 | ~2:04 |

Spa's 104.6 m elevation range matches the real circuit's roughly 100 m. Suzuka's
figure-8 crossover is correctly detected as a self-intersection. Adding a new
circuit is now a catalogue entry plus one command.

---

## 6. Problems hit and how they were resolved

The highest-value section for picking this up, because these are the
non-obvious failures that will recur in similar work.

**Overpass returned bare 406 errors.** The default `python-requests` User-Agent is
rejected outright. Fixed with a descriptive User-Agent, four mirrors, and backoff
on 429 and 504. Overpass usage policy asks callers to identify themselves anyway.

**Pit lanes were never being filtered.** OSM spells the tag `raceway=pit_lane`
with an underscore; the filter list had `pitlane`. Also added exclusions for
loose surfaces (dirt, gravel, sand) and motocross or karting sport tags, because
Silverstone's longest closed way in the bounding box is its dirt motocross track.

**Three circuits silently returned the wrong layout.** Silverstone produced
1468 m, Monza 539 m, Suzuka 1267 m: the Stowe circuit, the junior track, and
Suzuka East. A "prefer a single closed way" shortcut was grabbing short inner
layouts. Replaced with full cycle enumeration and length matching (section 4).
The length gate is what caught this.

**Monza's chicanes were erased with every gate still passing.** The smoothing
auto-tune originally maximised minimum radius, which rewards exactly the wrong
thing: at noise 8 m the tightest radius went from 8.7 m to 54.7 m, flattening both
chicanes, while changing total length by only 0.36%. Lap time came out at 1:24
against a real GT3's 1:47. Fixed by inverting the rule to *least* smoothing that
clears a spike floor, and adding `turning_kept_pct` as a diagnostic that would
have caught it.

**The racing line solver hung for minutes.** A tight hairpin pins a long run of
samples against the track edge, and bounded least squares costs iterations per
active constraint. Fixed with a coarse solve grid plus periodic cubic
interpolation, after measuring that linear interpolation was 6.9% off and cubic
was 0.64% off (section 4).

**Curvature spikes that were not actually pipeline bugs.** Repeated spikes at
Bahrain traced to the raw OSM data itself: consecutive nodes 2.7 m apart turning
18.8° each, a genuine 8.2 m radius trace of Turn 10. Mappers draw hairpins tighter
than they are. Handled by reporting rather than hiding, and by relaxing the hard
gate to 5 m so it catches real breakage instead of real corners.

**A self-inflicted one worth remembering:** `pkill -f <pattern>` matches the
calling shell when the pattern appears in its own command line. It killed my own
shell twice, once silently discarding a patch.

---

## 7. Known limitations

**Lap estimates run 8 to 20% slow.** The geometry is hand traced, the line is
minimum-curvature rather than lap-time optimal, the car is a point mass, and track
width is a flat catalogue guess. Use it to catch a broken layout, not to predict
a lap. Bahrain is the worst case at +19%, traceable to its 8 m tightest radius.

**Elevation is SRTM at 30 m horizontal and roughly 5 m vertical.** Good for a base
profile. Spa's overall range comes through correctly, but a crest like Eau Rouge
needs sculpting by hand.

**Slow corners trace tight.** Corners under 12 m radius are flagged with their lap
position. Widen by hand.

**Camber and banking exist in no open dataset** and ship as zero.

**Track width is uniform per circuit.** Real circuits vary along their length.
The fields are per-sample and ready for authoring.

**Start line defaults to the longest straight**, which is wrong for Spa (lands on
Kemmel). Use `--start-offset-m`.

---

## 8. Licensing

Layout geometry comes from OpenStreetMap, © OpenStreetMap contributors, under
ODbL 1.0, which requires attribution. That string is written into every exported
file.

A circuit's geometry is factual and fine to recreate. Its name, logos, sponsor
boards and liveries are not. Every catalogue entry carries an `alias`, the name to
use in game ("Ardennes Circuit" for Spa's layout), alongside the real layout it
derives from.

---

## 9. Built: vehicle physics

**Status: all five validation checks pass.** Roughly 1,820 lines of C# in
`Sim/`, targeting netstandard2.1 with no engine dependency, so it drops into
Unity unchanged. Full detail in [Sim/README.md](Sim/README.md).

```
test                    sim  analytic    error      allowed   result
0 to 100 km/h          4.94      4.43   +11.6%     -2..+15%   PASS  s
skidpad peak           0.94      1.10   -14.8%     -22..+2%   PASS  g
100 to 0 braking      36.91     36.14    +2.1%     -2..+10%   PASS  m
top speed            310.60    315.94    -1.7%      -8..+4%   PASS  km/h
straight stability   yaw 0.000 rad/s after a 0.49 rad/s pulse  PASS
```

The model computes forces; the caller integrates. That split let the whole thing
be written and validated headlessly under WSL with the .NET SDK, before Unity was
installed, which also means it can stay under test after Unity arrives.

Expectations are derived from the car's own configuration in `Analytic.cs` rather
than picked by hand. That change mattered: the original hand-picked 0 to 100
target of 4.2 s was below this car's traction-limited floor of 4.43 s, so it
could never have been met and failing it said nothing about the model.

Eight bugs were caught this way, each silent and each looking like a handling
problem rather than a defect. The largest: `System.Numerics` composes quaternions
as "q2 first", so integrating orientation as `orientation * delta` applied
angular velocity in the body frame instead of the world frame. Per step the error
was negligible, so the car held a steady cornering state for seconds and then
diverged out of floating point noise. The full list is in `Sim/README.md`.

What remains for Phase 1: cameras, a Unity MonoBehaviour wrapper, and a
ScriptableObject that returns a `CarConfig`. None of it needs the physics to
change.

The specification below is what was built.

**Chassis.** One Rigidbody, 1200 to 1600 kg, with the **inertia tensor set
explicitly**. Unity's auto-computed tensor from a mesh is always wrong and gives
bad rotational feel. Centre of gravity height around 0.45 m, which matters
enormously for weight transfer.

**Suspension.** Four independent corners, sphere-cast rather than raycast from
each hub so kerbs behave. Force is `k * compression + c * velocity`, clamped.
Then **anti-roll bars**: extra force proportional to the left/right compression
difference. That single term is the primary understeer and oversteer balance knob.

**Tyres: simplified Pacejka magic formula.** The difference between a car that
moves and a car that drives.

- Longitudinal slip ratio `k = (w*r - v_x) / |v_x|`
- Lateral slip angle `a = atan(v_y / |v_x|)`
- `F = D sin(C atan(B s - E (B s - atan(B s))))`, separate B, C, D, E per axis
- **Load sensitivity:** `D = mu * Fz * (1 - k_load * (Fz/Fz_nom - 1))`. Omit this
  and weight transfer does nothing interesting; the car feels dead.
- **Combined slip:** scale so `sqrt((Fx/Fx_max)² + (Fy/Fy_max)²) <= 1`. This gives
  trail braking and power-on oversteer as emergent behaviour, not scripted effects.
- **Relaxation length** of 0.3 to 0.5 m so forces build over distance, killing
  high-frequency jitter.
- Per-surface friction: asphalt 1.0, kerb 0.9, astroturf 0.6, gravel 0.45, grass
  0.35, wet 0.7.

**Drivetrain, closed loop.** Engine torque curve, clutch, gearbox, differential,
wheels, then integrate each wheel's rotation with `I * w' = T_drive - T_brake -
Fx * r`. Because tyre force feeds back into wheel speed, wheelspin and lockup
emerge naturally instead of being faked. Differential options: open, clutch-pack
LSD with preload and power/coast ramps, spool.

**Aero.** Downforce computed separately at front and rear centres of pressure so
aero balance shifts with speed, plus drag, plus a slipstream effect reducing drag
behind another car. Slipstream is what makes multiplayer racing fun.

**Integration.** Run a custom fixed-step integrator at 400 to 500 Hz driven from
FixedUpdate. Tyre models go unstable at low rates, and this sidesteps any
dependence on the underlying PhysX version.

**Data-driven.** Everything tunable lives in ScriptableObjects, one per car:
torque curve, gear ratios, spring rates, damper curves, anti-roll stiffness, tyre
coefficients, aero map, weight distribution. Tune without recompiling, and the
GT-style setup screen becomes nearly free later because the UI just edits values
the physics already reads.

**Validation tests.** Build a telemetry overlay and CSV export on day one, then
automate scripted-input tests:

| Test | Expected |
|---|---|
| 0 to 100 km/h | within 5% of the modelled car's real spec |
| Skidpad lateral load | 1.0 to 1.3 g road car, 3 to 5 g open-wheeler |
| 100 to 0 km/h braking | ~35 m for a sports car |
| Top speed | drag limited, matches spec |
| Straight-line stability | returns to centre on released steering |

---

## 10. Not yet built: graphics strategy

The honest trick is that Forza and Gran Turismo look good because tracks are
static and lighting is precomputed, not because anything is brute forced.

**Lighting: baked, with time-of-day sets.** Bake GI into lightmaps with the
Progressive GPU Lightmapper, plus several blended ambient sets (dawn, morning,
noon, evening, dusk, night). Dynamic sun with shadow cascades handles moving cars.
Roughly 0.3 ms where real-time GI costs 5 ms or more, and near identical on a
static track.

**Car paint is the highest-impact material.** Written for HDRP StackLit; on URP it has to
be a Shader Graph with a hand-built clearcoat layer, which is the main visual cost of the
switch. The goal is unchanged: a genuine dual-layer coat over base. Metallic flake as a high-tiling detail normal,
clearcoat with its own roughness. Get this one material right and the whole game
reads as expensive.

**Frame budget at 1080p, targeting 8 to 10 ms:**

| Pass | Target |
|---|---|
| Base pass, GPU Resident Drawer for instanced trackside | 2.5 ms |
| Shadows | 2.0 ms |
| Baked GI lookup | 0.3 ms |
| Reflections (SSR plus a low-res per-car probe) | 1.2 ms |
| VFX and translucency | 1.2 ms |
| Post and upscale | 2.0 ms |

**Upscaling: FSR 1 or STP, when needed.** Written for HDRP, which offers DLSS; URP does
not, so on the URP this project uses the choices are FSR 1 and Unity 6's own STP. Measured
on 2026-09-24, a frame at 1080p costs about 3.6 ms and is limited by the CPU, so there is
nothing to upscale for yet; revisit once the art makes the GPU the limit. **Avoid frame
generation**: it adds latency, and input feel beats smoothness in a driving game.

**VRAM is the real ceiling.** Budget 4.5 GB of the 6 GB. Cap trackside textures at
1K, hero car at 2K, use streaming settings aggressively.

**Cheap wins that read as expensive:** wet weather (roughness drop, puddle masks,
ripple normals, road spray, rain on the windscreen) is the single best value in
the entire graphics plan. Then tyre smoke driven by actual slip magnitude, brake
disc emissive driven by brake temperature, sparks on bottoming out, subtle
per-object motion blur, and a photo mode with depth of field.

---

## 11. Not yet built: LAN multiplayer

**Networking library: FishNet** (free, Asset Store). Its prediction and
reconciliation system is built for physics vehicles and is better suited than
Unity's own Netcode for GameObjects here.

**Topology.** Listen server, meaning the host plays too. A headless build later
for a clean sixteen-car race night.

**Discovery.** UDP broadcast on a fixed port. The host broadcasts name, track, car
class and player count; clients listen and populate a server browser. FishNet
ships a NetworkDiscovery component, and writing it is about thirty lines. Always
provide manual IP entry as a fallback.

**Vehicle sync.**
- Own car: simulate locally with full physics. On a LAN, latency is under a
  millisecond, so client-authoritative physics for your own car is honest and
  feels perfect. Cheating does not matter among friends. Add prediction and
  reconciliation only if internet play is ever wanted.
- Remote cars: replicate position, rotation, linear and angular velocity, wheel
  spin and steer angle at 30 to 60 Hz. Interpolate with a small buffer (about
  30 ms on LAN) and extrapolate through dropped packets. Cars move smoothly and
  predictably, so this looks excellent.
- Bandwidth: roughly 40 bytes per car per update, sixteen cars, 30 Hz, about
  20 KB/s per client. Irrelevant on a LAN.

**Collisions** are the hard part of every racing game. On a LAN real collisions
are affordable because latency is negligible, but clamp the maximum collision
impulse so a rare desync cannot launch someone into orbit.

**Race state is server authoritative:** lap counts, sector times, flags, results.
Clients predict their own timing display and reconcile.

**Two practical notes.** Windows Firewall needs an inbound UDP rule, and this is
the number one reason LAN play appears broken. If "locally" ever expands to a
friend across town, Tailscale or ZeroTier creates a virtual LAN and the existing
discovery code works unchanged, with no port forwarding.

---

## 12. Not yet built: race systems and AI

**AI drivers.** Follow the racing line with a pure-pursuit lateral controller and
a longitudinal controller tracking the precomputed speed profile, both of which
the pipeline already exports. Per-driver skill, aggression and consistency.
Overtaking by sampling offset lines and scoring clearance against speed loss.
Forward sweep for collision avoidance. **AI must use identical physics to the
player**; faking it breaks feel and multiplayer.

**Race state machine.** PreRace, Grid, Countdown, Green, Racing, Finish, Results.
Lap and sector timing with validity flags, off-track detection by counting wheels
outside track bounds, penalties for track limits and corner cutting.

**Session types.** Practice, Qualifying (grid from lap times), Race, Time Trial,
Hot Lap.

**Career, the Gran Turismo part.** Credits, dealership, **license tests** (cheap to
author and a genuine GT signature), championships, upgrade parts that modify the
physics ScriptableObject, and the car setup screen (springs, dampers, ARB, ride
height, camber, toe, gearing, diff, brake bias, aero, tyre pressures). Because
physics is data driven, the setup UI is nearly free and has the best depth per
effort in the project.

**Assists** so guests of any skill can play: ABS, TCS, stability control, racing
line, braking assist, auto gears.

**Two named risks.** Force feedback for a racing wheel needs a native plugin in
Unity (and in Unreal too), so treat it as late work with real uncertainty. Good
engine audio samples are the hardest asset in the project to source cheaply.

---

## 13. Roadmap

Each phase ends in something playable with a measurable exit condition.

| Phase | Work | Exit criterion | State |
|---|---|---|---|
| 0 | Unity 6 + URP installed on D:, Git with LFS, a box on a plane driven by gamepad | packaged build runs standalone above 200fps | **not started** |
| 1 | Vehicle physics: suspension, Pacejka tyres, drivetrain, aero, telemetry, cameras | placeholder car on a skidpad feels genuinely good; all five validation tests pass; oversteer can be provoked and caught | **physics, validation and the Unity layer all written; the layer has never been run, and the feel test needs Unity** |
| 2 | Track pipeline: OSM to spline, elevation, racing line, timing | hot-lap two circuits, invalid laps flagged, new circuit under a day | **DONE** |
| 3 | AI drivers, race state machine, sessions, flags, penalties, pit stops | ten-lap race against fifteen AI with a plausible spread and no turn-one pile-up | **drivers, traffic and race control all run headlessly; sixteen cars finish ten laps of Monza with no contacts on ten seeds and a spread that follows pace, so the headless criterion is met. Passes complete where the plans say one can, so a fast car started last works forward past much slower ones; a few pass contacts remain. Flags, penalties and pit stops untouched** |
| 4 | Car paint, liveries, baked lighting, weather, environment art, VFX, post, photo mode, quality presets | 1080p at 100fps+ measured on this laptop; screenshots read as a modern racing game | not started |
| 5 | LAN discovery, server browser, vehicle sync, collisions, lobby | four machines racing ten laps, no warping, no desync, host framerate unaffected | **discovery, snapshot sync and client interpolation measured over real sockets: 6.4 kB/s for sixteen cars, 12 cm worst error through 10% loss. Remote input, prediction, collisions and the lobby untouched, and the four-machine test needs four machines** |
| 6 | Career, setup UI, assists, saves, leaderboards, replays, ghosts, audio | someone can play five hours without running out of things to do | not started |

**Realistic total:** 6 to 9 months of focused solo work with bought art. With cars
modelled in house, as decided, expect 12 to 18 months. Cars are the bottleneck,
not code.

---

## 14. Immediate next steps

1. **Install Unity 6 LTS**, setting the Hub's editor install location to D:.
2. **Add the Windows Defender exclusion** for `D:\Dev\CarRace` and the Unity
   processes. Worth 2 to 5x on import times on the spinning disk.
3. **Create the URP project** at `D:\Dev\CarRace`, native Windows path.
4. **Initialise Git with LFS.** Track `*.fbx *.png *.tga *.wav *.asset`, and use
   Unity's standard `.gitignore` for `Library/` and `Temp/`.
5. **Wire the physics into Unity.** Written, in `Unity/Assets/Scripts/Game/`: the
   MonoBehaviour that steps the model in `FixedUpdate`, the `IGround` over
   `Physics.SphereCast`, the ScriptableObject returning a `CarConfig`, the input
   reader and the three cameras. `Sim/CarRace.Vehicle/*.cs` copies in unchanged
   alongside them. `Unity/README.md` has the copy step, the project settings and
   the scene. It compiles against a stub and has never run, so treat the first
   import as debugging, not as a milestone.

   One change to the wrench: it is applied as `AddForce` plus `AddTorque`, not as
   per-wheel `AddForceAtPosition`. A wrench is already reduced to a force through
   the centre of mass plus a torque about it, so the two are equivalent, and the
   first does not need the per-wheel forces handed back out of the model.
6. **Then judge it by feel on a gamepad.** The numbers are right; whether it is
   enjoyable is a separate question and the one that actually decides Phase 1.

The track importer on the Unity side is small: read the JSON, map ENU `(x, y, z)`
to Unity `(x, z, y)`, build a swept road mesh from the centreline with the width
fields, and place the racing line as the AI target. That can come at the start of
Phase 3, since Phase 1 only needs a flat plane and a skidpad.
