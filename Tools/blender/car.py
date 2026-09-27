"""The cars' bodies, built in Blender: the four designs the lobby offers (DESIGNS).

Run headless from WSL (Blender takes Windows paths):

    /mnt/d/Software/Blender/blender.exe -b --factory-startup \
        -P "$(wslpath -w Tools/blender/car.py)" -- \
        --out 'D:\\Dev\\CarRace\\Assets\\Art\\Cars' --preview "$(wslpath -w Tools/out/car)"

Writes <design>.fbx to --out for each design (or only --design), with three meshes: "Body",
the painted panels, one material; "Details", in five materials in this order: glass, trim,
headlight, taillight, metal; and "Interior", the cabin and its lining, which Unity draws
without shadows, as they fall inside the body where nobody sees them. --preview renders each from three sides, to
<prefix>-<design>-front.png, -rear.png and -side.png.

How the shape is made. A cage of cross-sections ("stations"), each the same thirteen points
round the half body from under the sill to the roof's centre line, mirrored, is smoothed
by Catmull-Clark subdivision, which is how car bodies are modelled by hand. Every cell of
the cage carries a part (paint, glass, headlight and so on) that the subdivision keeps, so
a window's edge comes out as a smooth curve along the cage's lines, with creases where an
edge should stay sharp. The wheel arches are stations whose lower points ride round a
circle over the tyre.

Coordinates are Unity's, as in wheel.py: x right, y up, z forward, in metres, with y
measured from the road here and moved to the car's origin (its centre of mass) on output.
"""

import math
import os
import sys

import bmesh
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import wheel  # noqa: E402  helpers and the wheel parts, for the preview

# The car, from CarDefinition's defaults: the body is built round these.
WHEELBASE, FRONT_BIAS, TRACK, CG = 2.65, 0.48, 1.60, 0.45
FRONT_AXLE = WHEELBASE * (1 - FRONT_BIAS)       # z of the axles from the centre of mass
REAR_AXLE = -WHEELBASE * FRONT_BIAS
TYRE_R = 0.34
ARCH_R = 0.378                                  # the arch's edge, round the axle

# Parts, as material slots. Body gets PAINT; Details the rest, in this order.
PAINT, GLASS, TRIM, HEADLIGHT, TAILLIGHT, INTERIOR, METAL = range(7)
DETAIL_PARTS = (GLASS, TRIM, HEADLIGHT, TAILLIGHT, METAL)
PART_NAMES = ("Paint", "Glass", "Trim", "Headlight", "Taillight", "Interior", "Metal")


def V(x, y, z):
    """Road-height Unity coordinates to Blender, about the car's centre of mass."""
    return wheel.U(x, y - CG, z)


def to_road(b):
    """Blender back to road-height Unity coordinates."""
    return Vector((-b.x, b.z + CG, -b.y))


# ---- the designs ---------------------------------------------------------------
#
# Each design's shape is its key stations, rear to front. Per station:
#   z     along the car
#   yb    bottom edge (sill) height          wb   half width at the widest line
#   yl    height of the widest line          yf   shoulder (top of the flank) height
#   wg,yg the greenhouse's base, or on a deck the edge of the bonnet or engine cover
#   wr,yr the roof rail                       yc   height on the centre line
#   gh    0 a deck (bonnet, engine cover), 1 the full cabin; between, a windscreen
#         or rear window rising from the deck to the roof
#
# cells: which cage cells are glass, lights, grilles or black pillars, as (part, z range,
# columns); the first that matches wins, and the rest are paint. A cell spans profile
# columns c and c + 1: 1 to 5 the flank, 6 to 8 the side window with its seals, 9 the roof
# rail, 10 and 11 the windscreen or rear window, or the middle of a bonnet or deck.
# nose, tail: the end rings (depth, width and height the opening inside keeps, the parts of
# the ring's cells that are not paint by column, and the opening's height or None for the
# section's middle).
# doors: front and rear shut lines as (z, y) on the flank; handles as (z0, z1, y).
# bonnet: (from z, to z, front edge z), its sides along the deck's edge. lid: the engine
# cover or boot as (x, z) seen from above. wing: which kind, and its numbers. exhausts:
# (x positions, height, radius, z of the tips). cabin: the driver's eye, the windscreen's
# foot (cowl), and what stands behind the front seats.

SIDE = {"GLASS": (7,), "SEAL": (6, 8)}


def glass(windscreen, rear, side, pillars=()):
    """The cells of a design's windows: black pillars first, then glass and its seals. The
    side glass must end where the roof is still at full height (gh 1): run on into the rear
    window's slope, its band twists up out of the body as a fin."""
    cells = [(TRIM, z, (6, 7, 8)) for z in pillars]
    cells += [(GLASS, windscreen, (10, 11)), (GLASS, rear, (10, 11)),
              (GLASS, side, SIDE["GLASS"]), (TRIM, side, SIDE["SEAL"])]
    return cells


