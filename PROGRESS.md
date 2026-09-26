# Progress

Live state of the project. Updated as work happens, not at the end, so that an
aborted session can be resumed instead of redone.

**Read section 1 first.** It says exactly where things stand and what the next
concrete action is. Everything below it is detail.

---

## 1. Resume here

**Last updated:** 2026-09-26 (start line, grid and timing points)

**Last completed:** Start line, grid boxes, START FINISH gantry and sector boards on
every circuit (section 3), built into all six scenes and the Windows build.

**Next action:** Your drive. Then, from section 3: flags, penalties and pit stops, or LAN
play, or sound.

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
| Stutter investigation | DONE | 2026-09-24, your request after the baseline. The benchmark now logs each frame's physics steps, garbage collections and allocation, and in a development build (`CarRace, Build Windows Development Player`) the profiler markers for scripts, PhysX, IMGUI and GC; `-noTelemetry` and `-noHud` switch those off. Findings: (1) one-second hitches every 20 to 25 s came from the Unity editor running alongside; with it closed they never happen. (2) A 90 to 219 ms stall at the same moment, 33 s after load, in every build: the telemetry recorder, which wrote a line from every physics step. It now runs only in the editor and development builds; the release build's worst frame is 14.6 ms. (3) The HUD (IMGUI) makes 95% of the garbage, 20 KB a frame and 412 collections a minute against 59 without it, but incremental GC hides it: hiding the HUD changed the average by 1%, inside run-to-run noise, so it is not rewritten for speed. (4) The remaining 1% lows (130 to 160 fps) are frames that run two physics steps after an OS hiccup; a step costs about 1 ms for four cars. Release, 1080p: 263 to 291 fps average. |
| URP settings for the graphics pass | DONE | 2026-09-24, reviewed with you and applied by `Unity/Assets/Editor/GraphicsSetup.cs` (`CarRace, Apply Graphics Settings`). The project was on the Universal 3D template defaults, and the track scenes had no post-processing and no anti-aliasing at all: the builder used Unity's plain camera, which has post-processing off. Now: MSAA 4x (TAA smears at racing speed), HDR colour grading, shadows to 150 m in 4 cascades at 2048 (was 50 m), opaque texture off, and a track profile `Assets/Settings/TrackPostProcessing.asset` with ACES tone mapping, bloom 0.3 above threshold 1 and a 0.2 vignette. Track scenes get a global volume and a camera with post-processing on. Kept: Forward+, HDR, SRP batcher. Later: GPU Resident Drawer and probe volumes, once there is trackside art and baked light. Benchmark after: 278 fps average, 1% low 138, worst frame 11.5 ms, the same as before: the frame is CPU bound. A screenshot confirmed smooth edges, tone mapping and contact shadows. The skidpad scene does not get the volume. Corrected `IMPLEMENTATION_REPORT.md`: URP has no DLSS, only FSR 1 and STP. |
| Sky and lighting | DONE | 2026-09-24, the first step of the graphics pass. Sky: Poly Haven "Kloofendal 48d Partly Cloudy (Pure Sky)", CC0, 4096x2048 HDR in `Assets/Art/Sky` (Git LFS), on Skybox/Panoramic. `Tools/sky_analysis.py` measures the image: sun 47.9 degrees up at (0.5543, 0.7416, -0.3778) in the shader's own mapping, colour (0.974, 1, 0.936), intensity 1.44 (its illuminance over pi, which puts it at its real ratio to the sky's ambient, 2.3 to 1 on level ground), horizon (0.439, 0.472, 0.573) linear. `GraphicsSetup.SetUpSkyAndSun` aims the directional light at the sun, takes ambient light and reflections from the sky, adds linear fog 300 to 3500 m in the horizon colour, and bakes the environment only (no lightmaps yet); the track builder calls it. Benchmark: 283 fps average, unchanged. The screenshot shows the sky and sky-coloured lighting, and also the edge of the world past the verges, now visible as the sky's lower half: needs ground. One 92 ms stall 30 s in on this run, with telemetry off, so the recorder may not have been its only cause (section 6). Waiting on your look. |
| Ground to the horizon | DONE | 2026-09-24. `Unity/Assets/Editor/GroundBuilder.cs`, called by the track builder: a Terrain reaching 3 km past the circuit (Monza 7.3 x 8.2 km, 2049 heights, 3.5 m cells), its height an inverse distance weighted average of the centreline (softened over 60 m, computed on a 257 grid and interpolated), held 0.4 m under the road and verges to 12 m past the barriers. No collider. Flat grass colour until textures. Checked in Python on Monza, Spa and Suzuka: at 380,000 road and verge points the terrain as drawn is 0.4 m below the surface, never closer. Its data is 10 MB, in LFS (`Ground.asset`). From the driver's seat the 1.2 m barriers hide it; the blue-grey band past the right verge in the screenshots is the barrier in shadow, not the void as I first said. |
| AI crash at the first chicane | DONE | 2026-09-24, and very likely the "third car into the wall" you saw. The cause was on the grid, not at the chicane: in Unity the cars settle with speeds of a few millimetres a second, so AI 3, creeping at 0.03 m/s, judged AI 1 ahead of it (0.00 m/s) "in trouble" under `RaceDriver`'s rule (less than 45% of the speed of the car behind) and pulled out to pass on the grid. It ran alongside AI 2 all the way to the first chicane, where the side-by-side rule moved AI 2's line 2.4 m out under braking from 199 km/h; AI 2 weaved and spun into the barrier, every race. The harness starts cars at exactly zero and never saw it. Fix in `RaceDriver`: `TroubleMinSpeedMs = 5`, a car must itself be doing 18 km/h to call another in trouble. Headless unchanged: Monza ten laps seeds 1 to 10, 0 contacts; fastest-last 2 contacts in 20 races, as before; 5 checks; 6 of 6 laps. In Unity no car pulls out on the grid, every car stays within 2.6 m of its line on lap one, and no car stopped in three logged runs. Found with `-aiLog`, a new switch that writes every AI car's state to `ai.csv` beside the executable in the harness's terms; kept. A first guess, that grid places were a sample off, was wrong and reverted. The temporary diagnostics are removed. |
| Frame rate options | DONE | 2026-09-24, your request: the game ran uncapped, fine on your machine and wrong for others. `Scripts/Settings/DisplaySettings.cs` applies the saved choice before the first scene: VSync (default), 30, 60, 120, 144 fps or unlimited; `-frameRate` overrides it for a session; `Time.maximumDeltaTime` is 0.1 s, so a hitch is followed by at most 20 catch-up physics steps, not 66. `SettingsMenu.cs` opens on Esc, pauses (time scale 0, so clock, countdown and physics wait) and shows the fps being reached; added to every scene at startup, so no scene rebuild. Results' Enter does not reload while paused. The benchmark stays uncapped unless given `-frameRate`. Measured on your laptop: 60.0 fps at 60 (1% low 58.9, worst frame 17.4 ms), 30.0 at 30, 143.5 at VSync on the 144 Hz panel. The menu itself needs your check: keys cannot be pressed headlessly. |
| Graphics quality presets | DONE | 2026-09-24, your request, for slower graphics cards. `GraphicsSetup` turns the template's "PC" quality level into High and adds Low and Medium before it, each with its own URP asset (`Medium_RPAsset`, `Low_RPAsset`, copies of High) and a shared renderer without SSAO (`NoAO_Renderer`); rerunning updates them, it does not add more. High: MSAA 4x, shadows 150 m, 4 cascades, 2048, soft, SSAO, LOD bias 2. Medium: MSAA 2x, 100 m, 2 cascades, soft, no SSAO, LOD bias 1.5. Low: no MSAA, 60 m, 1 cascade, 1024, hard, no SSAO, 80% render scale with FSR, per-texture anisotropy, LOD bias 1. Separate assets, not one changed at run time, because a change in play mode would overwrite the project's asset. `DisplaySettings` picks a first-run default from graphics memory (under 1.5 GB Low, under 3 GB Medium), keeps the choice, takes `-quality`, and reapplies the frame rate after each change since a level carries its own vSync. Measured in sequence as the laptop warmed: Low 279, Medium 238, High 193 fps (High was 218 to 262 in earlier cooler runs); screenshots show Low's jagged edges and hard, slightly speckled shadows. Waiting on your check of the Esc menu with both rows. |


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
| Engine drag torque control (MSR) | DONE | 2026-09-24, from your wish for faster downshifts under braking to cut sliding. Tested first and advised against: headless with the keyboard assist, faster downshifts never shortened braking and made a corner slide worse (sideslip 44.6, 48.0, 50.9 degrees for none, current, early), because engine braking acts on the rear wheels only and ABS never touches it. Built the opposite instead, a third aid in `VehicleSim` beside ABS and traction control: engine braking eased off a driven wheel as its combined grip use (friction ellipse, braking and cornering together) passes 85%, gone at 100%. Keyed to grip use, not slip: braking through a corner the rear sits at slip 0.05 to 0.12, under the aids' 0.13 target, so a slip trigger never fired. Moderate braking in a corner at 170 km/h: peak sideslip 48 to 16 degrees; straight braking, full braking in a corner and a plain lift unchanged. Gate passed: 5 checks identical, 6 of 6 laps within 0.02 s, Monza ten laps 0 contacts on seeds 1 to 10, fastest-last 3 contacts on seeds 1 to 10 and 2 against 4 without it on seeds 11 to 30. `Engine Drag Control` toggle on `CarController`. You drove it: "way better". |
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
| Bigger minimap, dashboard gauges | DONE | 2026-09-24, your request. Minimap 300 px (was 220), 350 m ahead and 40 m behind (was 220 and 30), 0.77 px per metre against 0.88, still close enough for a chicane; the scene stores these, so tracks built before need rebuilding. `Scripts/Race/Dashboard.cs`: rev counter (0 to 8,000 rpm, red from 7,000, gear in the middle, red from 95% of the rev limit) and speedometer (0 to 320 km/h, speed in figures), dial faces drawn once into textures at the screen's resolution, needles and figures each frame, scaled with `Hud`. Attaches to the player's car at every scene load, no rebuild. The first screenshot had the needle hub hiding the gear; needles are now drawn under the figures. Checked in a mid-race screenshot: 182 km/h, gear 4 and 6,677 rpm read correctly. The debug readout in the top left (`DriveHud`) now shows only in the editor and development builds, at your request: checked in screenshots from both kinds of build. |
| Racing line as a braking guide | DONE | 2026-09-24, your request, as racing games show it. `Scripts/Race/RacingLineGuide.cs` replaces the solid yellow strip: bars 2 m on, 2 m off, from 10 m behind to 350 m ahead, each coloured by the braking needed from the car's current speed to be at that bar's ideal speed on arrival, (v^2 - ideal^2) / 2d, as a share of the planned braking: green under 0.3, yellow to 0.8, red beyond. Ideal speed is SpeedPlan at pace 0.85 for the player's car (the "AI ref" plan); `RaceDirector.PlanningLimits` is now internal to share it. Three unlit URP materials, one submesh each; the whole lap's bars are built once and only triangle lists change per frame. Checked: screenshots show green dashes on the line at 160 and 174 km/h; the colour formula run on the Monza plan gives green when accelerating on plan, red at the first chicane from its ideal braking point, all red 40 km/h too fast, and green again once braked to its speed. Needs rebuilt track scenes. You drove it: "working fine". |
| Road markings, kerbs, timing panel | DONE | 2026-09-24, your request. `TrackSceneBuilder.RoadMarkings`: solid white lines 0.2 m wide, 0.25 m inside both edges, all round; a white start line across the road at sample 0; red and white kerbs 1.2 m wide outside both edges wherever the centreline's radius is under 150 m, running 10 m either side, 2 m blocks, with an asphalt `MeshCollider` so riding them keeps tarmac grip. On Monza that is 13% of the lap in 8 runs, exactly its corners (first chicane, Roggia, both Lesmos, Ascari, Parabolica); Spa 25%, Silverstone 30%. Asphalt lightened from 0.22 to 0.30 so the paint stands out, written on every build. `LapTimer` panel redrawn: dark panel with a red accent and a header (circuit, lap), current lap large, Last, Best (purple) and AI ref rows, three sector blocks coloured purple, yellow, green or grey with time and delta. Checked in screenshots at the grid, at 52 s and at Roggia: lines, start line, kerbs and panel all draw; a white stripe that looked stray was the right edge line seen from the car's grid slot near that edge. A completed sector could not be caught inside the 68 s benchmark. You found the panel too plain; it was redone the same day, next row. |
| Timing panel, broadcast style | DONE | 2026-09-24, your request for something flashier. `LapTimer` draws rounded panels from one small generated texture sliced nine ways (corner radius scales with `Hud`): a red header with an orange stripe, circuit name and a lap badge; the lap time large with a shadow; a live delta to the session's best lap at the same centreline sample (a lap trace recorded each physics step after the gates, measured from the line so lap one's grid start does not skew it; green down arrow ahead, red up arrow behind); a three-segment progress bar; Last, Best in purple, AI ref; sector blocks filled purple, yellow or green, pulsing 0.8 s as each finishes; a purple NEW BEST LAP banner flashing 3 s. Checked across lap one into lap two in a 232 s benchmark (new `-benchmarkSeconds` and multi-time `-screenshotAt`): green sectors 59.634 and 54.741 on lap one; lap two delta -3.1 to -4.4 s, sector 1 purple with -7.458; the banner, once the best lap left by an earlier test run was deleted from the built game's registry (the deterministic lap matched it exactly, so it was rightly no new best). That test value was deleted again afterwards. |
| Lobby | DONE | 2026-09-24, your request, the front end LAN play will build on. `Unity/Assets/Editor/LobbySceneBuilder.cs` builds `Assets/Scenes/Lobby.unity` (first in the build settings; `BuildTools` builds lobby then Monza): the circuits' sky, sun and post-processing, the car with its driving parts removed turning on a platform, and `Scripts/Lobby/LobbyMenu.cs`: title, a Players panel (you, as host; LAN players will be listed there), eight colour swatches and PLAY or Enter. `Scripts/Lobby/PlayerSetup.cs` keeps the colour, paints the player's car when a race loads (a property block, no asset changed), and repaints an AI car whose colour is too close to it in the first free palette colour; the minimap reads colours as drawn. The Esc menu gains Back to lobby; the benchmark passes the lobby and times from the race loading; `-carColour` and `-lobbyScreenshot` for testing. `Hud.Rounded` and `Hud.Fill` now shared by the timing panel and the lobby. Checked in screenshots: lobby with Ocean Blue and with Carbon Black (the black swatch was invisible, now outlined; the camera was too close, now framed); a race with blue chosen paints your car blue and turns the blue AI red, its minimap dot too. Waiting on your look. |
| Grid off the grass | DONE | 2026-09-24, your report of two cars starting on the grass. Grid slots were 2 m either side of the racing line, which on Monza's start straight runs near the right edge; now 2.5 m either side of the road's centre, pointing along it. Every circuit leaves 2.5 to 3.5 m from each car's outer side to the edge; in a logged race all four cars start with 3.0 m to spare, no car leaves the road on lap one (widest 1.4 m inside the edge) and none stops. |
| Grass texture | DONE | 2026-09-24, first of the surface textures, at your request. ambientCG Grass005 (CC0, licence checked on docs.ambientcg.com), 2K colour, normal (GL) and ambient occlusion, in `Assets/Art/Ground/Grass005` via LFS, credited in `Assets/Art/CREDITS.md`; chosen over Poly Haven's Leafy Grass (mostly leaf litter) and ambientCG Grass001, 004, 006, 008 as the most mown looking. `Unity/Assets/Editor/SurfaceTextures.cs` writes the import settings (normal map as normal, occlusion linear, 2048 cap, mipmaps, aniso 4) and applies the grass to the verge material, the terrain layer (4 m tile) and the lobby lawn (tiling given, a plane's UVs run 0 to 1). The track builder's strips now carry UVs in metres both ways, instead of stretching one texture across 15 m. The terrain's flat placeholder `grass_flat.png` is gone. Screenshots: blades visible on the verges, a lawn at mid distance, no stretching or obvious repeats; 246 fps average. One 124 ms hitch in that run, the open issue in section 6. |
| Asphalt texture | DONE | 2026-09-24, at your request. Poly Haven Asphalt Track (CC0, by Dimitrios Savva, 2 m square), chosen over Poly Haven Asphalt 02 (cracked), 04 and 06 (too light), Pit Lane (browner) and ambientCG Asphalt 031 and 033 for being dark, fine and even like circuit tarmac. 2K colour, normal (GL) and ambient occlusion in `Assets/Art/Ground/AsphaltTrack` via LFS; its roughness (0.71 to 0.88) turned by the new `Tools/smoothness_map.py` into URP's metallic-smoothness map (alpha 1 - roughness). `SurfaceTextures.ApplyAsphalt` puts all four on the road material with the right keywords, tiled at 2 m. Screenshots: dark tarmac with grain, the edge lines, kerbs and guide bars standing out; 230 fps average, no hitch. |
| Circuit choice in the lobby | DONE | 2026-09-24, your request: pick the circuit beside the colour. All six circuits are now built into the game (`BuildTools` takes the lobby plus every `Assets/Scenes/Track *.unity`). The track builder records each one in `Assets/Settings/TrackCatalog.asset` (`Scripts/Lobby/TrackCatalog.cs`: scene, name, theme, length and an outline texture drawn from the centreline). The lobby's Circuit panel shows a card per circuit with its outline, length and scenery theme; the choice is kept in PlayerPrefs (`CarRace.Track`) and falls back to the first circuit in the catalogue, the airfield. `-track "Track <name>"` picks one for a session. Checked: a lobby screenshot shows all six cards; a benchmark on the Ardennes circuit loads it, names it on the timing panel and races at 233 fps average. Waiting on your look. |
| Leave or restart a race | DONE | 2026-09-24, your request. The Esc pause menu has Restart race and Main menu (the lobby) side by side, in place of the single Back to lobby; the results table has Race again (Enter still works) and Main menu; a hint under the position banner, "Esc: pause, restart or main menu". `SettingsMenu.RestartRace`, `MainMenu` and `InRace` are shared by both, and the menu closes and unpauses on every scene load. Checked in a screenshot of a finished three-lap race on the Airfield: hint and both result buttons drawn. The pause menu's buttons need your check: keys cannot be pressed headlessly. `InstancedTrees` moved to `Scripts/Scenery` (with its .meta, so scenes keep it): the stub check compiles `Scripts/Game` only and has no rendering types, and had failed since the trees were added. |
| Scenery per circuit | DONE | 2026-09-24, all six themes built and checked by you. `Editor/Theme.cs` holds each circuit's theme (ground surface, steep surface, mountain height, sea, city, night); `GroundBuilder` shapes and paints the terrain from it; `SceneryBuilder` places structures (the same racing buildings everywhere), plantings per theme, and the city; `GraphicsSetup.SetUpSkyAndSun(sun, night)` does the night. Royal Park: countryside woods. Ardennes: ridged mountains up to about 390 m, rock (Rock030) on ground steeper than 24 degrees, conifer forest, boulders. Desert Park: sand (Ground097) on terrain and verges (verges still drive as grass), sandstone (Rock061) on steep ground, low ridges, palm oases, cacti, sandstone boulders. Ise Bay: sea along the circuit's longer side, its level 1.5 m under the lowest road near the coast (under the lowest on the lap the land hid it), a coastal plain, beach sand (Ground093A), rocks on the shore, no woods within 300 m of the coast; from car height the sea shows as a band past the barriers along the southern part. Northants: city blocks on a 44 m grid squared to the start straight, detailed shops nearest, towers, then low-detail blocks for the skyline, small parks, concrete ground (Concrete047A); buildings are terrain trees so they draw instanced. Airfield: the same city at night: procedural night sky, dim blue moon, fixed ambient, navy haze, neon bands and corner strips (unlit, bright enough to bloom) on buildings within 260 m, faintly glowing windows (a material swap in `InstancedTrees`), street lights every 25 m, alternate sides: a steel pole 0.8 m behind the barrier, an arm with a brace, and a glowing lamp head 12 m over the road's edge shining down (intensity 1400, 160 degree cone, 60 m range, pools overlapping); the pole is built from boxes, since the kit's post needed more room than the start straight's stands leave and those lamps had been left floating; a headlight and a soft light above every car; moon 0.4 and a brighter ambient. Brightened 2026-09-24 at your request: with a black car it could not be seen; in screenshots with Carbon Black it now reads as a dark grey car against lit road, 143.5 fps. Benchmarks at High, 1080p, 60 s, average and 1% low. First runs, cooler: Royal Park 134.7 / 74.7, Ardennes 114.6 / 66.5, Desert 183.6 / 59.6, Ise Bay 177.8 / 91.0 (150 s), Northants 152.1 / 84.0, Airfield 157.8 / 75.8. All six back to back after the final build, graphics card at 86 C: 122 / 53, 94 / 58, 132 / 70, 118 / 62, 132 / 75, 143 / 72; the Desert again alone, still hot: 141.5 / 74.7. Every circuit stays above 60 fps average; single worst frames of 406 ms (Royal Park, the first run after the build) and 119 ms (Ise Bay). |
| Car design | WIP | 2026-09-25: four body designs, chosen in the lobby. `Editor/CarModel.cs` builds, to the car's own dimensions (wheelbase 2.65 m, track 1.6 m, tyres 0.34 m, arches from the weight split): GT, the fastback coupe with a big wing; Muscle, long and square with a ducktail in the paint; Supercar, a low wedge with its cabin forward, a scooped bonnet and a low wing; Hot Hatch, tall and short with an almost upright hatch and a roof spoiler. Each is a body lofted through cross-sections plus details (tinted glass, black underbody, splitter, diffuser lip, mirrors, glowing lights); every wheel a tyre and a five-spoke rim. Looks only: wheels, wheelbase and handling are the same. Meshes in `Assets/Cars`, listed in `Assets/Resources/CarDesigns.asset` (`Scripts/Lobby/CarDesigns.cs`), from which `PlayerSetup` puts the saved design (`CarRace.CarDesign`, or `-carDesign N`) on the player's car when a race loads, as it does the colour. The lobby's YOUR CAR panel has a row of four design buttons over the colours, and the turning car changes with them. The AI cars take the other three designs, so the grid is mixed. Paint only on "Body" (smoothness 0.62). Checked: lobby screenshots of all four, a race with the player's supercar among a muscle car, a supercar and a hot hatch; Royal Park 132 fps with the muscle car. The muscle car is 4.75 m long against a 4.4 m collision box, so its bumpers can overlap another car's by up to 18 cm. Waiting on your look. |
| HUD scales with resolution | DONE | 2026-09-24, your question whether the HUD suits 1080p, QHD and UHD. It was placed relative to the screen edges but sized in fixed pixels, so at UHD every panel and font took half the share of the screen it does at 1080p. `Hud` gives a scale of screen height over 1080 (never below 0.5); `DriveHud`, `LapTimer`, `RaceDirector` and `MiniMap` scale font sizes and rectangles by it, not `GUI.matrix`, which would blur text at 4K. The minimap redraws at the screen's own resolution and reallocates on a resize. 1080p is unchanged. Compiles against the real Unity DLLs and the stub. You checked it at the higher resolutions and it looks fine. |
| Reverse on the brake key | DONE | 2026-09-24, from a stop against a wall you could not leave, and your suggestion. That run's telemetry showed no keys registering at all for 35 s (the Game view had most likely lost focus), but the real gap was that the keyboard had no reverse: S only braked, reverse was only on the manual shift key. `CarController.BrakeToReverse`, with the automatic gearbox: brake held for 0.3 s at a standstill or rolling back selects reverse, then brake drives backwards at 0.25 throttle (a full key spun the rear wheels at slip ratio -11, traction control only watches forward spin) and throttle brakes; throttle once stopped selects first. `DriverInput` steers plain ramped lock when reversing, since the turn-rate feedback pushes the wrong way backwards. Headless, the whole sequence runs: 61 km/h to reverse at 17 km/h and back to first. Compiles against real Unity and the stub (which gained `Mathf.Abs`). You drove it and confirmed it works. |
| Overtaking | DONE | Passes that complete where the plans say one can: `--fastest-last` puts the fastest car at the back, and it now gains places. 1 contact in 20 such races remains, from the lane model; section 1. |
| Passing lanes | DONE | 2026-09-25, your request (overtake better, crash less). `TrackData` has two lanes fixed on the road, 2.4 m either side of the centreline (less where the road is narrow), each with its own curvature and, per driver, its own speed plan; `PathDriver.Lane` drives one, the aim moving sideways at no more than 1.5 m/s. `RaceDriver`: passing takes the lane on the passing side; side by side with anyone (12 m along, 5.5 m across) a car takes the lane on its own side, the opposite one if the other has chosen already, and keeps its lane while every car alongside allows it; it leaves a lane only when nobody has been alongside for 1 s and the racing line is within 1.5 m of the lane. Side by side into a corner tighter than 60 m radius the car behind gives up the corner; a car that cannot take its lane at its speed gives way instead; a car ahead is in the way if the two paths come within 2.2 m before this car would catch it; a released speed cap rises away rather than vanishing; the start offset holds while a car moves into a lane. Headless, 8 cars, 3 laps, six circuits, both grids, 8 seeds: 142 contacts at the old code's rate, 8 now; 40 spins at the first lane version, 8 now; 128 places gained by passing at the old rate, 162 now. In the built game, AI only, six circuits x 3 min and three x 7 min: old AI 3 contacts, 132 car-seconds off the road; now 0 contacts, 0 recoveries, 0 s off the road. |
| Standing start with lock on | DONE | 2026-09-25, your report: at a standstill W with A or D sometimes stood still or rolled back. Two causes, both the throttle assist cutting power by sideslip, which is meaningless at walking pace: pulling away with lock the car moves sideways against its nose, so the cut left it at 3 to 5 km/h for six seconds; and in reverse gear W is the brake, so cutting it left the car rolling backwards with W held. `DriverInput` now fades the cut and the slip counter-steer in between 4 and 10 m/s of forward speed, never while reversing. Reproduced and checked in the built game with `-driveScript` (timed throttle, steer, brake in place of the keyboard, logging speed and gear): before, stuck at 3 to 5 km/h and rolling back with W held; after, 0 to 35 km/h in 3 s, and W in reverse stops the car and selects first within half a second. |
| AI recovery from grass | DONE | 2026-09-25, your request. The AI now knows the grip under its wheels (`PathDriver.SurfaceGrip`, from `RaceDirector` each step): with three wheels or more on grass it asks for the plan's speed times the square root of the grip and steers back at no more than 20 degrees; crawling off the road or facing more than 100 degrees wrong it is put back on its line after 3 s rather than 5. The real cause of the grass trips was upstream, one bend per circuit where every AI left the road (Desert Park 3.9 km, Ise Bay 4.7 km, the Ardennes 3.4 km): the speed plan ignored crests (`TrackData.VerticalCurvature`; the plan now takes the load a crest leaves on the tyres), the AI ran a steady metre wide in fast bends (a small slow cross-track correction, and a 4% lift per metre run wide), braked harder mid-bend than the tyres had left (road braking now squeezed, 6 per second, and kept inside the friction ellipse), and floored it while sliding (traction control, power eased from 4 to 12 degrees of sideslip). In the built game those three circuits now run seven minutes with every car on the road. |
| Start line, grid and timing points | DONE | 2026-09-26, your request: show where the race starts and where it is timed. `StartFinishBuilder` (Editor), called from `TrackSceneBuilder.Build`, replaces the thin white start line: a chequered line two squares deep across the road at sample 0, the point where `LapTimer` counts each lap; a steel gantry over it reading START FINISH both ways above a chequered band; a painted box for each grid slot (`TrackSceneBuilder.GridPlace`, shared with `GridSlot`); and a yellow line with a yellow `S1 \| S2` or `S2 \| S3` board either side at the two sector gates, a third and two thirds of the lap. Paint has no collider; the gantry legs and sector posts stand 3 and 4 m off the road, solid, on the barrier layer. Letters are built as 5x7 pixel squares: TextMesh's font would need its own shader. Signs are unlit: a street lamp over the Airfield gantry burned lit white out to a glare. Checked in screenshots on the Airfield at night (grid, gantry, S1 board) and Royal Park by day. AI only, six circuits x 3 min: 0 contacts, 0 recoveries, 0 s off the road, as before. |
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

