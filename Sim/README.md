# Vehicle physics

`CarRace.Vehicle` is the vehicle model. `CarRace.Harness` drives it headlessly and
checks it against closed-form physics.

The library targets **netstandard2.1 and has no engine dependency**, so these
sources drop into Unity unchanged. It computes forces and torques; the caller
owns the rigid body and integrates. In Unity that caller is a `Rigidbody` with
`AddForceAtPosition`; here it is `RigidBody.cs`. That split is the whole point:
the physics could be written and validated before Unity was installed.

## Run

```bash
dotnet run --project Sim/CarRace.Harness -c Release              # the five checks
dotnet run --project Sim/CarRace.Harness -c Release -- --trace   # launch and skidpad traces
dotnet run --project Sim/CarRace.Harness -c Release -- --grip    # lateral force probe
dotnet run --project Sim/CarRace.Harness -c Release -- --corner  # steady cornering sweep
dotnet run --project Sim/CarRace.Harness -c Release -- --csv run.csv

dotnet run --project Sim/CarRace.Harness -c Release -- --lap all      # a lap on every circuit
dotnet run --project Sim/CarRace.Harness -c Release -- --lap monza    # one circuit, in detail
dotnet run --project Sim/CarRace.Harness -c Release -- --lap monza --verbose --csv lap.csv
dotnet run --project Sim/CarRace.Harness -c Release -- --lap monza --pace 0.95

dotnet run --project Sim/CarRace.Harness -c Release -- --race monza --cars 8 --laps 3
dotnet run --project Sim/CarRace.Harness -c Release -- --race monza --cars 16 --laps 10 --verbose
dotnet run --project Sim/CarRace.Harness -c Release -- --race testcircuit --cars 8 --csv race.csv
dotnet run --project Sim/CarRace.Harness -c Release -- --race monza --reverse-grid
```

Exit code is 0 when all five checks pass, and 0 from `--lap` when the car gets
round inside the track edges.

## Driving a real circuit

`--lap` puts the validated car on a circuit the Python pipeline generated and has
it drive two laps: one from a standing start, then a flying one. It is the only
thing that exercises both halves of the project at once. The five checks say the
car obeys physics on an empty plane and the pipeline says a circuit is
geometrically sound; neither says the car can get round it.

```
circuit              lap  plan bound  vs bound    track edge  result
--------------------------------------------------------------------
bahrain         2:50.764    2:34.822    +10.3%         0.54 m  on track
monza           2:34.104    2:19.462    +10.5%         1.17 m  on track
silverstone     3:03.177    2:49.428     +8.1%         0.89 m  on track
spa             3:22.156    3:06.475     +8.4%         0.54 m  on track
suzuka          2:55.213    2:44.695     +6.4%         0.30 m  on track
testcircuit     1:05.182    1:01.093     +6.7%         1.26 m  on track
```

The plan bound is the speed plan driven perfectly, with no driver error, so the
gap to it is what the reference driver gives away. The ground is still flat:
elevation is in the track files and is ignored, so this measures cornering,
braking and gearing, not hills.

`--pace` scales how much of the car's measured grip the plan asks for. The
default is 0.85. At 0.95 the car spins on some circuits, which is the same
understeer balance the skidpad shows: the front axle saturates first, and past
its peak slip angle more steering means less grip, so a fixed-line driver cannot
recover what it could not anticipate.

## Current state

```
test                    sim  analytic    error      allowed   result
0 to 100 km/h          4.94      4.43   +11.6%     -2..+15%   PASS  s
skidpad peak           0.94      1.10   -14.8%     -22..+2%   PASS  g
100 to 0 braking      36.91     36.14    +2.1%     -2..+10%   PASS  m
top speed            310.60    315.94    -1.7%      -8..+4%   PASS  km/h
straight stability   yaw 0.000 rad/s after a 0.49 rad/s pulse  PASS
```

## What is modelled

| Piece | Notes |
|---|---|
| Chassis | Rigid body, inertia tensor set explicitly, never derived from a mesh |
| Suspension | Four corners, spring and bump/rebound dampers, anti-roll bars, capped damper force |
| Tyres | Pacejka magic formula, load sensitivity, friction ellipse, relaxation length, per-surface friction |
| Drivetrain | Torque curve, slipping launch clutch with ramped lock, gearbox, clutch-pack LSD, reflected engine inertia |
| Aero | Separate front and rear downforce so balance shifts with speed, plus drag |
| Steering | Ackermann geometry, speed-sensitive lock, rate-limited rack |
| Driver aids | ABS and traction control as smoothed proportional cuts, both defeatable |
| Roll centres | Per axle, so body roll does not swing the contact patch on a full-height lever |