DESIGNS = {
    # A rear-engined coupe in the manner of a 911 GT3: low bonnet between raised wings,
    # fastback, light bar, swan-neck wing.
    "GT": dict(
        keys=[
            #  z      yb    wb     yl    yf    wg    yg    wr    yr    yc    gh
            (-2.25, 0.34, 0.84, 0.50, 0.78, 0.60, 0.84, 0.46, 0.85, 0.86, 0.0),
            (-2.19, 0.24, 0.92, 0.52, 0.88, 0.70, 0.91, 0.54, 0.92, 0.93, 0.0),
            (-2.02, 0.20, 0.945, 0.54, 0.91, 0.73, 0.94, 0.56, 0.95, 0.955, 0.0),
            (-1.75, 0.17, 0.965, 0.55, 0.93, 0.74, 0.96, 0.57, 0.975, 0.98, 0.0),
            (-1.50, 0.15, 0.975, 0.55, 0.94, 0.75, 0.975, 0.58, 1.01, 1.02, 0.25),
            (-1.20, 0.13, 0.975, 0.55, 0.93, 0.77, 0.97, 0.60, 1.10, 1.12, 0.7),
            (-0.85, 0.12, 0.955, 0.55, 0.90, 0.79, 0.955, 0.61, 1.195, 1.215, 1.0),
            (-0.40, 0.12, 0.93, 0.55, 0.86, 0.80, 0.94, 0.62, 1.255, 1.275, 1.0),
            (0.00, 0.12, 0.92, 0.55, 0.85, 0.80, 0.93, 0.62, 1.255, 1.28, 1.0),
            (0.22, 0.12, 0.92, 0.55, 0.85, 0.79, 0.925, 0.62, 1.20, 1.23, 0.85),
            (0.62, 0.12, 0.93, 0.55, 0.85, 0.72, 0.905, 0.63, 0.93, 0.915, 0.0),
            (1.00, 0.12, 0.945, 0.54, 0.84, 0.66, 0.79, 0.60, 0.77, 0.745, 0.0),
            (1.40, 0.12, 0.955, 0.53, 0.83, 0.63, 0.745, 0.57, 0.72, 0.69, 0.0),
            (1.80, 0.13, 0.945, 0.51, 0.79, 0.61, 0.70, 0.55, 0.68, 0.655, 0.0),
            (2.08, 0.14, 0.90, 0.47, 0.70, 0.57, 0.64, 0.50, 0.63, 0.615, 0.0),
            (2.24, 0.16, 0.80, 0.42, 0.58, 0.49, 0.56, 0.42, 0.57, 0.565, 0.0),
            (2.31, 0.20, 0.64, 0.38, 0.49, 0.38, 0.50, 0.30, 0.51, 0.51, 0.0),
        ],
        cells=glass((0.14, 0.62), (-1.40, -0.70), (-0.74, 0.62)) + [
            (HEADLIGHT, (1.90, 2.24), (3, 4, 5)),
            (TAILLIGHT, (-2.25, -2.19), (5, 6, 7, 8, 9, 10, 11)),     # a light bar across the tail
            (TRIM, (-2.02, -1.60), (11,)),                            # engine grille
        ],
        nose=(0.018, 0.66, 0.42, {1: TRIM, 2: TRIM, 3: TRIM}, None), tail=(0.02, 0.70, 0.40, {1: TRIM, 2: TRIM}, None),
        doors=[[(0.90, 0.23), (0.89, 0.45), (0.84, 0.70), (0.70, 0.93)],
               [(-0.58, 0.23), (-0.58, 0.50), (-0.52, 0.80), (-0.44, 0.97)]],
        handles=[(-0.37, -0.23, 0.80)],
        bonnet=(0.70, 2.05, 2.17),
        lid=[(0.0, -1.56), (0.40, -1.57), (0.52, -1.66), (0.52, -2.02), (0.40, -2.12), (0.0, -2.14)],
        mirror=(0.97, 0.99, 0.47),
        wing=("swan", dict(zc=-2.02, yc=1.235, chord=0.33, span=0.84, aoa=7.0, mount_x=0.28,
                           neck=[(-1.85, 0.90), (-1.89, 1.08), (-1.94, 1.24), (-2.00, 1.30), (-2.06, 1.285)])),
        exhausts=((-0.11, 0.11), 0.34, 0.046, -2.33),
        cabin=dict(eye=(-0.37, 1.02, -0.38), cowl=0.62, bulkhead=-1.02, rear_seat=None, parcel=None,
                   lining=(-1.5, 0.95)),
    ),
    # Front-engined, in the manner of a Mustang: long flat bonnet, a blunt shark nose with
    # lamps either side of a wide grille, a fastback to a short deck with a ducktail, three
    # bar tail lamps, four tailpipes, and a back seat.
    "Muscle": dict(
        keys=[
            (-2.34, 0.36, 0.86, 0.60, 0.88, 0.62, 0.96, 0.50, 0.97, 1.00, 0.0),
            (-2.28, 0.26, 0.92, 0.58, 0.92, 0.70, 0.98, 0.55, 0.99, 0.995, 0.0),
            (-2.05, 0.20, 0.95, 0.56, 0.93, 0.74, 0.97, 0.58, 0.98, 0.975, 0.0),
            (-1.75, 0.17, 0.965, 0.55, 0.94, 0.75, 0.97, 0.58, 0.99, 0.98, 0.0),
            (-1.50, 0.15, 0.97, 0.55, 0.95, 0.77, 0.99, 0.60, 1.08, 1.09, 0.4),
            (-1.20, 0.14, 0.97, 0.55, 0.95, 0.79, 1.00, 0.62, 1.18, 1.20, 0.8),
            (-0.85, 0.13, 0.955, 0.55, 0.94, 0.80, 1.00, 0.63, 1.29, 1.31, 1.0),
            (-0.40, 0.13, 0.94, 0.55, 0.93, 0.80, 1.00, 0.64, 1.315, 1.335, 1.0),
            (-0.20, 0.13, 0.94, 0.55, 0.93, 0.80, 1.00, 0.64, 1.30, 1.325, 1.0),
            (0.05, 0.13, 0.94, 0.55, 0.93, 0.80, 0.99, 0.65, 1.20, 1.23, 0.75),
            (0.35, 0.13, 0.945, 0.55, 0.93, 0.78, 0.97, 0.66, 1.00, 1.00, 0.0),
            (0.80, 0.13, 0.955, 0.55, 0.92, 0.74, 0.95, 0.64, 0.97, 0.965, 0.0),
            (1.30, 0.13, 0.96, 0.55, 0.92, 0.72, 0.94, 0.62, 0.95, 0.945, 0.0),
            (1.80, 0.14, 0.955, 0.54, 0.91, 0.70, 0.93, 0.60, 0.94, 0.93, 0.0),
            (2.20, 0.16, 0.93, 0.52, 0.89, 0.66, 0.905, 0.56, 0.915, 0.91, 0.0),
            (2.30, 0.18, 0.90, 0.50, 0.87, 0.62, 0.885, 0.52, 0.895, 0.89, 0.0),
            (2.37, 0.22, 0.80, 0.46, 0.70, 0.52, 0.72, 0.44, 0.73, 0.72, 0.0),
        ],
        cells=glass((-0.20, 0.35), (-1.75, -0.85), (-0.86, 0.35)) + [
            (HEADLIGHT, (2.30, 2.37), (4, 5, 6)),
            (TRIM, (2.30, 2.37), (8, 9, 10, 11)),                     # the grille between the lamps
        ],
        # Three bar tail lamps each side on the rear panel, a small diffuser low down.
        nose=(0.02, 0.62, 0.40, {1: TRIM, 2: TRIM}, None),
        tail=(0.02, 0.60, 0.28, {1: TRIM, 2: TRIM, 5: TAILLIGHT, 6: TRIM, 7: TAILLIGHT, 8: TRIM, 9: TAILLIGHT}, 0.42),
        doors=[[(0.92, 0.23), (0.92, 0.60), (0.88, 0.90), (0.80, 0.98)],
               [(-0.75, 0.23), (-0.75, 0.60), (-0.72, 0.90), (-0.70, 1.00)]],
        handles=[(-0.55, -0.42, 0.86)],
        bonnet=(0.40, 2.20, 2.28),
        lid=[(0.0, -1.78), (0.55, -1.79), (0.62, -1.90), (0.62, -2.20), (0.50, -2.27), (0.0, -2.28)],
        mirror=(0.99, 1.07, 0.15),
        wing=None,                                               # the ducktail is in the shape
        exhausts=((-0.62, -0.50, 0.50, 0.62), 0.27, 0.042, -2.40),
        cabin=dict(eye=(-0.37, 1.05, -0.62), cowl=0.35, bulkhead=-1.45, rear_seat=-1.12, parcel=None,
                   lining=(-1.75, 0.70)),
    ),
    # Mid-engined, in the manner of a Huracan: a wedge, low and wide, cabin forward under a
    # long raked windscreen, intakes ahead of the rear wheels, a louvred engine cover and a
    # low wing on posts.
    "Supercar": dict(
        keys=[
            (-2.22, 0.36, 0.88, 0.55, 0.86, 0.66, 0.91, 0.50, 0.92, 0.93, 0.0),
            (-2.16, 0.26, 0.95, 0.55, 0.92, 0.72, 0.95, 0.55, 0.96, 0.96, 0.0),
            (-1.95, 0.20, 0.975, 0.55, 0.96, 0.74, 0.99, 0.56, 1.00, 1.00, 0.0),
            (-1.60, 0.16, 0.985, 0.52, 0.96, 0.75, 1.00, 0.57, 1.01, 1.02, 0.0),
            (-1.20, 0.14, 0.98, 0.50, 0.95, 0.76, 1.00, 0.58, 1.03, 1.04, 0.0),
            (-0.85, 0.13, 0.96, 0.48, 0.93, 0.76, 0.99, 0.58, 1.06, 1.07, 0.35),
            (-0.55, 0.12, 0.945, 0.47, 0.90, 0.77, 0.97, 0.59, 1.12, 1.135, 0.9),
            (-0.30, 0.12, 0.94, 0.46, 0.88, 0.77, 0.95, 0.59, 1.14, 1.155, 1.0),
            (0.05, 0.12, 0.94, 0.46, 0.86, 0.77, 0.93, 0.60, 1.135, 1.15, 1.0),
            (0.25, 0.12, 0.945, 0.46, 0.85, 0.76, 0.91, 0.61, 1.10, 1.11, 0.85),
            (0.60, 0.12, 0.95, 0.46, 0.83, 0.74, 0.86, 0.62, 0.97, 0.97, 0.45),
            (0.95, 0.12, 0.955, 0.46, 0.81, 0.70, 0.80, 0.60, 0.81, 0.80, 0.0),
            (1.35, 0.12, 0.96, 0.45, 0.79, 0.66, 0.74, 0.58, 0.74, 0.72, 0.0),
            (1.75, 0.12, 0.95, 0.43, 0.75, 0.62, 0.68, 0.54, 0.68, 0.655, 0.0),
            (2.05, 0.12, 0.90, 0.40, 0.66, 0.56, 0.60, 0.48, 0.60, 0.58, 0.0),
            (2.22, 0.13, 0.80, 0.36, 0.55, 0.48, 0.50, 0.40, 0.50, 0.49, 0.0),
            (2.30, 0.15, 0.62, 0.30, 0.40, 0.38, 0.38, 0.30, 0.38, 0.38, 0.0),
        ],
        cells=glass((0.05, 0.95), (-0.85, -0.30), (-0.50, 0.95)) + [
            (TRIM, (-0.86, -0.55), (3, 4)),                           # intake ahead of the rear wheel
            (HEADLIGHT, (2.00, 2.22), (4, 5)),
            (TRIM, (2.22, 2.30), (2, 3)),                             # side intakes in the nose
            (TAILLIGHT, (-2.22, -2.16), (5, 6, 7, 8)),
            (TRIM, (-2.22, -2.16), (9, 10, 11)),                      # the engine's vent between them
            (TRIM, (-1.55, -0.95), (11,)),                            # louvres on the engine cover
        ],
        nose=(0.018, 0.66, 0.44, {1: TRIM, 2: TRIM, 3: TRIM}, None),
        tail=(0.02, 0.66, 0.30, {1: TRIM, 2: TRIM}, 0.44),
        doors=[[(0.96, 0.23), (0.94, 0.50), (0.85, 0.78)],
               [(-0.42, 0.30), (-0.45, 0.60), (-0.55, 0.90)]],
        handles=[],
        bonnet=(1.00, 2.00, 2.15),
        lid=[(0.0, -0.95), (0.45, -0.95), (0.50, -1.10), (0.50, -1.90), (0.40, -2.05), (0.0, -2.06)],
        mirror=(0.99, 0.92, 0.70),
        wing=("posts", dict(zc=-2.02, yc=1.06, chord=0.24, span=0.80, aoa=8.0, mount_x=0.45)),
        exhausts=((-0.12, 0.12), 0.46, 0.05, -2.27),
        cabin=dict(eye=(-0.37, 0.98, -0.30), cowl=0.95, bulkhead=-0.72, rear_seat=None, parcel=None,
                   lining=(-1.0, 1.3)),
    ),
    # A hot hatch in the manner of a Golf GTI or Civic Type R: tall, short, an upright
    # tailgate under a roof spoiler, black B pillars, lamps either side of a grille, twin
    # tailpipes, and a back seat under a parcel shelf.
    "Hot Hatch": dict(
        keys=[
            (-1.99, 0.36, 0.84, 0.58, 0.90, 0.62, 0.96, 0.55, 1.00, 1.02, 0.0),
            (-1.95, 0.26, 0.93, 0.60, 0.95, 0.70, 1.00, 0.60, 1.05, 1.06, 0.0),
            (-1.88, 0.22, 0.95, 0.60, 0.97, 0.74, 1.02, 0.62, 1.18, 1.20, 0.45),
            (-1.72, 0.19, 0.955, 0.60, 0.98, 0.76, 1.03, 0.64, 1.39, 1.41, 1.0),
            (-1.40, 0.17, 0.96, 0.60, 0.98, 0.78, 1.03, 0.65, 1.41, 1.435, 1.0),
            (-0.60, 0.15, 0.94, 0.58, 0.96, 0.79, 1.02, 0.66, 1.43, 1.455, 1.0),
            (0.10, 0.15, 0.935, 0.58, 0.95, 0.79, 1.01, 0.66, 1.39, 1.415, 1.0),
            (0.45, 0.15, 0.94, 0.57, 0.94, 0.77, 1.00, 0.67, 1.22, 1.24, 0.7),
            (0.85, 0.15, 0.945, 0.56, 0.93, 0.74, 0.98, 0.66, 1.00, 0.99, 0.0),
            (1.30, 0.15, 0.955, 0.55, 0.92, 0.70, 0.95, 0.62, 0.95, 0.95, 0.0),
            (1.75, 0.15, 0.95, 0.54, 0.90, 0.66, 0.92, 0.58, 0.92, 0.915, 0.0),
            (2.08, 0.16, 0.92, 0.52, 0.86, 0.62, 0.87, 0.54, 0.87, 0.87, 0.0),
            (2.18, 0.18, 0.88, 0.50, 0.82, 0.58, 0.83, 0.50, 0.83, 0.83, 0.0),
            (2.25, 0.22, 0.78, 0.46, 0.72, 0.52, 0.74, 0.44, 0.74, 0.74, 0.0),
        ],
        cells=glass((0.10, 0.85), (-1.95, -1.72), (-1.40, 0.85), pillars=[(-0.50, -0.40)]) + [
            (HEADLIGHT, (2.18, 2.25), (4, 5, 6)),
            (TRIM, (2.18, 2.25), (8, 9, 10, 11)),                     # the grille
            (TAILLIGHT, (-1.99, -1.88), (4, 5, 6)),                   # lamps wrapping the corners
        ],
        nose=(0.02, 0.60, 0.36, {1: TRIM, 2: TRIM, 3: TRIM}, None),
        tail=(0.02, 0.64, 0.30, {1: TRIM, 2: TRIM}, 0.40),
        doors=[[(0.94, 0.23), (0.93, 0.55), (0.86, 0.90), (0.78, 1.02)],
               [(-0.84, 0.23), (-0.84, 0.60), (-0.80, 1.02)]],
        handles=[(-0.70, -0.58, 0.88)],
        bonnet=(0.88, 2.00, 2.17),
        lid=None,
        mirror=(0.99, 1.08, 0.60),
        wing=("roof", dict(zc=-1.79, yc=1.415, chord=0.26, span=0.62, aoa=4.0)),
        exhausts=((-0.42, 0.42), 0.30, 0.045, -2.05),
        cabin=dict(eye=(-0.37, 1.18, -0.25), cowl=0.85, bulkhead=None, rear_seat=-1.00, parcel=(-1.85, -1.30, 0.99),
                   lining=(-2.1, 1.2)),
    ),
}

