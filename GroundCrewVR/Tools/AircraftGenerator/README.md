# Aircraft Generator

Builds the game's two airliners from published dimensions as Quest-ready `.glb` models:

| File | Aircraft | LOD0 / LOD1 / LOD2 triangles |
|---|---|---|
| `Narrowbody737Class.glb` | 737-800 class | ~60k / ~22k / ~5.5k |
| `Narrowbody320Class.glb` | A320-200 class | ~59k / ~22k / ~5.5k |

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
  - Cabin: seats with seat numbers, overhead bins, galleys, lavatories with doors, cabin light strips, and a flight deck with its door, seats and panels.
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

- **Dimensions, doors, interior layout, service points:** edit `specs.py`. Values marked `APPROX` should be checked against the manufacturers' airport-planning documents before they drive gameplay.
- **Geometry:** edit `aircraft.py`.
- **Mesh helpers:** `meshlib.py`.
- **glTF writer:** `gltf_writer.py`.

The models are procedural and dimensionally faithful, with a plain white livery and no trademarks. They are good enough to build every gameplay system against. Later they can be swapped for artist-made models that keep the same node names; see `docs/ARCHITECTURE.md` §4 and §9.