**Performance**

- Stalls of 90 to 750 ms, followed by catch-up physics steps, came with an AI car being
  recovered after it crashed at Monza's first chicane (fixed, section 3). The recovery
  itself timed at 3 to 6 ms in four instrumented runs, so what made that frame slow was
  never pinned down; with the crash gone it no longer happens. Since then one run in four
  had a single 259 ms frame, cause unknown. Next time: `-aiLog` together with the frame
  log on every benchmark run, so a stall can be matched to what the cars were doing.
- Frame rate varies 218 to 262 fps between identical runs as the laptop warms up, so a
  change under about 10% cannot be measured from single runs. Compare several.

**Scenery**

- Ise Bay's AI crash at 130R (every AI off at 4.8 km): fixed 2026-09-25, see "AI recovery
  from grass" in section 3. Seven-minute AI races there are now clean.

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

### 2026-09-26, eleventh session

- Start line, grid boxes, a START FINISH gantry and sector boards, so the lap's start and
  its timing points are visible. Two things learned: Unity's legacy 3D text does not suit
  URP here, so the letters are geometry; and anything lit and white under a street lamp
  at night blooms into a glare that hides the gantry, so signs are unlit.

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

- Chased the stutter: the editor running alongside caused the second-long hitches, and
  the telemetry recorder the 90 ms stall, now off in release builds. The HUD makes nearly
  all the garbage but costs no measurable time.

