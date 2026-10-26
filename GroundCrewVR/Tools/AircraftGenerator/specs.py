"""Aircraft parameter sets, measured from the manufacturers' airport-planning documents.

See reference/SOURCES.md for the documents and how the outlines were traced. Values marked
APPROX are estimates (not given in those documents) and should be checked before they drive
gameplay.

Frame: s = metres aft of the nose tip, y = metres to the left, z = metres above the ground with
the aircraft LEVEL. "pitch_deg" then tilts the finished model nose-down about the main gear,
which is how both types actually sit on the ramp.

Fuselage outline tables ("profile"):
  nose  : (s, top, bottom, half_width) with top/bottom measured from the keel line
  tail_* : (s, value) with top/bottom from the keel line, half_width absolute
Between the nose and tail tables the fuselage is a constant section.

Names are generic on purpose ("737-class", "A320-class"): no trademarks or liveries in the asset.
"""

NARROWBODY_737 = {
    "id": "Narrowbody737Class",
    "label": "Narrowbody, 737-800 class",
    "length": 39.47,
    "width": 3.76,
    "height": 4.01,
    "keel_z": 1.30,             # level keel; gives Door 1 sill 2.65 m / Door 2 3.01 m once pitched (Boeing: 2.59-2.74 / 2.95-3.12)
    "floor_z": 2.85,
    "wall": 0.11,
    "pitch_deg": 0.76,          # sits nose-down on its short nose gear
    # Traced from Boeing fig. 2.2.6: long pointed nose, crown still rising 8 m back; belly sweeps up in a
    # progressive curve from ~26 m and the tail cone ends HIGH, level with the crown (APU exhaust at the top).
    "profile": {
        "nose": [(0.0, 1.52, 1.52, 0.0), (0.15, 2.02, 1.02, 0.25), (0.35, 2.12, 0.92, 0.41), (0.6, 2.26, 0.79, 0.56),
                 (0.9, 2.41, 0.63, 0.66), (1.35, 2.75, 0.42, 0.84), (1.8, 3.02, 0.29, 0.97), (2.25, 3.28, 0.16, 1.12),
                 (2.7, 3.46, 0.08, 1.25), (3.15, 3.57, 0.03, 1.35), (3.6, 3.65, 0.01, 1.45), (4.05, 3.70, 0.0, 1.53),
                 (4.5, 3.75, 0.0, 1.60), (4.95, 3.80, 0.0, 1.68), (5.4, 3.86, 0.0, 1.73), (5.85, 3.88, 0.0, 1.76),
                 (6.3, 3.94, 0.0, 1.78), (6.75, 3.96, 0.0, 1.81), (7.2, 3.99, 0.0, 1.83), (7.65, 3.99, 0.0, 1.86),
                 (8.1, 4.01, 0.0, 1.87), (8.55, 4.01, 0.0, 1.88)],
        "tail_bottom": [(25.8, 0.0), (26.4, 0.06), (27.0, 0.10), (27.6, 0.16), (28.2, 0.21), (28.8, 0.29), (29.4, 0.37),
                        (30.0, 0.46), (30.6, 0.55), (31.2, 0.67), (31.8, 0.79), (32.4, 0.89), (33.0, 1.03), (33.6, 1.18),
                        (34.2, 1.35), (34.8, 1.52), (35.4, 1.69), (36.0, 1.86), (36.6, 2.06), (37.2, 2.28), (37.8, 2.51),
                        (38.4, 2.85), (38.9, 3.25), (39.2, 3.50), (39.47, 3.62)],
        "tail_top": [(25.8, 4.01), (37.8, 4.01), (38.4, 3.99), (38.9, 3.97), (39.47, 3.95)],
        "tail_half": [(25.8, 1.88), (28.8, 1.88), (29.4, 1.86), (30.0, 1.83), (30.6, 1.81), (31.2, 1.73), (31.8, 1.68),
                      (32.4, 1.56), (33.0, 1.45), (33.6, 1.35), (34.2, 1.21), (34.8, 1.07), (35.4, 0.90), (36.0, 0.76),
                      (36.6, 0.60), (37.2, 0.48), (37.8, 0.38), (38.4, 0.28), (38.9, 0.22), (39.47, 0.18)],
    },
    # --- windows
    "window_size": (0.25, 0.36),
    "window_pitch": 0.508,
    "window_z": 3.76,
    "window_range": (6.2, 30.8),
    # Flight-deck panes as 4 corners: ("c", z) = point on the centre post at height z,
    # ("p", s, z) = point on the side at station s and height z. Mirrored to the right side.
    # 737: tall front panes on a long sloping nose; side windows taper towards the rear.
    "windshield": [
        [("c", 3.80), ("c", 4.50), ("p", 2.62, 4.44), ("p", 2.02, 3.82)],
        [("p", 2.70, 4.42), ("p", 3.50, 4.27), ("p", 3.50, 3.90), ("p", 2.10, 3.83)],
        [("p", 3.58, 4.25), ("p", 4.08, 4.06), ("p", 4.08, 3.93), ("p", 3.58, 3.91)],
    ],
    # --- doors (level frame). Boeing: Door 1 34 x 72 in centred 5.03 m from the nose.
    "doors": [
        {"name": "Door_1L", "side": "L", "kind": "pax", "s": (4.60, 5.46), "z": (2.85, 4.68), "opens": "out", "max": 170},
        {"name": "Door_1R", "side": "R", "kind": "pax", "s": (4.60, 5.36), "z": (2.85, 4.50), "opens": "out", "max": 170},
        {"name": "Door_2L", "side": "L", "kind": "pax", "s": (31.45, 32.31), "z": (2.85, 4.68), "opens": "out", "max": 170},
        {"name": "Door_2R", "side": "R", "kind": "pax", "s": (31.45, 32.21), "z": (2.85, 4.50), "opens": "out", "max": 170},
        {"name": "Exit_L1", "side": "L", "kind": "overwing", "s": (16.55, 17.06), "z": (3.35, 4.32), "opens": "out", "max": 80},
        {"name": "Exit_L2", "side": "L", "kind": "overwing", "s": (17.60, 18.11), "z": (3.35, 4.32), "opens": "out", "max": 80},
        {"name": "Exit_R1", "side": "R", "kind": "overwing", "s": (16.55, 17.06), "z": (3.35, 4.32), "opens": "out", "max": 80},
        {"name": "Exit_R2", "side": "R", "kind": "overwing", "s": (17.60, 18.11), "z": (3.35, 4.32), "opens": "out", "max": 80},
        # 737 cargo doors are SMALL (48 x 35 in fwd, 48 x 33 in aft): inward-opening plug doors, manual.
        {"name": "CargoDoor_Fwd", "side": "R", "kind": "cargo", "s": (7.95, 9.17), "z": (1.52, 2.41), "opens": "in", "max": 95},
        {"name": "CargoDoor_Aft", "side": "R", "kind": "cargo", "s": (27.40, 28.62), "z": (1.60, 2.44), "opens": "in", "max": 95},
    ],
    # --- interior
    "flight_deck_bulkhead": 4.35,
    "lining_start": 1.6,
    "aft_bulkhead": 34.0,
    "seat_rows": {"first": 6.95, "pitch": 0.787, "count": 28, "exit_rows_extra": [(16.3, 0.30), (17.3, 0.35)],
                  "seat_width": 0.43, "aisle": 0.50, "abreast": 3},
    "galleys": [{"side": "R", "s": (5.50, 6.55)}, {"side": "R", "s": (32.40, 33.80)}],
    "lavatories": [{"side": "L", "s": (5.55, 6.55)}, {"side": "L", "s": (32.40, 33.30)}, {"side": "R", "s": (29.70, 30.70)}],
    "holds": [   # Boeing: fwd 19.6 m3, aft 25.5 m3. Max loads APPROX.
        {"name": "Hold_Fwd", "s": (5.5, 13.3), "floor_z": 1.47, "max_kg": 3500},
        {"name": "Hold_Aft", "s": (21.5, 29.2), "floor_z": 1.75, "max_kg": 4800},
    ],
    # --- wing (left), traced from the plan view: swept inboard leading edge, straight inboard trailing edge.
    # stations: (y, leading-edge s, trailing-edge s, t/c, twist)
    "wing": {
        "stations": [(0.0, 13.63, 21.61, 0.155, 0.0), (4.8, 17.15, 21.61, 0.135, -0.5),
                     (5.8, 17.67, 21.61, 0.125, -0.8), (17.15, 23.65, 25.00, 0.10, -3.0)],
        "root_z": 1.66, "dihedral": 7.8, "hinge": 0.72, "root_y": 1.9,
        "controls": [("Flap_Inboard", 2.0, 5.6, "flap"), ("Flap_Outboard", 6.0, 12.9, "flap"),
                     ("Aileron", 13.0, 16.4, "aileron")],
        "flap_fairings": [3.7, 7.3, 10.1, 12.85],
        # Blended winglet: 2.44 m tall, leans out ~17 deg, leading edge swept ~36 deg (tip TE 26.01 m from nose).
        "tip": {"type": "blended", "radius": 0.60, "cant": 73.0, "height": 2.44, "le_sweep": 36.0,
                "root_chord": 1.20, "tip_chord": 0.60, "tc": 0.09},
    },
    "htail": {"root_le_s": 32.85, "root_z": 4.41, "root_chord": 4.53, "semi_span": 7.18, "tip_chord": 1.50,
              "tip_le_s": 38.16, "dihedral": 7.0, "tc": 0.10, "hinge": 0.70, "controls": [("Elevator", 0.9, 6.9, "elevator")]},
    # Fin with the long DORSAL FILLET that starts on the spine ~29 m back (737 trademark shape).
    "vtail": {"stations": [(28.8, 5.10, 8.40), (32.47, 6.76, 4.87), (36.60, 12.26, 1.84)],
              "tc": 0.10, "hinge": 0.70, "controls": [("Rudder", 7.0, 12.0, "rudder")]},
    # CFM56-7 class: flat-bottomed ("hamster pouch") short-duct nacelle, mounted far forward and HIGH
    # (top of the cowl almost level with the wing), very little ground clearance.
    "engine": {"type": "short", "y": 4.83, "front_s": 13.36, "center_z": 1.43, "radius": 1.00, "flatten": 0.84,
               "pylon": {"bottom_le_s": 14.0, "bottom_chord": 2.9, "top_le_s": 16.9, "top_chord": 3.0}},
    "belly_fairing": {"s": (13.2, 23.4), "center_z": 1.95, "half_height": 0.70, "extra_width": 0.10},
    "nose_gear": {"s": 4.09, "tyre_d": 0.69, "tyre_w": 0.20, "spacing": 0.48},              # 27x7.75R15
    "main_gear": {"s": 19.69, "y": 2.86, "tyre_d": 1.13, "tyre_w": 0.42, "spacing": 0.86},  # H44.5x16.5-21, track 5.72
    # Boeing fig. 5.4.5 locations (level frame; heights APPROX)
    "service_points": {
        "SP_GPU": ("R", 2.5, 1.90),               # "ELECTRICAL": lower right nose, ahead of the nose gear
        "SP_Interphone": ("C", 4.09, 1.05),
        "SP_TowFitting": ("C", 3.80, 0.34),
        "SP_RefuelPanel": ("RW", 8.6, None),      # "PRESSURE FUEL": right wing, outboard of the engine
        "SP_RefuelCoupling": ("RW", 8.9, None),
        "SP_PotableWater": ("R", 29.0, 1.95),
        "SP_LavService": ("R", 30.6, 2.25),       # "VACUUM LAVATORY"
        "SP_PCA": ("C", 15.6, 1.20),              # "AIR CONDITIONING"
        "SP_AirStart": ("C", 16.4, 1.20),         # "PNEUMATIC"
        "SP_StaticGround": ("C", 4.4, 0.45),
    },
}