D = DESIGNS["GT"]           # the design being built; main() sets it


def pchip(xs, ys, x):
    """Monotone cubic interpolation, so the shape never overshoots its key stations."""
    n = len(xs)
    if x <= xs[0]:
        return ys[0]
    if x >= xs[-1]:
        return ys[-1]
    h = [xs[i + 1] - xs[i] for i in range(n - 1)]
    d = [(ys[i + 1] - ys[i]) / h[i] for i in range(n - 1)]
    m = [d[0]] + [0.0] * (n - 2) + [d[-1]]
    for i in range(1, n - 1):
        if d[i - 1] * d[i] > 0:
            w1, w2 = 2 * h[i] + h[i - 1], h[i] + 2 * h[i - 1]
            m[i] = (w1 + w2) / (w1 / d[i - 1] + w2 / d[i])
    i = max(k for k in range(n - 1) if xs[k] <= x)
    t = (x - xs[i]) / h[i]
    t2, t3 = t * t, t * t * t
    return ((2 * t3 - 3 * t2 + 1) * ys[i] + (t3 - 2 * t2 + t) * h[i] * m[i]
            + (-2 * t3 + 3 * t2) * ys[i + 1] + (t3 - t2) * h[i] * m[i + 1])


def station_params(z):
    keys = D["keys"]
    zs = [k[0] for k in keys]
    return [pchip(zs, [k[j] for k in keys], z) for j in range(1, 11)]


def arch_edge(z):
    """Height of the arch's edge at z, or None away from the wheels."""
    for axle in (FRONT_AXLE, REAR_AXLE):
        d = z - axle
        if abs(d) <= ARCH_R:
            return TYRE_R + math.sqrt(ARCH_R * ARCH_R - d * d)
    return None


def profile(z):
    """The thirteen points of the half section at z, from under the sill to the centre line."""
    yb, wb, yl, yf, wg, yg, wr, yr, yc, gh = station_params(z)
    lower = [(wb - 0.12, yb + 0.01), (wb - 0.05, yb), (wb - 0.028, yb + 0.1), (wb, yl),
             (wb - 0.018, yf - 0.05), (wb - 0.07, yf)]
    edge = arch_edge(z)
    if edge is not None:
        # Over a wheel the lower points ride the arch: a lip turned in, the edge, a flare.
        e = max(edge, yb)
        top = min(e + 0.05, yf - 0.07)
        lower = [(wb - 0.09, e + 0.004), (wb - 0.025, e), (wb - 0.004, e + 0.018),
                 (wb, max(top, (e + yf) * 0.5 - 0.03)), (wb - 0.018, yf - 0.05), (wb - 0.07, yf)]
    # The top from the greenhouse base (or deck edge) to the centre line. On a deck the first
    # four points crowd its edge, so that where a windscreen or rear window rises from it
    # (columns 10 to 12) the glass is nearly the deck's full width, not a point.
    deck = []
    for t in (0.03, 0.06, 0.09, 0.12, 0.55, 1.0):
        deck.append((wg * (1 - t), yg + (yc - yg) * (1 - (1 - t) ** 2)))
    cabin = [(wg - 0.012, yg + 0.03),
             (wr + (wg - wr) * 0.12, yr - 0.05),
             (wr + 0.004, yr - 0.012),
             (wr - 0.035, yr + 0.012),
             (wr * 0.5, (yr + yc) * 0.5 + 0.012),
             (0.0, yc)]
    top = [(d[0] + (c[0] - d[0]) * gh, d[1] + (c[1] - d[1]) * gh) for d, c in zip(deck, cabin)]
    return lower + [(wg, yg)] + top


