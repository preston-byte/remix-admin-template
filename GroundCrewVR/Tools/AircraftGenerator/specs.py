"""Aircraft parameter sets.

Values come from public airport-planning data (overall length/span/height, fuselage width,
wheelbase, gear track, door sizes, sill heights, tyre sizes) plus shape parameters tuned by
eye. Anything marked APPROX is a reasonable estimate that should be checked against the
manufacturer's "Aircraft Characteristics for Airport Planning" document before it drives
gameplay (e.g. exact service-panel locations).

Frame: s = metres aft of the nose tip, y = metres to the left, z = metres above the ground.
All door/panel coordinates are given for the side named; 'L' = left (port), 'R' = right.

Names are generic on purpose ("737-class", "A320-class"): no trademarks or liveries are
used in the asset itself. Display names live in game data.
"""

# Door kinds: pax (passenger/service, plug door, forward hinge), overwing (exit hatch),
# cargo (lower-lobe hold door, top hinge). opens: 'out' or 'in'.

NARROWBODY_737 = {
    "id": "Narrowbody737Class",
    "label": "Narrowbody, 737-800 class",
    # --- overall
    "length": 39.47,            # 737-800 overall length
    "width": 3.76,              # fuselage width
    "height": 4.01,             # fuselage height
    "keel_z": 1.00,             # fuselage bottom above ground at the cylindrical section (APPROX)
    "floor_z": 2.70,            # cabin floor ~ door sill height (Door 1L sill ~2.6-2.7 m)
    "wall": 0.11,               # skin-to-lining thickness
    # --- nose and tail shape
    "nose_length": 6.2,
    "nose_tip_z": 2.20,         # 737's characteristic low, pointed nose
    "nose_top_shape": (2.1, 0.62),
    "nose_bottom_shape": (2.6, 0.52),
    "nose_width_shape": (2.2, 0.55),
    "tail_start": 30.6,         # taper starts tangent, so the aft door area stays ~constant
    "tail_tip_top": 4.30,
    "tail_tip_bottom": 3.75,
    "tail_tip_halfwidth": 0.24,
    "tail_top_exp": 2.0,
    "tail_bottom_exp": 1.45,
    "tail_width_exp": 1.7,
    # --- windows
    "window_size": (0.25, 0.36),
    "window_pitch": 0.508,      # 20 in
    "window_z": 3.62,           # window centre height (floor + ~0.92 m)
    "window_range": (6.6, 32.6),
    # flight-deck windows: (front-top, aft-top, aft-bottom, front-bottom) corners as (s, theta_deg),
    # theta measured from the top of the section towards the left side.
    "windshield": [
        [(2.02, 4.0), (2.62, 3.0), (2.66, 33.0), (2.06, 44.0)],        # No.1 front (sill ~1.05 m above floor)
        [(2.71, 32.0), (3.26, 35.0), (3.28, 57.0), (2.73, 53.0)],      # No.2 side (sliding)
        [(3.33, 37.0), (3.78, 41.0), (3.80, 57.0), (3.35, 57.0)],      # No.3 aft side
    ],
    # --- doors
    "doors": [
        {"name": "Door_1L", "side": "L", "kind": "pax", "s": (4.55, 5.41), "z": (2.70, 4.53), "opens": "out", "max": 170},
        {"name": "Door_1R", "side": "R", "kind": "pax", "s": (4.55, 5.31), "z": (2.70, 4.35), "opens": "out", "max": 170},
        {"name": "Door_2L", "side": "L", "kind": "pax", "s": (31.15, 31.99), "z": (2.70, 4.53), "opens": "out", "max": 170},
        {"name": "Door_2R", "side": "R", "kind": "pax", "s": (31.15, 31.91), "z": (2.70, 4.35), "opens": "out", "max": 170},
        {"name": "Exit_L1", "side": "L", "kind": "overwing", "s": (15.55, 16.06), "z": (3.20, 4.17), "opens": "out", "max": 80},
        {"name": "Exit_L2", "side": "L", "kind": "overwing", "s": (16.42, 16.93), "z": (3.20, 4.17), "opens": "out", "max": 80},
        {"name": "Exit_R1", "side": "R", "kind": "overwing", "s": (15.55, 16.06), "z": (3.20, 4.17), "opens": "out", "max": 80},
        {"name": "Exit_R2", "side": "R", "kind": "overwing", "s": (16.42, 16.93), "z": (3.20, 4.17), "opens": "out", "max": 80},
        # 737 cargo doors: inward-opening plug doors, spring counterbalanced, manual.
        {"name": "CargoDoor_Fwd", "side": "R", "kind": "cargo", "s": (7.90, 9.20), "z": (1.32, 2.54), "opens": "in", "max": 95},
        {"name": "CargoDoor_Aft", "side": "R", "kind": "cargo", "s": (24.30, 25.52), "z": (1.32, 2.62), "opens": "in", "max": 95},
    ],
    # --- interior
    "flight_deck_bulkhead": 4.35,
    "lining_start": 1.55,
    "aft_bulkhead": 33.9,
    "seat_rows": {"first": 7.35, "pitch": 0.787, "count": 28, "exit_rows_extra": [(14.9, 0.30), (15.7, 0.35)],
                  "seat_width": 0.43, "aisle": 0.50, "abreast": 3},
    "galleys": [{"side": "R", "s": (5.45, 6.55)}, {"side": "R", "s": (32.0, 33.7)}],
    "lavatories": [{"side": "L", "s": (5.48, 6.55)}, {"side": "L", "s": (32.05, 33.0)}, {"side": "R", "s": (30.0, 31.05)}],
    "holds": [
        {"name": "Hold_Fwd", "s": (5.9, 13.6), "floor_z": 1.27, "max_kg": 3500},   # APPROX structural limit
        {"name": "Hold_Aft", "s": (22.4, 29.9), "floor_z": 1.27, "max_kg": 4800},  # APPROX
    ],
    # --- wing (left side; mirrored)
    "wing": {
        "root_le_s": 14.15, "root_z": 1.82, "root_chord": 7.35,
        "kink_y": 5.6, "tip_y": 17.15, "le_sweep": 27.0, "dihedral": 6.0,
        "te_kink_s": 21.45, "tip_chord": 1.26,
        "tc_root": 0.15, "tc_kink": 0.125, "tc_tip": 0.10, "washout": 3.0,
        "hinge": 0.73,
        "controls": [  # (name, y0, y1, kind)
            ("Flap_Inboard", 2.0, 5.5, "flap"), ("Flap_Outboard", 6.6, 12.7, "flap"),
            ("Aileron", 12.85, 16.0, "aileron"),
        ],
        "flap_fairings": [3.6, 7.3, 10.1, 12.75],
        # Blended winglet: rises ~2.5 m
        "winglet": {"radius": 0.75, "cant": 76.0, "height": 2.45, "le_sweep": 52.0, "root_chord": 1.10, "tip_chord": 0.55, "tc": 0.09},
    },
    "htail": {"root_le_s": 32.6, "root_z": 4.05, "root_chord": 3.95, "semi_span": 7.18, "tip_chord": 1.25,
              "le_sweep": 33.0, "dihedral": 7.0, "tc": 0.10, "hinge": 0.70, "controls": [("Elevator", 0.9, 6.9, "elevator")]},
    "vtail": {"stations": [  # (le_s, z, chord) from root (incl. dorsal fin) to tip
                  (26.6, 4.55, 9.6), (29.6, 5.65, 6.6), (34.75, 12.50, 1.65)],
              "tc": 0.10, "hinge": 0.70, "controls": [("Rudder", 5.9, 12.2, "rudder")]},
    # --- engines (CFM56-7 class): short fan cowl, exposed core, flattened inlet bottom
    "engine": {"type": "short", "y": 4.90, "front_s": 13.25, "center_z": 1.30, "radius": 1.00, "flatten": 0.80,
               "pylon": {"bottom_le_s": 14.15, "bottom_chord": 2.9, "top_le_s": 16.5, "top_chord": 3.0}},
    "belly_fairing": {"s": (12.0, 24.8), "center_z": 1.75, "half_height": 0.98, "extra_width": 0.12},
    # --- landing gear
    "nose_gear": {"s": 4.05, "tyre_d": 0.69, "tyre_w": 0.20, "spacing": 0.48},     # 27x7.75R15
    "main_gear": {"s": 19.65, "y": 2.86, "tyre_d": 1.13, "tyre_w": 0.42, "spacing": 0.86},  # H44.5x16.5-21, track 5.72
    # --- service points (APPROX locations; tune against airport-planning drawings)
    "service_points": {
        "SP_GPU": ("R", 3.35, 1.55),                # external power receptacle, fwd right lower
        "SP_Interphone": ("C", 4.05, 0.95),         # headset jack at nose gear
        "SP_TowFitting": ("C", 3.75, 0.34),
        "SP_RefuelPanel": ("RW", 9.5, None),        # right wing leading edge, outboard of engine (wing y, s offset auto)
        "SP_RefuelCoupling": ("RW", 9.8, None),
        "SP_PotableWater": ("R", 33.0, 1.95),       # aft right lower (APPROX)
        "SP_LavService": ("R", 34.2, 2.30),         # aft right lower (APPROX)
        "SP_PCA": ("C", 15.8, 0.80),                # preconditioned-air connection in belly
        "SP_AirStart": ("C", 17.0, 0.80),
        "SP_StaticGround": ("C", 4.3, 0.40),
    },
}

