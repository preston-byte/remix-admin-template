# Aircraft Generator

Builds the game's two airliners as Quest-ready `.glb` models. Their shapes are traced from Boeing's and Airbus's airport-planning drawings; heights, doors and service points come from the same documents. See [`reference/SOURCES.md`](reference/SOURCES.md).

The distinguishing features of each type are modelled:
- 737: long pointed nose, tall raked windscreen, flat-bottomed CFM56-7 inlets tucked under the wing, 2.44 m blended winglets, dorsal fin fillet, a belly that curves up to a tail cone ending level with the crown, small bulk-cargo doors, and a 0.76° nose-down parked stance.
- A320: short round nose with a straight-lined window band, round long-duct CFM56-5B nacelles hanging well ahead of the wing, wing-tip fences, a broad fin with no fillet, a straight belly then a straight-line tail upsweep, big container cargo doors plus a bulk door, and a taller stance.

| File | Aircraft | LOD0 / LOD1 / LOD2 triangles |
|---|---|---|
| `Narrowbody737Class.glb` | 737-800 class | ~84k / ~22k / ~5.6k |
| `Narrowbody320Class.glb` | A320-200 class | ~85k / ~21k / ~5.6k |

About 25k of the LOD0 triangles are the flight deck and the finer nose skin around the windscreens.

Output goes to `GroundCrewVR/Assets/GroundCrew/Art/Aircraft/`, together with a `<id>.json` sidecar for each model.

```
python3 build.py              # needs Python 3.9+ and numpy, nothing else
python3 build.py some/folder  # alternative output folder
```

## What's in a model

- **LOD0:** full detail.
  - Fuselage with real door openings, inner lining, cabin floor and cargo holds.
  - Every passenger door, overwing exit and cargo door as a hinged part:
    - 737: inward plug cargo doors.
    - A320: outward hydraulic cargo doors, plus a bulk door.
  - Flaps, ailerons, elevators and rudder.
  - Fans that can spin, nose gear that can steer, wheels that can roll.
  - Exterior lights as separate nodes: anti-collision beacons, nav lights, strobes.
  - Cabin: seats with seat numbers, overhead bins, galleys, lavatories with doors, cabin light strips.
  - Flight deck behind see-through windscreens (`flightdeck.py`), laid out from the pilot eye point in each manufacturer's document:
    - 737: control columns with yokes, a tall control stand under the centre displays with thrust levers, speed brake, flap lever and big striped trim wheels, the two CDUs behind it, a tiller on the captain's side.
    - A320: sidesticks on side consoles, pull-out tables, no yokes, the two MCDUs at the front of the pedestal, short thrust levers in a gated housing, tillers on both sides.
    - Both: six displays, glareshield autopilot panel, overhead panel, radios, pilot and observer seats, a flight-deck door and a dome light (`FlightDeckLight`, a KHR_lights_punctual point light).
    - Screens and panels are painted in code (`paint.py`, numpy only, built-in pixel font) into one 2048 x 1024 texture per aircraft: Boeing-style grey panels with toggle switches and magenta routes, Airbus-style blue-grey panels with square pushbuttons, green routes and the DOOR page on the lower ECAM. Screens use an emissive material so they glow. No brand names are painted.
- **LOD1:** the same moving parts at lower resolution, without cabin furniture.
- **LOD2:** one merged static mesh for distance.
- **ServicePoints:** empty `SP_*` nodes for:
  - GPU receptacle, refuel panel and coupling, potable water, lav service, preconditioned air (PCA), air start, interphone, tow fitting, static ground.
  - Chock positions for every wheel and cone positions.
  - A sill point for every door, for stairs, the jet bridge and belt loaders.

The origin is the nose-gear ground contact, with the nose pointing to +Z. That makes "park on the stop mark" simply "put the origin on the line".

**Moving parts.** Each moving part has three empty children: `HingeEnd`, `TestPoint` and `OpenHint`. `AircraftRig.cs` uses them to work out each hinge axis and which way is "open", so it doesn't depend on the glTF importer's axis conventions.

**Sidecar JSON.** The `<id>.json` file lists:
- dimensions and triangle counts
- every moving part with its limits
- doors (type, size, sill height)
- hold volumes and maximum loads
- the seat map
- all service points

## Editing

- **Dimensions, outlines, doors, interior layout, service points:** edit `specs.py`. Values marked `APPROX` aren't given in the manufacturer documents and are estimates.
- **Geometry:** edit `aircraft.py`.
- **Flight deck layout and panel painting:** `flightdeck.py` (painter primitives and font in `paint.py`).
- **Mesh helpers:** `meshlib.py`.
- **glTF writer:** `gltf_writer.py` (also writes the PNG texture, the alpha-blended windscreen material and the point light).

The models are procedural and dimensionally faithful, with a plain white livery and no trademarks. They are good enough to build every gameplay system against. Later they can be swapped for artist-made models that keep the same node names; see `docs/ARCHITECTURE.md` §4 and §9.
