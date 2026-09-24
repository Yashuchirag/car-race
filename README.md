# car_race

A Gran Turismo style circuit racer: real circuit layouts, grounded vehicle
physics, AI opponents, a career, and LAN multiplayer for racing with friends.

Built to look good and run well on an ordinary gaming laptop rather than to chase
a spec sheet. The performance target is **1080p at 100 to 120fps on an RTX 2060
Mobile**, because racing games live on input latency and the reference machine has
a 144 Hz panel.

## Status

| Component | State |
|---|---|
| **Track pipeline** | **Working. 6 circuits passing, all within 0.17% of published length** |
| **Vehicle physics** | **Working. 5 of 5 checks pass against closed-form physics** |
| Unity project | Not started. Needs Unity 6 installed, which is a manual step |
| AI, race systems, multiplayer, art | Specified, not started |

The physics is engine-agnostic C# targeting netstandard2.1, so it drops into
Unity unchanged. It was written and validated headlessly, before Unity was
installed, by running it against closed-form expectations derived from the car's
own configuration. See [Sim/README.md](Sim/README.md).

**[PROGRESS.md](PROGRESS.md)** is the live state: what is done, what is
half-finished and where it stopped, and the next concrete action. Start there when
picking the project back up.

Read **[IMPLEMENTATION_REPORT.md](IMPLEMENTATION_REPORT.md)** for the full
picture: decisions and their reasoning, every algorithm choice, problems already
solved, and the phased roadmap. That document is the pick-up point.

## Circuits generated

| Circuit | Generated | vs official | Elevation range | GT3 estimate |
|---|---|---|---|---|
| Airfield Test (fictional) | 2286 m | +0.02% | flat | 0:48.7 |
| Bahrain | 5415 m | +0.06% | 21.6 m | 2:18.9 |
| Silverstone | 5893 m | +0.03% | 14.0 m | 2:14.4 |
| Monza | 5803 m | +0.17% | 18.4 m | 1:55.8 |
| Spa | 7005 m | +0.01% | 104.6 m | 2:30.0 |
| Suzuka | 5811 m | +0.07% | 42.8 m | 2:11.9 |

Adding another circuit is a catalogue entry plus one command.

## Tech stack

| Layer | Choice |
|---|---|
| Engine | Unity 6.3 LTS with URP (switched from HDRP on 2026-09-24) |
| Language | C#, with vehicle dynamics as plain math on a Rigidbody at 400 to 500 Hz |
| Networking | FishNet, listen server, UDP broadcast LAN discovery |
| Data pipeline | Python 3.10 under WSL: numpy, scipy, pyproj, shapely, matplotlib, requests |
| Physics validation | .NET 8 SDK under WSL, for running the vehicle model headlessly |
| Art | Blender 4.x, free and CC0 sources, cars modelled in house |
| Version control | Git with LFS |
| Profiling | Unity Profiler, RenderDoc, Nsight Graphics |

Unity was chosen over Unreal on hardware grounds, not on merit in the abstract.
Unreal's advantage is Lumen and Nanite, and a 6 GB RTX 2060 cannot run either at
the target framerate, so that ceiling is unreachable anyway. The full reasoning,
including what would justify revisiting it, is in the implementation report.

## Layout

```
car_race/
  README.md                     this file
  IMPLEMENTATION_REPORT.md      decisions, algorithms, roadmap. Start here.
  Tools/
    README.md                   track pipeline reference
    build_track.py              CLI: OSM circuit -> track JSON + plot
    verify_all.py               regression run across every circuit
    visualize.py                verification plot
    circuits.json               circuit catalogue
    trackgen/
      geo.py                    WGS84 to local metric ENU
      osm.py                    Overpass fetch, cycle search, loop selection
      centerline.py             smoothing spline, curvature, corner detection
      elevation.py              SRTM sampling and smoothing
      racing_line.py            minimum-curvature line, speed profile
    out/                        verification plots and caches (gitignored)
  Tracks_Data/                  generated track JSON, committed
  Sim/
    README.md                   vehicle physics reference
    CarRace.Vehicle/            the model: netstandard2.1, no engine dependency
      CarConfig.cs              every tunable number for one car
      Tyre.cs                   Pacejka magic formula, per-wheel state
      Drivetrain.cs             engine, launch clutch, gearbox, limited slip diff
      VehicleSim.cs             suspension, tyres, aero, steering, driver aids
      Types.cs                  inputs, ground interface, force accumulator
    CarRace.Harness/            headless validation
      Analytic.cs               closed-form expectations from the config
      RigidBody.cs              stands in for a Unity Rigidbody
      Rig.cs                    fixed 500 Hz substepping, measurement helpers
      TrackLoader.cs            reads the pipeline's JSON, ENU to Y-up
      LapRun.cs                 drives a generated circuit and reports
      RaceRun.cs                a field of AI cars, contacts counted not simulated
      NetRun.cs                 host and client over real sockets, error measured
      Program.cs                the five checks, traces, telemetry export
    CarRace.Track/              track geometry, drivers and race control, engine agnostic
      TrackData.cs              samples, widths, curvature, lateral offsets
      SpeedPlan.cs              speed at every sample, one friction ellipse both ways
      PathDriver.cs             curvature feedforward plus feedback, PI on speed
      RaceDriver.cs             pace personality, traffic, overtaking
      RaceControl.cs            grid, laps, positions, classification
    CarRace.Net/                multiplayer wire format, no sockets in it
      BitBuffer.cs              bit level writer and reader, quantisation
      Snapshot.cs               car state packed to 21 bytes, smallest-three rotation
      Interpolator.cs           the client's buffer: render late, blend two real states
      Beacon.cs                 what a host broadcasts so a client can find it
    CarRace.UnityCheck/         type-checks the Unity scripts without Unity
      UnityEngineStub.cs        signatures only, nothing here runs
  Unity/
    README.md                   scene setup, project settings, how to copy it in
    Assets/Scripts/Game/        the Unity integration layer
      Bridge.cs                 vector and quaternion conversion, convention check
      CarDefinition.cs          ScriptableObject returning a CarConfig
      UnityGround.cs            IGround over Physics.SphereCast
      CarController.cs          runs the model in FixedUpdate, applies the wrench
      DriverInput.cs            keyboard and gamepad to normalised inputs
      CarCamera.cs              chase, hood and cockpit views
```

