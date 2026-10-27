# Airport Generator

Builds the airport around the training stand as `Assets/GroundCrew/Art/Airport/Airport.glb`, plus the moving jet bridge `JetBridge.glb` (one model, placed at every gate), with:
- `Airport.json`: material list, spawn points, node roles
- tiling ground textures in `Textures/`: concrete with slab joints, asphalt, grass

It needs Python 3.9+ and numpy, and reuses `../AircraftGenerator`'s mesh and glTF tools and its 5x7 pixel font.

```
python3 build.py              # writes to Assets/GroundCrew/Art/Airport/
python3 build.py some/folder
```

A generic regional airport (APPROX). No real airport is copied. Markings and signs follow common ICAO patterns in simplified form. About 38k triangles in total.

| Area | Where (Unity metres, x east / z north; training stand at the origin) |
|---|---|
| Car park, landside road, curb | z -290 .. -200 |
| Terminal (390 m), 6 contact gates with jet bridges | z -190 .. -150; gates at x = -150 .. 150, stop bars at z -139, gate taxilane z -75 |
| Airside service (GSE) road | z -50 .. -42 |
| Remote apron, Stands 21-26 | z -42 .. 110; stands at x = -144 .. 96 (48 m apart), stop bars at z 12, taxilane z 80 |
| Apron exits B1-B3 | x = -190, 0, 190 |
| Taxiway A | z 168 .. 192, x ±1112 |
| Runway connectors A1-A4, holding positions | x = -1100, -400, 400, 1100; holding position at z 247.5 |
| Runway 09/27 | z 307.5 .. 352.5, x ±1200 (2400 x 45 m), blast pads and approach lights beyond |
| Tower, hangar, cargo, fuel farm, fire station | (240, -176), x -320 .. -250, x 250 .. 312, (330, -110), (-290, 125) |

**Nodes** (read by `GroundCrew.EditorTools.AirportPlacer`):

| Node | Role |
|---|---|
| `Ground_Grass`, `Ground_Apron`, `Ground_Roads`, `Ground_Taxiways`, `Ground_Runway` | Walkable / drivable ground: mesh colliders and teleport areas |
| `Markings_*` | Paint, raised 1.2 / 2 / 3 cm above the surface it lies on |
| `Lights_Airfield` | Emissive light fittings; no real lights |
| `Building_*`, `JetBridge_n`, `Signs`, `Floodlight_Masts` | Solid: mesh colliders |
| `Stand_21..26`, `Gate_1..6` | Empties at each stop position (nose-gear point, yaw 180 = nose to the south) |
| `JetBridge_n_Fixed` | Static fixed link and rotunda drum of each gate's bridge (walk-through; floors are box colliders added by the placer) |

**JetBridge.glb** (root at the rotunda floor centre, rest pose retracted along +z):
- `Slew` (yaw) > `Tunnel` (pitch) > `TunnelB` > `TunnelC` (telescopic, slide along z)
- `TunnelC` > `CabMount` (counter-pitch, keeps the cab level) > `Cab` (yaw), with `Canopy`, `CabFront` (the docking edge) and the console buttons `Btn_BridgeDock` / `Btn_BridgeRetract`
- `TunnelC` > `ColumnMount` (vertical) > `ColumnLegs` (scaled to length) + `Bogie` (yaw) > `Wheel_L` / `Wheel_R`

Positions are in `Airport.json` (`Bridge_n` spawn points) and its `jetBridge` block.

**Frame:** the generator works in Unity world space. `build.py` mirrors X when writing glTF, because glTFast mirrors it back on import.