NARROWBODY_320 = {
    "id": "Narrowbody320Class",
    "label": "Narrowbody, A320-200 class",
    "length": 37.57,
    "width": 3.95,
    "height": 4.14,
    "keel_z": 1.80,             # Airbus 2-3-0: fuselage bottom 1.73-1.86, top 5.87-6.00
    "floor_z": 3.49,            # Door 1 sill 3.38-3.48, Door 2 3.49-3.62 once pitched
    "wall": 0.11,
    "pitch_deg": 0.28,
    # Traced from Airbus 2-2-0: short, round, full nose with a steep windscreen; belly dead straight to
    # ~24.5 m then a STRAIGHT-LINE upsweep; the top line drops gently so the tail cone tip sits below the crown.
    "profile": {
        "nose": [(0.0, 1.31, 1.31, 0.0), (0.12, 1.66, 0.98, 0.28), (0.3, 1.84, 0.80, 0.47), (0.55, 2.06, 0.61, 0.72),
                 (0.8, 2.21, 0.52, 0.87), (1.2, 2.36, 0.40, 1.06), (1.6, 2.67, 0.27, 1.21), (2.0, 3.13, 0.18, 1.40),
                 (2.4, 3.38, 0.12, 1.53), (2.8, 3.53, 0.06, 1.62), (3.2, 3.65, 0.03, 1.74), (3.6, 3.81, 0.0, 1.81),
                 (4.0, 3.90, 0.0, 1.87), (4.4, 3.99, 0.0, 1.91), (4.8, 4.05, 0.0, 1.95), (5.2, 4.08, 0.0, 1.975),
                 (5.6, 4.11, 0.0, 1.975), (6.0, 4.14, 0.0, 1.975)],
        "tail_bottom": [(24.0, 0.0), (24.6, 0.03), (25.2, 0.06), (25.8, 0.12), (26.4, 0.18), (27.0, 0.27), (27.6, 0.37),
                        (28.2, 0.46), (28.8, 0.61), (29.4, 0.73), (30.0, 0.86), (30.6, 1.01), (31.2, 1.13), (31.8, 1.29),
                        (32.4, 1.41), (33.0, 1.59), (33.6, 1.75), (34.2, 1.93), (34.8, 2.12), (35.4, 2.27), (36.0, 2.45),
                        (36.6, 2.61), (37.2, 2.82), (37.57, 2.92)],
        "tail_top": [(24.0, 4.14), (30.0, 4.12), (31.2, 4.07), (32.4, 4.01), (33.6, 3.95), (34.8, 3.89),
                     (36.0, 3.80), (37.2, 3.62), (37.57, 3.56)],
        "tail_half": [(24.0, 1.975), (27.6, 1.975), (28.2, 1.96), (28.8, 1.88), (29.4, 1.79), (30.0, 1.67), (30.6, 1.55),
                      (31.2, 1.43), (31.8, 1.32), (32.4, 1.20), (33.0, 1.09), (33.6, 0.98), (34.2, 0.88), (34.8, 0.79),
                      (35.4, 0.71), (36.0, 0.63), (36.6, 0.47), (37.2, 0.30), (37.57, 0.22)],
    },
    "window_size": (0.23, 0.33),
    "window_pitch": 0.533,
    "window_z": 4.44,
    "window_range": (6.4, 29.2),
    # A320: one straight horizontal sill line and a straight top line across all six panes;
    # the aft side window has the characteristic slanted rear edge.
    "windshield": [
        [("c", 4.24), ("c", 5.02), ("p", 2.78, 4.98), ("p", 1.92, 4.24)],
        [("p", 2.86, 4.97), ("p", 3.50, 4.97), ("p", 3.50, 4.24), ("p", 2.00, 4.24)],
        [("p", 3.58, 4.96), ("p", 3.88, 4.96), ("p", 4.22, 4.24), ("p", 3.58, 4.24)],
    ],
    # Airbus 2-7-0 door locations (forward edges): Door 1 5.04, exits 14.43 / 15.28, Door 2 29.53,
    # fwd cargo 8.16, aft cargo 22.69, bulk 26.29 m from the nose.
    "doors": [
        {"name": "Door_1L", "side": "L", "kind": "pax", "s": (5.04, 5.85), "z": (3.49, 5.34), "opens": "out", "max": 170},
        {"name": "Door_1R", "side": "R", "kind": "pax", "s": (5.04, 5.85), "z": (3.49, 5.34), "opens": "out", "max": 170},
        {"name": "Door_2L", "side": "L", "kind": "pax", "s": (29.53, 30.34), "z": (3.49, 5.34), "opens": "out", "max": 170},
        {"name": "Door_2R", "side": "R", "kind": "pax", "s": (29.53, 30.34), "z": (3.49, 5.34), "opens": "out", "max": 170},
        {"name": "Exit_L1", "side": "L", "kind": "overwing", "s": (14.43, 14.94), "z": (3.89, 4.91), "opens": "out", "max": 80, "removable": True},
        {"name": "Exit_L2", "side": "L", "kind": "overwing", "s": (15.28, 15.79), "z": (3.89, 4.91), "opens": "out", "max": 80, "removable": True},
        {"name": "Exit_R1", "side": "R", "kind": "overwing", "s": (14.43, 14.94), "z": (3.89, 4.91), "opens": "out", "max": 80, "removable": True},
        {"name": "Exit_R2", "side": "R", "kind": "overwing", "s": (15.28, 15.79), "z": (3.89, 4.91), "opens": "out", "max": 80, "removable": True},
        # A320 cargo doors: big (1.82 x 1.24 m, LD3-45 containers), outward/upward, hydraulic.
        {"name": "CargoDoor_Fwd", "side": "R", "kind": "cargo", "s": (8.16, 9.98), "z": (1.89, 3.13), "opens": "out", "max": 120},
        {"name": "CargoDoor_Aft", "side": "R", "kind": "cargo", "s": (22.69, 24.51), "z": (1.89, 3.13), "opens": "out", "max": 120},
        {"name": "CargoDoor_Bulk", "side": "R", "kind": "cargo", "s": (26.29, 27.24), "z": (2.11, 3.06), "opens": "in", "max": 95},
    ],
    "flight_deck_bulkhead": 4.62,
    "lining_start": 1.45,
    "aft_bulkhead": 31.8,
    "seat_rows": {"first": 7.55, "pitch": 0.787, "count": 26, "exit_rows_extra": [(14.2, 0.30), (15.05, 0.35)],
                  "seat_width": 0.46, "aisle": 0.48, "abreast": 3},
    "galleys": [{"side": "R", "s": (5.95, 6.95)}, {"side": "L", "s": (30.45, 31.35)}, {"side": "R", "s": (30.45, 31.35)}],
    "lavatories": [{"side": "L", "s": (5.98, 6.98)}, {"side": "L", "s": (28.45, 29.40)}, {"side": "R", "s": (28.45, 29.40)}],
    "holds": [   # Airbus 2-6-0: fwd 3 x LD3-45, aft 4 x LD3-45 + bulk. Max loads APPROX.
        {"name": "Hold_Fwd", "s": (6.8, 12.6), "floor_z": 2.00, "max_kg": 5600},
        {"name": "Hold_Aft", "s": (20.8, 25.8), "floor_z": 2.00, "max_kg": 6000},
        {"name": "Hold_Bulk", "s": (25.8, 28.5), "floor_z": 2.45, "max_kg": 1500},
    ],
    # Airbus plan view: root chord 6.07 m, tip chord 1.50 m, LE sweep ~27 deg, straight inboard TE.
    "wing": {
        "stations": [(0.0, 11.83, 18.90, 0.155, 0.0), (6.3, 15.04, 18.90, 0.13, -0.8), (17.05, 20.53, 22.05, 0.105, -3.0)],
        "root_z": 2.20, "dihedral": 6.4, "hinge": 0.74, "root_y": 2.0,
        "controls": [("Flap_Inboard", 2.1, 6.2, "flap"), ("Flap_Outboard", 6.6, 13.0, "flap"),
                     ("Aileron", 13.2, 16.3, "aileron")],
        "flap_fairings": [3.9, 7.8, 10.6, 13.0],
        # Classic A320 wing-tip fence: small arrowhead plates above and below the tip
        # (Airbus 2-3-0: fence bottom 3.74-3.80 m, top 4.71-4.77 m).
        "tip": {"type": "fence", "up": 0.62, "down": 0.36, "chord": 1.55, "ahead": 0.38},
    },
    "htail": {"root_le_s": 31.60, "root_z": 4.64, "root_chord": 3.84, "semi_span": 6.225, "tip_chord": 1.29,
              "tip_le_s": 35.46, "dihedral": 6.0, "tc": 0.10, "hinge": 0.70, "controls": [("Elevator", 0.9, 5.95, "elevator")]},
    # Broad fin rising straight off the fuselage, no dorsal fillet.
    "vtail": {"stations": [(29.18, 5.50, 6.21), (29.63, 6.00, 5.87), (34.90, 11.76, 1.90)],
              "tc": 0.10, "hinge": 0.68, "controls": [("Rudder", 6.4, 11.4, "rudder")]},
    # CFM56-5B class: round long-duct nacelle hanging well ahead of and below the wing on a tall pylon.
    "engine": {"type": "long", "y": 5.75, "front_s": 11.19, "center_z": 1.58, "radius": 1.07, "flatten": 1.0,
               "pylon": {"bottom_le_s": 12.0, "bottom_chord": 3.6, "top_le_s": 14.3, "top_chord": 3.4}},
    # Large wing-to-body fairing (Airbus 2-3-0: belly fairing 1.60-1.72 m above the ground)
    "belly_fairing": {"s": (10.4, 23.6), "center_z": 2.62, "half_height": 1.0, "extra_width": 0.34},
    "nose_gear": {"s": 5.07, "tyre_d": 0.76, "tyre_w": 0.22, "spacing": 0.52},               # 30x8.8R15, wheelbase 12.64
    "main_gear": {"s": 17.71, "y": 3.795, "tyre_d": 1.17, "tyre_w": 0.43, "spacing": 0.93},  # 46x17R20, track 7.59
    # Airbus 5-4-1 layout (level frame; heights APPROX)
    "service_points": {
        "SP_GPU": ("R", 3.9, 2.35),               # 1: ground electrical power connector, lower right nose
        "SP_Interphone": ("C", 5.07, 1.45),
        "SP_TowFitting": ("C", 4.70, 0.38),
        "SP_RefuelPanel": ("R", 13.3, 2.30),      # 8: refuel/defuel integrated panel, right fuselage at the wing root
        "SP_RefuelCoupling": ("RW", 9.5, None),   # 12: refuel/defuel couplings under the right wing
        "SP_PotableWater": ("R", 29.0, 2.85),     # 21: potable water service panel, aft lower right
        "SP_LavService": ("R", 30.2, 3.20),       # 20: waste water service panel
        "SP_PCA": ("C", 11.4, 1.65),              # 6: LP air pre-conditioning
        "SP_AirStart": ("C", 12.2, 1.65),         # 7: HP air
        "SP_StaticGround": ("C", 5.3, 0.45),
    },
}

ALL = [NARROWBODY_737, NARROWBODY_320]
