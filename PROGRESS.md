# Progress

Live state of the project. Updated as work happens, not at the end, so that an
aborted session can be resumed instead of redone.

**Read section 1 first.** It says exactly where things stand and what the next
concrete action is. Everything below it is detail.

---

## 1. Resume here

**Last updated:** 2026-09-23 (diagnosed why cars touch in traffic, no code changed)

**Last completed:** The diagnosis the brief below asked for. Telemetry from a scratch
copy of the race loop traced every contact in the testcircuit repro, and both lap-one
contacts at Monza, to two causes in `RaceDriver`. Nothing is fixed yet, and the race
exit criterion still fails.

**Next action:** Step 1 of the brief below, then cause 1, the overtaking decision in
`RaceDriver.Observe`. Everything else in this file is still true.

**In progress (WIP, seventh session):** step 1, race telemetry, is DONE: `--race ...
--csv <path>`. With it on, the repro prints identical contacts, laps and summary, and
its rows agree with the scratch probe's. Step 2 is next and not started.

---

### Next: why cars touch in traffic (diagnosed, not fixed)

**Minimal repro. Four seconds, deterministic, verified identical across runs:**

```bash
dotnet run --project Sim/CarRace.Harness -c Release -- --race testcircuit --cars 8 --laps 3 --verbose
#   6 contacts, 3 on lap one. Every single one of them involves AI 02.
```

**It is a density effect, and the threshold is sharp:**

| circuit | clean up to | fails from |
|---|---|---|
| Monza | 8 cars | 10 cars (2 contacts, both on lap one) |
| testcircuit | 6 cars | 8 cars (6 contacts, 3 on lap one) |

Seed 1, the default, is one of the milder seeds. With 16 cars over 3 laps of Monza,
seeds 2 to 5 give 5 to 9 contacts, 3 to 8 of them on lap one (`--seed 4` gives 9 and 8).

**How it was found.** `--race` still has no `--csv`. A scratch copy of `RaceRun.Run`,
kept outside the repo, logged every car every 20 ms and reproduced the harness's
`CONTACT` lines exactly on both circuits, so its telemetry can be trusted. It was not
committed, which is why step 1 below is to add the same logging properly.

**Cause 1: the overtaking decision churns, and every change of mind is a lane change.**
A car held up behind another re-decides every 20 ms whether to pass and on which side,
with no memory of what it decided before. Behind AI 01, AI 02's target line went -2.8,
0, -2.8, 0 in four seconds. At around 160 km/h behind AI 07 it went from +2.8 to 0 and
then to -2.8 in about 0.8 s. Three things make it churn:

- The "in the way" test is a hard 2.2 m lateral threshold, which is about as far as
  the pass itself moves the car. Once the car has pulled 2.2 m out, the car it is
  passing stops counting, `_wantedOffset` resets to zero, and it steers back in behind,
  where the car counts again. Measured: AI 07 dropped out at a lateral gap of 2.22 m
  and came back at 2.12 m.
- `quicker` compares the follower's plan speed with the leader's actual speed. Out of a
  corner every car runs several m/s under its plan, so a slower driver judges itself
  quicker. AI 02 (pace 0.835) went to pass AI 01 (pace 0.847) because its plan said
  26.2 m/s and AI 01 was doing 24.7, while AI 01's own plan said 29.8.
- `SideToPass` only sees cars 0 to 20 m ahead. The `MathF.Abs` round `Distance` does
  nothing, because `Distance` is never negative, so a car alongside or just behind on
  the chosen side is invisible. The first lap-one contact at Monza is exactly this: at
  18.4 s AI 15 switched its pass from the left to the right and moved across AI 16,
  which was 6 to 8 m behind on the right, in the braking zone for the first chicane.

On the testcircuit the lane changes spin cars rather than hit them. AI 02's last swing
back towards the inside of a fast right-hander, at about 130 km/h on full throttle,
saturated its rear tyres. The path controller then turned the slide into a growing
weave: sideslip -15, then +16, then a spin, with the steering at full lock each way,
all inside about 2.5 s. Every spin that started an incident came after 1 to 7 of these
flips in the preceding 4 s, always while held up behind someone. No car spun on clear
road.

