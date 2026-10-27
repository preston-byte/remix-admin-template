# Ground Crew VR — Technical Architecture

Working title for a first-person VR airport ground-operations simulator for **standalone Meta Quest 2**.
This document is the design reference; `PROGRESS.md` tracks what exists, and `../README.md` explains how to build and test it.

---

## 1. Engine and technology stack

| Layer | Choice | Version policy |
|---|---|---|
| Engine | **Unity 6 LTS** (6000.0 LTS or newer LTS) | Stay on LTS; upgrade between phases, not during |
| Render pipeline | **URP**, Forward rendering, Vulkan | Quest-tuned URP asset (see §8) |
| XR runtime | **OpenXR** plug-in + *Meta Quest Support* feature group | Comes with the Unity "VR" template |
| Interaction | **XR Interaction Toolkit 3.x** + Starter Assets sample | Our code wraps XRI rather than forking it |
| Input | Unity Input System | Required by XRI 3 |
| Text | TextMesh Pro (in `com.unity.ugui` 2.x) | |
| Networking (later) | Netcode for GameObjects *or* Photon Fusion | Decided before Phase 7; see §6 |

**Why Unity + OpenXR + XRI** (as the spec prefers):
- Unity has the most mature Quest pipeline: Android/Vulkan builds, single-pass multiview, fixed foveated rendering, ASTC, GPU instancing, static batching, baked lighting and occlusion culling all work out of the box.
- OpenXR keeps us vendor-neutral. The same build could later target Pico or PC VR, which would not be possible on a Meta-only SDK.
- XRI 3 already provides solid teleport, smooth locomotion, snap/smooth turn, a comfort vignette, poke, sockets and grab transformers. We build *simulation-specific* behaviour on top instead of reinventing locomotion.

**Alternatives considered:**
- *Unreal Engine 5*: better visuals, but its baseline cost on the mobile XR2 GPU is much higher. Simulator-scale scenes (airport, many vehicles, NPCs) would fight the frame budget from day one.
- *Godot 4*: OpenXR support is good, but tooling for large scenes (occlusion, lightmapping at scale, LOD workflows) and the asset ecosystem are less mature.
- *WebXR*: no install needed, but the memory, CPU and baked-lighting limits rule it out for an airport-scale sim.
- *Meta XR SDK / Interaction SDK*: it has nicer hand-tracking grab poses. We stay on XRI for portability. Meta's OpenXR packages (passthrough, hand poses) can still be added later without changing our architecture.

---

## 2. Layered architecture

```
┌────────────────────────────────────────────────────────────────────┐
│ META        Career · XP · Money · Unlocks · Save/Load              │  Phase 7
├────────────────────────────────────────────────────────────────────┤
│ SIMULATION  (plain C#, authoritative, serializable)                │  Phase 3+
│   TurnaroundSession · TaskGraph · Rules/Interlocks · Clock          │
│   AircraftState (doors, holds, fuel, power, brakes) · Weather       │
├────────────────────────────────────────────────────────────────────┤
│ GAMEPLAY PRESENTATION (MonoBehaviours bound to sim state)          │  Phase 2+
│   AircraftView · VehicleController · GSE units · NPC agents         │
├────────────────────────────────────────────────────────────────────┤
│ INTERACTION (generic, reusable, no game rules)          ◄ Phase 1  │
│   GrabbableItem · RotaryHandle · PressButton · ConnectorPlug        │
│   TypedSocketInteractor · PlacementZone · CableRenderer · Interlock │
├────────────────────────────────────────────────────────────────────┤
│ PLAYER / PLATFORM                                       ◄ Phase 1  │
│   XRI Starter rig · Comfort settings · Fingertips · Quest perf      │
│   Wrist tablet UI                                                   │
├────────────────────────────────────────────────────────────────────┤
│ CORE        EventBus · Services · StableId                ◄ Phase 1 │
└────────────────────────────────────────────────────────────────────┘
```

