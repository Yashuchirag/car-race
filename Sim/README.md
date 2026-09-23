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
```

Exit code is 0 when all five checks pass.

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

## Next

Cameras, a Unity `MonoBehaviour` wrapper around `VehicleSim`, and a
`ScriptableObject` that returns a `CarConfig`. None of it needs the physics to
change.
