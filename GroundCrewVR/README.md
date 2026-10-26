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

There is no aircraft yet; that's Phase 2.

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
      Core/                EventBus, Services, EntityId
      Interaction/         GrabbableItem, ItemDefinition, NearOnlyInteraction, PlacementZone,
                           RotaryHandle, PressButton, FingertipPresser, ConnectorPlug,
                           TypedSocketInteractor, PanelInterlock, CableRenderer,
                           IndicatorLight, ValueReadout, Haptics, InteractionEvents
      Player/              ComfortSettings, LocomotionComfortApplier, FingertipSetup,
                           QuestPerformanceSettings
      UI/                  WristTablet
      Aircraft/            AircraftRig (doors, flaps, steering, wheels, fans, beacon, LODs)
      Vehicles/            VehicleController, VehicleSeat, VehicleDriverInput, SteeringWheelInput,
                           VehicleLights, VehicleAudio, TowHitch, TowCoupler, TrailerSteering,
                           TowBar, AircraftTowable, CateringLift, StairsMechanism, HydraulicAxis,
                           ConveyorBelt, DoorDockingSensor, GroundPowerUnit, BasketLeveller,
                           SprayNozzle, RideablePlatform, VehicleEvents
    Scripts/Editor/        ← editor tools (assembly GroundCrew.Editor)
      Phase1SandboxBuilder.cs   menu: Ground Crew > Build Phase 1 Sandbox Scene
      AircraftPlacer.cs         menu: Ground Crew > Place Aircraft on Stand 24
      VehicleAssembler.cs       menu: Ground Crew > Vehicles > Add Vehicles to Sandbox
      ProceduralMeshes.cs       cone + wheel-chock meshes for the sandbox
    Art/Aircraft/          ← generated .glb aircraft + JSON sidecars
    Art/Vehicles/          ← generated .glb ground vehicles + JSON sidecars
  Tools/AircraftGenerator/ ← Python generator for the aircraft models
  Tools/VehicleGenerator/  ← Python generator for the vehicles (reuses the aircraft tools)
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
| Press a button | Touch it with the tip of your controller (index finger) |
| Open a door/panel/lever | Grip the handle and move your hand the way it should swing |
| Wrist tablet | Turn your left wrist to look at it like a watch |
| Change comfort options | Poke **MOVE** / **TURN** on the tablet with your right fingertip |

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
1. Drive up square to the door, and stop with the platform just short of it.
2. Set the parking brake.
3. Hold **STAIRS UP / STAIRS DN** on the dash, or **UP / DOWN** on the platform column, until the **DOCK** lamp lights.
4. Slide **PLATE OUT** to bridge the gap.

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

---

## 8. Troubleshooting

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