Rules:
1. **Lower layers never reference higher ones.** A `ConnectorPlug` doesn't know what a turnaround is. It publishes `ConnectorEvent`, and the turnaround rules decide what that means.
2. **The simulation layer is the source of truth.** For example, "is the aircraft on ground power?" is answered by `AircraftState`, not by asking a socket. Presentation reads the state; interactions send intents.
3. **Data lives in ScriptableObjects.** Aircraft types, item types, turnaround templates, vehicles and career ranks are all data, so the A320 is mostly a new data set rather than new code.

### Core building blocks (implemented)

| Piece | Purpose |
|---|---|
| `EventBus` | Typed struct events, isolated handlers. Every interaction fact flows through here, which gives one choke point for scoring, radio, tablet and networking. Not for per-frame data. |
| `Services` | Tiny registry for genuinely global systems (turnaround manager, radio, audio, save). |
| `StableId` | Stable id on every simulation-relevant object. Events carry ids, so they can be serialized and sent over the network. |

---

## 3. Interaction system (Phase 1)

All of these sit on top of XRI interactables, so they work with the Starter Assets rig as-is.

| Component | Real-world uses | Key behaviour |
|---|---|---|
| `GrabbableItem` + `ItemDefinition` | Bags, boxes, cones, chocks, catering inserts | Real mass; heavier items lag behind the hand; two-hand items crawl when held one-handed; near-only grab; impact damage; grab/release/damage events |
| `NearOnlyInteraction` | Everything physical | XRI select/hover filter so nothing can be force-grabbed from across the apron |
| `PlacementZone` | Cargo holds, carts, chock positions, cone spots, catering bays | Counts items that are *placed* (inside, released, at rest); mass and count limits; accepted categories; over-capacity events |
| `RotaryHandle` | Cargo/passenger doors, access panels, levers, valves | Hand motion around a hinge drives the angle 1:1; speed limit for heavy doors; spring return (deadman levers); strut assist; detent haptics; limit events; lockable |
| `PressButton` + `FingertipPresser` | Jet-bridge panel, GPU start/stop, truck lifts, tablet | Physical fingertip press, latching or momentary, haptics |
| `ConnectorPlug` + `TypedSocketInteractor` | GPU cable, fuel nozzle, bonding clip, water/waste/PCA hoses, headset jack | Typed fit; lockable socket that never drops an existing plug; tether length; pulling equipment away while connected raises a **safety violation** |
| `PanelInterlock` | Any receptacle behind a panel | Socket accepts a plug only when the panel is open; the panel can't close on a cable |
| `CableRenderer` | All cables and hoses | Cheap sagging curve resting on the ground; no physics rope |
| `IndicatorLight`, `ValueReadout` | Status lamps, gauges | Material swap (no real lights), formatted values |

**Why the doors aren't physics joints:** hinge joints jitter at 72 Hz under hand forces, they are hard to make deterministic for networking, and they behave badly at Quest physics rates. `RotaryHandle` is kinematic and deterministic. Doors carry a kinematic Rigidbody so they still push bags.

**Vehicles (Phase 4)** will use the same layer. The steering wheel is a `RotaryHandle` (range ±540°, signed-angle unwrapping added), the gear selector is a `RotaryHandle` with detents, and pedals are thumbstick/trigger inputs. Real foot pedals don't exist on Quest, so the trigger acts as accelerator and grip as brake while seated. The parking brake is a lever handle.

---

## 4. Aircraft model contract (Phase 2)

Aircraft are imported meshes plus an `AircraftDefinition` ScriptableObject. Code only talks to **named service points**, never to mesh internals, so a licensed 737 model can replace a stand-in without code changes.