**The causal check.** Alone, with the same plan and grid slot, AI 02's worst sideslip
all race is 6.7 degrees, and it takes the place where it spun at 1.6 degrees. With
overtaking switched off (`OvertakeMargin = 1e6`, so nobody ever judges itself quicker),
contacts over five seeds:

| seed | testcircuit, 8 cars, on / off | Monza, 16 cars, 3 laps, on / off |
|---|---|---|
| 1 | 6 / 0 | 4 / 1 |
| 2 | 1 / 0 | 5 / 0 |
| 3 | 2 / 0 | 6 / 0 |
| 4 | 4 / 0 | 9 / 1 |
| 5 | 1 / 0 | 6 / 1 |

**Cause 2: rejoining the line in front of a car that is too close.** The Monza contact
that survives with overtaking off is the same one on every seed: AI 01 and AI 02, the
front row, at s = 716 m in the first chicane, about 24.7 s in. They go in side by side
about 3 m apart. AI 02 comes out roughly 8 m ahead, the side-by-side rule lets go, and it
moves back towards the racing line across AI 01's nose while braking. AI 01's safety cap
still allowed 12.2 m/s against AI 02's 10.5 and falling. One reason is that the gap is a
difference of racing line sample indices: it read about 10 m while the cars were 7.3 m
apart, and the minimum gap leaves only 2.1 m of daylight, so an error that size uses
all of it. That is hypothesis 3 of the previous brief.

**The previous brief's hypotheses.** 1 is confirmed, with the lane change churn as what
puts a car beyond its tyres. 3 is confirmed for cause 2. 2 and 4 were not tested on their
own, and nothing in the telemetry called for them.

**Found on the way, which make things worse but do not start them** (detail in section
6): a car spun past 90 degrees looks almost straight to its own recovery logic and gets
throttle while sliding backwards; the path controller has no yaw rate term; and a spun
car's negative speed can switch off the safety cap of the car behind it.

**Steps, in order:**

1. DONE. Add per-car telemetry to `--race`: `--csv` with the `--lap` columns plus the
   traffic decisions (blocked by, following, overtaking, wanted offset, line offset,
   lateral from line, cap). Check it by confirming the repro still prints the same six
   contacts.
2. Cause 1. A pass needs to be a commitment rather than a verdict retaken every 20 ms:
   keep the side until the car is ahead or the pass is clearly off, judge "quicker" on
   like-for-like terms, and look alongside and behind on the chosen side as well as
   ahead. Done when the testcircuit repro has no contacts.
3. Cause 2. Measure the gap as distance on the road rather than index difference, and do
   not rejoin the line in front of a car closer than the braking bound allows. Done when
   `--race monza --cars 16 --laps 10` passes, on seeds 2 to 5 as well as seed 1.

---

**After that:** Install Unity 6 LTS (manual, section 5). Then follow
`Unity/README.md`: copy both script folders in, build the skidpad scene it
describes, and drive it. Expect a few compile errors on first import where the
stub's signatures differ from the real engine, and fix the stub when you hit one.

**Nothing is half-finished.** Three checks pass on a clean run:

```bash
Tools/.venv/bin/python Tools/verify_all.py                     # expect: 6 of 6 circuits pass
dotnet run --project Sim/CarRace.Harness -c Release            # expect: All 5 checks pass
dotnet run --project Sim/CarRace.Harness -c Release -- --lap all  # expect: 6 of 6 on track
dotnet build Sim/CarRace.UnityCheck -c Release                 # expect: 0 Error(s)
```

One suite does **not** pass, on purpose rather than by neglect:

