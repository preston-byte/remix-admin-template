# Hand Generator

Builds the player's gloved hand as `Assets/GroundCrew/Art/Hands/Hand_R.glb` (+ `Hand_R.json`). It needs Python 3.9+ and numpy, and reuses `../AircraftGenerator`'s mesh and glTF tools.

```
python3 build.py              # writes to Assets/GroundCrew/Art/Hands/
python3 build.py some/folder
```

The model is a generic work glove (~4.7k triangles):
- dark palm and fingers
- padded palm patch
- hi-vis back panel
- rubber knuckle guards
- orange cuff with a reflective band

Proportions are an average adult hand (APPROX). Nothing is copied from a product.

Only a right hand is generated. In Unity, `HandsSetup` mirrors it for the left hand with a −1 X scale.

**Node contract** (read by `GroundCrew.Player.ControllerHands`):

| Node | Meaning |
|---|---|
| `Hand_R` | Root, at the middle of the wrist; palm facing −Y, fingers along +Z, thumb on −X (Unity, after import) |
| `Palm`, `Cuff` | Static meshes |
| `Thumb1..3`, `Index1..3`, `Middle1..3`, `Ring1..3`, `Pinky1..3` | Joint pivots, each the parent of the next; rest rotation is identity |
| `Bend` (child of every joint) | Its local position is the joint's curl axis: `Quaternion.AngleAxis(+angle, Bend.localPosition)` curls towards the palm |
| `IndexTip`, `ThumbTip`, ... | Empties at the fingertip pads; the poke point goes on `IndexTip` |
