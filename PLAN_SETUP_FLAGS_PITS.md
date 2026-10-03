# Plan: car setup screen, then flags, penalties, tyre wear, pits and the safety car

Approved 2026-10-02. PROGRESS.md tracks where each stage stands; this file is the design it works from.

## Context

You asked to plan two roadmap items: Phase 3's last open row, "Flags, penalties, pit stops", and Phase 6's car setup screen. Your other idea waits until these are done. Reading the code showed what shapes the work:

* The physics has no tyre wear or fuel, so today a pit stop could only lose time. Solo races are also fixed at 3 laps.
* No circuit has a pit lane. The cached OpenStreetMap data does hold real pit lanes for all five real circuits, but the pipeline puts the start line in the middle of the longest straight, so only Monza's real pit lane runs beside the in-game grid.
* Much already exists to build on. Every setup value is a field of `CarConfig`, and every wheel reports its contact point. The AI drive fixed lanes offset from the centreline (`TrackData.EnsureLanes`, `PathDriver.Lane`), and `SpeedPlan.Build` plans any path along the samples. `RaceControl` is engine-agnostic bookkeeping shared by the harness and Unity, and the lobby already draws −/+ rows (the LAN host's AI CARS and LAPS).

The intended outcome is a setup screen in the lobby, plus races judged for track limits with time penalties. Races also get yellow, blue, black and white, and black flags, a safety car, and tyre wear that makes a stop worth taking. All six circuits get a pit lane, and all of it works in LAN races.

## Your decisions (2026-10-02)

* Setup: only what the physics models, one setup per circuit, plus the driver assists.
* Penalties: seconds added to race time, 5 s per offence.
* Flags: yellow, blue, black and white with black, and the safety car.
* Pits: for tyre wear, with one compound. The pit lane is generated beside the start straight in front of today's garages, and an autopilot drives your car inside it.

## Order

| Stage | Work | Size | Needs |
|---|---|---|---|
| 1 | Car setup screen and assists | medium | nothing |
| 2 | Track limits, corner cuts, time penalties, black and white and black flags | medium | nothing |
| 3 | Yellow and blue flags | medium | 2 |
| 4 | Tyre wear in the physics | medium | nothing |
| 5 | Pit lane, pit stops, solo race settings | large | 4 |
| 6 | Safety car | large | 3 and 5 |
| 7 | All of it in LAN races | medium | 2 to 6 |

Setup goes first because it stands alone, is the smallest, and gives you something to drive soonest. Every stage ends playable, committed and recorded, with your drive as its last check, so the work can pause after any of them. Swapping stages 1 and 2, or 3 and 4, costs nothing.

## Rules for every stage

* Logic goes into `Sim/` first and is proven headlessly before Unity sees it, against expectations derived from the car's own numbers.
* Then it is copied into Unity as `Unity/README.md` describes, followed by `dotnet build Sim/CarRace.UnityCheck -c Release` with 0 errors, a release build and screenshots.
* Harness commands below are short for `dotnet run --project Sim/CarRace.Harness -c Release -- <args>`.
* The regression gate stays green: `verify_all.py` 6 of 6; All 5 checks (6 from stage 4); `--lap all` 6 of 6; Monza with 16 cars for 10 laps on seeds 1 to 10 with 0 contacts; testcircuit `--fastest-last` on seeds 1 to 10, no worse than today's 1 contact.
* Tyre wear starts off and the safety car only comes out for a real incident, so a default race drives as it does today.
* PROGRESS.md gets the new rows as TODO before stage 1 starts. In Phase 1 that is tyre wear. In Phase 3 the "Flags, penalties, pit stops" row splits into track limits and penalties, yellow and blue flags, pit lane and stops, and the safety car. Phase 5 gets the LAN row, and a new Phase 6 table holds the setup screen. Each row is marked WIP when started and DONE when its command passes, with new commands added to section 7. Commits in both repos stay local.

## Stage 1: car setup screen

What you can set, each as a −/+ row showing the real value and unit with the default marked:

* Suspension: springs front and rear; bump and rebound front and rear; anti-roll bars front and rear.
* Brakes: bias to the front, and pressure as a share of `MaxBrakeTorque`.
* Gearbox: final drive and the six ratios (kept descending), with top speed read out from `Analytic.TopSpeedKph`.
* Differential: preload, power ramp and coast ramp.
* Aero: front and rear downforce, with aero balance read out. Drag rises with added downforce at a lift to drag ratio of 4 (each 0.1 of ClA adds 0.025 of CdA), so a big wing costs top speed.
* Assists, shared by every circuit: ABS, traction control, engine braking control and the automatic gearbox.

Ride height is left out, because lowering the car has no downside here: `VehicleSim.AttachmentHeight` keeps it at `CgHeight`. Steering lock is left out because it calibrates the keyboard assist, and so are tyres and engine. Starting ranges, narrowed wherever the sweep finds trouble: springs 70 to 160%, dampers 50 to 200%, bars 0 to 250%, bias 50 to 75%, pressure 60 to 100%, final drive 2.90 to 4.20, ratios within 25%, preload 0 to 200 N m, ramps 0 to 100%, front downforce 0 to 0.40 and rear 0 to 0.60. PROGRESS.md already notes 0.40 and 0.60 as an option.

Changes:

* New `Sim/CarRace.Vehicle/CarSetup.cs` holds the table of settings (key, group, label, unit, range, step and a one line hint), with defaults read from a baseline `CarConfig`. `Apply` returns a modified copy, and `key=value` text is used for saving (invariant culture, unknown keys ignored). `Apply` makes new arrays, because `CarDefinition.ToConfig` hands out its own `gearRatios` array and editing that in place would change the asset.
* New `Unity/Assets/Scripts/Lobby/SetupStore.cs` is a static like `PlayerSetup`. It keeps a setup per circuit scene in PlayerPrefs (`CarRace.Setup.<scene>`), plus the assists.
* `CarController.Awake` applies the stored setup for `gameObject.scene.name` and sets the four assist fields, but only for the car with a `DriverInput`. AI cars have none, and neither do the cars `LanRace` copies from them. It is skipped under `-benchmark`, so benchmarks stay comparable.
* New `Unity/Assets/Scripts/Lobby/SetupScreen.cs` opens from a SETUP button beside PLAY in `LobbyMenu`'s YOUR CAR panel. It is a full panel in the lobby's style (`PanelWithHeader`, `Hud.Rounded`, the −/+ rows), with a tab per group, Reset and Done, and Esc to close. It edits the setup of the chosen circuit, and it is locked while READY in a LAN lobby, as the car is.
* A new harness mode, `--setup-sweep`, runs every setting at its minimum and maximum, with the rest at default, through the existing check scenarios.

Verify:

* `--setup-sweep` passes when every extreme is stable (no spin on the steady skidpad, no suspension at full travel, no NaN) and the default case reproduces the five checks exactly. Each effect must also point the right way: geared top speed falls as the final drive rises, a stiffer rear bar raises yaw rate at the same steer, rear downforce adds grip at speed and costs top speed, and stiffer springs reduce roll.
* In the game, setups are kept per circuit across restarts and the AI cars' configs are unchanged (logged at race start). The `CarDefinition` asset must also be unchanged after a play session in the editor.
* Your drive: a stiff rear bar and a big rear wing at Ise Bay should feel clearly different from the default, and each assist should switch.

## Stage 2: track limits and time penalties

Rules:

* A car is off track when every grounded wheel's contact point is more than 0.15 m past the road edge. Kerbs count as off, as in F1, which also keeps the test identical in the harness (flat ground, no verge) and in Unity.
* An excursion ends once two wheels have been back for 0.2 s. If the car spins (heading over 60 degrees off) or nearly stops (under 30 km/h) during it, it is an incident rather than an offence. A recovery, by R or by the AI, cancels it.
* An excursion is a cut when the distance driven while off is under 97% of the shortest legal path between the same two points, and at least 3 m shorter. The shortest legal path is the shortest one inside the road edges, found with the funnel algorithm. A cut costs 5 s at once.
* Any other excursion is a track limits offence. The first two are warnings under the black and white flag, and the third and every one after it costs 5 s.
* A fifth penalty brings the black flag: the car is disqualified and classified last as DSQ, and your race ends at the results.
* An offence of either kind makes the lap invalid: it shows INVALID on the timing panel and never counts as a best lap.
* Penalties apply at the flag, as FIA time penalties do; the live order stays the order on the road.

Changes:

* New `Sim/CarRace.Track/TrackLimits.cs` holds the judge for one car, fed with the contact points from `VehicleSim.Wheels` and the car's sample and position, plus the corridor shortest path.
* `RaceControl.Entry` gains penalty seconds, warnings, penalties, disqualified and lap valid. Best laps come from valid laps only, `Classification` orders by finish time plus penalties, and an event list feeds the HUD.
* In the harness, `RaceRun` judges every car, and a new `--limits-test` drives scripted paths on all six circuits: across the inside of every corner, wide on every exit, and one clean lap.
* In Unity, `RaceDirector` adds a new `TrackLimitsMonitor` to each car it drives, the way it adds `CarContacts`. `LapTimer` reads the player's monitor to show INVALID and stops saving invalid laps and sectors. A new `RaceHud` shows the flags and penalty banners, with flag images generated like the HUD's rounded panels, and the results get a Pen column and DSQ.

Verify:

* `--limits-test all` passes when every scripted cut is penalised, every wide run is warned and never penalised, and clean laps show 0 offences, on six of six circuits.
* The gate's Monza race still shows 0 contacts, and the AI should commit 0 offences. If they do collect offences, their line or the rule gets fixed before stage 3.
* Your drive: cut Royal Park's first chicane (+5 s, INVALID), run wide three times (two warnings, then +5 s), and read the results.

## Stage 3: yellow and blue flags

Rules:

* An incident is a car stopped (under 3 m/s) or spun (over 90 degrees off) after the start, unless it is in its pit box. Its yellow zone runs from 250 m before it to 30 m past it, and clears 2 s after the car moves on or is recovered.
* Under yellow nobody passes except the car in trouble, and the AI lift to 90% of their plan. You get +5 s if you leave the zone ahead of a car that was ahead of you when you entered it, leaving the car in trouble aside. Giving the place back therefore clears it.
* A blue flag shows when a car a lap or more up is within 200 m behind. The AI yield: they take the lane away from the faster car and ease to 92% of plan until it is 10 m past. For you the blue flag is a warning only, as you chose.
* Green shows briefly as a yellow zone clears, and the chequered flag shows at your finish.

Changes:

* New `Sim/CarRace.Track/RaceFlags.cs` is computed every reaction interval from the field and `RaceControl`, and is shared by `RaceRun` and `RaceDirector`.
* `RaceDriver` gets `UnderYellow`, which blocks new passes and abandons an ongoing one, and `YieldTo`. `WorthPassing` treats a car under a blue flag as worth passing.
* `RaceHud` shows the flags, and `MiniMap` draws a yellow zone on the road ahead.
* In the harness, `--stop-car N --stop-at S` parks a car on the racing line, and `--lapped N` slows one car enough to be lapped.

Verify:

* `--race testcircuit --cars 8 --laps 5 --stop-car 3 --stop-at 60` passes with a yellow zone, no passes inside it except of car 3, and 0 contacts.
* `--race testcircuit --cars 8 --laps 10 --lapped 8` passes when car 8 is lapped, each blue flag clears within 15 s, and there are 0 contacts.
* The full regression gate runs again, since `RaceDriver` changed.
* Your drive: stop on the racing line and watch the AI pass you single file, then get lapped and see the blue flag.

## Stage 4: tyre wear

* Each wheel gets `Wear`, from 0 new to 1 worn. Grip falls 4% by 0.6 wear and then to 80% at 1, a cliff that makes stopping worth it, and `Pacejka.PeakForce` takes that factor.
* Wear grows with the power dissipated in the contact patch, tyre force times slip speed in both directions, so sliding, wheelspin and locking all wear tyres faster. It is divided by a wear energy in `TyreConfig` (mirrored in `CarDefinition`) and multiplied by `VehicleSim.TyreWearRate`. That rate defaults to 0, which is off and leaves every existing check unchanged.
* Wear is calibrated so that at ×1 the rear tyres wear out in about 30 laps of Royal Park at AI pace 0.85. Races offer off, ×1, ×2, ×5 and ×10, which is about 6 laps at ×5.
* The AI scale their plan by the square root of their tyres' grip. That is exact where grip is the limit, and it is the same scaling the grass rule uses. The braking guide scales the same way for you.
* A display of all four tyres' wear sits beside the dashboard.

Changes: `Tyre.cs`, `VehicleSim.cs`, `CarConfig.cs` and `CarDefinition.cs`; `PathDriver`, which gets a grip scale beside `SurfaceGrip`; `RaceDirector`, `RacingLineGuide` and `Dashboard`; and the harness `Program.cs` and `Analytic.cs`, for a sixth check and a `--wear <circuit>` run.

Verify:

* The sixth check: with wear off the five checks come out bit for bit as before. The skidpad at wear 0, 0.6 and 1 gives 1.00, 0.96 and 0.80 of the fresh figure within 2%, and a sliding lap wears more than a clean one. Expect "All 6 checks pass".
* `--wear monza` reaches full wear at ×1 near 30 laps. Across that run, lap times rise along the curve and no car spins.

## Stage 5: pit lane and pit stops

The pit lane is built in C# from the centreline, in a new `Sim/CarRace.Track/PitLane.cs`. The harness, the AI and the scene builder then share one definition, and neither the track files nor the pipeline change.

* It runs on the right of the start straight from about 180 m before the line to 180 m after it, or 250 m where the straight allows, with 60 m entry and exit ramps. The test circuit's straight is the shortest, at 192 m either side of the line.
* A fast lane runs beside a working lane with eight boxes (LAN's `MaxCars`), 12 m apart, in front of the garages. The garages stay where they are: the pit lane fits inside today's 15 m verge, with an apron up to the garage fronts about 25 m from the centreline.
* A pit wall runs between the ramps where the verge is now, and a 60 km/h limit applies between the entry and exit lines.
* To the AI it is a third lane in `TrackData` (`LanePoints[2]`), identical to the right passing lane outside the pit span. `PathDriver`'s lane blending, lap counting and progress therefore work unchanged. `SpeedPlan.Build` gains a per sample ceiling for the limit, and the stop is a speed cap from the distance to the box.

Stops:

* The AI pit when average wear passes a threshold of their own between 0.65 and 0.75 with at least two laps left. They stand for 4 s while tyres are changed, then drive out through the exit. In the pit lane they are exempt from the stuck rule and cannot raise a yellow flag.
* You drive onto the entry ramp yourself. At the entry line an autopilot takes over: a `RaceDriver` for your car, which `AiDrivesPlayer` already builds. It drives to your box, changes the tyres with a countdown, and hands control back at the exit line.
* Solo race settings go in the lobby's PLAYERS panel, like the LAN host's rows: LAPS (1 to 20), TYRE WEAR, and from stage 6 SAFETY CAR. `RaceDirector` reads them through a new `RaceSettings` static.
* The results get a Pits column.

Changes beyond the above:

* Sim: `TrackData`, `SpeedPlan`, `PathDriver`, `RaceDriver` and `RaceControl`, plus `--tyre-wear N` in the harness `RaceRun`.
* New `Unity/Assets/Editor/PitLaneBuilder.cs` is called from `TrackSceneBuilder.Build`, as `StartFinishBuilder` is. It builds the pit road and apron, the ramps, the pit wall on the Barrier layer, lines and box markings, and 60 boards.
* `TrackSceneBuilder` leaves the right verge and barrier out along the pit span, and `SceneryBuilder.Trackside` moves that stretch's catch fence and guardrail onto the pit wall. `SceneryBuilder`'s land grid treats the pit area as road.
* `MiniMap` draws the pit lane, and `RaceDirector` and `LobbyMenu` change as described.
* All six scenes are rebuilt. They are outside Git, so the Unity repo does not grow.

Verify:

* `--race testcircuit --cars 8 --laps 12 --tyre-wear 5` on seeds 1 to 10, then on each other circuit, passes when every car stops once, nobody exceeds 60 km/h in the limit zone and nobody stops outside a box. There must also be 0 contacts and every car must finish; pit loss is reported per circuit.
* Screenshots of each pit lane come from `-viewpoints`, with a pit lane view added, plus a benchmark at Royal Park on High against the last figures.
* Your drive: a 10 lap race at ×5 with a stop of your own and AI stops around you.

## Stage 6: safety car

* When it is switched on in race settings, it comes out in two cases: two or more cars are stopped or spun in one yellow zone at once, or one car has stood on the racing surface for over 10 s. It never comes out on the first or last lap, and at most once every three laps. `-safetyCarAt S` in the game and `--safety-car-at S` in the harness force it.
* From deployment nobody passes, and cars catch up at no more than 70% of their plan. The safety car waits at the pit exit, pulls out ahead of the leader, and the field queues behind it.
* SAFETY CAR IN THIS LAP shows once the incident has cleared and every car has queued for a lap. The safety car turns into the pit lane at the end of that lap, and green comes at the line, with no passing before it.
* Passing under the safety car costs 5 s per car. It is judged at the green, as yellow passes are, ignoring cars that pitted or were in trouble. The AI take the cheap stop under it when wear is over 0.4.
* The safety car itself is a copy of an AI car made at race start, as `LanRace` copies cars. It is driven by a `RaceDriver` at safety car pace and kept outside `RaceControl`. It gets the Supercar body in a fixed colour and a light bar, which is easy to restyle if you have a look in mind.

Changes: new `Sim/CarRace.Track/SafetyCar.cs` (states and pace); `RaceFlags`; `RaceDriver`, for queueing and the catch up cap; the harness `RaceRun`; and in Unity, `RaceDirector`, `RaceHud` and the lobby setting.

Verify:

* `--race monza --cars 16 --laps 10 --safety-car-at 300` on seeds 1 to 10 passes when the queue forms within a lap, nobody passes under the safety car and the restart is clean. There must also be 0 contacts, and every car must finish.
* Your drive: start with `-safetyCarAt 120` and race through to the restart.

## Stage 7: LAN races

* `Control.Version` goes from 2 to 3, so mismatched copies are turned away cleanly.
* `LobbyState` carries tyre wear and the safety car, set by new host rows. `Standing` carries penalty seconds, warnings, flags (blue, black and white, black, in the pits) and stops.
* A new `Offence` message goes from client to host. Each machine judges the car it drives, since only that machine knows the wheels, and the host applies the penalty and sends standings as it does now.
* A new `Flags` message goes from host to clients, with the yellow zones and the safety car's state. The safety car is one more host-driven car in the snapshots, and pit stops run on whichever machine drives the car.

Verify: `--lan` and `--lan-bots` are extended so that a bot's offence reaches every client's standings, flags arrive, and an old version is refused. Then several copies on this laptop race with a penalty, a stop and a forced safety car.

## Not in this plan

Camber, toe and tyre pressures; tyre compounds; fuel; damage; drive-through penalties; collision blame; unlapping under the safety car; red flags and the virtual safety car. Flags waved at the marshal posts are also out: that is trackside scenery, which I would ask you about first.
