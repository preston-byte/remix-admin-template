# Ground Crew VR

A first-person airport ground-operations simulator for **standalone Meta Quest 2**, built in Unity 6 + OpenXR + XR Interaction Toolkit 3.

- Architecture and design decisions: [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)
- What's done and what's next: [`docs/PROGRESS.md`](docs/PROGRESS.md)

Phase 1 gives you a playable VR training sandbox with these systems:
- locomotion: teleport, smooth walk, snap or smooth turn
- realistic grabbing: weight, two-handed heavy bags, no force-grab
- physical buttons, levers and hinged doors
- typed cable connectors behind access-panel interlocks
- placement zones with weight limits
- a wrist tablet that shows what's happening

Later phases add the aircraft (§6), ground vehicles (§7), gloved hands (§8) and the airport (§9).

> **Status:** the code was written and type-checked outside Unity (no Unity editor was available while writing it). The first open in Unity is the real compile test. If the console shows errors, copy them back and they'll be fixed. The most likely cause is a small API difference in your exact XR Interaction Toolkit version.

---

## 1. What's in this folder

```
GroundCrewVR/
  README.md                ← this guide
  docs/ARCHITECTURE.md     ← engine choice, layers, systems, budgets, IP
  docs/PROGRESS.md         ← running list of completed / remaining systems
  .gitignore               ← Unity ignore rules (for when this becomes its own repo)
  Assets/GroundCrew/       ← copy this folder into your Unity project's Assets/
    Scripts/Runtime/       ← game code (assembly GroundCrew.Runtime)
      Core/                EventBus, Services, StableId
      Interaction/         GrabbableItem, ItemDefinition, NearOnlyInteraction, PlacementZone,
                           RotaryHandle, PressButton, FingertipPresser, ConnectorPlug,
                           TypedSocketInteractor, PanelInterlock, CableRenderer,
                           IndicatorLight, ValueReadout, Haptics, InteractionEvents
      Player/              ComfortSettings, LocomotionComfortApplier, FingertipSetup,
                           QuestPerformanceSettings, ControllerHands (gloved hands)
      UI/                  WristTablet, VehicleSpawnMenu (the tablet's VEHICLES page), OpsTablet (iPad)
      Airport/             JetBridge, StandRegistry
      Crew/                CrewAgent, CrewAnimator, CrewAppearance (pilots, cabin crew, ground crew)
      Operations/          TurnaroundOps (AI jobs + crew boarding), AutoDriver, OpsConfig,
                           AircraftCrewLayout
      Aircraft/            AircraftRig (doors, flaps, steering, wheels, fans, beacon, LODs)
      Vehicles/            VehicleController, VehicleSeat, VehicleDriverInput, SteeringWheelInput,
                           VehicleLights, VehicleAudio, TowHitch, TowCoupler, TrailerSteering,
                           TowBar, AircraftTowable, CateringLift, StairsMechanism, HydraulicAxis,
                           ConveyorBelt, DoorDockingSensor, GroundPowerUnit, BasketLeveller,
                           SprayNozzle, RideablePlatform, VehicleEvents, VehicleCatalog,
                           VehicleDashDisplay
    Scripts/Editor/        ← editor tools (assembly GroundCrew.Editor)
      Phase1SandboxBuilder.cs   menu: Ground Crew > Build Phase 1 Sandbox Scene
      AircraftPlacer.cs         menu: Ground Crew > Place Aircraft on Stand 24
      VehicleAssembler.cs       menu: Ground Crew > Vehicles > Add Vehicles to Sandbox
      HandsSetup.cs             menu: Ground Crew > Player > Add Gloved Hands to Rig
      AirportPlacer.cs          menu: Ground Crew > Airport > Build Airport Around Sandbox
      OperationsSetup.cs        menu: Ground Crew > Operations > Set Up iPad, Crew and AI Ground Crew
      QuestMaterials.cs         glTF -> URP Simple Lit material swap (airport, bridges, people)
      ProceduralMeshes.cs       cone + wheel-chock meshes for the sandbox
    Art/Aircraft/          ← generated .glb aircraft + JSON sidecars
    Art/Vehicles/          ← generated .glb ground vehicles + JSON sidecars
    Art/Hands/             ← generated glove model
    Art/Airport/           ← generated airport, jet bridge, ground textures
    Art/Characters/        ← generated pilot, cabin crew and ground crew figures
    Shaders/               ← GroundPaint (markings drawn with a depth offset)
  Tools/AircraftGenerator/ ← Python generator for the aircraft models
  Tools/VehicleGenerator/  ← Python generator for the vehicles (reuses the aircraft tools)
  Tools/HandGenerator/     ← Python generator for the glove
  Tools/AirportGenerator/  ← Python generator for the airport and the jet bridge
  Tools/CharacterGenerator/← Python generator for the people
```

