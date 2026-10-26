# Where the aircraft shapes come from

The fuselage outlines, wing / tailplane planforms and fin outlines in `specs.py` were
traced from the manufacturers' public airport-planning documents. Numeric heights,
door positions and sizes come from the tables and dimensioned drawings in the same
documents.

| Aircraft | Document | Figures used |
|---|---|---|
| 737-800 class | Boeing D6-58325-7 *737 NG Airplane Characteristics for Airport Planning*, Rev C (Oct 2025), boeing.com/commercial/airports/plan-manuals | 2.2.6 General Dimensions 737-800W (side / plan / front views), 2.3.3 Ground Clearances 737-800W, 2.6.1 Lower Cargo Compartments, 2.7.1 Door Clearances (Door 1), 5.4.5 Ground Service Connections |
| A320-200 class | Airbus *A320 Aircraft Characteristics - Airport and Maintenance Planning*, Rev 44 (Jun 2024), aircraft.airbus.com | 2-2-0 General Aircraft Dimensions (wing-tip fence, sheets 1-2), 2-3-0 Ground Clearances (wing-tip fence table), 2-6-0 Cargo Compartments, 2-7-0 Door Location, 5-4-1 Ground Service Connections Layout |

**How the tracing works** (`trace_drawings.py`):
1. Render each drawing page at 300 dpi.
2. Take the outline (Boeing: blue line art; Airbus: pale-yellow fill plus outline) column by column.
3. Calibrate the pixel scale from a dimensioned feature: the 15.60 m wheelbase for the 737, the 37.57 m overall length for the A320.
4. Read off the fuselage top and bottom, the plan-view half-width, and the leading and trailing edges of the wing, tailplane and fin.

Points where wings, engines, gear, text or dimension lines overlap the outline were dropped or bridged by hand.

**Calibration choices:**
- Absolute heights come from the ground-clearance tables, not the drawings.
- Both aircraft are modelled level, then pitched nose-down by the measured door-sill difference:
  - 737: Door 2 sill is about 0.36 m higher than Door 1, giving about 0.76°.
  - A320: about 0.12 m difference, giving about 0.28°.
