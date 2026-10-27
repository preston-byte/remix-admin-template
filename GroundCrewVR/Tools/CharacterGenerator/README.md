# Character Generator

Builds the people as low-poly, jointed `.glb` figures in `Assets/GroundCrew/Art/Characters/`:
- `Pilot.glb`
- `CabinCrew.glb`
- `GroundCrew.glb`
- `Passenger.glb` (walking) and `PassengerSeated.glb` (one mesh, already sitting, for seated passengers)
- `Characters.json`, which lists the materials for the Quest Simple Lit swap

It needs Python 3.9+ and numpy, and reuses `../AircraftGenerator`'s mesh and glTF tools.

```
python3 build.py              # writes to Assets/GroundCrew/Art/Characters/
python3 build.py some/folder
```

| Figure | Look | Triangles |
|---|---|---|
| Pilot | White shirt, epaulettes (4 / 3 bars), tie, navy trousers, peaked cap | ~2.4k |
| CabinCrew | Teal uniform, white collar, red scarf, hair in a bun | ~2.2k |
| GroundCrew | Workwear, hi-vis vest with reflective bands, reflective trouser bands, ear defenders, gloves | ~2.8k |
| Passenger | Casual top and trousers (colours vary per person: materials `Top`, `Bottom`), optional backpack node `Backpack` | ~2.2k |

The uniforms are generic: no airline or handling-company liveries.

**Rig:** rigid segments on joint nodes, posed in code by `GroundCrew.Crew.CrewAnimator`. There's no skinning and no animation clips. Every limb hangs straight down from its pivot, and rest rotations are identity.

```
Root > Hips > Spine > Neck > Head
              Spine > UpperArm_L > LowerArm_L > Hand_L > HandAttach_L   (L = -x in Unity)
              Spine > UpperArm_R > LowerArm_R > Hand_R > HandAttach_R
       Hips > UpperLeg_L > LowerLeg_L > Foot_L
       Hips > UpperLeg_R > LowerLeg_R > Foot_R
```

**Joint conventions in Unity:**
- −angle about x swings an arm or a leg forward.
- +angle about x bends a knee.
- −angle about x bends an elbow forward.
- ±angle about z raises an arm sideways (+ for the right arm).

The materials named `Skin` and `Hair` are recoloured per person at runtime.