- URP settings: MSAA 4x, HDR grading, 150 m shadows and a post-processing profile, at no
  measurable cost. Found the track scenes had been rendering with no post-processing or
  anti-aliasing at all.

- Sky and lighting: a CC0 Poly Haven sky with the sun and its brightness measured from
  the image, sky ambient and reflections, horizon fog. No cost to the frame rate.

- Ground to the horizon, verified offline. Chasing the stall found its trigger: an AI car
  missing Monza's first chicane in Unity (never headless) and being recovered.

- Fixed the AI car that crashed at Monza's first chicane every race: a car on the grid
  called the car ahead "in trouble" from millimetre-a-second noise and pulled out to pass.
  Took `-aiLog` to see; the headless race could not show it.

- Frame rate options for machines other than yours: VSync by default, 30 to 144 fps or
  unlimited in an Esc menu, and physics catch-up capped so slow machines do not freeze.

- Graphics quality presets Low, Medium and High, as Unity quality levels with their own
  URP assets, chosen in the Esc menu and defaulted from graphics memory on a first run.

- Engine drag torque control instead of faster downshifts, which the model showed would
  make corner slides worse. Cuts the peak slide braking into a corner by two thirds.

- MSR confirmed. A bigger minimap reaching 350 m ahead, and a rev counter and speedometer
  in the bottom right, checked in a mid-race screenshot.