```
AircraftDefinition (ScriptableObject)
  id, displayName, family (NarrowbodyBoeing737Class / NarrowbodyA320Class)
  dimensions (span, length, wheelbase, nose-gear-to-stop-mark offset)
  holds[]       : id, zoneName, maxMassKg, bulk|ULD, door ref
  doors[]       : id, type (Passenger/Service/Cargo/Emergency), side, opening style
                  737 cargo: inward plug, spring counterbalanced
                  A320 cargo: outward, hydraulic, hand-pump selector
  servicePoints : GPU receptacle, refuel panel + coupling(s), bonding point,
                  potable water panel, lavatory service panel, PCA connection,
                  air-start connection, interphone jack, tow fitting, chock positions,
                  cone positions (engines, wingtips, tail)
  fuel          : tank capacities, density, max refuel rate
  turnaroundTemplate ref
```

**Status:** generated models implementing this contract exist for both aircraft (`Tools/AircraftGenerator`, `Assets/GroundCrew/Art/Aircraft`). Their JSON sidecar carries the data an `AircraftDefinition` needs, and `AircraftRig` drives the moving parts.

Prefab contract: the model prefab contains empty transforms named `SP_<ServicePointId>` (for example `SP_GPU`, `SP_RefuelPanel`, `SP_Door1L`, `SP_FwdCargo`, `SP_NoseGearTow`, `SP_Chock_MLG_L_Fwd`). An `AircraftView` binder finds them by name, then spawns sockets, zones and handles from the definition.

### Vehicles (Phase 4)

- **Models** come from `Tools/VehicleGenerator` with a node contract like the aircraft:
  - `Wheel_*`, `SteeringWheel` + `SteerAxis`, `DriverEye`, `ExitPoint`
  - `Btn_*` + `Out`, `Light_*`, `Hitch_*`, `Collision_*`
  - moving parts (`Drawbar`, `LiftBox`, `Scissor*`, `Platform`, `HoseReel`)
  - a JSON sidecar with mass, torque, speeds and wheels
- **`VehicleAssembler`** turns a model into a vehicle: Rigidbody, convex hulls, WheelColliders, controller, seat, wheel, buttons.
- **Driving:** `VehicleController` runs on WheelColliders: automatic D/N/R, parking brake, governor, interlocks (`AddDriveInhibit`).
- **Intents:** inputs arrive as a `VehicleInput` struct, so AI drivers and remote players use the same path as the seated player.
- **Seating:** `VehicleSeat` parents the XR Origin to the vehicle and turns locomotion off. The controls are triggers for the pedals, a grabbable wheel (or the stick), and dash buttons that mirror the controller buttons.
- **Trailers:** baggage carts are separate Rigidbodies. The drawbar is its own body, jointed at the turntable, and the cart's front wheels steer to the drawbar's angle. Couplings are ConfigurableJoints created by `TowCoupler`.
- **Aircraft towing is kinematic, not a 70 t rigid body:**
  - The tow bar is rigid, pinned at both ends; the main gear never slips sideways; the nose castors.
  - `AircraftTowable` re-solves the aircraft around the main gear every physics step from the tug pin's position.
  - That gives true pushback behaviour (pushing is unstable and needs correction; pulling is stable) with no jitter.
  - The towing steering limit acts as a shear pin.
  - The tug only "feels" the load through a lower torque and speed limit.
- **Equipment functions:**
  - Platform lifts, belt tilt and boom joints use one component, `HydraulicAxis`: hold-to-run buttons, a parking-brake interlock and a drive inhibit until stowed. Its axis comes from a generator helper node.
  - Linkages that need geometry have their own small components: the stairs flight and treads, the catering scissor, the de-icer basket leveller.
- **Docking:** `DoorDockingSensor` measures an edge point against the nearest `SP_Sill_*` node (height error, gap from the fuselage, lateral offset). It drives a DOCK lamp and publishes `DockingEvent`. The jet bridge will use the same sensor.
- **Riding:** `RideablePlatform` parents the XR Origin to a moving platform or basket while the player's head is inside its volume.
- **Events:**
  - `VehicleSeatEvent`, `VehicleCollisionEvent` (`HitAircraft`)
  - `CouplingEvent`, `AircraftTowEvent`, `TowShearPinEvent`
  - `DockingEvent`, `GroundPowerEvent`, `SprayEvent`