def station_zs():
    """Cage stations: evenly along the car, plus a fan round each arch."""
    zs = set()
    keys = D["keys"]
    z0, z1 = keys[0][0], keys[-1][0]
    n = 24
    for i in range(n + 1):
        zs.add(round(z0 + (z1 - z0) * i / n, 4))
    for axle in (FRONT_AXLE, REAR_AXLE):
        for a in range(0, 181, 30):
            zs.add(round(axle - ARCH_R * math.cos(math.radians(a)), 4))
        zs.add(round(axle - ARCH_R - 0.03, 4))
        zs.add(round(axle + ARCH_R + 0.03, 4))
    for z in [b for _, r, _ in D["cells"] for b in r] + [keys[-2][0], keys[1][0]]:
        zs.add(round(z, 4))
    # Drop near-duplicates, which subdivide into slivers.
    out = []
    for z in sorted(zs):
        if not out or z - out[-1] > 0.025:
            out.append(z)
    return out


def part_of(z0, z1, c):
    """The part a cell is, from its stations' z and its column (0 under the sill, 12 the
    centre line; the cell spans columns c and c + 1)."""
    zm = (z0 + z1) * 0.5
    inside = lambda r: r[0] - 1e-4 <= z0 and z1 <= r[1] + 1e-4
    if c == 0:
        return TRIM                              # under the sill and the arch lips
    if c == 1 and REAR_AXLE + ARCH_R < zm < FRONT_AXLE - ARCH_R:
        return TRIM                              # the side skirt
    for part, zr, cols in D["cells"]:
        if c in cols and inside(zr):
            return part
    return PAINT


# ---- the shell ------------------------------------------------------------------

def build_cage():
    zs = station_zs()
    rows = []
    for z in zs:
        half = profile(z)
        row = [(-x, y, z) for x, y in half[:-1]] + [(x, y, z) for x, y in reversed(half)]
        rows.append(row)
    cols = len(rows[0])                     # 25: the centre line is shared
    verts = [v for row in rows for v in row]
    faces, parts = [], []
    for r in range(len(rows) - 1):
        for c in range(cols - 1):
            half_col = c if c < 12 else 23 - c      # the mirror column on the left side
            a, b = r * cols + c, r * cols + c + 1
            quad = (a, b, b + cols, a + cols)
            faces.append(quad)
            parts.append(part_of(zs[r], zs[r + 1], half_col))
    # The underside, flat and black, closing the section at the bottom.
    for r in range(len(rows) - 1):
        a, b = r * cols + cols - 1, r * cols
        faces.append((a, b, b + cols, a + cols))
        parts.append(TRIM)
    # Nose and tail: a painted ring set in from each end section, the bumper's face, whose
    # lower corners are intakes (in front) or the diffuser's sides (behind). The opening
    # left inside it is filled after subdivision (fill_ends).
    for end, (depth, sx, sy, ring, centre), step in ((len(rows) - 1, D["nose"], 1), (0, D["tail"], -1)):
        row = rows[end]
        cy = sum(v[1] for v in row) / len(row)
        middle = cy if centre is None else centre
        base = len(verts)
        verts.extend((x * sx, middle + (y - cy) * sy, z + depth * step) for x, y, z in row)
        for c in range(cols):
            c1 = (c + 1) % cols
            half_col = c if c < 12 else 23 - c
            faces.append((end * cols + c, end * cols + c1, base + c1, base + c))
            parts.append(TRIM if c == cols - 1 else ring.get(half_col, PAINT))
    return verts, faces, parts, zs, cols


def shell(level):
    """The cage, subdivided and applied, with its cells' parts as material indices."""
    verts, faces, parts, zs, cols = build_cage()
    mesh = bpy.data.meshes.new("Shell Cage")
    mesh.from_pydata([V(*v) for v in verts], [], faces)
    mesh.update()
    for poly, part in zip(mesh.polygons, parts):
        poly.material_index = part
    # Crisp lines: the sill and arch edge (column 1), the shoulder (5), window edges.
    crease = mesh.attributes.new("crease_edge", "FLOAT", "EDGE")
    sharp_cols = {1: 0.8, 23: 0.8, 3: 0.3, 21: 0.3, 5: 0.35, 19: 0.35}
    edge_of = {tuple(sorted(e.vertices)): e.index for e in mesh.edges}
    rows = len(zs)
    for r in range(rows - 1):
        for c, w in sharp_cols.items():
            key = tuple(sorted((r * cols + c, (r + 1) * cols + c)))
            if key in edge_of:
                crease.data[edge_of[key]].value = w
    # Part boundaries stay crisp too.
    face_parts = {}
    for poly in mesh.polygons:
        for ek in poly.edge_keys:
            face_parts.setdefault(ek, set()).add(poly.material_index)
    for ek, ps in face_parts.items():
        if len(ps) > 1:
            crease.data[edge_of[tuple(sorted(ek))]].value = 0.9
    obj = bpy.data.objects.new("Shell", mesh)
    bpy.context.scene.collection.objects.link(obj)
    for name in PART_NAMES:
        mesh.materials.append(material(name))
    mod = obj.modifiers.new("Subdivision", "SUBSURF")
    mod.levels = mod.render_levels = level
    mod.boundary_smooth = "PRESERVE_CORNERS"
    depsgraph = bpy.context.evaluated_depsgraph_get()
    smooth = bpy.data.meshes.new_from_object(obj.evaluated_get(depsgraph))
    bpy.data.objects.remove(obj)
    bpy.data.meshes.remove(mesh)
    smooth.name = "Shell"
    fill_ends(smooth)
    out = bpy.data.objects.new("Shell", smooth)
    bpy.context.scene.collection.objects.link(out)
    smooth.shade_smooth()
    return out


def fill_ends(mesh):
    """Closes the two openings the subdivided shell has, nose and tail, each with a fan to a
    point set back inside it, so the grille and the diffuser read as recesses. Then winds
    every face outward, which the cage's faces were not all built to do."""
    bm = bmesh.new()
    bm.from_mesh(mesh)
    boundary = {e for e in bm.edges if e.is_boundary}
    while boundary:
        start = boundary.pop()
        loop = [start.verts[0], start.verts[1]]
        while True:
            nxt = [e for e in loop[-1].link_edges if e in boundary]
            if not nxt:
                break
            boundary.discard(nxt[0])
            loop.append(nxt[0].other_vert(loop[-1]))
        if loop[0] == loop[-1]:
            loop.pop()
        centre = sum((v.co for v in loop), Vector()) / len(loop)
        nose = to_road(centre).z > 0
        # Back into the car along its length: Blender's -Y is Unity's forward.
        centre = centre + Vector((0, 0.05 if nose else -0.05, 0))
        hub = bm.verts.new(centre)
        for a, b in zip(loop, loop[1:] + loop[:1]):
            face = bm.faces.new((a, b, hub))
            face.material_index = TRIM
            face.smooth = False
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.to_mesh(mesh)
    bm.free()


# ---- details: gathering ------------------------------------------------------------

def wind_outward(bverts, faces):
    """Rewinds a closed part so its normals face out."""
    bm = bmesh.new()
    bv = [bm.verts.new(v) for v in bverts]
    for f in faces:
        bm.faces.new([bv[k] for k in f])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.verts.index_update()
    out = [tuple(v.index for v in f.verts) for f in bm.faces]
    bm.free()
    return out


def facing(bverts, faces, want):
    """Rewinds an open part's faces so each faces along want(centre) (Blender vectors)."""
    out = []
    for f in faces:
        p = [bverts[k] for k in f]
        n = (p[1] - p[0]).cross(p[2] - p[0])
        c = sum(p, Vector()) / len(p)
        out.append(f if n.dot(want(c)) >= 0 else tuple(reversed(f)))
    return out