- The debug readout is now development only; players see the dashboard dials instead.

- The racing line became a braking guide: dashed bars coloured green, yellow and red by
  the braking needed to reach each point at its ideal speed.

- Road markings (edge lines, start line, kerbs on the corners) and a broadcast style
  timing panel with coloured sectors.

- The timing panel went broadcast style: header band, live delta, progress bar, filled
  and pulsing sectors, a new best lap banner. Checked over a two lap benchmark.

- A lobby to start from, with a car colour picker and a players panel ready for LAN, and
  the grid moved off the grass.

- Grass texture (ambientCG Grass005) on verges, terrain and lobby, with metre-based UVs.
  You have scenery ideas: I will ask before any scenery work.

- Asphalt texture (Poly Haven Asphalt Track) on the road, with a smoothness map made from
  its roughness.

- Scenery themes settled with you (section 3). Circuit choice added to the lobby: all six
  circuits are in the build, each as a card with its outline, length and theme. Next is the
  scenery system, with Monza's countryside first so you can judge the look.

- Countryside scenery on Royal Park from Kenney's CC0 kits. Two lessons: terrain mesh trees
  with URP shaders draw as black slivers past 50 m and one draw call each otherwise, so the
  trees are drawn instanced from the terrain data instead; and the remaining cost was
  triangles, not pixels, so distant woods use the simplest models. Screenshot frames stall
  0.4 to 1 s while the PNG is written, so frame-rate numbers come from runs without them.

