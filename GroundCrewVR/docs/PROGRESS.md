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

## Phase 2 — Aircraft  ⏳ (models done, gameplay binding next)

Done (models type-checked and rendered outside Unity; 🧪 in Unity/Quest):
- [x] Procedural generator for both aircraft: `Tools/AircraftGenerator` (737-class **and** A320-class, so Phase 5's model work is done early)
- [x] Shapes traced from Boeing D6-58325-7 and Airbus A320 AC drawings (fuselage outlines, wing/tailplane planforms, fins); door sills, cargo doors and clearances checked against the manufacturer tables
- [x] Exterior with 3 LODs (~85k incl. flight deck / 22k / 5.5k tris); engines (737 flat short-duct vs A320 round long-duct), gear, wheels, winglets / sharklets, flap-track fairings, antennas
- [x] Moving parts: all passenger doors, overwing exits, cargo doors (737 inward plug, A320 outward + bulk door), flaps, ailerons, elevators, rudder, fans, nose-gear steering, wheels
- [x] Exterior lights as nodes (beacons, nav, strobes)
- [x] Interior: cabin lining, floor, 168 / 162 seats with seat map, bins, galleys, lavatories with doors, cabin lights, flight deck with door; cargo holds with volumes and limits
- [x] Flight decks placed from the documented pilot eye points, behind see-through windscreens with exact openings: 737 yokes, tall control stand with striped trim wheels and CDUs behind it; A320 sidesticks, tables, MCDUs at the front of the pedestal and a gated thrust-lever housing; glowing displays, glareshield, overhead and pedestal panels painted into one texture atlas per type; dome light (glTF point light) switchable from `AircraftRig`
- [x] Service-point contract (`SP_*` nodes + JSON sidecar), origin at the nose-gear contact point
- [x] `AircraftRig` runtime API + LODGroup setup; `AircraftPlacer` editor menu with a door/beacon/flaps test pedestal

Remaining:
- [ ] `AircraftDefinition` ScriptableObject generated from the sidecar + `AircraftView` binder (holds → `PlacementZone`, service points → `TypedSocketInteractor`, door handles → `RotaryHandle`)
- [ ] Grab-to-open door handles (737 cargo door: unlatch, push in and up, counterbalance)
- [ ] Aircraft state: power (battery/APU/ext), beacon, brakes, doors
- [ ] Swap glTFast PBR materials for URP Simple Lit; on-device performance pass
- [ ] Optional: artist-made models following the same node contract

## Phase 3 — First complete turnaround  ⬜
- [ ] Simulation layer: `TurnaroundSession`, task graph, clock, rules/validators, scoring
- [ ] Aircraft arrival (AI taxi-in) + marshalling to stop mark
- [ ] Chocks, cones, GPU, bags off/on, boarding (stairs), pushback (simple)
- [ ] Radio messages (text + placeholder audio) and tablet assignment/task pages
- [ ] Results screen

## Phase 4 — Vehicles  ⏳ (first pass done 🧪)
- [x] Vehicle models: baggage tractor, baggage cart, pushback tug, tow bar, fuel truck, catering truck (`Tools/VehicleGenerator`, ~2-8k tris each)
- [x] Vehicle framework: enter/exit (`VehicleSeat`), grabbable steering wheel, triggers for throttle/brake, D/N/R, parking brake, engine, horn, lights, beacon, reverse beeper, speed governor, drive interlocks, collision events
- [x] Synthesised engine / horn / reverse-alarm audio (no assets needed)
- [x] Baggage tractor + 3 carts: drawbar bodies, turntable steering, couple / uncouple by hand, automatic cart brake
- [x] Pushback tug + tow bar: latch to the nose-gear tow fitting, kinematic aircraft towing around the main gear, nose-gear steering + wheel roll on the aircraft, shear pin at the towing limit (737 78°, A320 95°)
- [x] Catering truck: scissor lift and platform on hold-to-run buttons, rides the player up, drive interlock
- [x] Fuel truck: drivable; hose reel and bonding reel modelled (refuelling itself is Phase 6)
- [x] `VehicleAssembler` editor menu parks the fleet on Stand 24
- [ ] On-device tuning (wheel friction, suspension, steering feel, drawbar joints)
- [ ] Driving comfort options (vignette while moving, gentler acceleration)
- [ ] Towbarless tug, belt loader, stairs, GPU cart, water / waste trucks, de-icer, vans
- [ ] Recorded engine sounds; dash speed/gear display; AI drivers

## Phase 5 — A320-class aircraft  ⬜ (model already generated in Phase 2)
- [ ] A320 workflow differences: hydraulic cargo door operation (hand pump / selector), containerised (ULD) loading, different sill heights for GSE

## Phase 6 — Advanced services  ⬜
- [ ] Refuelling with bonding/deadman/quantity; potable water; waste; catering lift; GPU/PCA/air start; de-icing; maintenance calls & failures

## Phase 7 — Career  ⬜
- [ ] Ranks, XP, pay/bonuses/penalties, unlocks, save system

## Phase 8 — Polish  ⬜
- [ ] Weather, day/night, AI passengers/crew/pilots, airport life, 3D audio, performance pass, multiplayer prototype