Substeps at 500 Hz. Tyre models go unstable much below 300 Hz.

## Why the checks compare against analytic values

`Analytic.cs` derives each expectation from the car's own configuration, so
agreement means the simulation matches closed-form physics rather than a number
someone liked.

This matters. The first 0 to 100 target was hand-picked at 4.2 s, which is below
this car's traction-limited floor of 4.43 s. It could never have been met, and
failing it said nothing about the model.

The allowed bands are asymmetric on purpose. The analytic 0 to 100 assumes a
perfect launch, so the simulation should land slightly above it. The analytic
skidpad figure puts all four tyres at peak simultaneously, which no real car
manages because one axle always saturates first, so the simulation should land
below it.

## Tuning

Everything lives in `CarConfig`. `ReferenceSportsCar()` is a 1500 kg, 420 hp,
rear wheel drive car. Balance is currently understeer-biased: front slip angles
reach 13 to 20 degrees at the limit while the rear sits at 3 to 5, which is why
the skidpad figure lands 15% under the tyre ceiling. Softening
`AntiRollFront` or stiffening `AntiRollRear` moves it.

## Bugs this harness caught

Recorded because each was silent, each looked like a handling problem rather than
a defect, and each would have been far harder to find inside an engine.

1. **Quaternion composition order.** `System.Numerics` `q1 * q2` applies **q2
   first**. Integrating orientation as `orientation * delta` therefore applied
   angular velocity in the body frame instead of the world frame. The per-step
   error was tiny, so the car held a steady cornering state for seconds and then
   diverged out of floating point noise. This was the single largest cause of the
   cornering instability. `ConventionCheck()` now tests composition order, not
   just a single rotation.
2. **Anti-roll bars inverted.** They transferred load to the inside wheel, making
   them pro-roll bars that fed the roll they exist to resist.
3. **Reflected engine inertia with the clutch out.** Driven wheels carried
   thirteen times their real inertia while coasting, so they lagged road speed,
   held a standing slip ratio, and the friction ellipse quietly ate their
   cornering grip. The rear tyres were stuck at 71% of peak.
4. **Missing roll centres.** Wheels bolted rigidly to the body meant body roll
   rate swung the contact patch on a lever the full height of the centre of mass,
   injecting slip angle and feeding more roll. That loop very nearly cancelled
   the dampers.
5. **Damper spike on landing.** A wheel touching down went from zero compression
   to real compression in one step, which differentiates to a closing speed of
   tens of metres per second and hundreds of kilonewtons of damper force.
6. **Launch clutch re-triggering.** The slip condition was tested fresh each step,
   so flooring the throttle at 35 km/h re-entered "launching" and flipped
   effective wheel inertia twentyfold mid corner.
7. **Relaxation keyed to road speed.** At a standstill the tyre took 400 ms to
   build any force, so the wheel reached a slip ratio of seven before it pushed
   back. It now keys off contact patch speed.
8. **Proportional-only speed controller.** Holding a speed needs non-zero
   throttle to balance drag, so it settled at 7.89 m/s against an 8.00 target and
   silently failed every skidpad run. This one was in the harness, not the model.

## Racing a field

`--race` grids a field of AI cars and runs a race: standing start, lap times,
positions, overtaking, and contact detection. Sixteen cars over ten laps of Monza
takes about twenty seconds.

Contact is counted, never simulated. Making two cars bounce off each other is the
physics engine's job, in Unity, where they are rigid bodies that already collide.
What this can answer is the question that comes first: do sixteen drivers get off a
grid, round a lap and past each other without needing to touch.

Off the grid and round without touching, yes: sixteen cars over ten laps of Monza
finish with no contacts at all, on ten different seeds, where the same race used to
leave two on the opening lap. Past each other, not yet. A driver commits to a pass
and holds it, but the safety bound will not let it close on a car in the next lane,
so passes rarely complete, and with the fastest car gridded last it finishes last.
`PROGRESS.md` section 6 says why and what fixing it would take.

Drivers differ only in pace, which is the share of the car's grip they will use.
Each one plans its own speeds at its own limits rather than scaling a shared plan,
because a driver who corners slower also has to brake earlier.

## Bugs the lap runner caught

1. **Braking planned without the friction ellipse.** The plan let the car brake at
   full capability while already at the cornering limit, which no tyre can do. The
   car arrived hot, was still braking past turn-in, the load left the rear axle
   exactly as it was asked for lateral grip, and it spun. Same corner, every run.