- Ardennes mountains and forest. The rock paint first came out as plain grass because it was
  set before the terrain became an asset, and the paint textures were never saved; a check
  of the saved data (0 rock texels on 17% steep ground) found it.

- The other four themes. `Theme` gathers each circuit's settings. Lessons: a sea set under
  the lowest road on the lap was hidden by land on a hilly circuit, so its level follows the
  road near the coast and the water plane covers only the seaward side; `FindAnyObjectByType<Light>`
  picked a floodlight as the moon once the night city had lamps, leaving Unity's default sun
  lighting the night; URP drops the emission keyword when the GI flag is None; and lamps aimed
  from the tower lit dark asphalt at a grazing angle, so they hang over the road instead.
  Frame rates fall 10 to 30% when the laptop is hot (86 C): compare runs taken cool.

- You hit a block on Ise Bay twice: the figure of eight crossed itself with the two roads
  1.7 m apart, so each one's walls stood across the other. SRTM sees the ground, not the
  bridge. `trackgen/elevation.lift_crossings` now raises the road that passes over (the
  later one, 3,855 m, as at Suzuka) 9.2 m with a 300 m raised-cosine either side, 7.5 m
  clear; only z changed in `suzuka.json`. `TrackSceneBuilder.Bridges` puts a concrete deck
  under it. In Unity all four cars passed under at 28.9 m and over at 36.5 m. A check of
  every circuit for sections whose walls reach each other's road found no others. The
  headless harness drives on flat ground, so heights never reach it.

