# Vehicle Generator

Builds the airport ground-support vehicles as Quest-ready `.glb` models with a JSON sidecar each. It reuses the aircraft generator's mesh and glTF tools (`../AircraftGenerator`), so it also needs only Python 3.9+ and numpy.

```
python3 build.py              # writes to Assets/GroundCrew/Art/Vehicles/
python3 build.py some/folder  # alternative output folder
```

| Model | What it is | Triangles |
|---|---|---|
| `BaggageTractor` | Diesel baggage tractor with an open driver's platform under a canopy, rear pintle hitch | ~5.3k |
| `BaggageCart` | Covered baggage cart, roll-up curtains, turntable front axle with drawbar, rear hitch | ~4.3k |
| `PushbackTug` | Conventional tow-bar tug for narrowbodies: low heavy body, glazed cab, tow pins front and rear | ~5.6k |
| `TowBar` | Narrowbody tow bar: lunette eye, wheels, tow head with shear-pin housing | ~1.7k |
| `FuelTruck` | 4x2 refueller (~20 000 l): cab-over chassis, elliptical tank, rear hose reel and bonding reel | ~8.2k |
| `CateringTruck` | High-loader: cab-over chassis, scissor lift, insulated box with an extending front platform | ~7.6k |

Dimensions are typical of each class of equipment (APPROX). No maker's shapes, names or logos are copied; colours are generic GSE yellow, white, blue and red.

**Frame:** glTF +Y up, +Z forward, +X = vehicle left. The origin is on the ground under the middle of the wheelbase; the tow bar's origin is under its eye.

**Node contract:**

| Node | Meaning |
|---|---|
| `Body` | Static visual mesh |
| `Collision_<n>` | Convex collision hulls; Unity hides their renderers |
| `Wheel_<FL\|FR\|RL\|RR>` | Wheel visual, pivot at the wheel centre; a WheelCollider is put here |
| `SteeringWheel` + child `SteerAxis` | Hub pivot; `SteerAxis` lies on the column axis on the driver's side |
| `DriverEye`, `ExitPoint` | Seated eye point; where you stand after getting out |
| `Btn_<Name>` + child `Out` | Dash push button; `Out` gives the outward press normal |
| `Light_<Head\|Brake\|Reverse\|Beacon\|Work>_<n>` | Emissive lamp lens, switched by enabling the node (an unlit lens sits underneath) |
| `Hitch_Front`, `Hitch_Rear` | Tow pin / pintle points |
| Cart: `Drawbar` (`DrawbarEye`, `DrawbarHandle`) | Drawbar, pivot at the turntable |
| Tow bar: `Eye`, `TowHead`, `TowHeadHandle` | |
| Catering: `LiftBox` (`Platform`, `PlatformEdge`, `Btn_Lift*`, `Btn_Platform*`), `ScissorA_L/R`, `ScissorB_L/R` (each with `End`) | Scissor arms and the box they lift |
| Fuel: `HoseReel`, `HoseNozzle`, `BondingReel` | |

Every helper is an empty node, so Unity code works out axes and directions from node positions, not from importer axis conventions.

**Sidecar JSON:** label, mass, max speed, steering lock, motor and brake torque, centre of mass, suspension travel, wheels (position, radius, steer, drive), named points, and per-vehicle blocks (`tow`, `lift`, `cargo`, `drawbar`). `GroundCrew.EditorTools.VehicleAssembler` reads it.