class Parts:
    """Geometry gathered by part, in Blender coordinates, to be merged into the two meshes."""

    def __init__(self):
        self.verts, self.faces, self.parts = [], [], []

    def add(self, verts, faces, part, closed=False, road=True, want=None):
        """verts in road-height Unity coordinates, or Blender ones with road=False. A closed
        part is wound outward here; an open one along want, a function of a face's centre
        giving the way it should face, both in road coordinates."""
        bv = [V(*v) if road else Vector(v) for v in verts]
        if closed:
            faces = wind_outward(bv, faces)
        elif want is not None:
            faces = facing(bv, faces, lambda c: wheel.U(*want(to_road(c))))
        base = len(self.verts)
        self.verts.extend(bv)
        self.faces.extend(tuple(base + i for i in f) for f in faces)
        self.parts.extend([part] * len(faces) if isinstance(part, int) else part)

    def both_sides(self, verts, faces, part, closed=False, want=None):
        """A right-side part and its mirror on the left."""
        self.add(verts, faces, part, closed, want=want)
        mirror_want = None if want is None else (lambda c: (lambda w: (-w[0], w[1], w[2]))(want((-c[0], c[1], c[2]))))
        self.add([(-x, y, z) for x, y, z in verts], [tuple(reversed(f)) for f in faces], part, closed,
                 want=mirror_want)

    def build(self, name, keep, slots):
        """An object of the faces whose part is in keep, one material slot per entry of slots."""
        used = {}
        verts, faces, mats = [], [], []
        # In slot order: Unity numbers an FBX's submeshes by the order their materials first
        # appear among the faces, not by the slots.
        order = sorted(range(len(self.faces)), key=lambda i: slots.index(self.parts[i]) if self.parts[i] in keep else -1)
        for i in order:
            f, p = self.faces[i], self.parts[i]
            if p not in keep:
                continue
            idx = []
            for k in f:
                if k not in used:
                    used[k] = len(verts)
                    verts.append(self.verts[k])
                idx.append(used[k])
            faces.append(idx)
            mats.append(slots.index(p))
        mesh = bpy.data.meshes.new(name)
        mesh.from_pydata(verts, [], faces)
        for poly, m in zip(mesh.polygons, mats):
            poly.material_index = m
        for p in slots:
            mesh.materials.append(material(PART_NAMES[p]))
        mesh.validate()
        bm = bmesh.new()
        bm.from_mesh(mesh)
        bm.faces.ensure_lookup_table()
        bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=0.0002)
        bmesh.ops.triangulate(bm, faces=bm.faces[:], quad_method="FIXED", ngon_method="BEAUTY")
        bm.to_mesh(mesh)
        bm.free()
        mesh.shade_smooth()
        mesh.set_sharp_from_angle(angle=math.radians(40))
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        return obj


# ---- details: shapes ---------------------------------------------------------------

def prism(centre, size, corner, axis="z", rotate=None, taper=1.0):
    """A box with its edges along `axis` rounded by `corner`, flat ends; optionally turned
    about x by `rotate` degrees round its centre, its far end scaled by taper. Closed."""
    cx, cy, cz = centre
    a_axes = {"z": (0, 1), "x": (2, 1), "y": (0, 2)}[axis]
    along = {"z": 2, "x": 0, "y": 1}[axis]
    ring2d = wheel.rounded_rect(size[a_axes[0]] * 0.5, size[a_axes[1]] * 0.5, corner, 3)
    verts = []
    for end, scale in ((-0.5, 1.0), (0.5, taper)):
        for a, b in ring2d:
            p = [0.0, 0.0, 0.0]
            p[a_axes[0]], p[a_axes[1]], p[along] = a * scale, b * scale, end * size[along]
            verts.append(p)
    if rotate:
        r = math.radians(rotate)
        verts = [[p[0], p[1] * math.cos(r) - p[2] * math.sin(r), p[1] * math.sin(r) + p[2] * math.cos(r)] for p in verts]
    verts = [(p[0] + cx, p[1] + cy, p[2] + cz) for p in verts]
    m = len(ring2d)
    faces = [(j, (j + 1) % m, m + (j + 1) % m, m + j) for j in range(m)]
    faces += [tuple(range(m)), tuple(m + j for j in range(m))]
    return verts, faces


def swept(path, ring):
    """Rings (ring(k) gives road points) along a path, capped: a closed tube."""
    verts = []
    for k in range(len(path)):
        verts.extend(ring(k))
    m = len(ring(0))
    faces = []
    for k in range(len(path) - 1):
        for j in range(m):
            j1 = (j + 1) % m
            faces.append((k * m + j, k * m + j1, (k + 1) * m + j1, (k + 1) * m + j))
    faces += [tuple(range(m)), tuple((len(path) - 1) * m + j for j in range(m))]
    return verts, faces


def disc(centre, rx, ry, plane, sides=20):
    """A flat ellipse as a fan, in the plane named ('xy' faces z, 'zy' faces x)."""
    cx, cy, cz = centre
    verts = [centre]
    for j in range(sides):
        a = 2 * math.pi * j / sides
        u, v = rx * math.cos(a), ry * math.sin(a)
        verts.append((cx + u, cy + v, cz) if plane == "xy" else (cx, cy + v, cz + u))
    faces = [(0, 1 + j, 1 + (j + 1) % sides) for j in range(sides)]
    return verts, faces


# ---- details: the parts ------------------------------------------------------------

def add_shell(parts, shell_obj):
    """The subdivided shell into parts, less its headlight cells: the lamps (headlamps) sit
    open in those holes. A cover of the car's tinted glass hid them."""
    mesh = shell_obj.data
    verts = [v.co.copy() for v in mesh.vertices]
    polys = [p for p in mesh.polygons if p.material_index != HEADLIGHT]
    parts.add(verts, [tuple(p.vertices) for p in polys], [p.material_index for p in polys], road=False)


def headlamps(parts, shell_obj):
    """In each headlight opening: a flat dark back behind it, black walls to it, a ring of
    daytime running light and a chrome projector in the middle. The opening wraps round
    the wing's corner, so anything following its curve would fold behind itself."""
    bm = bmesh.new()
    bm.from_mesh(shell_obj.data)
    region = [f for f in bm.faces if f.material_index == HEADLIGHT]
    bm.normal_update()
    islands = []
    seen = set()
    for f in region:
        if f in seen:
            continue
        stack, island = [f], []
        seen.add(f)
        while stack:
            g = stack.pop()
            island.append(g)
            for e in g.edges:
                for h in e.link_faces:
                    if h.material_index == HEADLIGHT and h not in seen:
                        seen.add(h)
                        stack.append(h)
        islands.append(island)
    for island in islands:
        fset = set(island)
        normal = sum((f.normal for f in island), Vector()).normalized()
        loop = boundary_loop([e for f in island for e in f.edges if sum(1 for g in e.link_faces if g in fset) == 1])
        rim = [v.co.copy() for v in loop]
        centre = sum(rim, Vector()) / len(rim)
        # A flat back 25 mm behind the opening's deepest point, and each rim point's place on it.
        deepest = min((p - centre).dot(normal) for p in rim)
        back_centre = centre + normal * (deepest - 0.025)
        back = [p - normal * (p - back_centre).dot(normal) for p in rim]
        n = len(rim)
        # The housing's back, and black walls from the opening's rim to it, facing in.
        hv = [back_centre] + back
        hf = facing(hv, [(0, 1 + i, 1 + (i + 1) % n) for i in range(n)], lambda c: normal)
        parts.add(hv, hf, TRIM, road=False)
        wv = [q for pair in zip(rim, back) for q in pair]
        wf = [(2 * i, 2 * ((i + 1) % n), 2 * ((i + 1) % n) + 1, 2 * i + 1) for i in range(n)]
        wf = facing(wv, wf, lambda c: centre - c)
        parts.add(wv, wf, TRIM, road=False)
        # A ring of daytime running light and a chrome projector, just in front of the back.
        lift = normal * 0.004
        rv = []
        for q in back:
            rv += [back_centre + (q - back_centre) * 0.82 + lift, back_centre + (q - back_centre) * 0.7 + lift]
        rf = [(2 * i, 2 * ((i + 1) % n), 2 * ((i + 1) % n) + 1, 2 * i + 1) for i in range(n)]
        rf = facing(rv, rf, lambda c: normal)
        parts.add(rv, rf, HEADLIGHT, road=False)
        side = normal.cross(Vector((0, 0, 1))).normalized()
        up = side.cross(normal).normalized()
        pv = [back_centre + lift] + [back_centre + lift + (side * math.cos(a) + up * math.sin(a)) * 0.034
                                     for a in (2 * math.pi * j / 16 for j in range(16))]
        pf = facing(pv, [(0, 1 + j, 1 + (j + 1) % 16) for j in range(16)], lambda c: normal)
        parts.add(pv, pf, METAL, road=False)
    bm.free()