## Quick start

```bash
# one-time setup
python3 -m venv Tools/.venv
Tools/.venv/bin/python -m pip install -r Tools/requirements.txt

# generate a track (writes Tracks_Data/<name>.json and Tools/out/<name>.png)
Tools/.venv/bin/python Tools/build_track.py spa
Tools/.venv/bin/python Tools/build_track.py testcircuit --no-elevation
Tools/.venv/bin/python Tools/build_track.py monza --car f1

# check nothing is broken
Tools/.venv/bin/python Tools/verify_all.py

# vehicle physics: the five validation checks
dotnet run --project Sim/CarRace.Harness -c Release

# both halves together: drive the validated car round every generated circuit
dotnet run --project Sim/CarRace.Harness -c Release -- --lap all
dotnet run --project Sim/CarRace.Harness -c Release -- --lap monza --verbose

# a field of AI cars. Sixteen cars finish ten laps of Monza without touching, which is
# the Phase 3 exit criterion. --fastest-last tests passing; PROGRESS.md section 1 has numbers.
dotnet run --project Sim/CarRace.Harness -c Release -- --race monza --cars 8 --laps 3
dotnet run --project Sim/CarRace.Harness -c Release -- --race monza --cars 16 --laps 10 --verbose

# LAN sync: a host and a client over real UDP, with a bad network simulated
dotnet run --project Sim/CarRace.Harness -c Release -- --net monza --cars 16 --seconds 30
dotnet run --project Sim/CarRace.Harness -c Release -- --net monza --latency 120 --jitter 40 --loss 10

# Unity integration layer: compiles against a UnityEngine stub, so a broken
# call into the model fails here instead of in the editor
dotnet build Sim/CarRace.UnityCheck -c Release
```

**Look at the generated plot before importing anything.** A wrong loop,
over-smoothed corners or a nonsense speed profile are obvious there and expensive
to find later. Overpass and elevation responses are cached, so repeat runs need
no network.

See [Tools/README.md](Tools/README.md) for flags, the output format, and how the
pipeline decides which loop to take and how much to smooth.

## A note on where files live

The whole project stays on **D:**, deliberately, to keep the C: SSD from filling.
WSL already lives at `D:\Software\Ubuntu`, so this directory is physically on D:
and the Python side needs no special handling.

When the Unity project is created it goes at a **native Windows path** such as
`D:\Dev\CarRace`, never inside the WSL filesystem, which Unity reads slowly
through `\\wsl.localhost`. Set Unity Hub's editor install location to D: as well.

D: is a 5400rpm drive, which costs iteration time (asset import, shader cache) but
not runtime framerate, since the game runs from RAM and VRAM once loaded. Adding a
Windows Defender exclusion for the project folder and the Unity processes is
typically worth 2 to 5x on import times.

## Accuracy

Lap-time estimates run 8 to 20% slow against real times for the same class. The
geometry is hand traced, the line is minimum-curvature rather than lap-time
optimal, and the reference car is a point mass. Use the estimate to catch a broken
layout, not to predict a lap. Elevation is SRTM at 30 m horizontal and roughly 5 m
vertical, which gives a good base profile but needs hand sculpting for features
like Eau Rouge. Camber and banking exist in no open dataset and ship as zero.

## Licensing

Two licences, because the repository holds two different kinds of thing.

**Code is MIT.** Everything in `Tools/` and `Sim/`, and the documentation. See
[LICENSE](LICENSE). Use it for anything.

**Generated circuit data is ODbL.** The files in `Tracks_Data/` are a Derived
Database built from OpenStreetMap, © OpenStreetMap contributors, so they carry
[ODbL 1.0](https://opendatacommons.org/licenses/odbl/1-0/) and its share-alike
terms. See [Tracks_Data/LICENSE](Tracks_Data/LICENSE). Each file also records its
own provenance in its `attribution` field.

ODbL covers the database, not programs that read it, so the MIT licence on the
pipeline is unaffected. The one exception in that directory is
`testcircuit.json`, an original fictional layout with no OpenStreetMap data in it,
which is MIT like the rest.

Elevation comes from SRTM via OpenTopoData and is public domain.

A circuit's geometry is factual and fine to recreate. Its name, logos, sponsor
boards and liveries are not, and none are reproduced here. Every catalogue entry
carries an in-game alias, so Spa's layout ships as "Ardennes Circuit".
