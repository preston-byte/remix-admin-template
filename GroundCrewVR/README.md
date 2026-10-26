# Ground Crew VR — Phase 1: VR Foundation

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
    Scripts/Editor/        ← editor tools (assembly GroundCrew.Editor)
      Phase1SandboxBuilder.cs   menu: Ground Crew > Build Phase 1 Sandbox Scene
      AircraftPlacer.cs         menu: Ground Crew > Place Aircraft on Stand 24
      ProceduralMeshes.cs       cone + wheel-chock meshes for the sandbox
    Art/Aircraft/          ← generated .glb aircraft + JSON sidecars
  Tools/AircraftGenerator/ ← Python generator for the aircraft models
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

## 7. Troubleshooting

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
