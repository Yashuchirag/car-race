# Unity integration layer

The Unity-side glue for the vehicle model. Six scripts, and nothing in them decides
how the car behaves: the physics lives in `Sim/CarRace.Vehicle/`, which has no
reference to Unity and is validated headlessly. These files carry the model's forces
into a `Rigidbody` and put a camera behind the car.

| File | What it does |
|---|---|
| `Bridge.cs` | Converts vectors and quaternions, and checks at startup that Unity and `System.Numerics` agree about rotation |
| `CarDefinition.cs` | ScriptableObject holding every tunable number, returns a `CarConfig` |
| `UnityGround.cs` | `IGround` over `Physics.SphereCast`, with a raycast fallback |
| `CarController.cs` | Runs the model in `FixedUpdate` and applies the wrench |
| `DriverInput.cs` | Keyboard and gamepad to normalised inputs |
| `CarCamera.cs` | Chase, hood and cockpit views |

## State

Compiled, never run. On 2026-09-24 both folders were copied into the Unity 6.3 LTS project
at `D:\Dev\CarRace` (URP) and compiled against the real engine with no errors and no
warnings, so the stub below matched Unity everywhere these scripts touch it. The project
settings below are applied. Nothing has been driven yet: that needs the scene and a person.
The stub check still earns its place, because it runs in seconds without the editor:

```bash
dotnet build Sim/CarRace.UnityCheck -c Release
```

That project compiles these scripts against a signature-only `UnityEngine` stub. It
catches typos and wrong calls into the model. It cannot catch a stub signature that
differs from the real engine, so expect a few compile errors on first import, and fix
the stub when you hit one rather than deleting the check.

## Getting it into the Unity project

The Unity project lives on `D:` as a native Windows path, outside this repo. Both
folders copy in, and the model copies unchanged:

```bash
# from the repo root, in WSL
mkdir -p /mnt/d/Dev/CarRace/Assets/Scripts/Vehicle /mnt/d/Dev/CarRace/Assets/Scripts/Game
cp Sim/CarRace.Vehicle/*.cs        /mnt/d/Dev/CarRace/Assets/Scripts/Vehicle/
cp Unity/Assets/Scripts/Game/*.cs  /mnt/d/Dev/CarRace/Assets/Scripts/Game/
```

Copy again after editing either side. This repo stays the source of truth for the
scripts, because it is the copy under test. The Unity project is its own Git repo, for
everything else it holds (settings, packages, scenes, materials and, later, art in Git
LFS); commit the copied scripts there too, so the two stay in step. Use `git.exe` for it
from WSL: WSL's git over `/mnt/d` is slow and churns line endings.

## Project settings that matter

1. **Time, Fixed Timestep: `0.005`.** The default 0.02 is 50 Hz. The model substeps
   internally, but it reads the car's pose only once per physics step, so at 50 Hz load
   transfer lags by 20 ms and the car feels vague and late. `CarController` logs a
   warning if this is left alone.
2. **Player, Active Input Handling: `Both`** or `Input Manager (Old)`. `DriverInput`
   uses the old manager so that a car drives with no input asset to author first.
3. **A layer for the car.** Put the car on its own layer and clear that layer from
   `Ground Layers` on `CarController`. The wheel probes start at the top of the strut,
   which is inside the body, so a mask that includes the car reports every wheel fully
   compressed and throws it into the sky. `UnityGround` logs an error if it notices.
4. **Physics, Default Solver Iterations: 12** or so. Only matters once cars collide.

## Scene for the feel test

**The quick way:** `Assets/Editor/SkidpadSceneBuilder.cs` adds a menu, `CarRace, Build
Skidpad Scene`, that does every step below, adds the two trigger axes to the Input Manager,
and saves `Assets/Scenes/Skidpad.unity`. Copy it in with the rest:

```bash
mkdir -p /mnt/d/Dev/CarRace/Assets/Editor
cp Unity/Assets/Editor/*.cs /mnt/d/Dev/CarRace/Assets/Editor/
```

