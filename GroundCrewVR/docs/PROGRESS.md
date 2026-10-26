# Progress Tracker

Legend: ✅ done (code complete) · 🧪 needs on-device verification · ⏳ next · ⬜ not started

> All Phase 1 code was type-checked against stubs of the Unity/XRI APIs, but has **not yet been compiled in the Unity editor or run on a Quest 2**. Every item below is 🧪 until the checklist in `README.md` §5 passes on the device.

## Phase 1 — VR foundation  🧪

| System | Status | Files |
|---|---|---|
| Engine/stack decision, architecture | ✅ | `docs/ARCHITECTURE.md` |
| Project setup + Quest build guide | ✅ | `README.md` |
| Core: event bus, services, entity ids | ✅ 🧪 | `Core/*` |
| Player rig (XRI Starter Assets) | ✅ 🧪 | reused, configured by the builder |
| Teleport + smooth locomotion + snap/smooth turn | ✅ 🧪 | Starter Assets + `LocomotionComfortApplier`, `ComfortSettings` |
| Comfort options saved between sessions | ✅ 🧪 | `ComfortSettings` |
| Grab system: mass, lag, two-handed heavy items, near-only, throw | ✅ 🧪 | `GrabbableItem`, `ItemDefinition`, `NearOnlyInteraction` |
| Impact damage for items | ✅ 🧪 | `GrabbableItem` |
| Placement zones with weight/count/category rules | ✅ 🧪 | `PlacementZone` |
| Hinged handles: doors, panels, levers (spring return, strut assist, detents) | ✅ 🧪 | `RotaryHandle` |
| Physical fingertip buttons | ✅ 🧪 | `PressButton`, `FingertipPresser`, `FingertipSetup` |
| Typed connectors, tether length, drive-away safety event | ✅ 🧪 | `ConnectorPlug`, `TypedSocketInteractor` |
| Access-panel interlock | ✅ 🧪 | `PanelInterlock` |
| Cables/hoses (cheap sag renderer) | ✅ 🧪 | `CableRenderer` |
| Haptics helper | ✅ 🧪 | `Haptics` |
| Wrist tablet UI (fade-on-look, feed, FPS, comfort buttons) | ✅ 🧪 | `WristTablet` |
| Quest perf: foveation, physics = display rate | ✅ 🧪 | `QuestPerformanceSettings` |
| Sandbox scene generator (5 test stations) | ✅ 🧪 | `Editor/Phase1SandboxBuilder.cs` |

Known limitations in Phase 1:
- Props are simple generated geometry (by design; real models arrive with the aircraft in Phase 2).
- Controllers only; hand tracking isn't configured or tested.
- `LocomotionComfortApplier` drives the Starter Assets `ControllerInputActionManager` by property name. If XRI renames those properties, comfort switching logs a warning and the defaults stay.
- Wrist tablet offset is a best guess; it may need a per-player tweak.

## Phase 2 — 737-class aircraft  ⏳
- [ ] Asset decision (commission / licence / in-house) and 737-class model brief (see ARCHITECTURE §9)
- [ ] `AircraftDefinition` + service-point prefab contract + `AircraftView` binder
- [ ] Exterior with LODs; gear, wheels, engines, control surfaces (static/animated)
- [ ] Cargo holds (fwd/aft bulk) as `PlacementZone`s with real limits
- [ ] 737 cargo doors: inward plug door, counterbalance
- [ ] Passenger doors (1L/2L/1R/2R) open/close; overwing exits (interaction only)
- [ ] Cabin interior: seats, overhead bins, galleys, lavatories, flight-deck door, cabin lighting (streamed)
- [ ] Aircraft state: power (battery/APU/ext), beacon, brakes, doors

## Phase 3 — First complete turnaround  ⬜
- [ ] Simulation layer: `TurnaroundSession`, task graph, clock, rules/validators, scoring
- [ ] Aircraft arrival (AI taxi-in) + marshalling to stop mark
- [ ] Chocks, cones, GPU, bags off/on, boarding (stairs), pushback (simple)
- [ ] Radio messages (text + placeholder audio) and tablet assignment/task pages
- [ ] Results screen

## Phase 4 — Vehicles  ⬜
- [ ] Vehicle framework: enter/exit, steering wheel, gear lever, throttle/brake, parking brake, horn, lights, reverse beeper
- [ ] Baggage tractor + carts (coupling), pushback tug (tow bar / towbarless), fuel truck, catering truck

## Phase 5 — A320-class aircraft  ⬜
- [ ] A320 data set + model; outward hydraulic cargo doors; workflow differences

## Phase 6 — Advanced services  ⬜
- [ ] Refuelling with bonding/deadman/quantity; potable water; waste; catering lift; GPU/PCA/air start; de-icing; maintenance calls & failures

## Phase 7 — Career  ⬜
- [ ] Ranks, XP, pay/bonuses/penalties, unlocks, save system

## Phase 8 — Polish  ⬜
- [ ] Weather, day/night, AI passengers/crew/pilots, airport life, 3D audio, performance pass, multiplayer prototype