NARROWBODY_320 = {
    "id": "Narrowbody320Class",
    "label": "Narrowbody, A320-200 class",
    "length": 37.57,
    "width": 3.95,
    "height": 4.14,
    "keel_z": 1.55,             # sits noticeably higher than the 737 (APPROX)
    "floor_z": 3.40,            # Door 1L sill ~3.4 m
    "wall": 0.11,
    "nose_length": 6.6,
    "nose_tip_z": 3.25,         # rounder, more central nose than the 737
    "nose_top_shape": (2.0, 0.55),
    "nose_bottom_shape": (2.3, 0.50),
    "nose_width_shape": (2.0, 0.52),
    "tail_start": 30.2,
    "tail_tip_top": 4.95,
    "tail_tip_bottom": 4.45,
    "tail_tip_halfwidth": 0.22,
    "tail_top_exp": 2.0,
    "tail_bottom_exp": 1.4,
    "tail_width_exp": 1.7,
    "window_size": (0.23, 0.33),
    "window_pitch": 0.533,      # 21 in
    "window_z": 4.33,
    "window_range": (7.4, 30.8),
    "windshield": [
        [(2.28, 4.0), (2.88, 3.0), (2.92, 38.0), (2.32, 52.0)],        # front
        [(2.95, 36.0), (3.55, 38.0), (3.57, 58.0), (2.97, 55.0)],      # sliding side window
        [(3.62, 40.0), (3.98, 43.0), (4.00, 58.0), (3.64, 58.0)],      # aft fixed
    ],
    "doors": [
        {"name": "Door_1L", "side": "L", "kind": "pax", "s": (5.95, 6.76), "z": (3.40, 5.25), "opens": "out", "max": 170},
        {"name": "Door_1R", "side": "R", "kind": "pax", "s": (5.95, 6.76), "z": (3.40, 5.25), "opens": "out", "max": 170},
        {"name": "Door_2L", "side": "L", "kind": "pax", "s": (31.10, 31.91), "z": (3.40, 5.25), "opens": "out", "max": 170},
        {"name": "Door_2R", "side": "R", "kind": "pax", "s": (31.10, 31.91), "z": (3.40, 5.25), "opens": "out", "max": 170},
        {"name": "Exit_L1", "side": "L", "kind": "overwing", "s": (14.35, 14.86), "z": (3.95, 4.96), "opens": "out", "max": 80, "removable": True},
        {"name": "Exit_L2", "side": "L", "kind": "overwing", "s": (15.25, 15.76), "z": (3.95, 4.96), "opens": "out", "max": 80, "removable": True},
        {"name": "Exit_R1", "side": "R", "kind": "overwing", "s": (14.35, 14.86), "z": (3.95, 4.96), "opens": "out", "max": 80, "removable": True},
        {"name": "Exit_R2", "side": "R", "kind": "overwing", "s": (15.25, 15.76), "z": (3.95, 4.96), "opens": "out", "max": 80, "removable": True},
        # A320 cargo doors: outward/upward opening, hydraulic (hand pump / yellow system). ULD-capable.
        {"name": "CargoDoor_Fwd", "side": "R", "kind": "cargo", "s": (8.45, 10.27), "z": (2.00, 3.24), "opens": "out", "max": 120},
        {"name": "CargoDoor_Aft", "side": "R", "kind": "cargo", "s": (23.40, 25.22), "z": (2.00, 3.24), "opens": "out", "max": 120},
        {"name": "CargoDoor_Bulk", "side": "R", "kind": "cargo", "s": (26.55, 27.50), "z": (2.20, 3.15), "opens": "in", "max": 95},
    ],
    "flight_deck_bulkhead": 5.55,
    "lining_start": 1.75,
    "aft_bulkhead": 33.0,
    "seat_rows": {"first": 8.35, "pitch": 0.787, "count": 27, "exit_rows_extra": [(13.95, 0.30), (14.85, 0.35)],
                  "seat_width": 0.46, "aisle": 0.48, "abreast": 3},
    "galleys": [{"side": "R", "s": (6.85, 7.95)}, {"side": "L", "s": (32.0, 32.9)}, {"side": "R", "s": (32.0, 32.9)}],
    "lavatories": [{"side": "L", "s": (6.88, 7.95)}, {"side": "L", "s": (30.0, 31.0)}, {"side": "R", "s": (30.0, 31.0)}],
    "holds": [
        {"name": "Hold_Fwd", "s": (7.3, 12.3), "floor_z": 1.92, "max_kg": 5600},    # APPROX (containerised, LD3-45)
        {"name": "Hold_Aft", "s": (21.2, 28.3), "floor_z": 1.92, "max_kg": 7000},   # APPROX (incl. bulk)
    ],
    "wing": {
        "root_le_s": 12.75, "root_z": 2.38, "root_chord": 6.95,
        "kink_y": 6.1, "tip_y": 17.05, "le_sweep": 27.5, "dihedral": 5.1,
        "te_kink_s": 19.6, "tip_chord": 1.45,
        "tc_root": 0.15, "tc_kink": 0.13, "tc_tip": 0.105, "washout": 3.0,
        "hinge": 0.74,
        "controls": [("Flap_Inboard", 2.1, 6.0, "flap"), ("Flap_Outboard", 7.1, 12.9, "flap"),
                     ("Aileron", 13.1, 16.3, "aileron")],
        "flap_fairings": [3.9, 7.8, 10.6, 13.0],
        # Sharklet: tall, swept, smoothly blended (A320ceo retrofit / neo standard)
        "winglet": {"radius": 1.05, "cant": 82.0, "height": 2.30, "le_sweep": 48.0, "root_chord": 1.35, "tip_chord": 0.42, "tc": 0.09},
    },
    "htail": {"root_le_s": 31.55, "root_z": 4.75, "root_chord": 3.75, "semi_span": 6.23, "tip_chord": 1.35,
              "le_sweep": 32.0, "dihedral": 6.0, "tc": 0.10, "hinge": 0.70, "controls": [("Elevator", 0.9, 5.95, "elevator")]},
    "vtail": {"stations": [(27.9, 5.15, 7.1), (28.7, 5.95, 6.0), (33.55, 11.76, 1.85)],
              "tc": 0.10, "hinge": 0.68, "controls": [("Rudder", 6.1, 11.5, "rudder")]},
    # CFM56-5B class: long-duct, round nacelle, hangs further below and ahead of the wing
    "engine": {"type": "long", "y": 5.75, "front_s": 11.55, "center_z": 1.65, "radius": 1.07, "flatten": 1.0,
               "pylon": {"bottom_le_s": 12.3, "bottom_chord": 3.4, "top_le_s": 15.5, "top_chord": 3.2}},
    "belly_fairing": {"s": (10.6, 23.0), "center_z": 2.30, "half_height": 1.02, "extra_width": 0.12},
    "nose_gear": {"s": 5.07, "tyre_d": 0.76, "tyre_w": 0.22, "spacing": 0.52},       # 30x8.8R15, wheelbase 12.64
    "main_gear": {"s": 17.71, "y": 3.795, "tyre_d": 1.17, "tyre_w": 0.43, "spacing": 0.93},  # 46x17R20, track 7.59
    "service_points": {
        "SP_GPU": ("R", 4.2, 2.05),
        "SP_Interphone": ("C", 5.07, 1.30),
        "SP_TowFitting": ("C", 4.70, 0.38),
        "SP_RefuelPanel": ("C", 18.6, 1.30),        # refuel control panel in right belly fairing (APPROX)
        "SP_RefuelCoupling": ("RW", 10.5, None),
        "SP_PotableWater": ("R", 9.9, 2.40),        # fwd lower right (APPROX)
        "SP_LavService": ("R", 29.6, 2.70),         # aft lower right (APPROX)
        "SP_PCA": ("C", 16.0, 1.30),
        "SP_AirStart": ("C", 17.3, 1.30),
        "SP_StaticGround": ("C", 5.3, 0.45),
    },
}

ALL = [NARROWBODY_737, NARROWBODY_320]