---

### Vehicle prefabs and spawning
`VehicleAssembler` builds each vehicle (or set: cart + drawbar, tug + tow bar, GPU + drawbar) at the origin and saves it as a prefab in `Generated/Vehicles/`, listed in a `VehicleCatalog` asset. The scene fleet is made of prefab instances; the only per-instance override is a cart's `startCoupledTo` hitch. The wrist tablet's `VehicleSpawnMenu` instantiates catalog entries in front of the player, after a `Physics.CheckBox` clearance test.

Assists live in `ComfortSettings.DrivingAssists` (PlayerPrefs). `VehicleController.StatusLine` is the single source of "why won't it move" text: drive inhibits carry a reason string from the equipment that set them.

### Gloved hands
`ControllerHands` sits on each controller. The glove model is a right hand; the left is the same model with a −1 X scale, so all posing happens in local space. Each joint's curl axis is the local position of its `Bend` helper node. The generator places it so that Unity's `AngleAxis(+angle, axis)` curls towards the palm. Input comes from Input System actions created in code (`<XRController>{Hand}/grip`, `trigger`, `triggerTouched`, `thumbstickTouched`...), so no action asset is needed. In `Awake` the XRI poke attach point is re-parented to the index fingertip, before `FingertipSetup` adds its presser.

### Airport
The airport is one generated `.glb` in Unity world coordinates. The training stand is the origin; the generator's `convert()` pre-mirrors X for glTFast. Nodes are grouped by purpose: `Ground_*`, `Markings_*`, `Lights_*`, `Building_*`, `JetBridge_n`, `Signs`, `Floodlight_Masts`, and `Stand_nn` / `Gate_n` spawn empties.

`AirportPlacer` reads `Airport.json` for the material list (sRGB colours, textures, smoothness, emission) and swaps glTFast's PBR materials for URP Simple Lit. It also adds colliders, teleport areas and static flags. Paint sits 1.2 cm (apron), 2 cm (taxiways) or 3 cm (runway) above its surface, so it doesn't z-fight at distance.

### Jet bridges
Each gate has a `JetBridge`: the moving parts come from `JetBridge.glb`, a chain of nodes with identity rest rotations. The fixed link and the rotunda are static, in `Airport.glb`. `Solve(gap)` turns a target into joint values in closed form:
- **Target:** the cab floor's front edge, `gap` m out from the door sill, facing the fuselage.
- **Slew:** `atan2` of the horizontal offset.
- **Pitch and telescope length:** from the vertical offset, allowing for the cab mount's height above the tunnel axis.
- **Cab angle:** relative to the tunnel.

Every joint moves towards its target at its own speed, so the bridge moves as one smooth motion.

**States:** Stowed → Docking (approach 1.2 m out, then final at 20 % speed, then canopy) → Docked (keeps re-solving: auto-level; retracts if the error exceeds 0.5 m) → Retracting.

### Operations
`StandRegistry` lists the stands and gates (written by the airport placer; Stand 24 alone without it). `TurnaroundOps` keeps a `StandOps` per stand: job states, placed items, workers, crew. Every job is a coroutine made of `CrewAgent` actions (walk a route, crouch, hold, put down, sit, enter or leave a vehicle) and `AutoDriver` moves.

**Routes:** explicit waypoints, no NavMesh. The apron changes all the time, and aircraft are the only fixed obstacles. Anything behind the engine line is reached along a corridor 9.5 m off the centre line.

**Equipment docking:**
1. Drive kinematically to a pose square to the sill, with the docking edge 0.9-1.0 m out.
2. Raise to the sill height.
3. Creep in to the final gap.
4. Extend the plate.

**Flight crew positions** come from `AircraftCrewLayout`: the pilot eye point and the bulkhead and galley stations of each type, in the aircraft's local frame.