Running it again rebuilds the scene from scratch but keeps `Assets/Cars/ReferenceCar.asset`,
so tuned car values survive. The steps below are what it does, for building it by hand.

| Action | Keyboard | XInput gamepad |
|---|---|---|
| Steer | A / D | Left stick |
| Throttle, brake | W / S | Right trigger, left trigger (or left stick up, down) |
| Reverse (automatic gearbox): hold brake at a standstill, then brake drives backwards and throttle brakes | S | Left trigger |
| Handbrake | Space | A |
| Shift up, down (manual gearbox) | E / Q | RB / LB |
| Recover onto the track where you are (the start on the skidpad) | R | View |
| Restart from the start | Backspace | Menu |
| Cycle camera | C | Y |
| Pause and settings (quality, frame rate) | Esc | |

Keyboard steering is assisted (`Steering Assist` on `DriverInput`, on by default): a key
asks for a turn rate, a share of what the tyres can hold at that speed, and the assist
turns the wheels for it, counter-steering when the car rotates more than asked or starts
to slide. `Throttle Assist` eases the power off while the car slides, between 3 and 8
degrees of sideslip, because a key cannot feed the throttle in. Untick both for a wheel,
or a pad driven with care.

The exit criterion for Phase 1 is whether this is enjoyable on a gamepad, which needs
no track and no art:

1. A plane, scaled 100x, on the Default layer. Give its collider a physics material
   with `Dynamic Friction 1`, which is the grip multiplier the tyre coefficients were
   measured at.
2. An empty GameObject for the car, on a `Car` layer, at `y = 0.45` (the config's
   `CgHeight`, because **the transform origin has to be the centre of mass**).
3. Add `Rigidbody`, a `BoxCollider` with Center 0, 0.3, 0 and Size 1.9 x 0.8 x 4.4, `DriverInput`, and
   `CarController`. Do not touch mass, drag or the inertia tensor: `CarController`
   sets all three from the config, and Unity's derived tensor turns the car into a bus.
4. Right-click in the Project window, `Create, CarRace, Car Definition`. A new asset is
   the reference sports car the harness validates. Assign it to `CarController`.
   The collider's bottom must clear the ground: centred on the origin, a taller box
   reaches into the tarmac and the car grinds on it and will not move.
5. Four empty children as wheel pivots, assigned to `Wheel Visuals` in the order
   front-left, front-right, rear-left, rear-right, each holding a cylinder rotated 0, 0,
   90. The controller sets the pivot's rotation to spin and steer, so a bare cylinder
   would be stood upright. They are cosmetic: the model does the suspension, so give
   them no colliders.
6. On the camera, add `CarCamera` and assign the car as the target. `C` cycles views.
7. Press play. `WASD` drives, `Space` is the handbrake, `R` respawns, `E` and `Q` shift
   when `Automatic Gearbox` is off.

Watch the console on the first run. Three of the four scripts report their own
misconfiguration, and all three failures look like a physics bug if you do not read it.

## Driving a generated circuit

`Assets/Editor/TrackSceneBuilder.cs` adds `CarRace, Build Track Scene`, with one entry per
circuit. It reads the pipeline's JSON and builds the road from the centreline and its
widths, a 15 m grass verge either side, white lines just inside both edges, a white start line,
red and white kerbs (with tarmac grip) outside both edges wherever the road bends tighter
than a 150 m radius, the racing line as a braking guide, and a grid
behind the start line (see Racing the AI). The road follows the file's elevation (Spa climbs
105 m); camber and banking are zero in every file, so it is flat across. Asphalt grips at
1 and grass at 0.45, read by the wheels from each collider's physics material. A 1.2 m
wall with a little friction (0.3), so that sliding along it scrubs speed, runs along the
outside of each verge, on a `Barrier` layer the wheel
probes ignore, so the car glances off it and cannot leave the circuit. White arrows
every 50 m down the middle of the road point the way the lap runs, and a red WRONG WAY
warning appears when the car faces back along the lap for more than 0.75 s. `R` puts the car
back on the track where it is, and `Backspace` back on the grid.

The circuits copy in from the repo, with their licence, since `Tracks_Data/` is ODbL:

```bash
mkdir -p /mnt/d/Dev/CarRace/Assets/Tracks
cp Tracks_Data/*.json /mnt/d/Dev/CarRace/Assets/Tracks/
cp Tracks_Data/LICENSE /mnt/d/Dev/CarRace/Assets/Tracks/LICENSE.txt
```

Each builds to `Assets/Scenes/Track <name>.unity`. The skidpad and the tracks share one
car setup (`SkidpadSceneBuilder.PlaceCar`), so a fix to the car reaches every scene.

## Surface textures

`Assets/Editor/SurfaceTextures.cs` sets up and applies the surfaces' textures in one place.
Grass is ambientCG's Grass005 (CC0, 2K colour, normal and ambient occlusion, in
`Assets/Art/Ground/Grass005`, Git LFS) on the verges, the terrain and the lobby lawn, one
tile every 3 m (4 m on the terrain). The track builder's strips carry texture coordinates
in metres, so any texture tiles at a real size. The road is Poly Haven's Asphalt Track (CC0,
2K colour, normal, ambient occlusion, and a smoothness map made from its roughness by
`Tools/smoothness_map.py`, since URP reads smoothness from the metallic map's alpha), in
`Assets/Art/Ground/AsphaltTrack`, one tile every 2 m, its real size. Credits in
`Assets/Art/CREDITS.md`.

## Render settings

`Assets/Editor/GraphicsSetup.cs` applies the render settings the graphics pass starts from:
`CarRace, Apply Graphics Settings`. MSAA 4x, HDR colour grading, shadows to 150 m, and a
post-processing profile (`Assets/Settings/TrackPostProcessing.asset`: ACES tone mapping,
light bloom, slight vignette), created once and then left for editing. The track builder
adds a global volume with it and turns post-processing on for the camera.

Frame rate and graphics quality are the player's choice, in the Esc menu and kept between
sessions. Frame rate: VSync (the default), 30, 60, 120 or 144 fps, or unlimited. Quality:
Low, Medium or High, Unity quality levels each with its own URP asset (built by `CarRace,
Apply Graphics Settings`); the first run picks from the graphics card's memory. High is
MSAA 4x, 150 m soft shadows in 4 cascades and SSAO; Medium MSAA 2x, 100 m shadows in 2
cascades, no SSAO; Low no MSAA, 60 m hard shadows at 1024, no SSAO, 80% render scale with
FSR. `-frameRate 60` and `-quality Low` on the command line override them for one session. Physics runs at 200 Hz whatever the frame rate, and a frame
catches up at most 0.1 s of it, so a slow machine slows briefly instead of freezing.

`CarRace, Build Windows Player` builds the Monza scene to `Builds/Windows`; run it with
`-benchmark` for a 60 s frame time report beside the executable (see `Benchmark.cs`), and
`-aiLog` to have every AI car's state written to `ai.csv` in the harness race CSV's terms.

## Lap timing

The timing panel in the top right, in the manner of broadcast graphics: a red header with
the circuit and a lap badge, the lap being driven in large figures, a live delta to the
best lap of the session at the same point of the lap (green ahead, red behind, from the
second lap), a lap progress bar in three sector segments, last, best and AI reference, and
sector blocks that fill purple for a new best, yellow for slower, green with no best yet,
pulsing as each finishes. A new best lap flashes a purple banner for 3 s.

`-benchmarkSeconds` lengthens the benchmark, and `-screenshotAt 150,210` takes screenshots at
several moments, for looking at the game later in a race.

Track scenes time laps: `TrackPath` on the circuit holds the centreline, start line at
sample 0, and `LapTimer` on the car shows current, last and best lap, three sectors
against your best sectors, and the headless reference driver's lap for that circuit. A
lap counts only after passing both sector gates in order, so reversing over the line or
cutting across cannot complete one. The clock starts when the car first moves; a respawn
abandons the lap in progress. The best lap is kept per circuit in PlayerPrefs.

## Lobby