```bash
dotnet run --project Sim/CarRace.Harness -c Release -- --race monza --cars 16 --laps 10
#   expect: 16 of 16 finish, 2 contacts on lap one, FAIL, exit code 1
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
| Unity 6 LTS installed | BLOCKED | Manual step, needs your account login. Section 5. |
| Unity project created at `D:\Dev\CarRace` | BLOCKED | Depends on the row above |
| Git LFS configured | TODO | Only needed once binary art assets exist |


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
| Unity MonoBehaviour wrapper | WIP | `CarController.cs`. Written and type-checked, not run in the editor. Applies the wrench as `AddForce` plus `AddTorque`, which is equivalent to per-wheel `AddForceAtPosition` and cheaper. |
| `IGround` over `Physics.SphereCast` | WIP | `UnityGround.cs`. Written and type-checked, not run. Raycast fallback for a probe that starts already overlapping. |
| ScriptableObject returning a `CarConfig` | WIP | `CarDefinition.cs`. Every number mirrored as a serialized field; a new asset defaults to the validated reference car. |
| Cameras: chase, hood, cockpit | WIP | `CarCamera.cs`. Chase rig follows the velocity vector, not the car's facing, so a slide is visible. |
| Driver input, keyboard and gamepad | WIP | `DriverInput.cs`. Old input manager, so a car drives with no input asset authored. |
| Manual shifting in the model | DONE | `Drivetrain.Shift`. Setting `Gear` directly skipped the shift time, so a manual upshift was free lap time. |
| **Feel test on a gamepad** | BLOCKED | The real Phase 1 exit criterion. Needs Unity. Numbers passing is not the same as enjoyable. |

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
| Headless race | WIP | `--race` runs. **The exit criterion is not met.** See below. |
| Race telemetry | DONE | `--race ... --csv <path>`. One row per car every 20 ms: the `--lap` columns plus blocked by, following, overtaking, wanted and driven offset, and the cap. |
| Catching a slide | WIP | Partial. `PathDriver` counter-steers and lifts above 12 degrees of sideslip, which stopped spun cars crawling for the rest of the race, but 11 crawl reports in a ten-lap race still show more than 25 degrees. |
| Flags, penalties, pit stops | TODO | Not started. |

**Where the race stands.** Sixteen cars, ten laps of Monza, in about 20 seconds of
wall time. Everyone finishes, the grid order changes on merit, and lap times spread
by a few seconds across the field. What does not pass is the criterion itself:

```
16 cars, 10 laps, Monza   4 contacts, 2 on lap one    FAIL
16 cars,  3 laps, Monza   4 contacts, 2 on lap one    FAIL
 8 cars,  3 laps, Monza   0 contacts                  PASS
10 cars,  3 laps, testcircuit   4 contacts, 4 on lap one   FAIL
```

Eight cars is clean; sixteen is not, and the test that matters is sixteen. The
failure is a density effect with a sharp threshold, and it is deterministic. The
brief in section 1 has the current evidence, the minimal four second repro, and what
has already been ruled out.

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

1. **Install Unity 6 LTS.** Set the Hub's editor install location to D:. This is
   the only thing gating Phase 1 completion and everything after it.
2. **Add a Windows Defender exclusion** for the project folder and the Unity
   processes once it exists. Typically 2 to 5x on import times on a spinning disk.
3. **Push after meaningful work.** `git push` now that `origin` is configured. The
   repo is private; making it public later is a one-line change, the reverse is
   not really possible.

---

## 6. Open issues and deferred decisions

Known, deliberate, and not blocking. Recorded so they are not rediscovered.

**Vehicle physics**

- Car is understeer-biased: front slip angles reach 13 to 20 degrees at the limit
  while the rear sits at 3 to 5. That is why skidpad lands 15% under the tyre
  ceiling. Tuning, not a defect. Soften `AntiRollFront` or stiffen `AntiRollRear`.
- 0 to 100 is 11.6% above the perfect-launch analytic floor. Tyre relaxation and
  traction control catching the wheel account for it. Real launches lose time too.
- Tyre temperature and wear are not modelled. Phase 6 if wanted at all.
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

- Two contacts on the opening lap with sixteen cars, none with eight. Diagnosed in
  section 1. Contact is counted and reported, never simulated: making two cars
  bounce off each other is the physics engine's job, in Unity, where they are
  already rigid bodies that collide.
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
  than a wobble, though it was not tested on its own. Fixing cause 1 in section 1
  removes the lane changes that set it off, which is why this comes second.
- `Seen.SpeedMs` is speed along the car's own nose, so a spun car reports a negative
  speed. Inside the minimum gap `Stoppable` then returns a negative number, `SafetyCap`
  passes it on, and `EaseCap` reads any negative cap as no cap at all. Seen once, on
  AI 03 at 40.7 s, which was already spinning and so changed nothing, but it is the
  wrong answer in exactly the situation the cap exists for.
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

# a field of AI cars. Passes at eight cars, fails at sixteen; see section 3.
dotnet run --project Sim/CarRace.Harness -c Release -- --race monza --cars 8 --laps 3
#   expect: "8 of 8 finished, 0 contacts", exit code 0

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