### People
The figures are rigid segments on joint nodes (no skinning, no clips). `CrewAnimator` sets the joint rotations each frame from the pose and speed. It's cheap: about 15 transforms per person, no animator. Skin and hair vary per person through a `MaterialPropertyBlock`.

## 5. Turnaround system (Phase 3 — the heart of the game)

A turnaround is a **task graph**: a DAG of tasks with preconditions, built from a `TurnaroundTemplate` and adjusted by dynamic events.

```
TurnaroundSession
  flight (BA123), aircraftDefinition, gate, STA/STD, weather, seed
  clock (game minutes; real-time scaled)
  tasks: TaskState { id, status: Locked/Available/InProgress/Done/Failed, owner, startedAt, doneAt }
  violations[], score
```

Excerpt of the default narrowbody template (realistic order, simplified):

| Task | Preconditions | Completed when |
|---|---|---|
| Marshal in / park on stop mark | — | aircraft stopped within tolerance of the stop mark |
| Chocks | engines off **and** anti-collision beacon off | chocks in the nose-gear (or MLG) chock zones |
| Cones | chocks | cones in the cone zones (engines, wingtips, tail) |
| Connect GPU | chocks | `GroundPower400Hz` connected, GPU running → aircraft on ext power |
| Open cargo doors | chocks, cones | door handles at max |
| Unload bags | hold doors open | hold zones empty of inbound items |
| Disembark | stairs/bridge connected, door 1L open | all passengers off |
| Cleaning | disembark | cabin crew report |
| Refuel | chocks, **bonding cable connected**, panel open, fuel truck parking brake on | target fuel reached, hose disconnected, panel closed |
| Catering / water / waste | disembark | each service complete, equipment withdrawn |
| Load bags | unload done | hold zones contain the outbound manifest, under max mass, fragile/heavy rules respected |
| Boarding | cleaning, (fuelling-with-passengers rules) | all passengers seated |
| Close doors | all loading/boarding done | all doors closed, crew confirm |
| Remove equipment | doors closed | no GSE inside the restraint area except the tug |
| Pushback | everything above, clearance on radio | pushed to the release point, tug disconnected, bypass pin removed |

Rules (validators) subscribe to `EventBus` facts and to sim state. For example, "fuelling started without bonding" produces a violation. Interlocks feed back into the interaction layer (such as `TypedSocketInteractor.Locked` on the refuel coupling).

**Procedure references:** game procedures are *inspired by* real ramp practice and should be checked against public sources while designing each phase: IATA Ground Operations Manual (IGOM), IATA Airport Handling Manual (AHM), airport ramp safety rules and manufacturer airport-planning documents (Boeing "737 Airplane Characteristics for Airport Planning", Airbus "A320 Aircraft Characteristics – Airport and Maintenance Planning"). These planning documents publish dimensions, service-point locations and turnaround charts.

---

## 6. Multiplayer readiness (not implemented; designed for)

- **Authority:** the simulation layer (`TurnaroundSession`, `AircraftState`) runs on one authority. That is the local player in single-player and the host later.
- **Intents, not mutations:** interactions publish facts, and rules apply state changes. Later, a client sends the intent ("connect plug X to socket Y") and the authority validates and broadcasts the result.
- **Stable ids:** `StableId` on every relevant object, with deterministic ids for scene objects.
- **Physics ownership:** grabbing an item transfers ownership to the grabbing player (the usual NGO/Fusion pattern). Items at rest are owned by the host.
- **No static gameplay state** outside `Services`/`EventBus`. Both are reset on play-mode start and can be scoped per session later.
- **Roles:** baggage, fuel, catering, pushback and supervisor map naturally onto task ownership in the task graph.

---

## 7. Scenes and content structure

```
Bootstrap.unity          (systems, rig, persistent services)          Phase 3
Airport_<ICAO>.unity     (additive: static airport, baked lighting)    Phase 3+
Stand_<n> sub-scenes     (additive streaming of gates)                 Phase 8
Aircraft interiors       (loaded additively when entering)             Phase 2
Phase1_Sandbox.unity     (generated test scene)                        ◄ now
```