- The night city was too dark for a black car. Street lights now every 25 m with visible
  lamp heads and arms, three times brighter, in overlapping pools; a soft light over each
  car; a stronger moon and ambient. The start straight had stayed dark because its stands
  left no room for posts, which dropped the lamp with the post; the lamp now stays.
  The car paint is matte (smoothness 0.2), so a black car shows by the lit road round it,
  not by reflections. Glossier paint would help but changes every circuit, so not done.

- You saw the first street lights floating with no pole: they were the start straight's,
  where the kit's post did not fit beside the stands and I had kept the lamp without it.
  Every lamp now has its own thin pole, arm and brace behind the barrier; the kit's
  post model is no longer used and was removed.

- A white patch on Ise Bay's road near the end of sector two: the bridge deck, a flat box,
  stood up through the road where the bridge's ramp dipped below the crossing's height.
  It is now a slab built under the road sample by sample, just below it all the way; views
  along the bridge in both directions are clean.

- Four car designs. The panel's buttons, the saved choice and the swap at race load follow
  the colour's pattern; the meshes live in one catalogue in Resources, so rebuilding the
  lobby regenerates them for every scene without rebuilding the circuits.

- Your four fixes. Measured throughout: a headless batch of 96 races (8 cars, 3 laps,
  six circuits, both grids, 8 seeds) and the built game with AI only, `-aiLog` now logging
  CONTACT and RECOVER lines. Lessons: offsets from a racing line cannot keep two cars apart,
  since the line sweeps across the road; lanes need entry, exit and give-way rules or they
  cause more crashes than they prevent; and most of the Unity crashes were one bend per
  circuit, a crest, a steady wide line, a stamp on the brake and full power while sliding,
  none of which the flat headless harness can show. A single race is chaotic: switching any
  one change off avoided one spin; judge by the batch.

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