2. **The ellipse, applied naively, was worse.** Taking the lateral demand at the
   cornering-limit speed makes it the whole ellipse by definition, so the plan
   allowed no braking at all through turn-in and made the car reach apex speed the
   moment the road started bending. Half a minute a lap, and it looked like a slow
   car rather than a bad plan. It has a closed form; use it.
3. **Curvature measured across adjacent samples.** At 2 m spacing that measures the
   wiggle in the sampling as much as the bend in the road: peak curvature came out
   30 to 50% high on three circuits, putting corners in the plan that are not there.
   A stride spanning about 6 m reproduces the pipeline's own curvature exactly.
4. **Pure pursuit alone.** Aiming at a point ahead cuts the corner, so the error
   grows through the turn and the correction arrives with the front tyres already
   near their limit. Steering for the curvature the road actually has, and letting
   feedback only trim, is what made it stable.
5. **Cross-track error measured at the centre of mass.** Measured at the front axle
   instead, the same car could be trusted with far more of its grip, because the
   axle is ahead of the mass and that lead is most of the stability.
6. **Lap counter read after the driver moved.** The start line crossing was never
   seen, every lap went unrecorded, and the run ended with nothing to report.

## Bugs the race caught

1. **A driver's line error is not its position on the road.** `LineErrorM` is the
   distance from the line the car is aiming at, so it is near zero whenever the car
   is driving well. Passing it around as each car's lateral position meant every car
   reported itself as sitting on the racing line, so no car could tell whether
   another was alongside it or half the track away. An entire field's worth of
   avoidance logic ran on zeros and looked like bad tuning for hours.
2. **Lifting off mid-corner for traffic spins the car.** Correct physics, terrible
   driving. The speed cap now comes down no faster than the friction ellipse allows,
   which is what a real driver does: give up the corner, back off on the straight.
3. **A proportional following rule cannot answer an 80 m/s closing rate.** A quicker
   car brakes later by design, so the gap it was holding disappears in under a
   second. It needs the braking-distance bound, and that bound needs a wider lateral
   window than the question of whether a car is in the way: something three metres to
   one side of a car doing 25 km/h still cannot be arrived at, at 85.
4. **A separation rule must be a constraint, not a target.** Aiming at the gap it
   wants makes a car drive towards a car it already had more room than. Giving one
   car priority instead is worse: the priority car converges onto the racing line
   without asking who is there.
5. **A car removed from a list by moving it to a nonsense index is still on the
   track.** Index arithmetic wraps, so a retired car reappeared as a stationary
   obstacle at a real place on the circuit, and the field crawled towards a car that
   was not there.
6. **A minimum following distance set generously breaks a standing start.** With the
   grid spaced at exactly that distance, no car may exceed the speed of the one in
   front, so each lags the one ahead and sixteen deep the back of the field was doing
   7 km/h ten seconds in.
7. **A pass decided afresh every 20 ms undoes itself.** A car counted as in the way
   only within 2.2 m to the side, which is about as far as a pass moves the car, so a
   car that had pulled out stopped being held up, steered back in, and was held up
   again. Held-up cars swapped lanes every second, and that is what spun them. A pass
   is now a commitment with its own reasons to end.
8. **Comparing a plan with an actual speed is not comparing drivers.** Out of a corner
   every driver runs well under its plan, so the slower driver behind was told it was
   quicker than the faster one in front. It now compares pace, or a car in trouble.
9. **A following cap applied only while below the car's speed is a switch.** The moment
   it rose past the speed it vanished, the target jumped to the plan, and the car went
   from full throttle to the brakes and back every two seconds through a corner.
10. **The gap along the racing line is not the gap.** Two cars inside the line through
    a chicane are closer than the line says, and whole samples add 2 m of rounding: it
    read 5.9 m with the cars touching. Close up, positions decide.
11. **A speed controller that integrates while pinned holds the throttle past its
    target.** A full throttle climb stored a whole pedal of demand, which kept the car
    accelerating 7 km/h over a follow cap in a chicane. This one was in `PathDriver`
    and was there for single laps too, hidden by the margin a lap alone has.
12. **A negative speed must never become a negative cap.** A negative cap means none,
    so the car behind a stopped car that had rolled back a few centimetres was sent to
    full throttle into it.
13. **Two cars side by side are not in each other's way.** The safety bound treated a
    car in the next lane like one in front, so a pair running side by side braked each
    other every time either edged ahead, and sixteen deep that stopped the back of the
    grid dead. It now holds alongside rather than dropping back.

## Next

The feel test on a gamepad, which needs Unity. The Unity integration layer is
written and compiles; see `Unity/README.md`.