Project folder layout (inside a Unity project):

```
Assets/GroundCrew/
  Scripts/Runtime/  GroundCrew.Runtime.asmdef
    Core/           EventBus, Services, StableId
    Interaction/    grab, zones, handles, buttons, connectors, cables
    Player/         comfort, fingertips, performance
    UI/             wrist tablet
    Aircraft/       AircraftRig
    Vehicles/       driving, seats, steering wheel, coupling, pushback, catering lift
    (Simulation/, NPC/, Audio/, Career/ — later phases)
  Scripts/Editor/   GroundCrew.Editor.asmdef, scene builders, validators
  Data/             ScriptableObjects (items, aircraft, templates)
  Generated/        builder-generated materials/meshes (safe to delete)
  Scenes/
  Art/              imported models, textures (Phase 2+)
```

---

## 8. Quest 2 performance budget

Quest 2 has a Snapdragon XR2 (Adreno 650 GPU) and renders 1832×1920 per eye. The baseline target is **72 Hz (13.9 ms)**; 90 Hz is optional if a scene allows it.

| Budget | Target |
|---|---|
| Batches / draw calls per frame (multiview) | ≤ 150 typical, ≤ 200 worst case |
| Triangles in view | ≤ 750 k |
| Aircraft LOD0 | 60–90 k tris (generated models: ~85 k including cabin and flight deck; split the interior into a streamed asset if a scene needs the headroom) |
| Vehicles | 8–15 k tris LOD0, 3 LODs |
| Textures | ASTC 6×6 (4×4 for UI/text), 2 k atlases max, mipmaps on |
| Real-time lights | 1 directional main light; everything else baked or emissive (the aircraft's flight-deck dome light starts off) |
| Shadows | 1 cascade, hard, ≤ 25 m distance; baked shadows for static airport |
| Post-processing | Off (no bloom, no tonemapping cost) |
| MSAA | 4× (cheap on tiled GPU; much better than FXAA in VR) |
| HDR | Off; render scale 1.0 |
| Physics | fixed timestep = display rate (done in `QuestPerformanceSettings`), ≤ 60 active rigidbodies near the player, sleeping elsewhere |
| NPCs | ≤ 20 animated characters near the player, GPU skinning, animator culling, crowds as LOD impostors |
| Audio | ≤ 32 real voices, spatialised, distance-culled |

Techniques: static batching, GPU instancing for repeated props (cones, lights, signs), baked lightmaps plus light probes, baked occlusion culling, LOD groups with crossfade off, object pooling for passengers/bags/vehicles, fixed foveated rendering, and Simple Lit shaders for props.

Measure on the device, not in the editor: use OVR Metrics Tool on the headset, plus the Unity Profiler connected over USB/ADB.

---

## 9. Assets and IP

- **Trademarks:** "Boeing", "737", "Airbus" and "A320" are trademarks, and real airline liveries belong to the airlines. A shippable game needs licences, or must use generic designations and fictional airlines. Internally we use `NarrowbodyBoeing737Class` / `NarrowbodyA320Class`; the display names are data and can change without code.
- **Models:** Phase 2 needs a 737-class exterior + cabin + holds. Options, best first:
  1. Commission a 3D artist using the public airport-planning documents (dimensions are facts; the risk lies in trademarks and liveries, not geometry).
  2. Buy a model whose licence explicitly allows use in a commercial game (check for trademark/"editorial use only" clauses).
  3. Model it in Blender in-house against the contract in §4.

  Never use ripped flight-sim or game models.
- **Audio:** record or licence engine, APU, GPU and vehicle sounds (licensed SFX libraries or field recordings); radio voice lines need voice actors or licensed TTS.
- **What code can and cannot generate:** code can generate the test props in the Phase 1 sandbox. It cannot reasonably generate production aircraft, vehicles, characters or animations, which will be requested as assets at the start of each phase.