A built game opens on the lobby (`Assets/Scenes/Lobby.unity`, built by `CarRace, Build Lobby
Scene`, which puts it first in the build settings): the car turning on a platform under the
circuits' sky, a Players panel (just you for now; LAN players will be listed there), eight
body colours, a Circuit panel with a card for each of the six circuits (outline, length and
scenery theme, from `Assets/Settings/TrackCatalog.asset`, which the track builder fills), and
PLAY (or Enter), which loads the chosen circuit. `-track "Track <name>"` picks one for a session. The colour is kept between sessions
and put on your car when a race loads (`PlayerSetup`); an AI car whose colour is too close
to yours is repainted in one that is not. The Esc menu in a race has Back to lobby.
`-carColour 4` picks a colour for one session, and `-lobbyScreenshot <file>` saves a picture
of the lobby and quits. Under `-benchmark` the lobby goes straight to the race.

## Racing the AI

Track scenes grid three AI cars ahead of you, laid out as the harness does it but either
side of the road's centre (the harness uses the racing line, which on Monza's straight runs
near the edge and put cars on the grass): two abreast 2.5 m either side, rows 10 m apart,
the front row 10 m behind the line, fastest on pole, you at the back. `RaceDirector` on the circuit drives them with the same `RaceDriver` the headless
race uses, through `CarController.Autopilot`, and shows every driver the whole field,
you included, every 20 ms. An AI car stopped for 5 s is put back on its racing line where
it was.

A race starts with a 3, 2, 1, GO countdown, every car held on its brakes, and runs for
`Race Laps` (3 by default, on `RaceDirector`). Laps, positions and finish times are kept
by the harness's `RaceControl`, updated every physics step, with the clock starting at GO;
your position and lap show at the top of the screen. When you take the flag a results
table appears and fills in as the AI finish: position, grid, places gained, best lap (the
fastest marked), race time, gap to the winner and contacts with other cars. Enter races
again.

`MiniMap` shows the road around you in the bottom left corner, 350 m ahead and 40 m
behind, 300 px square at 1080p, turned so the road ahead points up (with the track, not the car, so a spin does not
spin it). Corners tighter than 150 m radius are orange and tighter than 60 m red, the start
line is blue, and the AI show as dots in their body colours when in view.

`RacingLineGuide` draws the racing line as bars, 2 m on and 2 m off, from just behind the
car to 350 m ahead, coloured each frame by how hard you would have to brake from your
current speed to reach each bar at the reference plan's speed (pace 0.85): green under 30%
of the car's planned braking, yellow to 80%, red beyond. Red at a corner means brake now,
or too fast for it; the bars turn green as you slow to its speed.

`Dashboard` puts a rev counter and a speedometer in the bottom right corner, dials with
needles like a car's instrument cluster: 0 to 8,000 rpm with a red zone from 7,000 and the
gear in the middle (red near the rev limit, as a shift light), and 0 to 320 km/h with the
speed in figures. It attaches itself to the player's car at every scene load.

The AI code copies in from `Sim/`, with the closed-form numbers its speed plan is built
from:

```bash
mkdir -p /mnt/d/Dev/CarRace/Assets/Scripts/Track
cp Sim/CarRace.Track/*.cs Sim/CarRace.Harness/Analytic.cs /mnt/d/Dev/CarRace/Assets/Scripts/Track/
```

Each AI car's pace (the share of grip it uses) is on `RaceDirector`, 0.85, 0.82 and 0.79
by default; the harness races 0.78 to 0.85. The AI plan on flat ground, as in the harness,
so crests and dips on Spa are not in their plan.

## Analogue triggers

Throttle and brake share one axis by default, which is the only thing the stock input
manager can do without authoring. For real triggers, add two axes under
`Project Settings, Input Manager` and tick `Use Trigger Axes` on `DriverInput`:

| Name | Type | Axis |
|---|---|---|
| `Throttle` | Joystick Axis | 10th axis (right trigger, XInput) |
| `Brake` | Joystick Axis | 9th axis (left trigger, XInput) |

Trigger axis numbers are not standard across pads. If a trigger does nothing, or reads
`-1` at rest, it is on a different axis: no code change, just the number here.