Unity generates the `.meta` files, `Data/`, `Generated/` and `Scenes/` folders when you run the builder.

---

## 2. One-time setup

### 2.1 Install
1. Install **Unity Hub**.
2. Install **Unity 6 LTS** (6000.0 LTS or newer LTS) with the modules **Android Build Support → OpenJDK** and **Android SDK & NDK Tools**.

### 2.2 Create the project
1. Unity Hub → **New project** → template **VR** (Unity's URP-based VR template). Name it `GroundCrewVR`.
   This template already contains URP, OpenXR, XR Interaction Toolkit 3, the Input System and the XRI **Starter Assets** rig.
2. Open it once and let it finish importing.
3. **Window → Package Manager → XR Interaction Toolkit**: check that the version is 3.x and that **Samples → Starter Assets** is imported. If it isn't, click Import.
4. **Window → TextMeshPro → Import TMP Essential Resources** (if Unity hasn't already asked).

### 2.3 Add the game code
Copy `GroundCrewVR/Assets/GroundCrew` from this repository into your project, so that you have `<YourProject>/Assets/GroundCrew/Scripts/...`. Wait for Unity to compile, and check that the **Console** shows no red errors.

### 2.4 Quest build settings
1. **File → Build Profiles → Android → Switch Platform**.
2. **Edit → Project Settings → XR Plug-in Management → Android tab**: tick **OpenXR**, then under **OpenXR**:
   - Feature groups: tick **Meta Quest Support**.
   - Interaction profiles: add **Oculus Touch Controller Profile**.
3. **XR Plug-in Management → Project Validation**: click **Fix All** until it's clean. This applies Meta's current Android requirements (API levels, ARM64, IL2CPP, Vulkan, etc.).
4. **Player → Android → Other Settings** (confirm):
   - Color space **Linear**
   - Graphics APIs **Vulkan** first
   - Scripting backend **IL2CPP**
   - Target architectures **ARM64** only
   - Texture compression **ASTC**
5. **URP asset** (the one used for Android in **Quality** settings; the VR template already uses a mobile-tuned one):
   - Render scale 1.0, **MSAA 4x**, **HDR off**
   - Main light shadows on, 1 cascade, max distance 20–25 m, soft shadows off
   - Post-processing off; additional lights per-vertex or off

### 2.5 Build the sandbox scene
Menu **Ground Crew → Build Phase 1 Sandbox Scene**.

This creates `Assets/GroundCrew/Scenes/Phase1_Sandbox.unity` and adds it as the first scene in the build. You can rerun it at any time to regenerate the scene.

---

## 3. Running it on the Quest 2

1. **Developer mode:** create (or join) a developer organisation at the Meta developer site, then in the **Meta Horizon** phone app → your headset → *Headset settings* → **Developer Mode: On**. Reboot the headset.
2. Connect the Quest to the PC with a USB-C data cable. Put the headset on and accept **Allow USB debugging** (tick "always allow").
3. In Unity: **File → Build Profiles → Android → Run Device**. Select your Quest 2 (press Refresh if needed), then **Build And Run**.
4. The game launches automatically. Later you'll find it in the headset under **Library → Unknown Sources**.

**Faster iteration without building:**
- *Windows + Quest Link / Air Link:* enable OpenXR on the **Windows** tab of XR Plug-in Management (with the Meta Quest Touch profile), set the Meta Horizon Link app as the active OpenXR runtime, then press **Play** in the editor.
- *No headset at hand:* import the **XR Device Simulator** sample from the XR Interaction Toolkit package, drop its prefab into the scene and press Play. Use it for logic only; always check feel and performance on the device.

---

## 4. Controls

| Action | Control |
|---|---|
| Walk (smooth) | Left thumbstick |
| Teleport | Right thumbstick forward → aim → release |
| Turn | Right thumbstick left/right (snap 30/45/90° or smooth) |
| Grab / hold | Grip button (hand must be close; no long-range grabbing) |
| Heavy bag | Grip with **both** hands |
| Point / press a button | Squeeze **grip** without touching the trigger: your index finger points. Press buttons with its tip |
| Open a door/panel/lever | Grip the handle and move your hand the way it should swing |
| Wrist tablet | Turn your left wrist to look at it like a watch |
| Change comfort options | Poke **MOVE** / **TURN** on the tablet with your right fingertip |
| Spawn / reset vehicles | Poke **VEHICLES** on the tablet (see §7) |
| Ops iPad | Floats at your waist, front-left. Press its keys with a fingertip, or grab it (grip) to hold it closer; let go and it goes back (see §10) |

Comfort settings are saved between sessions. **Teleport only** mode turns off smooth walking for anyone who gets motion sick.

---

## 5. Test checklist (Phase 1 acceptance)

Start the scene. You spawn facing station **C**: **A** is front-left, **B** front-right, **D** to your left, **E** to your right.

**Locomotion**
- [ ] Left stick walks, right stick forward shows the teleport arc, and releasing teleports you.
- [ ] Right stick sideways snap-turns; TURN on the tablet cycles 30° → 45° → 90° → smooth.
- [ ] MOVE on the tablet switches to teleport-only (the left stick then teleports instead of walking).
- [ ] The setting persists after quitting and relaunching.

**Wrist tablet**
- [ ] Turning your left wrist toward you fades the tablet in; looking away fades it out.
- [ ] It shows the time, what you're holding, recent events, the FPS (green ≥ 70) and the comfort mode.

**A — Baggage**
- [ ] The cabin bag (8 kg) follows the hand quickly; the 18 kg suitcase visibly lags.
- [ ] The 32 kg heavy suitcase barely moves with one hand (the tablet says "use both hands") and moves normally with two.
- [ ] You can't grab bags with the ray from a distance.
- [ ] Bags collide with the cart and the hold walls; they don't pass through.
- [ ] Bags placed in the TEST HOLD show its weight. Loading all four (64 kg) turns the label red and the tablet reports **OVER LIMIT**.
- [ ] Throwing the fragile box hard reports damage on the tablet.

**B — Safety**
- [ ] Cones and chocks can be picked up and placed.
- [ ] A chock against the front or back of the tyre reports "Wheel Chock in Chock FWD/AFT".
- [ ] A cone in a chock position reports "doesn't belong".

**C — Controls**
- [ ] Touching the green button toggles the amber beacon (it blinks when on) and the button stays latched down.
- [ ] Grabbing the red lever knob moves it ±35°, the readout shows -100…100%, and it springs back to 0 when released.
- [ ] You feel detent ticks in the controller while moving the lever.

**D — Cargo door**
- [ ] Grab the door handle and lift; the door swings outward and up. Release it past halfway and the struts finish opening it.
- [ ] Pull it back down to close it. The tablet logs "Cargo door open/closed".
- [ ] Bags thrown or placed through the opening count in **BULK HOLD**.

**E — Ground power**
- [ ] The plug can't connect while the access panel is closed.
- [ ] Open the panel, push the plug into the receptacle and release: it snaps in, the EXT PWR lamp turns green, and the tablet logs "GPU cable connected".
- [ ] The panel can't be closed while the cable is plugged in.
- [ ] Carrying the plug more than 4 m from the GPU yanks it out of your hand.
- [ ] Pull the plug out, close the panel.

**Performance**
- [ ] The tablet FPS stays at ~72 (green) while looking around every station.

---

## 6. Aircraft models (Phase 2 preview)

Generated 737-class and A320-class aircraft are in `Assets/GroundCrew/Art/Aircraft/`. They come from `Tools/AircraftGenerator/`; see its README for what each model contains. You can preview them in a browser before trying them in Unity.

**To use them in Unity:**

1. **Window → Package Manager → + → Add package by name:** `com.unity.cloud.gltfast`. This is Unity's official glTF importer; without it Unity can't read `.glb` files.
2. Once it's installed, the two `.glb` files import as models. Reimport the `Aircraft` folder if they don't.
3. Build the sandbox scene, then use **Ground Crew → Place Aircraft on Stand 24 → 737-class** (or **A320-class**). This:
   - parks the aircraft on the stop bar
   - adds `AircraftRig`
   - adds static colliders to the airframe
   - adds a pedestal by the nose with **DOORS**, **BEACON**, **FLAPS** and **CKPT LT** (flight-deck light) buttons
4. Save the scene, then **Build And Run**.

**On the Quest, check that:**

- [ ] The aircraft stands on its wheels at the stop bar, and you can walk under the wings and around the engines.
- [ ] **DOORS** opens and closes every door. Pax doors swing forward. The 737 cargo doors go *in* and up; the A320's go *out* and up.
- [ ] Through an open door you see the cabin (seats, bins) or the hold.
- [ ] **BEACON** flashes the red anti-collision lights on the top and belly.
- [ ] **FLAPS** lowers the flaps to 15°.
- [ ] From the ramp you can see into the flight deck through the windscreen (the glass is transparent on the close-up LOD only). **CKPT LT** switches the dome light (it starts off); if nothing changes, glTFast didn't import the light (check its import settings for *Lights*).
- [ ] Through the forward door (**DOORS**, then the flight-deck door): the 737 has yokes and big striped trim wheels; the A320 has sidesticks, tables and no yokes. The screens glow.
- [ ] Frame rate stays at ~72 fps (wrist tablet) while you look at the whole aircraft.

`AircraftRig` gives gameplay code these methods:
- `SetOpen01("Door_1L", 1)`
- `SetFlaps(t)`
- `SetSteering(deg)`
- `RollWheels(metres)`
- `FanRpm`
- `SetBeacon(bool)`
- `SetFlightDeckLight(bool)`

Phase 2 builds the real interactions on top of these: door handles you grab, holds as placement zones, service points as sockets.

---

## 7. Ground vehicles (Phase 4)

Generated models are in `Assets/GroundCrew/Art/Vehicles/` (from `Tools/VehicleGenerator/`):

| Vehicle | What you can do |
|---|---|
| Baggage tractor + 3 carts | Drive; couple / uncouple carts by hand |
| Pushback tug + tow bar | Latch the bar to the nose gear; push back or tow the aircraft |
| Passenger stairs | Drive to a door; raise the platform 2.2-4.1 m; slide the bridge plate out; walk up and down |
| Belt loader | Drive to a hold door; tilt the belt up to the sill (to ~3.5 m); run the conveyor in or out with bags on it |
| Ground power unit | Tow it with the tractor; start the diesel, switch the 115 V 400 Hz output on |
| Fuel truck (refueller) | Drive (refuelling itself is Phase 6) |
| Catering truck (high-loader) | Raise the box to the door (to 5.6 m) riding on the platform; slide the platform out |
| Potable water / lavatory trucks | Drive; raise the rear service platform with you on it (hoses are Phase 6) |
| De-icing truck | Ride the basket: slew, lift and extend the boom (reaches the top of the fin), spray orange fluid |
| Airside van | Drive (maintenance / staff transport) |

**To use them in Unity** (glTFast must be installed, see §6):

1. Build the sandbox scene and place an aircraft (§6).
2. **Ground Crew → Vehicles → Add Vehicles to Sandbox.** This parks the whole fleet around Stand 24, including:
   - a baggage tractor towing three coupled baggage carts (left of the stand)
   - the pushback tug with its tow bar on the front pin (right of the stand)
   - the passenger stairs (facing the aircraft's left side, door 1L) and the belt loader (facing the right side, forward hold)
   - the ground power unit, fuel, catering, water and lavatory trucks, the de-icer and the van further out
3. Save the scene, then **Build And Run**.

**Spawning vehicles from your wrist.** Look at the wrist tablet and poke **VEHICLES**. The page lists every vehicle:

| Button | Does |
|---|---|
| **PREV / NEXT** | Pick a vehicle |
| **SPAWN** | Put it on the ground in front of you, side-on, with the driver's door towards you. If something is in the way it tries further out; if there's no room it says so |
| **FIX** | Puts the nearest vehicle (within 15 m) back on its wheels, stopped, parking brake on. Use it if one tips over or gets wedged |
| **CLEAR** | Removes the vehicles you spawned (the parked fleet stays) |
| **ASSIST** | Driving assists on / off (see below) |
| **BACK** | Back to the normal tablet page |

Each vehicle is also a prefab in `Assets/GroundCrew/Generated/Vehicles/`, so you can drag extra ones into the scene in the editor.

**Driving assists** (on by default, saved between sessions). With assists on, pressing the throttle starts the engine, selects **D** from neutral and releases the parking brake; getting out sets the parking brake; using a lift or platform sets the parking brake for you. Turn them off on the VEHICLES page to do every step yourself.

**Dash display.** A small green display above the steering wheel shows the gear and speed, or exactly why the vehicle won't move: *ENGINE OFF*, *PARKING BRAKE ON*, *N - right stick up = D*, or the equipment that's still deployed (*Bridge plate out*, *Box raised*, *Belt lift not stowed*...).

**Driving** (also printed on `VehicleDriverInput`):

| Action | Controller | Keyboard (editor testing) |
|---|---|---|
| Get in | Grab (grip) the yellow **GRAB TO DRIVE** handle by the driver's side | |
| Get out | **Y** | F |
| Engine start / stop | **X**, or the green **ENGINE** button on the dash | E |
| Drive / Reverse | Right stick up / down, or the **D** / **R** buttons | T / R |
| Neutral | Right stick click, or **N** | N |
| Accelerate / brake | Right trigger / left trigger | W / S |
| Steer | Grab the steering wheel (one or both hands) and turn it; or the left stick | A / D |
| Parking brake | **B**, or the red **PARK** button | P |
| Horn | **A** (hold), or the centre of the steering wheel | H |
| Lights | **LIGHTS** button on the dash | |
| Re-centre your view in the seat | Left stick click | C |

Vehicles start parked with the engine off. Shifting between D and R needs a near standstill. Airside speed is governed to 25 km/h, 10 km/h for a tug with an aircraft on the bar. A reverse alarm beeps whenever reverse is selected, and the amber beacon flashes while the engine runs. The engine, horn and reverse alarm are synthesised, so no sound files are needed yet.

**Baggage carts:** each cart's front wheels steer with its drawbar. To uncouple a cart, hold its drawbar handle (grip) and pull the trigger. To couple one, carry the drawbar eye over a free hitch (tractor or the cart in front) and let go. An uncoupled cart brakes itself.

**Pushback:**
1. Drive the tug in front of the aircraft's nose, square to it, with the tow bar pointing at the nose gear.
2. Park, get out, and grab the **TOW BAR HEAD** handle (grip).
3. Carry the head to the nose-gear tow fitting and let go: it latches.
4. Get back in, select **D** (the tug faces the aircraft, so driving forward pushes it back) and push gently, steering the tug to swing the tail.
5. Turn past the towing limit (737 78°, A320 95°) and the shear pin breaks: the bar lets go.
6. To unlatch the bar, hold the head handle and pull the trigger.

Pushing a castoring nose gear is unstable, as in real life: keep correcting with small steering inputs. Pulling forward is stable.

**Docking at a door:**
- The stairs, the catering platform and the belt loader each have a green **DOCK** lamp.
- It lights when the platform edge or belt end is lined up with a door sill: just below it (a passenger door swings out over the platform), no more than 25 cm out from the fuselage, and centred on the door.
- Touching the fuselage reports **Contact**.
- Every change is published as a `DockingEvent`, which the turnaround rules will score in Phase 3.

**Passenger stairs:**
1. Drive up square to the door, slowly, until the front of the platform is about half a metre from the fuselage. The platform overhangs the cab, so it reaches the door before the cab touches the aircraft.
2. Set the parking brake (with assists on, the controls set it for you).
3. Hold **STAIRS UP / STAIRS DN** on the dash, or **UP / DOWN** on the platform column, until the **DOCK** lamp lights.
4. Slide **PLATE OUT** (up to 0.8 m) to bridge the gap.

Raising the platform steepens the flight; its foot rolls on rails and the treads stay level. The truck won't drive with the plate out.

**Belt loader:**
1. Nose the belt end up to a hold door.
2. Set the parking brake and hold **BELT UP / BELT DN** until the **DOCK** lamp lights.
3. With the engine running, press **BELT IN** to carry bags up into the hold, **BELT OUT** to bring them down, **STOP** to stop.

Bags you're holding are left alone.

**Water / lavatory trucks:** park, step onto the rear platform and hold **UP / DOWN** to reach the service panel.

**De-icer:**
1. Lower the boom (**BOOM DN**) until the basket is near the ground, and step in.
2. Use **SWING + / −**, **BOOM UP / DN** and **OUT / IN**; the basket stays level and carries you.
3. **SPRAY** toggles the fluid while the engine runs (6000 l).

The truck can't be driven unless the boom is stowed.

**Catering truck:** park (parking brake on), climb onto the front platform, then hold **LIFT UP** / **LIFT DOWN** with a fingertip. The box rises to a 5.6 m floor height and the platform carries you with it. **PLATFORM OUT / IN** slides the bridge plate towards the door. The truck can't be driven while the box is up or the platform is out.

**On the Quest, check that:**
- [ ] Grabbing **GRAB TO DRIVE** seats you at the wheel facing forward; **Y** puts you down beside the door.
- [ ] Each vehicle starts, drives, brakes, reverses (with the beeper) and parks; the parking brake holds it.
- [ ] Turning the steering wheel with one or two hands steers; the left stick also works.
- [ ] The tractor pulls all three carts round a corner without them jack-knifing or jittering.
- [ ] A cart uncouples and recouples.
- [ ] The tow bar latches to the nose gear; pushing moves the aircraft back, and steering swings the tail.
- [ ] Over-steering the nose gear breaks the shear pin.
- [ ] The catering box rises and lowers with you on the platform.
- [ ] Stairs: the platform rises and falls and the treads stay level; you can walk up to the platform; the **DOCK** lamp lights at door 1L (737 sill 2.65 m, A320 3.42 m).
- [ ] Belt loader: the belt tilts; bags dropped on it ride up into the forward hold; the **DOCK** lamp lights at the hold sill.
- [ ] GPU: towable behind the tractor; **ENGINE** then **OUTPUT** light the green and amber lamps.
- [ ] De-icer: the basket stays level through the whole boom range; the spray is visible.
- [ ] Driving feels comfortable. If it doesn't, say so: comfort options for driving (vignette, reduced acceleration) come next.
- [ ] Frame rate stays at ~72 fps with all vehicles and the aircraft in view.
- [ ] Every vehicle moves when you press the throttle (assists on), and the dash display explains it when it doesn't (assists off).
- [ ] VEHICLES → SPAWN puts each vehicle in front of you; FIX rights a tipped one; CLEAR removes spawned ones.
- [ ] The stairs' bridge plate reaches door 1L of both aircraft with the **DOCK** lamp lit.

---

## 8. Gloved hands

The controller models are replaced by work gloves (generated in `Tools/HandGenerator/`). The fingers follow what yours are doing on the Touch controller:

| On the controller | The glove |
|---|---|
| Grip | Middle, ring and little fingers close |
| Trigger | Index finger curls; resting on the trigger half-curls it |
| Grip, finger off the trigger | Pointing: use the index fingertip to press buttons |
| Thumb on the stick or A/B (X/Y) | Thumb rests; lift it for a thumbs-up |

Buttons are pressed by the tip of the glove's index finger (the XRI poke point is moved there).

**Setting up:** the sandbox builder adds the gloves automatically. For a scene you already have, use **Ground Crew → Player → Add Gloved Hands to Rig** and save the scene. If a glove sits wrong on your hand, move the `Glove Hand` child of *Left Controller* / *Right Controller*.

This is controller-driven hand posing. Quest hand tracking (no controllers) needs the XR Hands package and isn't set up; driving and the physical buttons are designed around controllers.

- [ ] Both gloves appear where the controllers were, and the controller models are hidden.
- [ ] Grip makes a fist; grip without the trigger points; the trigger curls the index finger.
- [ ] Physical buttons (stations, dash, tablet) press with the index fingertip.

---

## 9. The airport

`Tools/AirportGenerator/` builds a whole regional airport around the training stand (about 38k triangles):

- **Remote apron:** Stands 21-26. The training area is **Stand 24**. Lead-in lines, stop bars, stand numbers, equipment restraint lines, GSE parking boxes, stand ID boards and floodlight masts.
- **Terminal:** 390 m long, glass curtain wall, baggage-hall openings, gate numbers, six contact gates with jet bridges (rotunda, telescopic tunnel, drive column, cab at door 1L height), landside canopy, road and car park.
- **Airside service road** between the terminal apron and the remote apron, with aircraft-crossing stop lines and a zebra crossing.
- **Airfield:** taxiway A with exits B1-B3, runway connectors A1-A4 with holding-position markings, stop-bar lights and mandatory / location / direction signs. Runway 09/27 is 2400 x 45 m with threshold, designator, touchdown-zone, aiming-point and centre-line markings, edge, threshold and end lights, blast pads, approach lights and windsocks.
- **Buildings:** control tower, maintenance hangar, cargo shed, fuel farm, fire station.

**To use it in Unity** (glTFast must be installed, see §6):
1. Build the sandbox scene (§2.5). Place an aircraft (§6) and add the vehicles (§7) before or after; the order doesn't matter.
2. **Ground Crew → Airport → Build Airport Around Sandbox.** This:
   - places the airport
   - converts its materials to URP Simple Lit (cheaper on Quest), with tiling concrete, asphalt and grass textures
   - adds colliders and teleport areas
   - hides the sandbox's own 100 m apron and stand markings
   - sets the camera's far clip to 3 km with light fog
   - parks aircraft at gates 2, 4 and 5 and at Stand 22
3. Save the scene, then **Build And Run**.

If the frame rate drops, **Ground Crew → Airport → Remove Parked Aircraft (faster)** removes the four extra aircraft. **Park Aircraft at Gates** puts them back.

- [ ] You start at Stand 24 on the remote apron. The terminal is behind you (south) and the runway is beyond the taxiway (north).
- [ ] You can walk and teleport on all paved areas and the grass, and drive vehicles anywhere on them.
- [ ] Signs and markings are readable and don't flicker. (Jet bridges: see §10.)
- [ ] The frame rate stays at ~72 fps looking towards the terminal with the parked aircraft in view.

---

## 10. Ops iPad, jet bridges, flight crew and AI ground crew

**The iPad** floats at your waist, front-left, tilted towards you. Press its keys with your index fingertip; grab it if you want it closer; let go and it returns. The tabs along the top:

| Tab | What's on it |
|---|---|
| **STAND** | Which stand or gate you're working (**< PREV / NEXT >**, **NEAREST**, **GO THERE** teleports you to it), the aircraft and its doors, the bridge, the crew and the ground jobs at a glance. **SETUP ALL**, **CLEAR ALL**, **CALL CREW**, **CREW OFF** |
| **BRIDGE** | **DOCK**, **RETRACT**, **STOP**, **DOOR 1L**, and what the bridge is doing |
| **CREW** | **CALL CREW** / **CREW OFF**, **BOARD PAX** / **DEPLANE**, and where everyone is |
| **GROUND** | **CHOCKS**, **CONES**, **STAIRS** (or **BRIDGE** at a gate), **BELT**, **LOAD BAGS** / **UNLOAD BAGS**, **GPU**. A finished job's key becomes **REMOVE ...**. Plus **SETUP ALL**, **CLEAR ALL**, and who's doing what |
| **SERVICE** | **FUEL**, **WATER**, **LAV**, **CATERING**, **ALL SERVICES**, **GPU**, with fuel (kg), water and waste (%), catering and power |
| **PLANE** | Doors 1L / 2L, forward / aft hold, all doors, beacon, flaps, flight-deck light; power, fuel, bags and passengers on board |

The bottom line shows the latest news ("Gate 3: Docked at door 1L", "Stand 24: Wheels chocked").

**Jet bridges** (the six contact gates). **DOCK**, from the iPad or the yellow button on the console in the bridge cab:
1. The bridge finds door 1L of the aircraft on its gate.
2. It works out the angles: slew round the rotunda, slope, telescope length, cab angle.
3. It drives there: the drive column's wheels steer and roll, and its legs stretch to the ground.
4. It stops 1.2 m short, creeps in to the sill, and closes the canopy.
5. It opens the door.

While docked it follows the sill (auto-level). If the aircraft moves it retracts by itself. **RETRACT** (or the red button) closes the door, backs off, and parks the bridge. You can walk through the bridge from the terminal end to the aircraft, or teleport onto its floor. A barrier stops you walking out of the cab when it isn't docked.

**Flight crew.** **CALL CREW** brings a captain, a first officer, a purser and three cabin crew:
1. **At a gate,** they come out of the terminal through the bridge, which docks first if it isn't already.
2. **On a remote stand,** they walk from the crew drop-off and up the passenger stairs. If the stairs aren't at the door yet, the ground crew bring them first.
3. Inside, the pilots sit in their seats and the flight-deck light comes on. The purser and one crew member stay at the forward door; two go to the aft galley.

**CREW OFF** walks them all back out.

**AI ground crew.** Give them a job on the GROUND page; up to three workers per stand appear from the equipment area (or the baggage hall door at a gate):

| Job | What they do |
|---|---|
| Chocks | Fetch chocks two at a time and wedge the nose gear and both main gears, fore and aft |
| Cones | Put cones at the nose, wing tips, engines and tail; they walk round outboard of the engines, never under the belly |
| Stairs | Take the nearest free passenger stairs, drive to door 1L, raise the platform to the sill, creep in, slide the plate out, walk up and open the door |
| Belt | Same with the belt loader at the forward hold; then open the hold door |
| Bridge | Dock the jet bridge (gates) |
| GPU | Tow the ground power unit by hand to the nose, run the cable to the receptacle, start it and switch the output on: the aircraft is on ground power (nav lights on) |
| Load bags | A cart of 16 bags is parked at the foot of the belt loader. One worker lifts bags onto the running belt; the other kneels in the forward hold and stows them in rows from the far end. **UNLOAD BAGS** does it the other way |
| Fuel | Drive the refueller ahead of the right wing, carry the hose to the underwing coupling, refuel to the planned load (the iPad counts the kg), stay on the deadman, disconnect, drive back |
| Water / Lav | Back the truck up to the panel under the aft fuselage, ride the platform up, connect the hose, fill the water tank or drain and flush the waste tank |
| Catering | Drive to door 2R, ride the box up, extend the platform, open the door, exchange the trolleys, close up and drive back |

Water, lavatory and catering all work under the aft fuselage on the right, so they take turns. Fuel, water and waste flow 4x faster than real life (`serviceTimeScale` on `Generated/Operations/OpsConfig`) so you don't wait 15 minutes.

**Passengers:** **BOARD PAX** brings 24 passengers (`passengersPerFlight`).
- They come from the terminal through the bridge, or from the drop-off up the stairs.
- The back rows board first, and each walks down the aisle to a real seat from the aircraft's seat map.
- Once seated, each becomes a single pre-posed mesh, so a full cabin stays cheap to render on Quest.
- **DEPLANE**: they stand up and walk off, front rows first.

**REMOVE** / **CLEAR ALL** undoes them:
- Chocks and cones are collected (not the ones you're holding).
- The door is closed and the stairs or belt loader is lowered, backed off and parked.
- The bridge retracts.

If you've already docked the stairs yourself, the AI uses them as they are. If you sit in a vehicle the AI is driving, it hands it over to you. The chocks and cones they place are normal items: you can pick them up and move them.

**Setting up:** it's automatic. **Build Phase 1 Sandbox Scene**, **Add Vehicles** and **Build Airport** all add the iPad and the operations manager to the scene. Without the airport, there is just Stand 24. If you ever need it by hand: **Ground Crew → Operations → Set Up iPad, Crew and AI Ground Crew**, then save the scene.

**On the Quest, check that:**
- [ ] The iPad sits at your waist and follows you; its keys press with a fingertip; grab and release works.
- [ ] Gate 2, 4 or 5 (aircraft parked): **DOCK** drives the bridge to door 1L, the canopy closes, the door opens; **RETRACT** reverses it.
- [ ] You can walk from the rotunda through the tunnel into the aircraft.
- [ ] Stand 24 with an aircraft: **SETUP ALL** gets chocks, cones, the stairs at 1L (door opened) and the belt loader at the forward hold.
- [ ] **CALL CREW** at Stand 24 (by the stairs) and at a gate (by the bridge): six people board; the pilots sit in the flight deck.
- [ ] **CLEAR ALL** and **CREW OFF** tidy everything away.
- [ ] **GPU**: the GPU is towed to the nose, the cable reaches the receptacle, the GPU's lamps light and the nav lights come on.
- [ ] **LOAD BAGS**: bags ride the belt into the hold and are stacked inside; **UNLOAD BAGS** brings them back to the cart.
- [ ] **FUEL**, **WATER**, **LAV**, **CATERING**: each truck parks at its point without driving under the wing, the hose reaches, and the SERVICE page counts up.
- [ ] **BOARD PAX** / **DEPLANE**: passengers sit in seats (not floating, not in the aisle) and leave again.
- [ ] Frame rate stays at ~72 fps with the crew and workers moving.

---

## 11. Updating to a new version

1. In Unity, close the scene (or quit Unity).
2. In Finder, replace `Assets/GroundCrew` in your project with the new one from the zip. Copy it in Finder (**Reveal in Finder**), not by dragging into the Unity window.
3. Open Unity and wait for it to import and compile.
4. Run the menus again, in this order:
   1. **Build Phase 1 Sandbox Scene**
   2. **Place Aircraft on Stand 24**
   3. **Vehicles → Add Vehicles to Sandbox**
   4. **Airport → Build Airport Around Sandbox**

   The iPad, the crew and the AI ground crew are added by these menus automatically.
5. Save the scene, then **Build And Run**.

---

## 12. Troubleshooting

| Symptom | Fix |
|---|---|
| Pink materials | The project isn't on URP. Create it from the **VR** template, or assign a URP asset in Graphics/Quality settings, then rerun the builder. |
| No text anywhere | Import TMP Essential Resources, then rerun the builder. |
| Builder says Starter Assets missing | Package Manager → XR Interaction Toolkit → Samples → Starter Assets → Import. |
| Console: "ControllerInputActionManager … not found" | Your XRI version renamed the Starter Assets comfort properties. Teleport/move still work with XRI defaults; report the XRI version. |
| Buttons can't be pressed | The console should show a FingertipSetup warning; the rig needs XRI poke interactors (Starter Assets rig has them). |
| Tablet sits in the wrong place on your wrist | Select `Wrist Tablet` under *Left Controller* in the scene and adjust its local position/rotation. |
| Held objects stutter | Make sure the `Systems` object (QuestPerformanceSettings) is in the scene; it matches physics to the display rate. |
| Aircraft menu says "model not imported" | Install glTFast (see §6) and reimport `Assets/GroundCrew/Art/Aircraft`. |
| Aircraft looks too shiny / costs too much on Quest | glTFast imports glTF PBR materials. For Quest, switch them to URP *Simple Lit*, keeping colour and emission. |
| Compile errors mentioning XRI members | Copy the exact errors and your XRI version from Package Manager; they'll be adapted. |
| A vehicle won't move | Read the dash display: it says why (engine, parking brake, gear, or equipment still deployed). With assists on, the throttle handles the first three. If it's stuck on something, use **FIX** on the tablet's VEHICLES page. |
| VEHICLES page says "No vehicle catalog" | Run **Ground Crew → Vehicles → Add Vehicles to Sandbox** once; it builds the prefabs and the catalog. |
| Controllers still visible / no gloves | Run **Ground Crew → Player → Add Gloved Hands to Rig** (glTFast must have imported `Art/Hands/Hand_R.glb`). |
| Airport menu says "model not imported" | Install glTFast (§6) and reimport `Assets/GroundCrew/Art/Airport`. |
| iPad says "No operations manager" | Run **Ground Crew → Operations → Set Up iPad, Crew and AI Ground Crew** and save the scene. |
| No crew / workers appear | glTFast must have imported `Art/Characters/*.glb`; then rerun the Operations setup menu. |
| Bridge says "out of reach" | The aircraft isn't on the gate's stop bar (towed away?). Park it at the stop bar (nose gear on the line). |
| Ground shimmers in the distance | Expected on Quest at grazing angles; anisotropic filtering is set on the ground textures. Raise MSAA to 4x if it's lower. |