def boundary_loop(edges):
    """Edges forming one closed loop, as its vertices in order."""
    edges = set(edges)
    start = edges.pop()
    loop = [start.verts[0], start.verts[1]]
    while edges:
        nxt = [e for e in loop[-1].link_edges if e in edges]
        if not nxt:
            break
        edges.discard(nxt[0])
        v = nxt[0].other_vert(loop[-1])
        if v == loop[0]:
            break
        loop.append(v)
    return loop


def surface_lines(parts, shell_obj):
    """Shut lines for the doors, bonnet and engine cover, and the door handles: thin dark
    ribbons laid on the paint, found by casting onto the shell."""
    mesh = shell_obj.data
    bvh = BVHTree.FromPolygons([v.co for v in mesh.vertices], [p.vertices for p in mesh.polygons])

    def ribbon(points, direction, width=0.004, lift=0.0012, part=TRIM, mirror=True):
        dirb = wheel.U(*direction).normalized()
        dense = []
        for a, b in zip(points, points[1:]):
            a, b = Vector(a), Vector(b)
            steps = max(1, int((b - a).length / 0.012))
            dense += [a + (b - a) * (i / steps) for i in range(steps)]
        dense.append(Vector(points[-1]))
        hits = []
        for p in dense:
            origin = V(*p) - dirb * 0.8
            loc, nrm, _, _ = bvh.ray_cast(origin, dirb, 2.0)
            if loc is not None:
                hits.append((loc, nrm))
        if len(hits) < 2:
            return
        verts, faces = [], []
        for i, (loc, nrm) in enumerate(hits):
            a = hits[max(i - 1, 0)][0]
            b = hits[min(i + 1, len(hits) - 1)][0]
            side = nrm.cross(b - a).normalized() * width * 0.5
            verts += [loc + nrm * lift + side, loc + nrm * lift - side]
        for i in range(len(hits) - 1):
            faces.append((2 * i, 2 * i + 2, 2 * i + 3, 2 * i + 1))
        normals = [h[1] for h in hits]
        faces = [f if (verts[f[1]] - verts[f[0]]).cross(verts[f[2]] - verts[f[0]]).dot(normals[f[0] // 2]) >= 0
                 else tuple(reversed(f)) for f in faces]
        road = [to_road(v) for v in verts]
        if mirror:
            parts.both_sides([tuple(v) for v in road], faces, part)
        else:
            parts.add([tuple(v) for v in road], faces, part)

    side_ray = (-1.0, 0.0, 0.0)
    down = (0.0, -1.0, 0.0)
    # Doors: shut lines from the skirt to the window seal; handles, flush.
    for line in D["doors"]:
        ribbon([(1.3, y, z) for z, y in line], side_ray)
    for z0, z1, y in D["handles"]:
        ribbon([(1.3, y, z0), (1.3, y, z1)], side_ray, width=0.024, lift=0.002)
    # Bonnet: its sides along the deck's edge, its front edge above the lights.
    z0, z1, front = D["bonnet"]
    bonnet = []
    for i in range(5):
        z = z0 + (z1 - z0) * i / 4
        bonnet.append((station_params(z)[4] - 0.03, 2.0, z))
    bonnet += [(0.40, 2.0, front - 0.03), (0.0, 2.0, front)]
    ribbon(bonnet, down)
    # Engine cover or boot lid.
    if D["lid"]:
        ribbon([(x, 2.0, z) for x, z in D["lid"]], down)


def mirrors(parts):
    """Door mirrors: a painted housing, flat at the back where the glass is, on a stalk."""
    c = Vector(D["mirror"])
    rx, ry, rz = 0.085, 0.055, 0.11
    rings, segs = 10, 20
    verts = []
    for i in range(1, rings):
        phi = math.pi * i / rings
        for j in range(segs):
            th = 2 * math.pi * j / segs
            d = Vector((math.sin(phi) * math.cos(th), math.cos(phi), math.sin(phi) * math.sin(th)))
            p = Vector((c.x + rx * d.x, c.y + ry * d.y, c.z + rz * d.z))
            p.z = max(p.z, c.z - 0.035)
            verts.append(tuple(p))
    top, bottom = len(verts), len(verts) + 1
    verts += [(c.x, c.y + ry, c.z), (c.x, c.y - ry, c.z)]
    faces = []
    for i in range(rings - 2):
        for j in range(segs):
            j1 = (j + 1) % segs
            faces.append((i * segs + j, i * segs + j1, (i + 1) * segs + j1, (i + 1) * segs + j))
    for j in range(segs):
        faces.append((top, (j + 1) % segs, j))
        last = (rings - 2) * segs
        faces.append((bottom, last + j, last + (j + 1) % segs))
    parts.both_sides(verts, faces, PAINT, closed=True)
    gv, gf = disc((c.x, c.y, c.z - 0.0365), 0.07, 0.042, "xy")
    parts.both_sides(gv, gf, METAL, want=lambda p: (0, 0, -1))
    sv, sf = prism((c.x - 0.07, c.y - 0.025, c.z + 0.03), (0.12, 0.025, 0.05), 0.008, axis="x")
    parts.both_sides(sv, sf, TRIM, closed=True)


def aerofoil(x, zc, yc, chord, aoa, thick=0.12):
    """An inverted aerofoil's section at x, its trailing edge raised by aoa degrees."""
    pts = []
    n = 14
    for i in range(n + 1):
        t = (1 - math.cos(math.pi * i / n)) / 2
        yt = 5 * thick * (0.2969 * math.sqrt(t) - 0.126 * t - 0.3516 * t * t + 0.2843 * t ** 3 - 0.1015 * t ** 4)
        pts.append((t, -yt * 0.35))
    for i in range(n - 1, 0, -1):
        t = (1 - math.cos(math.pi * i / n)) / 2
        yt = 5 * thick * (0.2969 * math.sqrt(t) - 0.126 * t - 0.3516 * t * t + 0.2843 * t ** 3 - 0.1015 * t ** 4)
        pts.append((t, yt * 0.65))
    a = math.radians(aoa)
    out = []
    for t, y in pts:
        dz, dy = chord * (0.5 - t), y * chord
        out.append((x, yc + dy * math.cos(a) - dz * math.sin(a), zc + dz * math.cos(a) + dy * math.sin(a)))
    return out


def rear_wing(parts):
    """The design's wing: a swan-neck wing with end plates (mounts hooked over it from above),
    a low wing on posts, or a spoiler off the back of the roof. None for a ducktail."""
    if D["wing"] is None:
        return
    kind, w = D["wing"]
    zc, yc, chord, span, aoa = w["zc"], w["yc"], w["chord"], w["span"], w["aoa"]
    xs = [-span, -0.3, 0.3, span]
    thick = 0.12 if kind != "roof" else 0.08
    v, f = swept(xs, lambda k: aerofoil(xs[k], zc, yc, chord, aoa, thick))
    parts.add(v, f, TRIM, closed=True)
    if kind == "roof":
        return
    pv, pf = prism((span + 0.008, yc + 0.005, zc - 0.01), (0.008, 0.17, chord + 0.09), 0.02, axis="x")
    parts.both_sides(pv, pf, TRIM, closed=True)
    if kind == "posts":
        deck = station_params(zc)[8]
        qv, qf = prism((w["mount_x"], (deck + yc) * 0.5 - 0.01, zc), (0.014, yc - deck + 0.02, chord * 0.6), 0.005, axis="x")
        parts.both_sides(qv, qf, TRIM, closed=True)
        return
    path = w["neck"]

    def neck(k):
        z, y = path[k]
        a, b = path[max(k - 1, 0)], path[min(k + 1, len(path) - 1)]
        t = Vector((0, b[1] - a[1], b[0] - a[0])).normalized()
        nrm = Vector((0, -t.z, t.y))
        wd = 0.035 - 0.01 * k / (len(path) - 1)
        p = Vector((w["mount_x"], y, z))
        return [tuple(p + Vector((dx, 0, 0)) + nrm * dn) for dx, dn in ((0.007, wd), (-0.007, wd), (-0.007, -wd), (0.007, -wd))]

    nv, nf = swept(path, neck)
    parts.both_sides(nv, nf, TRIM, closed=True)


def exhausts(parts):
    """The design's tailpipes, through the diffuser."""
    xs, y0, r0, tip = D["exhausts"]
    for x in xs:
        ri = r0 - 0.007
        prof = [(0.002, tip + 0.21), (r0, tip + 0.21), (r0, tip), (ri, tip), (ri, tip + 0.04), (0.002, tip + 0.04)]
        n = 20
        verts = []
        for r, z in prof:
            for j in range(n):
                a = 2 * math.pi * j / n
                verts.append((x + r * math.cos(a), y0 + r * math.sin(a), z))
        faces = []
        for i in range(len(prof) - 1):
            for j in range(n):
                j1 = (j + 1) % n
                faces.append((i * n + j, i * n + j1, (i + 1) * n + j1, (i + 1) * n + j))
        parts.add(verts, faces, METAL, closed=True)
        dv, df = disc((x, y0, tip + 0.0405), ri - 0.001, ri - 0.001, "xy")
        parts.add(dv, df, TRIM, want=lambda p: (0, 0, -1))


def wheel_wells(parts):
    """A black tub over each wheel, so the arch shows the well and not the car's inside."""
    for axle in (FRONT_AXLE, REAR_AXLE):
        r = ARCH_R + 0.012
        outline = [(axle + r + 0.004, 0.24)]
        for i in range(19):
            th = math.radians(-90 + 10 * i)
            outline.append((axle - r * math.sin(th), TYRE_R + r * math.cos(th)))
        outline.append((axle - r - 0.004, 0.24))
        x0, x1 = 0.50, 0.95         # the tyre's outer face is at 0.93, the body's at 0.97
        verts = [(x0, y, z) for z, y in outline] + [(x1, y, z) for z, y in outline]
        n = len(outline)
        faces = [(i, i + 1, n + i + 1, n + i) for i in range(n - 1)]
        centre = (axle, TYRE_R)
        parts.both_sides(verts, faces, TRIM, want=lambda p, c=centre: (0, c[1] - p[1], c[0] - p[2]))
        wall = [(x0, y, z) for z, y in outline]
        parts.both_sides(wall, [tuple(range(n))], TRIM, want=lambda p: (1, 0, 0))


def interior(parts):
    """What shows through the glass and from the cockpit camera: floor, dashboard with a
    binnacle, steering wheel, two bucket seats, the tunnel, and behind them a bulkhead, a
    back seat or a parcel shelf. Laid out from the driver's eye and the windscreen's foot,
    the way a real cabin is packaged round its driver."""
    cab = D["cabin"]
    ex, ey, ez = cab["eye"]
    cowl_z = cab["cowl"]
    cowl_y = station_params(cowl_z)[8]
    add = lambda vf: parts.add(vf[0], vf[1], INTERIOR, closed=True)

    # The steering wheel 56 cm ahead of the eye and 22 below; the dash from just behind
    # its rim to the windscreen's foot, kept under the glass.
    centre = Vector((ex, ey - 0.22, ez + 0.56))
    z_r, z_f = centre.z + 0.12, max(cowl_z + 0.01, centre.z + 0.30)
    y_t = min(centre.y + 0.085, cowl_y - 0.03)
    dash = [(z_f, y_t - 0.285), (z_f + 0.01, y_t - 0.045), (max(z_f - 0.11, z_r + 0.10), y_t), (z_r + 0.08, y_t - 0.015),
            (z_r, y_t - 0.085), (z_r + 0.02, y_t - 0.185), (z_r + 0.14, y_t - 0.255)]
    xs = [-0.80, 0.80]
    add(swept(xs, lambda k: [(xs[k], y, z) for z, y in dash]))
    add(prism((ex, y_t + 0.04, z_r + 0.13), (0.34, 0.09, 0.16), 0.03))                  # binnacle
    add(prism((0.0, (0.19 + y_t - 0.135) * 0.5, z_f + 0.03), (1.56, y_t - 0.325, 0.03), 0.005))   # footwell wall

    seat_y, seat_z = ey - 0.72, ez + 0.10
    back = min(z for z in (cab["bulkhead"], cab["rear_seat"], cab["parcel"] and cab["parcel"][0]) if z is not None)
    add(prism((0.0, 0.19, (back + z_f + 0.2) * 0.5), (1.60, 0.02, z_f + 0.2 - back), 0.005))   # floor
    add(prism((0.0, seat_y + 0.01, (seat_z - 0.2 + z_f - 0.05) * 0.5), (0.22, 0.24, z_f - 0.05 - seat_z + 0.2), 0.04))  # tunnel
    for x in (ex, -ex):
        add(prism((x, seat_y, seat_z), (0.50, 0.12, 0.52), 0.04))                                  # cushion
        add(prism((x, seat_y + 0.38, seat_z - 0.30), (0.52, 0.72, 0.12), 0.05, rotate=-14))         # back
        for s in (-1, 1):
            add(prism((x + s * 0.23, seat_y + 0.25, seat_z - 0.22), (0.07, 0.40, 0.22), 0.03, rotate=-14))  # bolsters
    if cab["rear_seat"] is not None:
        rz = cab["rear_seat"]
        add(prism((0.0, seat_y + 0.02, rz), (1.30, 0.12, 0.45), 0.04))
        add(prism((0.0, seat_y + 0.36, rz - 0.28), (1.30, 0.60, 0.12), 0.05, rotate=-12))
    if cab["bulkhead"] is not None:
        # The body's own section there, 3 cm in and cut at the floor: a flat panel poked out
        # through the pillars where the body narrows towards the roof.
        bz = cab["bulkhead"]
        half = [(x * 0.95 - 0.02, max(y - 0.03, 0.19)) for x, y in profile(bz)]
        outline = [(x, y) for x, y in half[:-1]] + [(-x, y) for x, y in reversed(half)]
        verts = [(x, y, bz + dz) for dz in (-0.015, 0.015) for x, y in outline]
        n = len(outline)
        faces = [tuple(range(n)), tuple(range(n, 2 * n))] + [(i, (i + 1) % n, n + (i + 1) % n, n + i) for i in range(n)]
        add((verts, faces))
    if cab["parcel"] is not None:
        z0, z1, py = cab["parcel"]
        add(prism((0.0, py, (z0 + z1) * 0.5), (1.50, 0.02, z1 - z0), 0.005))

    # Steering wheel: rim, three spokes and hub, column to the dash, facing the driver.
    radius, tilt = 0.175, math.radians(22)
    axis = Vector((0, math.sin(tilt), -math.cos(tilt)))            # towards the driver
    u = Vector((1, 0, 0))
    w = axis.cross(u).normalized()
    ring = []
    n, m = 32, 8
    for i in range(n):
        a = 2 * math.pi * i / n
        rc = centre + (u * math.cos(a) + w * math.sin(a)) * radius
        radial = (rc - centre).normalized()
        for j in range(m):
            b = 2 * math.pi * j / m
            ring.append(tuple(rc + (radial * math.cos(b) + axis * math.sin(b)) * 0.017))
    faces = [(i * m + j, i * m + (j + 1) % m, ((i + 1) % n) * m + (j + 1) % m, ((i + 1) % n) * m + j)
             for i in range(n) for j in range(m)]
    parts.add(ring, faces, INTERIOR, closed=True)
    hv, hf = prism(tuple(centre), (0.09, 0.09, 0.05), 0.02, axis="z", rotate=-68)
    add((hv, hf))
    for a in (0.0, math.pi, -math.pi / 2):
        tip = centre + (u * math.cos(a) + w * math.sin(a)) * radius
        mid = (centre + tip) * 0.5
        length = (tip - centre).length
        d = (tip - centre).normalized()
        # A flat spoke: a thin box along d.
        s = axis.cross(d).normalized() * 0.018
        t = axis * 0.008
        corners = [mid + d * (length * 0.5) * e + s * f + t * g for e in (-1, 1) for f in (-1, 1) for g in (-1, 1)]
        box_faces = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
        parts.add([tuple(c) for c in corners], box_faces, INTERIOR, closed=True)
    col = [tuple(centre + Vector((0, 0.03, 0.04))), (ex, y_t - 0.045, z_r + 0.15)]
    cv, cf = swept(col, lambda k: [(col[k][0] + 0.025 * math.cos(a), col[k][1] + 0.025 * math.sin(a), col[k][2])
                                   for a in (2 * math.pi * j / 10 for j in range(10))])
    add((cv, cf))


def lining(parts, shell_obj):
    """The shell once more, a coarser copy turned inside out, where the cabin can be seen:
    the insides of the doors, the pillars and the headlining. Each of its points goes onto
    the smooth shell and then 15 mm in: the coarse copy itself lies outside the smooth one
    wherever the body is concave, and showed through it as black fins."""
    smooth = shell_obj.data
    bvh = BVHTree.FromPolygons([v.co for v in smooth.vertices], [p.vertices for p in smooth.polygons])
    obj = shell(1)
    mesh = obj.data
    mesh.update()
    keep = []
    for p in mesh.polygons:
        c = to_road(p.center)
        z0, z1 = D["cabin"]["lining"]
        if p.material_index in (GLASS, HEADLIGHT, TAILLIGHT) or not (z0 < c.z < z1) or c.y < 0.22:
            continue
        keep.append(p)
    verts = []
    for v in mesh.vertices:
        loc, nrm, _, _ = bvh.find_nearest(v.co)
        verts.append(loc - nrm * 0.015 if loc is not None else v.co - v.normal * 0.015)
    faces = [tuple(reversed(p.vertices)) for p in keep]
    parts.add(verts, faces, INTERIOR, road=False)
    bpy.data.objects.remove(obj)


# ---- materials (for the preview; Unity uses its own) -----------------------------

_MATS = {}


def material(name):
    if name in _MATS:
        return _MATS[name]
    looks = {
        "Paint": ((0.02, 0.06, 0.32), 0.3, 0.18),
        "Glass": ((0.02, 0.025, 0.03), 0.0, 0.03),
        "Trim": ((0.018, 0.018, 0.02), 0.0, 0.55),
        "Headlight": ((0.9, 0.9, 0.85), 0.0, 0.2),
        "Taillight": ((0.5, 0.0, 0.0), 0.0, 0.25),
        "Interior": ((0.05, 0.05, 0.055), 0.0, 0.7),
        "Metal": ((0.8, 0.8, 0.82), 1.0, 0.15),
    }
    colour, metallic, rough = looks[name]
    mat = wheel.principled(name, colour, metallic, rough)
    bsdf = wheel.node_tree(mat).nodes.get("Principled BSDF")
    if name == "Paint":
        bsdf.inputs["Coat Weight"].default_value = 1.0
        bsdf.inputs["Coat Roughness"].default_value = 0.03
    if name in ("Headlight", "Taillight"):
        bsdf.inputs["Emission Color"].default_value = (*colour, 1.0)
        bsdf.inputs["Emission Strength"].default_value = 3.0
    if name == "Glass":
        bsdf.inputs["Alpha"].default_value = 0.55
    _MATS[name] = mat
    return mat


# ---- preview -----------------------------------------------------------------------

def place_wheels():
    """wheel.py's parts at the four corners, for the renders only."""
    parts = [wheel.game_tyre(), wheel.game_rim(), wheel.game_disc()]
    looks = [((0.035, 0.035, 0.038), 0.0, 0.75), ((0.8, 0.81, 0.83), 1.0, 0.22), ((0.42, 0.42, 0.43), 1.0, 0.45)]
    for part, (c, m, r) in zip(parts, looks):
        part.data.materials.clear()
        part.data.materials.append(wheel.principled(part.name + " Preview", c, m, r))
    for x, z in ((TRACK / 2, FRONT_AXLE), (-TRACK / 2, FRONT_AXLE), (TRACK / 2, REAR_AXLE), (-TRACK / 2, REAR_AXLE)):
        for part in parts:
            copy = part.copy()
            bpy.context.scene.collection.objects.link(copy)
            copy.location = V(x, TYRE_R, z)
            copy.rotation_euler = (0, 0, math.pi if x < 0 else 0)
    for part in parts:
        bpy.data.objects.remove(part)


def render_views(prefix):
    scene = bpy.context.scene
    world = bpy.data.worlds.new("World")
    scene.world = world
    bg = wheel.node_tree(world).nodes.get("Background")
    bg.inputs["Color"].default_value = (0.6, 0.66, 0.75, 1.0)
    bg.inputs["Strength"].default_value = 0.8
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 3.0
    sun.data.angle = math.radians(8)
    sun.rotation_euler = (math.radians(40), 0, math.radians(-150))
    scene.collection.objects.link(sun)
    floor = bpy.data.meshes.new("Floor")
    floor.from_pydata([V(-8, 0, -8), V(8, 0, -8), V(8, 0, 8), V(-8, 0, 8)], [], [(0, 1, 2, 3)])
    fobj = bpy.data.objects.new("Floor", floor)
    fobj.data.materials.append(wheel.principled("Road", (0.08, 0.08, 0.085), 0.0, 0.9))
    scene.collection.objects.link(fobj)

    cam = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
    cam.data.lens = 50
    scene.collection.objects.link(cam)
    scene.camera = cam
    scene.render.resolution_x, scene.render.resolution_y = 1600, 900
    scene.cycles.samples = 48
    scene.cycles.use_denoising = True
    views = {
        "front": ((4.2, 1.5, 5.6), (0.0, 0.55, 0.3), 50),
        "rear": ((-4.0, 1.8, -5.4), (0.0, 0.6, -0.3), 50),
        "side": ((16.0, 0.8, 0.0), (0.0, 0.6, 0.0), 110),     # far and long, for true proportions
    }
    for name, (eye, look, lens) in views.items():
        cam.data.lens = lens
        cam.location = V(*eye)
        direction = V(*look) - cam.location
        cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = f"{prefix}-{name}.png"
        bpy.ops.render.render(write_still=True)
        print("CAR preview", scene.render.filepath)


def build(name, args):
    """One design: its body and details, exported and rendered as asked."""
    global D
    D = DESIGNS[name]
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj)
    for mesh in list(bpy.data.meshes):
        bpy.data.meshes.remove(mesh)

    body_shell = shell(2)
    parts = Parts()
    add_shell(parts, body_shell)
    headlamps(parts, body_shell)
    surface_lines(parts, body_shell)
    mirrors(parts)
    rear_wing(parts)
    exhausts(parts)
    wheel_wells(parts)
    interior(parts)
    lining(parts, body_shell)
    bpy.data.objects.remove(body_shell)

    body = parts.build("Body", {PAINT}, [PAINT])
    details = parts.build("Details", set(DETAIL_PARTS), list(DETAIL_PARTS))
    cabin = parts.build("Interior", {INTERIOR}, [INTERIOR])
    for obj in (body, details, cabin):
        counts = {}
        for p in obj.data.polygons:
            part = obj.data.materials[p.material_index].name
            counts[part] = counts.get(part, 0) + 1
        print("CAR", name, "triangles", obj.name, sum(counts.values()), counts)

    if args.out:
        os.makedirs(args.out, exist_ok=True)
        bpy.ops.object.select_all(action="DESELECT")
        for obj in (body, details, cabin):
            obj.select_set(True)
        path = os.path.join(args.out, f"{name}.fbx")
        bpy.ops.export_scene.fbx(
            filepath=path, use_selection=True,
            object_types={"MESH"}, apply_scale_options="FBX_SCALE_UNITS",
            axis_forward="-Z", axis_up="Y", bake_space_transform=True,
            mesh_smooth_type="FACE", use_tspace=False, use_mesh_modifiers=True,
            path_mode="STRIP", embed_textures=False, add_leaf_bones=False, bake_anim=False)
        print("CAR exported", path)

    slug = name.lower().replace(" ", "-")
    if args.preview:
        place_wheels()
        render_views(f"{args.preview}-{slug}")
    if args.blend:
        bpy.ops.wm.save_as_mainfile(filepath=args.blend.replace(".blend", f"-{slug}.blend"))


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument("--design", choices=list(DESIGNS), help="one design; all of them if left out")
    parser.add_argument("--out")
    parser.add_argument("--preview")
    parser.add_argument("--blend")
    args = parser.parse_args(argv)
    wheel.setup_cycles()
    for name in [args.design] if args.design else list(DESIGNS):
        build(name, args)


if __name__ == "__main__":
    main()
