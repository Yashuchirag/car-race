# Progress

Live state of the project. Updated as work happens, not at the end, so that an
aborted session can be resumed instead of redone.

**Read section 1 first.** It says exactly where things stand and what the next
concrete action is. Everything below it is detail.

---

## 1. Resume here

**Last updated:** 2026-09-24 (AI race in Unity confirmed, with the driving fixes)

**Last completed:** Overtaking, the "make the pass work" approach. A driver now tries a
pass only when both cars' plans say it gets alongside within 10 s, sits 0.4 s behind a
car it is clearly quicker than instead of 0.9 s, gets those same 10 s to make progress
before giving up, and may close on the car it is passing at up to 4 m/s while the
sideways gap holds. The race criterion still passes on ten seeds, with no contacts.

**Next action:** Your choice. Everything in Unity so far is confirmed by your drives. For
the AI race, the natural next step is a race format: start countdown, a set lap count and a
results screen. Otherwise: passing lanes (section 3, TODO), the Phase 0 remainder (Git LFS
and a standalone build above 200 fps), or a first graphics pass (Phase 4).

---

### Where the race stands

**Default grid, fastest car on pole.** Seeds 1 to 5, three laps, before and after the
traffic fix:

| scenario | contacts | spins |
|---|---|---|
| testcircuit, 8 cars | 14 before, 0 after | 66 before, 0 after |
| Monza, 16 cars | 30 before, 0 after | 109 before, 0 after |

Seeds 6 to 10 are clean as well, still so after the overtaking work, and so is the full
ten lap race on seeds 1 to 10.

**Fastest last** (`--fastest-last`: pace order, except that the fastest car starts at the
back). This is the test for passing. Seeds 1 to 10, three laps, both circuits together:

| | pass attempts | completed | contacts | spins | where the fastest car finishes |
|---|---|---|---|---|---|
| before | 545 | 0 | 0 | 3 | last, or one place up |
| after | 197 | 16 | 1 | 5 | usually two places up |

**Reverse grid.** Seeds 1 to 10, three laps, both circuits together:

| | contacts | spins | clean passes |
|---|---|---|---|
| before the traffic fix | 105 | 345 | 6 |
| after it | 2 | 37 | 16 |
| after overtaking | 2 | 5 | 1 |

A reversed grid is not a passing test. Every car starts behind one barely slower than
itself, and plan against plan a neighbour on a sixteen car grid gains about a metre in
ten seconds, so nobody should try, and now nobody does. That is why its clean passes fell
while its spins went from 37 to 5.

A clean pass is one car getting from 5 m behind another to 5 m ahead with neither
sliding past 15 degrees within 2 s of it. These counts come from a scratch script
reading `--csv` output, not from the harness.

### What is left

**Contacts from the lane model.** The three left across forty non-default races are all
squeezes. Lanes are offsets from the racing line, which sweeps across the road through
every corner, so the car on the side it sweeps towards is pushed into the other car
faster than the other can move away. Two guards were tried and backed out: a look-ahead
on the room at the side made fastest-last worse, 1 contact to 5, and keeping cars in the
same lane out of the side-by-side rule removed one contact at the start and added three
elsewhere. Guards moving contacts around rather than removing them is the sign that the
fix is structural: passing lines fixed on the road, not hung off the racing line.

**Neighbours cannot pass each other.** The cars are identical and there is no slipstream,
so between adjacent drivers of a sixteen car field the plans differ by about a metre every
ten seconds. Real spec racing looks much the same. Changing it needs slipstream in the
physics, or a wider pace spread in the field.

**One mechanism outside the lane model.** In a braking zone into a corner the speed cap
may only come down as fast as the tyres allow while cornering (`EaseCap`), so a passer
whose target brakes hard there can arrive too fast. That rule is what stops mid-corner
lifts from spinning cars, so loosening it is a trade, not a fix.

Also open, in section 6: a car spun past 90 degrees looks straight to its own recovery,
and the path controller has no yaw rate term.

---

**After that:** Install Unity 6 LTS (manual, section 5). Then follow
`Unity/README.md`: copy both script folders in, build the skidpad scene it
describes, and drive it. Expect a few compile errors on first import where the
stub's signatures differ from the real engine, and fix the stub when you hit one.

**Nothing is half-finished.** These pass on a clean run:

```bash
Tools/.venv/bin/python Tools/verify_all.py                     # expect: 6 of 6 circuits pass
dotnet run --project Sim/CarRace.Harness -c Release            # expect: All 5 checks pass
dotnet run --project Sim/CarRace.Harness -c Release -- --lap all  # expect: 6 of 6 on track
dotnet build Sim/CarRace.UnityCheck -c Release                 # expect: 0 Error(s)
dotnet run --project Sim/CarRace.Harness -c Release -- --race monza --cars 16 --laps 10
#   expect: 16 of 16 finished, 0 contacts, PASS
dotnet run --project Sim/CarRace.Harness -c Release -- --race testcircuit --cars 8 --laps 3 --fastest-last
#   expect: 0 contacts, PASS, and AI 08 finishing ahead of its grid slot
```

The four WIP rows in section 3 are WIP because compiling is not running. Nothing
about them is unfinished code; what is missing is the editor.

If either fails on a fresh checkout, that is a regression and not something you
left unfinished.

---

## 2. Status key

| Mark | Meaning |
|---|---|
| DONE | Built and verified. A command in section 7 proves it. |
| WIP | Started. The row says exactly where it stopped and what comes next. |
| BLOCKED | Cannot proceed. The row says what is blocking it and who can clear it. |
| TODO | Not started. |

A row only becomes DONE when its verification command passes.

---

## 3. Work breakdown

### Phase 0, environment

| Task | Status | Notes |
|---|---|---|
| Hardware audit and spec decisions | DONE | Section 3 of IMPLEMENTATION_REPORT.md |
| Python venv and dependencies | DONE | `Tools/.venv`, `Tools/requirements.txt` |
| .NET 8 SDK for headless physics | DONE | `dotnet --version` reports 8.0.131 |
| Git repository | DONE | Initialised 2026-09-22 |
| Off-machine backup | DONE | GitHub repo `Yashuchirag/car-race`, public, `main` tracking `origin/main` |
| Licensing | DONE | MIT for code, ODbL for `Tracks_Data/`. See README. |
| Unity 6 LTS installed | DONE | 6.3 LTS (6000.3.24f1) installed 2026-09-23 at `D:\Software\Unity\Editor\6000.3.24f1` with Windows IL2CPP and offline docs; Hub 3.21.3 at `D:\Software\Unity Hub`; Defender exclusions for both folders, `D:\Dev\CarRace` and the two executables (`D:\Software\Unity\Downloads\setup-admin.ps1` has the undo); `UPM_CACHE_ROOT` and `ASSETSTORE_CACHE_PATH` point at `D:\Software\Unity\Cache`. The CLI is `D:\Software\Unity Hub\resources\cli\unity.exe`, not on PATH. Unity Personal activated 2026-09-24 with `unity auth login` and `unity license activate --personal`; a headless start (`Unity.exe -batchmode -quit`) exits 0 as Unity Personal. Also set the Asset Store cache in Preferences, Package Manager, once it opens. Use `pwsh.exe`, not `powershell.exe`: `PSModulePath` lists PowerShell 7 first and breaks 5.1's security module. |
| Unity project created at `D:\Dev\CarRace` | DONE | 2026-09-24, from the Universal 3D template (`com.unity.template.3d-cross-platform`), URP 17.3.0, editor 6000.3.24f1, created headless with `Unity.exe -createProject -cloneFromTemplate`. URP, not HDRP: your decision, see IMPLEMENTATION_REPORT.md, engine section. The template turns on the new Input System only; `DriverInput` needs Active Input Handling set to `Both` (Unity/README.md, settings item 2). |
| Scripts in the project, settings applied | DONE | 2026-09-24. `Sim/CarRace.Vehicle` and `Unity/Assets/Scripts/Game` copied per `Unity/README.md`; a headless open compiles all seven classes into `Assembly-CSharp.dll`, 0 errors, 0 warnings. Fixed Timestep 0.005, Active Input Handling Both, Default Solver Iterations 12, layer 8 named `Car`. Recopy after editing either side; the repo is the source of truth. |
| Skidpad scene, car driving in Unity | DONE | 2026-09-24. `CarRace, Build Skidpad Scene` (`Unity/Assets/Editor/SkidpadSceneBuilder.cs`) builds it; you confirmed the car drives on the keyboard. Three bugs found on the way, all fixed: the builder lost the Car Definition when it opened the new scene; the README's body collider reached 15 cm into the ground, so the car sat on it and would not move; and bare cylinder wheels were stood upright by the controller. Checker ground, coloured car and a `DriveHud` readout (speed, gear, rpm, inputs, raw axes) make it readable. |
| Circuits in Unity, keyboard steering assist | DONE | 2026-09-24. `CarRace, Build Track Scene` builds any of the six circuits with real elevation, 0.35-grip verges and the racing line; `TelemetryRecorder` writes `D:\Dev\CarRace\Telemetry\run-*.csv`. Your Monza run showed straight-line braking is fine (0.85 g) and the car settles by itself from one steering tap, but a 0.1 s key tap at 150 km/h swung the wheels 10 to 13 degrees and four taps built a weave onto the grass; braking mid-slide let ABS release to 4 to 14%. Fixed in `DriverInput`: steering scaled to what the tyres use (full key 8.8 degrees at 150 km/h, was 16.6; a tap 3.5) and ramped in. Cars that leave the world respawn below y = -30. You confirmed the assist feels much better. Barriers added after that: a 1.2 m frictionless wall outside each verge on a `Barrier` layer the wheels ignore; you confirmed laps now work. |
| Lap timing in Unity | DONE | 2026-09-24. `TrackPath` and `LapTimer` in `Unity/Assets/Scripts/Race`, wired by the track builder: lap, last, best (kept per circuit), sectors against best, and the headless reference lap. Compiles against Unity 6.3's real DLLs with no errors or warnings. Then, from telemetry of a spin: R now recovers onto the track where the car is (`TrackRecovery`), Backspace restarts, and the chase camera smooths yaw only, so a spin no longer swings it through the sky. Then direction cues, because after a spin it was hard to tell which way the lap runs: white arrowheads every 50 m down the middle of the road, and a red WRONG WAY banner from `LapTimer` once the car has faced back along the lap for 0.75 s. Waiting on your drive to confirm lap timing, recovery and the direction cues. You confirmed lap timing, R recovery, the camera and the direction cues. |
| Git LFS configured | DONE | 2026-09-24, as the first step of preparing the graphics pass. The Unity project at `D:\Dev\CarRace` had no version control at all: project settings, packages, materials and scenes existed only on D:. It is now its own Git repo (your choice: separate from this one, which stays the scripts' source of truth), branch `main`, first commit `03fa435`, 182 files. `.gitignore` drops Library, Temp, Logs, UserSettings and Telemetry; `.gitattributes` sends textures, models, audio, fonts and lighting data to LFS and keeps scenes and assets as diffable text (serialization is Force Text). Use `git.exe` for it from WSL, not WSL git, to avoid line ending churn. GitHub's free LFS allowance is 10 GB storage and 10 GB transfer a month and applies to public repos too. At your request the initial setup commit, and only that, is pushed to the public repo `Yashuchirag/car-race-unity`; later work stays local until you confirm a push. |
| Standalone build, performance baseline | DONE | 2026-09-24, the Phase 0 exit criterion and the baseline for the graphics pass. `CarRace, Build Windows Player` (`Unity/Assets/Editor/BuildTools.cs`) builds the Monza scene to `Builds/Windows/CarRace.exe` (Mono, Direct3D 12, 99 MB, 124 s headless). `-benchmark` (`Scripts/Debug/Benchmark.cs`) has the AI drive your car too, turns vsync and the frame cap off, and records 60 s of frames from 8 s after load. At 1920x1080 fullscreen on the RTX 2060 and i7-10750H, two runs: 291 and 269 fps average, 1% lows 129 and 159 fps, 0.1% lows 90 and 112, worst frame 12 to 14 ms. The 200 fps target is met on average; the 1% lows, likely IMGUI string allocations and the minimap's per-frame texture upload, are worth a profile before art adds to the frame. |


### Phase 1, vehicle physics

| Task | Status | Notes |
|---|---|---|
| Config and tyre data model | DONE | `Sim/CarRace.Vehicle/CarConfig.cs`, `Tyre.cs` |
| Pacejka tyres, load sensitivity, friction ellipse, relaxation | DONE | `Tyre.cs`, `VehicleSim.cs` |
| Suspension, dampers, anti-roll bars, roll centres | DONE | `VehicleSim.cs` |
| Drivetrain: curve, launch clutch, gearbox, limited slip diff | DONE | `Drivetrain.cs` |
| Aero, Ackermann steering, ABS and traction control | DONE | `VehicleSim.cs` |
| Headless harness and rigid body | DONE | `Sim/CarRace.Harness/` |
| Closed-form expectations | DONE | `Analytic.cs` |
| Five validation checks | DONE | All pass. Section 7. |
| Telemetry CSV export | DONE | `--csv <path>` |
| Unity MonoBehaviour wrapper | DONE | `CarController.cs`. Runs in Unity 6.3 on the skidpad, 2026-09-24. Applies the wrench as `AddForce` plus `AddTorque`, which is equivalent to per-wheel `AddForceAtPosition` and cheaper. |
| `IGround` over `Physics.SphereCast` | DONE | `UnityGround.cs`. Runs in Unity 6.3. Raycast fallback for a probe that starts already overlapping. |
| ScriptableObject returning a `CarConfig` | DONE | `CarDefinition.cs`. Every number mirrored as a serialized field; a new asset defaults to the validated reference car. |
| Cameras: chase, hood, cockpit | DONE | `CarCamera.cs`. Chase rig follows the velocity vector, not the car's facing, so a slide is visible. |
| Driver input, keyboard and gamepad | DONE | `DriverInput.cs`. Old input manager, so a car drives with no input asset authored. | Keyboard driven in Unity; the gamepad mapping (XInput buttons and trigger axes) is written but never tried, since no pad is in use.
| Manual shifting in the model | DONE | `Drivetrain.Shift`. Setting `Gear` directly skipped the shift time, so a manual upshift was free lap time. |
| Automatic gearbox downshifts | DONE | 2026-09-24. It changed down only at 1.45 times idle, 1,305 rpm, so cars left corners in 4th or 5th with an eighth of their power. Found from your report that the AI felt slow: the headless AI lost 12 s a lap at Monza, all at full throttle, and your telemetry showed 5th and 6th at 80 to 120 km/h. Now it changes down whenever the gear below stays under 80% of the rev limit. Monza flying lap 2:34.5 to 2:27.0 at pace 0.85; every circuit 2 to 6% faster. All 5 checks, 6 of 6 circuits and the ten-lap Monza race on seeds 1 to 10 (0 contacts) still pass. |
| **Feel test** | DONE | 2026-09-24, on the keyboard, by you: acceleration, cornering, braking, handbrake slides and keyboard control all fine. The Phase 1 exit criterion. Not tried on a gamepad. |
| Keyboard stability above 120 km/h | DONE | 2026-09-24, from your report that the car is hard to hold above 120 km/h. Telemetry: at 175 km/h a key tap turned the wheels 8 degrees where the corner needs half of one, and the car stayed in a 15 degree slide after the keys were released. Replayed headlessly, the model is right (a 1 to 2 degree pulse recovers, 3 or more lifted does not) and the assist was wrong. `DriverInput` now asks for a turn rate (1.2 times grip over speed) and counter-steers on yaw rate error and on sideslip past 2 degrees. Headless keyboard scenarios: old assist spun 7 of 10, new 0 of 10, held key corners at 0.77 to 0.91 g. Compiles against real Unity and the stub. You drove it and confirmed it is much better; the telemetry agrees: on the tarmac above 120 km/h, sideslip past 5 degrees 4% of the time, slides caught near 8 degrees. Not done: throttle is still all or nothing on the keyboard. |
| Getting back from the grass | DONE | 2026-09-24, from your report that once on the grass and against the wall it is hard to get back. Telemetry: 21 trips off the road in one Monza run, most ending at the wall. Two causes. Full throttle on the grass spun the car, 100 to 180 degrees; headless, both assists spin there. Fixed: `DriverInput` fades the throttle out between 3 and 8 degrees of sideslip, grass spins drop to 11 to 22 degrees and the tarmac results are unchanged. Not fixed: pinned against the frictionless wall at 140 km/h with the key held, the car reached 0.12 g where open grass gives 0.31, because the wall holds the rear. You chose walls that scrub speed and slightly grippier grass: the barrier now has friction 0.3 (was 0) and grass grips at 0.45 (was 0.35), which headless turns the car 30% harder on the grass (0.40 g) with no spins. The builder now writes both materials on every build, since it used to create them only when missing. Waiting on your drive. You confirmed it is easier now. |
| Braking at high speed | DONE | 2026-09-24, from your request for stronger braking at speed without more sliding. Headless the car already brakes at the tyres' full grip, 1.29 g from 250 km/h, and ABS holds 18% slip where the tyre gives 99.4% of its peak (the peak is at 24% and flat), so neither brakes nor ABS had anything left. In Unity your clean stops averaged about 1.0 g, swinging 0.83 to 1.45 g every half second: the car was bouncing, and at 218 km/h it left the ground on a Monza crest. Cause: the pipeline joined its 25 m elevation samples with straight lines, a slope kink every 25 m, worth up to 4.6 g vertically at 216 km/h at Monza and 7.3 g at Suzuka. `Tools/trackgen/elevation.py` now uses a periodic cubic spline plus a 25 m Gaussian: worst 0.5 g at Monza, 0.9 Silverstone, 0.6 Spa, 1.6 Suzuka (its bridge), climbs within a metre. Rebuilt the five real circuits from the cache; only `z` changed, 6 of 6 pass `verify_all.py`. More downforce (ClA 0.40/0.60 from 0.12/0.20) would add 6 to 8% braking at 190 to 250 km/h and is held back as an option. You drove it and confirmed braking works as expected. Telemetry: at steady speed above 150 km/h the tyre load swing fell from 5,200 N to 1,200 N standard deviation, and the car no longer leaves the ground (lowest total load 13,300 N, was 0). |

### Phase 2, track pipeline

**DONE.** Six circuits pass, all within 0.17% of published length. Detail in
`Tools/README.md`. Remaining polish is listed under section 6, not here, because
none of it blocks anything.

### Phase 3, AI and race systems

Brought forward out of order, because everything left in Phase 1 needs Unity and
this does not. It is also the first time the two halves of the project meet: the
physics has never driven a corner the track pipeline produced.

| Task | Status | Notes |
|---|---|---|
| Track data into C# | DONE | `Sim/CarRace.Track/`, engine agnostic. JSON loading stays in the harness because Unity brings its own. |
| Speed plan from the car's own limits | DONE | Quasi-steady-state, both passes sharing one friction ellipse. The plan in the JSON is for a GT3, not this car. |
| Path-following driver | DONE | Curvature feedforward plus Stanley feedback at the front axle, PI on speed. Reused by Unity AI later. |
| Headless lap on a real circuit | DONE | `--lap all`, 6 of 6 on track. Section 7. |
| Racing line offsets and a speed cap on the driver | DONE | `PathDriver.LineOffsetM`, `SpeedCapMs`. Offset lines also get a lower speed limit, because the inside of a corner is a tighter radius than the plan was written for. |
| AI personalities and traffic awareness | DONE | `RaceDriver`. Pace spread, braking-distance safety bound, side-by-side separation, picking a side to pass. |
| Race control: grid, laps, positions, classification | DONE | `RaceControl`. Grid behind the line so every car drives the same distance. |
| Headless race | DONE | `--race monza --cars 16 --laps 10`: 16 of 16 finish with no contacts, on seeds 1 to 10. Section 7. |
| AI opponents in Unity | DONE | 2026-09-24. You chose three AI plus you. `RaceDirector` (`Unity/Assets/Scripts/Race`) drives three cars with the headless `RaceDriver` through a new `CarController.Autopilot` hook, and shows every driver the field, you included, every 20 ms, as `RaceRun` does. `TrackPath` now carries the racing line and widths and builds the harness's `TrackData`. Grid as in the harness, AI ahead with paces 0.80, 0.77, 0.74, you at the back; the AI hold until you move off; a car stopped 5 s is put back on its line; your place is shown. The AI assume you drive at pace 0.7 when judging a pass on you, since with no plan for you they would never try one. `Sim/CarRace.Track` and `Analytic.cs` copy into `Assets/Scripts/Track`. Compiles against the real Unity DLLs and the stub, 0 errors, 0 warnings. Not yet run: the editor was open, so no headless build. Waiting on your drive. You drove it: it runs with no errors, and the AI felt slightly slow. Their paces are now 0.85, 0.82 and 0.79, and the gearbox fix makes every car about 5% quicker. You confirmed the race feels fine after the gearbox fix and the faster paces. |
| Race format in Unity | DONE | 2026-09-24, your request: `RaceControl` wired into `RaceDirector`, a 3, 2, 1, GO countdown holding every car, a lap count, contacts, and a results screen with position, grid, places gained, best lap, race time, gap and contacts. Done: `RaceDirector` keeps a `RaceControl` updated every physics step, you as the last entry, your laps from the distance you have covered measured as `PathDriver.ProgressM` measures the AI; every car is held by its `Autopilot` until GO; `CarContacts` counts touches with other cars, once a second at most; Enter reloads the scene. Compiles against the real Unity DLLs, 0 errors, 0 warnings. You raced it and said it seems nice. |
| Minimap | DONE | 2026-09-24. First a map of the whole circuit; you found tight corners invisible at that scale and asked for only the road ahead. Now `MiniMap` draws 220 m ahead and 30 m behind at about 1 px per metre, the road at its real width, turned with the track's direction at your position, corners orange under 150 m radius and red under 60 m, AI dots when in view. Redrawn into a small texture every frame. Compiles against the real Unity DLLs; a Python port at three places on Monza shows the first chicane right then left, Roggia left then right and the Parabolica right, so it is not mirrored. You confirmed it looks good, then asked for a more see-through square and a slightly see-through road: backdrop alpha 55 of 255 (was 120), road 200 (was solid). |
| HUD scales with resolution | DONE | 2026-09-24, your question whether the HUD suits 1080p, QHD and UHD. It was placed relative to the screen edges but sized in fixed pixels, so at UHD every panel and font took half the share of the screen it does at 1080p. `Hud` gives a scale of screen height over 1080 (never below 0.5); `DriveHud`, `LapTimer`, `RaceDirector` and `MiniMap` scale font sizes and rectangles by it, not `GUI.matrix`, which would blur text at 4K. The minimap redraws at the screen's own resolution and reallocates on a resize. 1080p is unchanged. Compiles against the real Unity DLLs and the stub. You checked it at the higher resolutions and it looks fine. |
| Reverse on the brake key | DONE | 2026-09-24, from a stop against a wall you could not leave, and your suggestion. That run's telemetry showed no keys registering at all for 35 s (the Game view had most likely lost focus), but the real gap was that the keyboard had no reverse: S only braked, reverse was only on the manual shift key. `CarController.BrakeToReverse`, with the automatic gearbox: brake held for 0.3 s at a standstill or rolling back selects reverse, then brake drives backwards at 0.25 throttle (a full key spun the rear wheels at slip ratio -11, traction control only watches forward spin) and throttle brakes; throttle once stopped selects first. `DriverInput` steers plain ramped lock when reversing, since the turn-rate feedback pushes the wrong way backwards. Headless, the whole sequence runs: 61 km/h to reverse at 17 km/h and back to first. Compiles against real Unity and the stub (which gained `Mathf.Abs`). You drove it and confirmed it works. |
| Overtaking | DONE | Passes that complete where the plans say one can: `--fastest-last` puts the fastest car at the back, and it now gains places. 1 contact in 20 such races remains, from the lane model; section 1. |
| Passing lanes | TODO | Inside and outside lines fixed on the road, each with its own speed plan, and a forward check for whether two cars' lines cross, in place of offsets hung off a racing line that sweeps across the road. The fix for the squeeze contacts left in passing (section 1), and the design in section 12 of IMPLEMENTATION_REPORT.md. Deferred on 2026-09-23 in favour of the Unity install. |
| Race telemetry | DONE | `--race ... --csv <path>`. One row per car every 20 ms: the `--lap` columns plus blocked by, following, overtaking, wanted and driven offset, and the cap. |
| Catching a slide | WIP | Partial. `PathDriver` counter-steers and lifts above 12 degrees of sideslip, which stopped spun cars crawling for the rest of the race, but 11 crawl reports in a ten-lap race still show more than 25 degrees. |
| Flags, penalties, pit stops | TODO | Not started. |

**Where the race stands.** Sixteen cars, ten laps of Monza, in about 18 seconds of
wall time. Everyone finishes, nobody touches, and lap times spread by pace, 2:32 to
2:38. The finishing order is the grid order, which is right for a grid with the fastest
car on pole. Started last, the fastest car now passes the much slower cars and stops
behind ones nearly as quick, which is as far as identical cars without slipstream go.
Section 1 has the numbers.

```
16 cars, 10 laps, Monza         0 contacts    PASS, seeds 1 to 10
16 cars,  3 laps, Monza         0 contacts    PASS, seeds 1 to 10
 8 cars,  3 laps, testcircuit   0 contacts    PASS, seeds 1 to 10
```

### Phase 5, LAN multiplayer

Brought forward for the same reason Phase 3 was: the netcode is testable without an
engine, and everything left in Phases 1 and 4 is not.

| Task | Status | Notes |
|---|---|---|
| Snapshot format and bit packing | DONE | `Sim/CarRace.Net/`. 21 bytes a car, orientation in 4 of them. |
| Interpolation buffer on the client | DONE | Renders two snapshot intervals late, handles reordering and loss. |
| LAN discovery | DONE | 36 byte beacon, verified over loopback. Broadcast is sent but cannot be proved on a one-machine setup. |
| Measured host to client run | DONE | `--net`. Section 7. |
| Input prediction, collisions, lobby | TODO | Not started. This is where the hard part of multiplayer lives: everything above is one machine talking, not two disagreeing. |

**What the sync measures.** Sixteen cars, real UDP sockets on loopback, with latency,
jitter and loss added on purpose:

```
network                     bandwidth     worst error    view lag
25 ms, 10 ms jitter, 2%     6.4 kB/s      0.116 m        151 ms
120 ms, 40 ms jitter, 10%   5.9 kB/s      0.116 m        248 ms
```

The error does not move when the link gets worse, and that is the point rather than a
mistake: interpolation between two snapshots the host really sent does not become less
accurate on a slow link, it becomes further behind. Latency is spent on freshness, which
is why the lag column is reported next to it.

### Phases 4 and 6

TODO, all of them. Specified in IMPLEMENTATION_REPORT.md sections 11 to 13.
Nothing started, nothing to resume.

---

## 4. Files and what owns what

```
Tools/          Python track pipeline, runs in WSL          Phase 2, DONE
Tracks_Data/    six generated circuits, committed           Phase 2, DONE
Sim/            C# vehicle physics, engine agnostic         Phase 1, core DONE
  CarRace.Vehicle/    the model. Copies into Unity unchanged.
  CarRace.Harness/    headless validation. Stays outside Unity.
  CarRace.UnityCheck/ compiles the Unity scripts against a stub. Never ships.
Unity/          the integration layer, written, never run    Phase 1, WIP
  Assets/Scripts/Game/  copies into the Unity project on D:
(Unity project)  not created yet, lives at D:\Dev\CarRace     Phase 0, BLOCKED
```

---

## 5. Blocked on you

1. **Choose the next piece of work.** Phase 1 is complete; section 1 lists the options.
3. **Push after meaningful work.** `git push` now that `origin` is configured. The
   repo is private; making it public later is a one-line change, the reverse is
   not really possible.

---

## 6. Open issues and deferred decisions

Known, deliberate, and not blocking. Recorded so they are not rediscovered.

**Vehicle physics**

- Braked to a standstill, the car rocks back at up to 4.5 km/h for half a second. The
  tyre force lags over the relaxation length, timed by patch speed with a 1 m/s floor, so
  at rest the braking force built while stopping takes 0.4 s to fade and pushes the car
  backwards. Raising the floor to 5 m/s cut the roll-back to under 1 km/h and kept all 5
  checks and every lap, but lap-one contacts in fastest-last races rose from 4 to 13 in 40
  races, even applied only when the lagged force opposes the slip. Reverted on 2026-09-24;
  the passing contacts are that sensitive to low-speed tyre behaviour. Worth revisiting
  with the passing lanes work, which is the fix for those contacts.

- Car is understeer-biased: front slip angles reach 13 to 20 degrees at the limit
  while the rear sits at 3 to 5. That is why skidpad lands 15% under the tyre
  ceiling. Tuning, not a defect. Soften `AntiRollFront` or stiffen `AntiRollRear`.
- 0 to 100 is 11.6% above the perfect-launch analytic floor. Tyre relaxation and
  traction control catching the wheel account for it. Real launches lose time too.
- Tyre temperature and wear are not modelled. Phase 6 if wanted at all.
- No rolling resistance. A car left on a slope rolls until something stops it, as it did
  on Monza's grid in Unity, on a 4.4% grade from SRTM elevation that is probably steeper
  than the real track. Real rolling resistance, around 1%, would not have held it there
  either, so this is realism rather than a bug.
- Force feedback for a wheel needs a native plugin. Real uncertainty, Phase 6.

- The model reads the car's pose once per Unity physics step and holds it across its
  own substeps, because Unity owns the integration and there is no correct pose to
  read in between. The harness re-reads every substep, so the two differ slightly at
  the same nominal rate. Mitigated by asking for a 0.005 s fixed timestep, which
  caps the lag at 5 ms. Worth re-measuring against the harness once Unity runs.
- The Unity scripts are checked against a hand-written `UnityEngine` stub, so a stub
  signature that differs from the real engine hides a compile error until first
  import. Known limit of the approach, not a defect in it.

**Driving a circuit**

- The plan is trusted with 0.85 of the grip the car measures on a skidpad, and not
  more. At 0.95 it spins on some circuits. Same cause as the understeer note above:
  the front saturates first, and past its peak slip angle more steering means less
  grip, so a fixed-line driver cannot recover what it did not anticipate. A driver
  that eased the throttle on rear slip angle would carry more, and that is a Phase 3
  decision rather than a physics one.
- The reference driver gives away 6 to 11% against its own plan bound. Most of that
  is corner exit, where it is still tracking a target speed rather than simply using
  the throttle it has.
- Peak sideslip reaches 18 degrees at Suzuka. The car gets round, but it is sliding
  more than a quick driver would allow.
- The ground under the lap runner is flat. Elevation is in the track files and is
  ignored, so nothing here says anything about Eau Rouge.

**Racing a field**

- A few contacts remain in passing and in running side by side, three across forty
  non-default races, all squeezes from the lane model. Section 1 has what was tried and
  why the fix is passing lines. Contact is counted and reported, never simulated: making
  two cars bounce off each other is the physics engine's job, in Unity, where they are
  already rigid bodies that collide.
- The pass logic's numbers are reasoned rather than tuned, and exercised on two circuits
  only: 10 s both to predict a pass and to give it before giving up, a 0.4 s gap behind
  a car that is 1% slower over a lap, 4 m/s of closing with 2.7 m of sideways room, 5 s
  before trying the same car again, 10 m past before it counts as done, 10 m looked at
  behind.
- Drivers know each other's speed plans, the way lap times would tell them, and that is
  what decides whether a pass can work. A driver judging it from watching the car in
  front would be more honest and far more work, and nothing yet needs it.
- The side-by-side rule counts a car directly behind in the same lane as alongside once
  it is within 8 m, and pushes the car in front sideways, away from it. The 0.4 s attack
  gap makes that happen more, and it caused a contact at the start of one reverse-grid
  race. Leaving such cars out of the rule fixed that one and added three elsewhere, so it
  was backed out. It belongs with the passing lines.
- Spin recovery is partial. A car that loses it still ends up crawling, and being
  hit while crawling is what most of the remaining contacts are.
- Recovery cannot see a spin past 90 degrees. `PathDriver.Sideslip`, and
  `Rig.SideslipDegrees` which the logs print, take `Atan2(right, Abs(forward))`, so a
  car sliding backwards reads as nearly straight. Recovery switches off and the speed
  controller asks for throttle. At 40.2 s in the repro AI 02 was at -158 degrees, read
  -22, and was given half throttle while doing 75 km/h backwards; the repro spends 2.2 s
  like that in all. The `SLOW` log has the same fold, so the crawl sideslip figures
  quoted elsewhere in this file may be folded readings of larger angles.
- The path controller has no yaw rate term. Its heading and cross-track terms act on
  angles alone, and in the repro a rear slide at about 130 km/h grew through two
  reversals into a spin. That is the likely reason a lane change became a spin rather
  than a wobble, though it was not tested on its own. The lane changes that set it off
  are fixed and the default grid has not spun since, but the ten spins left across
  forty non-default races make this the first suspect for those.
- The side-by-side rule still flickers for two cars hovering near its 8 m edge,
  because close up the gap is the smaller of the racing line distance, which moves in
  2 m steps, and the straight line. It moves the line they drive by a few centimetres.
  Measuring along the road's direction instead removed the flicker and broke three other
  rules, since it reads two cars side by side as no distance apart; see `Gap`.
- Every car in the field is the same car. Different models per driver is a Phase 6
  question and needs more than one validated `CarConfig`.
- The AI has no notion of defending a position, of tyres, or of fuel.

**Networking**

- Only the host talks. A remote player's inputs, prediction of the local car, and two
  cars wanting the same piece of road are all untouched, and that is where multiplayer
  gets hard.
- Discovery is proved over loopback only. The broadcast is sent and does not throw, but
  one machine cannot show that a second one hears it. Two machines on a real LAN is the
  test that counts, and it needs your second computer.
- Snapshots are whole, not delta compressed against what a client already has. At
  6.4 kB/s there is no reason to bother yet.
- No reconnection, no version negotiation beyond the beacon rejecting a version it does
  not know.

**Track pipeline**

- Lap estimates run 8 to 20% slow. Documented and expected, see `Tools/README.md`.
- Start line defaults to the longest straight, which is wrong for Spa. It lands on
  the Kemmel straight. Use `--start-offset-m` when Spa matters.
- Corners traced under 12 m radius are flagged but not widened. Hand work.
- Camber and banking ship as zero. No open dataset has them.
- Track width is uniform per circuit. Fields are per sample and ready to author.
- Exported track JSON uses `indent=1`, which puts every array element on its own
  line: 259,000 lines across the six circuits. They commit fine at 2.8 MB, but
  regenerating one circuit produces a 50,000 line diff. Worth a more compact
  writer before the set grows much. Noted 2026-09-22, not urgent.

---

## 7. Verification commands

Every DONE row is backed by one of these. Run them after any change.

```bash
# track pipeline: rebuilds all six circuits from cache, no network needed
Tools/.venv/bin/python Tools/verify_all.py
#   expect: "6 of 6 circuits pass", exit code 0

# vehicle physics: five checks against closed-form expectations
dotnet run --project Sim/CarRace.Harness -c Release
#   expect: "All 5 checks pass", exit code 0

# Unity integration layer: type-checks the scripts against a UnityEngine stub.
# Proves they compile and call the model correctly. Proves nothing about behaviour.
dotnet build Sim/CarRace.UnityCheck -c Release
#   expect: "0 Error(s)", exit code 0

# both halves together: the car drives every generated circuit, twelve laps in ~5 s
dotnet run --project Sim/CarRace.Harness -c Release -- --lap all
#   expect: "6 of 6 circuits completed on track", exit code 0

# LAN sync: host and client over real sockets, with a bad network simulated
dotnet run --project Sim/CarRace.Harness -c Release -- --net monza --cars 16 --seconds 30
#   expect: "PASS", worst error under 0.5 m, exit code 0

# a field of AI cars. The second is the Phase 3 exit criterion; try other seeds with
# --seed N, since seed 1 was one of the easier ones before the fix.
dotnet run --project Sim/CarRace.Harness -c Release -- --race monza --cars 8 --laps 3
#   expect: "8 of 8 finished, 0 contacts", exit code 0
dotnet run --project Sim/CarRace.Harness -c Release -- --race monza --cars 16 --laps 10
#   expect: "16 of 16 finished, 0 contacts", PASS, exit code 0, about 18 s
dotnet run --project Sim/CarRace.Harness -c Release -- --race testcircuit --cars 8 --laps 3
#   expect: "8 of 8 finished, 0 contacts", the old four second repro
dotnet run --project Sim/CarRace.Harness -c Release -- --race testcircuit --cars 8 --laps 3 --fastest-last
#   expect: 0 contacts, PASS, and AI 08, the fastest car, started last, finishing ahead of 8th

# diagnostics, when something is wrong
dotnet run --project Sim/CarRace.Harness -c Release -- --trace    # launch, skidpad
dotnet run --project Sim/CarRace.Harness -c Release -- --grip     # lateral force probe
dotnet run --project Sim/CarRace.Harness -c Release -- --corner   # steady cornering sweep
Tools/.venv/bin/python Tools/build_track.py spa                   # one circuit, verbose
dotnet run --project Sim/CarRace.Harness -c Release -- --lap monza --verbose --csv lap.csv
dotnet run --project Sim/CarRace.Harness -c Release -- --race monza --laps 3 --verbose  # CONTACT and SLOW lines
dotnet run --project Sim/CarRace.Harness -c Release -- --race testcircuit --cars 8 --csv race.csv  # every car, every 20 ms
```

---

## 8. Session log

Newest first. One entry per working session, recording what moved and what was
learned, so context is not lost between sessions.

### 2026-09-22

- Audited the machine. RAM, fast disk and VRAM together ruled out Unreal Engine 5.
  Settled on Unity 6 with HDRP, everything on D:.
- Built the track pipeline end to end. Six circuits, all within 0.17% of published
  length. Two findings worth keeping: a venue maps several layouts that share
  tarmac so loop selection must match published length, and the smoothing search
  must take the *least* smoothing that clears a spike floor, because maximising
  minimum radius erases real chicanes while no length check notices.
- Installed the .NET 8 SDK and built the vehicle physics as an engine-agnostic
  library plus a headless harness, so it could be validated before Unity existed.
  All five checks pass against closed-form expectations derived from the config.
- Eight bugs caught by that harness, listed in `Sim/README.md`. The largest was
  quaternion composition order in `System.Numerics`, which applied angular
  velocity in the body frame and pulled steady cornering apart after seconds.
- Replaced hand-picked validation targets with closed-form ones. The original 0 to
  100 target was below the car's own traction-limited floor and could never have
  been met.
- Initialised git and committed everything as `b3836af`. Before this, 40 files of
  work existed with no history and no backup.
- Added this file, and a rule in `CLAUDE.md` section 5 to keep it current during
  work rather than at the end.

### 2026-09-24, tenth session

- Found why the fast-jev-compaction plugin never compacted: it has no TypeSafe key, so
  it falls back to the built-in summary. Left for you to supply the key.
- AI opponents in Unity, your pick, with three AI. The AI code needed no change to run
  in the engine: `RaceDriver`, `PathDriver` and `SpeedPlan` copy in as they are, and
  `Analytic.cs` with them for the planning limits. What was new is the plumbing.
  `CarController` takes an `Autopilot` delegate, so it still knows nothing about racing.
  `TrackPath` builds the harness's `TrackData` from the scene. `RaceDirector` is the
  Unity side of `RaceRun`: reaction interval, field, grid, stuck cars.
- One thing the harness never had to answer: the player has no speed plan, and
  `WorthPassing` refuses a pass on a car with none, so the AI would have queued behind
  a slow player forever. They now assume a plan at pace 0.7 for you.
- Compiles against the real Unity DLLs and the stub. Not run: the editor was open.
- You found the car hard to hold above 120 km/h. Your Monza telemetry had sideslip past
  5 degrees for 14 to 24% of the time above 120, some of it on the grass but the worst on
  the tarmac: a 0.2 to 0.35 s tap turned the wheels 8 degrees at 175 km/h and the car kept
  rotating with the keys released. A scratch program replaying keyboard inputs on the
  headless rig reproduced it, and showed the physics is not the fault: engine braking is
  0.09 g with negligible slip, aero is rear biased, a 1 to 2 degree pulse recovers. Lifted,
  3 degrees for 0.3 s leaves both axles at 15 degrees of slip past the tyre peak, a steady
  drift a real car would need counter-steer to leave too. The first assist's idea, full key
  equals the corner's lock plus the tyre's useful slip, was the error: steering beyond the
  corner's need is all slip at the instant it is applied. Rewrote it to ask for a turn rate
  and counter-steer; 0 spins in 10 scenarios and in the tap weave.

- You said the AI felt slow and the grass was hard to leave. The AI lost 12 s a lap at
  Monza to an automatic gearbox that changed down only near idle, and so did you. Fixed
  in the model, with every check, lap and race still passing. Full throttle on the grass
  spun the car; the keyboard assist now eases the throttle off in a slide. For the wall you
  chose friction 0.3 on the barrier and grass at 0.45 instead of 0.35.

- You asked for stronger braking at speed. The brakes and ABS were already at the tyres'
  limit headlessly; in Unity the car was bouncing over kinks in the elevation profile, one
  every 25 m where the pipeline joined its samples with straight lines. Smoothed with a
  spline, and the five real circuits rebuilt.

- Started preparing the graphics pass. Found the Unity project had never been under
  version control; it now has its own repo with LFS. I had told you GitHub's LFS
  allowance was 1 GB; it is 10 GB, for public and private repos alike.

- Performance baseline in a standalone build: 269 to 291 fps average at 1080p, 1% lows
  129 to 159. Phase 0's exit criterion, 200 fps, is met.

### 2026-09-23, ninth session

- Direction cues on the Unity circuits, asked for because a spun car gave no clue which
  way the lap runs. The track builder lays a flat white arrowhead every 25 samples
  (50 m) on the centreline, each corner taking the height of its nearest sample so it
  lies on a sloping road, with the winding checked so it faces up. `LapTimer`, which
  already follows the car along the lap, shows WRONG WAY once the car's facing has been
  more than about 100 degrees from the lap direction for 0.75 s. It uses facing rather
  than velocity, so the warning is there while the car sits still after a spin.
- Compiles against the real Unity 6.3 DLLs and the stub. Not yet seen in the editor.

### 2026-09-23, eighth session

- Overtaking, the "make the pass work" approach, chosen from three after measuring why
  passes failed: 400 attempts in four reverse-grid races and not one completed, passers
  on average slower than their targets, and held below their own plan by a cap for about
  half of each attempt.
- Built as planned: a pass is tried only when both cars' plans say it gets alongside
  within 10 s, it may close on the car it is passing at up to 4 m/s while the sideways gap
  holds, and it is given up when it stops gaining. Two things were not in the plan and
  turned out to be needed. A driver 1% quicker over a lap follows at 0.4 s instead of
  0.9 s, because from thirty metres back no pass can start at all. And the give-up clock
  has to be as long as the prediction, because a quicker driver's gain arrives in the
  next braking zone, not out of the corner where the pass begins.
- Added `--fastest-last`. A reversed grid cannot test passing: plan against plan, a
  neighbour on a sixteen car grid gains about a metre in ten seconds, so the correct
  number of attempts there is roughly none, and that is what it now makes.
- Result over ten seeds of fastest-last: 545 attempts and no completed pass before, 197
  and 16 after, with the fastest car usually gaining two places. The default grid is
  still spotless, and the reverse grid kept its 2 contacts while its spins went from 37
  to 5. One contact appeared in fastest-last, from a pass.
- Two guards for the remaining contacts were tried and backed out, a look-ahead on the
  room at the side and leaving in-line cars out of the side-by-side rule, because both
  moved contacts rather than removing them. The lesson is to recognise that pattern
  sooner: when every guard shifts the failure somewhere else, the cause is the model,
  here lanes hung off a racing line that sweeps across the road.

### 2026-09-23, seventh session

- Fixed the race. Sixteen cars finish ten laps of Monza with no contacts on seeds 1 to
  10, and the testcircuit repro is clean. Added `--race --csv` first and checked it
  changed nothing, then worked only from telemetry and a scratch benchmark that counted
  contacts, spins, clean passes, line changes and how deep the start compresses, over
  seeds 1 to 5, with 6 to 10 held back to check the result at the end.
- Kept: a pass is a commitment; "quicker" compares pace, or spots a car in trouble;
  choosing a side looks alongside and behind; the follow cap applies all the time rather
  than only below the car's speed; close up, the gap is the straight line when that is
  shorter; the speed controller in `PathDriver` stops integrating while pinned; a car
  going backwards counts as stopped; and in the next lane a car is held alongside rather
  than dropped behind. `Sim/README.md` has each one as a bug the race caught.
- The `PathDriver` change reaches single laps too. They are 0.1 to 0.3% slower, all six
  still on track, and every circuit keeps the same or more room to the edge: the old
  controller was overshooting its plan by a few km/h into slow corners, alone as well.
- Backed out, and written down so they are not tried blind again: letting a car close on
  one in the next lane, which made passing work and then crashed where lanes merge, and
  measuring the gap along the road's direction, which read two cars side by side as no
  distance apart.
- The lesson is that fixing one thing kept uncovering an older one it had been hiding.
  The windup hid the side-by-side cap blips, the rounded index gap hid a rule that pushed
  cars away from the one following them, and the old chaos hid the follow cap switching
  on and off. Counting more than contacts is what caught each of these: the start
  compression and the spin count moved long before any contact did.
- Zero contacts is easy to get by never passing, and switching overtaking off did
  exactly that during the diagnosis. That is why clean passes are counted. There had
  never been one on the default grid before this session.

### 2026-09-23, sixth session

- No code changed. Diagnosed the failing race with a scratch copy of the race loop that
  logged every car every 20 ms. It reproduced the harness's `CONTACT` lines exactly on
  both circuits, and seed 4 was cross-checked against the harness as well.
- Two causes, both in `RaceDriver`, written up in section 1. The overtaking decision is
  retaken every 20 ms with nothing to hold it, so held-up cars swap lanes back and forth,
  and on the testcircuit that spins them. Separately, a car coming out of a side-by-side
  rejoins the line across the nose of one that is closer than the index-based gap says.
- The check that settled it: overtaking switched off, five seeds. On the testcircuit,
  contacts went from between 1 and 6 a seed to none on every seed. At Monza they went
  from between 4 and 9 to either none or the single front-row contact that is cause 2.
- The lesson matches the earlier sessions. The brief asked what puts AI 02 in a bad
  state, and the answer was its own lane changes, driven by a 2.2 m threshold that the
  lane change itself crosses. Nothing about the symptom, a slow car being hit, pointed
  there. It came from reading the decisions next to the tyres.

### 2026-09-23, fifth session

- Built the LAN sync: `Sim/CarRace.Net` (bit packing, snapshot codec, interpolation
  buffer, discovery beacon) and `--net` in the harness, which runs a host and a client
  over real UDP sockets and measures what the client actually sees.
- Sixteen cars cost 6.4 kB/s and 21 bytes a car a snapshot. Most of that saving is
  refusing to send floats: a position to the centimetre is seven bytes rather than
  twelve, and an orientation is four rather than sixteen, because a unit quaternion's
  largest component can be recomputed from the other three.
- The result that needed care in the reporting: reconstruction error does not change
  when latency goes from 25 ms to 120 ms. That is correct and not a broken test.
  Interpolating between two snapshots the host really sent is exactly as accurate on a
  slow link; what gets worse is how far behind the view is. So the test reports view
  lag next to the error, or a 200 ms connection would look identical to a 20 ms one.
- One self-inflicted failure worth recording: the first run reported the client's
  initial buffering as the buffer running dry, and failed a connection that was
  healthy. A client that has just joined holds less history than the delay it renders
  at and has to wait rather than draw.

### 2026-09-23, fourth session

- Built the race framework: `RaceDriver` (pace personality, traffic, overtaking),
  `RaceControl` (grid, laps, positions, classification), and `--race` in the
  harness. Sixteen cars, ten laps of Monza, about 20 seconds of wall time.
- The exit criterion is not met and the row says so. Eight cars is clean, sixteen
  leaves two contacts on the opening lap.
- The bug that mattered most was invisible from the outside: the driver's
  `LineErrorM` is the error from the line it is AIMING at, so it is near zero
  whenever the car is driving well. Passing that around as each car's position
  across the road meant every car reported itself as sitting on the racing line,
  and the whole field's avoidance logic ran on zeros while looking merely badly
  tuned. There is now a separate `LateralFromLineM` and a comment on both.
- Three other findings worth keeping. Lifting off mid-corner because of traffic
  spins a rear wheel drive car at the limit, which is correct physics and terrible
  driving, so the speed cap now comes down no faster than the friction ellipse
  allows. A follower cannot be kept safe by a proportional rule at a closing rate
  of 80 m/s; it needs the braking-distance bound, and that bound has to use a wider
  lateral window than the question of whether a car is in the way. And a separation
  rule has to be a constraint rather than a target, because a target makes a car
  drive towards the gap it is trying to keep.
- Every one of those was found by logging and reading, never by reasoning about the
  code. The pattern from the previous session held: the symptom never resembled the
  cause. A phantom car at a wrapped index looked like a slow field; zeros in a
  lateral position looked like bad tuning.

### 2026-09-23, third session

- The physics drove a circuit the pipeline made, for the first time. `--lap all`:
  six circuits, twelve laps, about five seconds, all on track. Added
  `Sim/CarRace.Track` (track data, speed plan, path driver) and the harness side
  that loads the JSON and reports.
- It did not work at first, and guessing did not fix it. What fixed it was dumping
  per-step telemetry and reading it: the car was braking hard past turn-in, the rear
  axle unloaded exactly as it was asked for lateral grip, and it spun. The plan was
  letting it brake at full capability while already at the cornering limit, which no
  tyre can do.
- The obvious fix for that made it much worse, in a way that read as a slow car
  rather than a bad plan, and cost half a minute a lap. Taking lateral demand at the
  cornering-limit speed uses the whole ellipse by definition, so no braking is
  allowed through turn-in at all. The constraint has a closed form; use it.
- Measured rather than assumed: curvature across adjacent samples at 2 m spacing is
  30 to 50% noise on three of the six circuits. A stride of about 6 m reproduces the
  pipeline's own curvature with a correlation of 1.0000. Worth remembering that the
  pipeline had already solved this and the two only disagreed because I recomputed
  it without asking why theirs looked different.
- The lap times are honest but slow, because the plan only dares use 0.85 of the
  car's measured grip. That number is the understeer balance showing up again, and
  it is the most useful thing this run found.

### 2026-09-23, second session

- Wrote the Unity integration layer: `Bridge`, `CarDefinition`, `UnityGround`,
  `CarController`, `DriverInput`, `CarCamera`, with `Unity/README.md` covering the
  scene, the project settings and the copy step.
- Added `Sim/CarRace.UnityCheck`, which compiles those scripts against a
  signature-only `UnityEngine` stub. Verified the check works by breaking a call on
  purpose and watching it fail. It catches typos and wrong calls into the model; it
  runs nothing, and every method in the stub throws so it can never be mistaken for
  a behaviour test.
- Added `Drivetrain.Shift`. `AutomaticGearbox = false` was unusable from outside the
  model: `Gear` is a public field, and setting it swapped ratios with no shift time,
  so a manual upshift interrupted no drive at all and was worth free lap time.
- Talked myself out of one piece of superstition. Unity is described as left handed
  and `System.Numerics` as right handed, which suggests the quaternion conversion
  needs a sign flip. It does not: Unity defines its cross product by the left hand
  rule, so the component arithmetic is identical and a positive rotation about +Y
  takes +Z to +X in both. `Bridge.VerifyConventions` asserts that against the real
  engine at startup, because reasoning of that kind is worth nothing unchecked.
- Three traps in the integration are things that look like physics bugs and are not,
  so all three now report themselves: a ground mask that includes the car's own
  layer, a 50 Hz fixed timestep, and a torque curve out of rpm order.

### 2026-09-23

- Created a GitHub repo, `Yashuchirag/car-race`, pushed `main`, then made it
  public. Work now exists off the machine.
- Added licensing: MIT for the code, ODbL for `Tracks_Data/` since it is a Derived
  Database from OpenStreetMap. Found and fixed a real error doing so: the exporter
  stamped an OpenStreetMap attribution onto every file including the synthetic
  test circuit, which both encumbered original work with share-alike and credited
  OSM for something they had no part in. Attribution is now per source.
