"""Scenery for the circuits, built in Blender: trackside pieces, landmarks and the horizon.

Run headless from WSL (Blender takes Windows paths):

    /mnt/d/Software/Blender/blender.exe -b --factory-startup \
        -P "$(wslpath -w Tools/blender/scenery.py)" -- \
        --out 'D:\\Dev\\CarRace\\Assets\\Art\\Scenery' --preview "$(wslpath -w Tools/out/scenery)"

Writes one FBX per model to --out (and the textures it generates), and with --preview a
render of each to <prefix>-<model>.png. --only name,name builds just those.

Conventions, as StructureModels': a model's base is at y 0, its front (the side facing the
track) towards -z, in metres. Its materials are named for the Unity materials
TracksideBuilder puts on them (MATERIALS), and its texture coordinates are metres over each
material's real size, projected along whichever axis a face most faces, so a texture runs on
across faces at its true scale.
"""

import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import wheel  # noqa: E402  U(), materials and render setup
import car  # noqa: E402  prism, swept, disc, wind_outward

# Material name: (metres one texture repeat covers, preview colour, metallic, roughness).
MATERIALS = {
    "Concrete": (3.0, (0.55, 0.55, 0.52), 0.0, 0.85),
    "Steel": (1.2, (0.62, 0.64, 0.66), 0.8, 0.4),
    "DarkSteel": (2.0, (0.12, 0.12, 0.13), 0.6, 0.5),
    "White": (1.0, (0.85, 0.85, 0.83), 0.0, 0.5),
    "Orange": (1.0, (0.9, 0.35, 0.05), 0.0, 0.5),
    "Rubber": (1.0, (0.03, 0.03, 0.03), 0.0, 0.8),
    "BeltRed": (1.0, (0.6, 0.04, 0.03), 0.0, 0.6),
    "BeltWhite": (1.0, (0.8, 0.8, 0.78), 0.0, 0.6),
    "Plaster": (3.0, (0.85, 0.72, 0.5), 0.0, 0.8),
    "Stone": (3.0, (0.82, 0.8, 0.74), 0.0, 0.8),
    "RoofTiles": (2.0, (0.45, 0.2, 0.12), 0.0, 0.7),
    "Glass": (1.0, (0.05, 0.07, 0.09), 0.3, 0.1),
    "Roof": (3.0, (0.35, 0.35, 0.34), 0.0, 0.8),
    "Alps": (1.0, (0.55, 0.6, 0.7), 0.0, 0.9),       # horizons: their own texture coordinates
    "Ridges": (1.0, (0.2, 0.26, 0.27), 0.0, 0.9),
    "Dunes": (1.0, (0.78, 0.66, 0.5), 0.0, 0.9),
    "Timber": (2.0, (0.35, 0.22, 0.12), 0.0, 0.7),
    "Slate": (2.0, (0.2, 0.21, 0.24), 0.0, 0.6),
    "Sandstone": (3.0, (0.82, 0.68, 0.48), 0.0, 0.85),
    "Red": (1.0, (0.7, 0.06, 0.05), 0.0, 0.4),
    "Blue": (1.0, (0.08, 0.25, 0.7), 0.0, 0.4),
    "Yellow": (1.0, (0.95, 0.75, 0.1), 0.0, 0.4),
    "NeonPink": (1.0, (1.0, 0.1, 0.6), 0.0, 0.3),
    "NeonCyan": (1.0, (0.1, 0.9, 1.0), 0.0, 0.3),
    "Corrugated": (2.0, (0.75, 0.77, 0.8), 0.6, 0.45),
    "Seats": (1.0, (0.08, 0.22, 0.62), 0.0, 0.5),
}


# ---- building blocks ---------------------------------------------------------------

class Model:
    """Faces gathered by material, in road coordinates (y up from the ground), then built."""

    def __init__(self, name):
        self.name = name
        self.verts, self.faces, self.mats = [], [], []

    def add(self, vf, material, closed=True):
        verts, faces = vf
        bv = [wheel.U(*v) for v in verts]
        if closed:
            faces = car.wind_outward(bv, faces)
        base = len(self.verts)
        self.verts.extend(bv)
        self.faces.extend(tuple(base + k for k in f) for f in faces)
        self.mats.extend([material] * len(faces))

    def box(self, centre, size, material, rotate=None):
        """An axis-aligned box, or turned about x by rotate degrees."""
        cx, cy, cz = centre
        sx, sy, sz = (s * 0.5 for s in size)
        verts = [(cx + dx * sx, cy + dy * sy, cz + dz * sz) for dx in (-1, 1) for dy in (-1, 1) for dz in (-1, 1)]
        if rotate:
            r = math.radians(rotate)
            verts = [(x, cy + (y - cy) * math.cos(r) - (z - cz) * math.sin(r), cz + (y - cy) * math.sin(r) + (z - cz) * math.cos(r))
                     for x, y, z in verts]
        faces = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
        self.add((verts, faces), material)

    def cylinder(self, a, b, radius, material, sides=12, caps=True):
        """A cylinder from a to b (road points)."""
        a, b = Vector(a), Vector(b)
        axis = (b - a).normalized()
        u = axis.cross(Vector((0, 1, 0)) if abs(axis.y) < 0.9 else Vector((1, 0, 0))).normalized()
        w = axis.cross(u)
        ring = lambda c: [tuple(c + (u * math.cos(t) + w * math.sin(t)) * radius)
                          for t in (2 * math.pi * j / sides for j in range(sides))]
        verts = ring(a) + ring(b)
        faces = [(j, (j + 1) % sides, sides + (j + 1) % sides, sides + j) for j in range(sides)]
        if caps:
            faces += [tuple(range(sides)), tuple(range(sides, 2 * sides))]
        self.add((verts, faces), material, closed=caps)

    def build(self, uvs=None):
        """The object, its texture coordinates box-projected unless uvs gives one per vertex."""
        names = []
        for m in self.mats:
            if m not in names:
                names.append(m)
        mesh = bpy.data.meshes.new(self.name)
        mesh.from_pydata(self.verts, [], self.faces)
        for poly, m in zip(mesh.polygons, self.mats):
            poly.material_index = names.index(m)
        for n in names:
            mesh.materials.append(preview_material(n))
        mesh.validate()
        if uvs is not None:
            layer = mesh.uv_layers.new(name="UVMap")
            for li, loop in enumerate(mesh.loops):
                layer.data[li].uv = uvs[loop.vertex_index]
        bm = bmesh.new()
        bm.from_mesh(mesh)
        if uvs is None:
            bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=0.0002)
        bmesh.ops.triangulate(bm, faces=bm.faces[:], quad_method="FIXED", ngon_method="BEAUTY")
        bm.to_mesh(mesh)
        bm.free()
        if uvs is None:
            box_uv(mesh)
        mesh.shade_smooth()
        mesh.set_sharp_from_angle(angle=math.radians(35))
        obj = bpy.data.objects.new(self.name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        return obj


def box_uv(mesh):
    """Texture coordinates in metres over each material's repeat, projected along the axis
    each face most faces."""
    layer = mesh.uv_layers.new(name="UVMap")
    for poly in mesh.polygons:
        tile = MATERIALS[mesh.materials[poly.material_index].name][0]
        n = poly.normal
        ax = max(range(3), key=lambda k: abs(n[k]))
        for li in poly.loop_indices:
            co = mesh.vertices[mesh.loops[li].vertex_index].co
            u, v = [(co.y, co.z), (co.x, co.z), (co.x, co.y)][ax]
            layer.data[li].uv = (u / tile, v / tile)


_PREVIEW = {}


def preview_material(name):
    if name not in _PREVIEW:
        _, colour, metal, rough = MATERIALS[name]
        mat = wheel.principled(name, colour, metal, rough)
        mat.name = name
        _PREVIEW[name] = mat
    return _PREVIEW[name]


# ---- trackside ---------------------------------------------------------------------

def armco_post():
    """A guardrail post, both rails bolted to its front on spacer blocks: its top level with
    the upper rail's, 1.1 m up, set into the ground."""
    m = Model("armco_post")
    m.box((0, 0.5, 0.0), (0.1, 1.2, 0.15), "Steel")           # the post, a stand-in for its C section
    for y in (0.575, 0.935):
        m.box((0, y, -0.1), (0.12, 0.28, 0.08), "DarkSteel")  # spacer blocks, one per rail
    return m


def tyre_bundle(belt):
    """A tyre wall module, 1.2 m wide: two stacks of five tyres bolted together, faced with a
    conveyor belt in the given colour."""
    m = Model(f"tyre_{belt.lower()}")
    r_out, r_in, h = 0.3, 0.19, 0.2
    for x in (-0.3, 0.3):
        # A stack as one lathe: the tyres' outer walls with a groove between each, then its
        # top ring down to the hole. Cheap: the belt hides its front, and a corner has dozens.
        prof = []
        for k in range(5):
            prof += [(r_out - 0.03, k * h + 0.005), (r_out, k * h + 0.04), (r_out, k * h + h - 0.04)]
        prof += [(r_out - 0.03, 5 * h), (r_in, 5 * h - 0.02), (r_in, 5 * h - 0.3)]
        sides = 8
        verts = [(x + r * math.cos(t), y, 0.35 + r * math.sin(t)) for r, y in prof for t in (2 * math.pi * j / sides for j in range(sides))]
        n = len(prof)
        faces = [(i * sides + j, i * sides + (j + 1) % sides, (i + 1) * sides + (j + 1) % sides, (i + 1) * sides + j)
                 for i in range(n - 1) for j in range(sides)]
        faces += [tuple(range(sides)), tuple((n - 1) * sides + j for j in range(sides))]
        m.add((verts, faces), "Rubber")
    # The belt across the front, 1 m tall and 10 mm thick, bowed over the stacks.
    front = []
    for i in range(13):
        x = -0.6 + i * 0.1
        front.append((x, 0.35 - r_out - 0.02 - 0.015 * math.cos((x + 0.3) / 0.3 * math.pi)))
    k = len(front)
    ring = [(x, z) for x, z in front] + [(x, z + 0.01) for x, z in reversed(front)]
    verts = [(x, y, z) for y in (0.02, 1.02) for x, z in ring]
    m2 = len(ring)
    faces = [(j, (j + 1) % m2, m2 + (j + 1) % m2, m2 + j) for j in range(m2)] + [tuple(range(m2)), tuple(range(m2, 2 * m2))]
    m.add((verts, faces), f"Belt{belt}")
    pts = front
    for x in (-0.45, -0.15, 0.15, 0.45):
        m.cylinder((x, 0.9, pts[0][1] - 0.02), (x, 0.9, pts[0][1] + 0.1), 0.02, "DarkSteel", sides=6)
    return m


def fence_post():
    """A catch fence post 4 m tall, its top 0.8 m leaning towards the track to catch debris,
    on a base plate. The fence itself is a textured strip TracksideBuilder runs between them."""
    m = Model("fence_post")
    m.cylinder((0, 0.0, 0), (0, 3.3, 0), 0.05, "Steel", sides=10)
    m.cylinder((0, 3.3, 0), (0, 4.0, -0.55), 0.045, "Steel", sides=10)
    m.box((0, 0.02, 0), (0.3, 0.04, 0.3), "DarkSteel")
    return m


def marshal_post():
    """A marshal post: a concrete shelter with a roof, painted orange and white, a flag
    board and a light panel facing the track, and a ladder to the roof."""
    m = Model("marshal_post")
    w, d, h = 2.6, 2.0, 2.4
    m.box((0, 0.5, 0), (w, 1.0, d), "White")                    # lower wall
    m.box((0, 1.05, -d / 2 + 0.05), (w, 0.1, 0.12), "Orange")    # stripe
    for x in (-w / 2 + 0.1, w / 2 - 0.1):                       # corner posts
        for z in (-d / 2 + 0.1, d / 2 - 0.1):
            m.box((x, h / 2, z), (0.14, h, 0.14), "White")
    m.box((0, h + 0.08, 0.05), (w + 0.4, 0.16, d + 0.5), "Orange")   # roof
    m.box((0, h + 0.2, 0.05), (w + 0.2, 0.08, d + 0.3), "Roof")
    m.box((0.7, 1.9, -d / 2 - 0.05), (0.7, 0.5, 0.06), "DarkSteel")  # light panel
    for x in (-0.35, 0.35):
        m.cylinder((0.7 + x * 0.4, 1.9, -d / 2 - 0.09), (0.7 + x * 0.4, 1.9, -d / 2 - 0.1), 0.1, "Orange", sides=10)
    m.box((-0.8, 1.4, -d / 2 - 0.05), (0.6, 0.02, 0.4), "DarkSteel")   # flag board
    m.cylinder((-w / 2 - 0.2, 0, 0.3), (-w / 2 - 0.2, h + 0.3, 0.3), 0.025, "DarkSteel", sides=6)
    m.cylinder((-w / 2 - 0.2, 0, -0.1), (-w / 2 - 0.2, h + 0.3, -0.1), 0.025, "DarkSteel", sides=6)
    for k in range(8):
        y = 0.3 + k * 0.3
        m.cylinder((-w / 2 - 0.2, y, 0.3), (-w / 2 - 0.2, y, -0.1), 0.015, "DarkSteel", sides=6)
    return m


def chain_link(path, size=512, cells=8):
    """A chain-link fence texture: galvanised wire in a diamond mesh, transparent between,
    `cells` diamonds across one repeat (0.5 m: 60 mm mesh)."""
    y, x = np.mgrid[0:size, 0:size] / size * cells
    a = np.abs(((x + y) % 1.0) - 0.5)
    b = np.abs(((x - y) % 1.0) - 0.5)
    wire = np.clip(1.0 - np.minimum(a, b) * size / cells / 1.6, 0.0, 1.0)
    shade = 0.62 + 0.25 * np.clip(1 - np.minimum(a, b) * 6, 0, 1)
    rgba = np.zeros((size, size, 4), dtype=np.float32)
    rgba[..., 0] = shade * 0.95
    rgba[..., 1] = shade * 0.97
    rgba[..., 2] = shade
    rgba[..., 3] = wire
    img = bpy.data.images.new("Chainlink", size, size, alpha=True)
    img.pixels.foreach_set(rgba.ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()


TRACKSIDE = {
    "armco_post": armco_post,
    "tyre_red": lambda: tyre_bundle("Red"),
    "tyre_white": lambda: tyre_bundle("White"),
    "fence_post": fence_post,
    "marshal_post": marshal_post,
}


# ---- the circuit's buildings -------------------------------------------------------

def rod(m, a, b, radius, material):
    m.cylinder(a, b, radius, material, sides=6)


def pit_bay():
    """One bay of the pit building, 9 m wide and meant to stand in a row: a garage with a
    roller shutter between concrete pillars, a lintel carrying a dark sponsor band, and over
    it a glass-fronted hospitality floor with a balcony, a glass balustrade and handrail, and
    an overhanging roof with a white fascia."""
    m = Model("pit_bay")
    w, front, back = 9.0, -3.5, 6.0
    m.box((0, 2.4, (front + back) / 2 + 0.1), (w, 4.8, back - front - 0.2), "Concrete")   # the garage's shell
    m.box((0, 2.1, front - 0.02), (7.8, 4.2, 0.05), "Corrugated")                        # roller shutter
    for x in (-4.2, 4.2):
        m.box((x, 2.4, front - 0.1), (0.6, 4.8, 0.5), "Concrete")                       # pillars
    m.box((0, 4.5, front - 0.15), (w, 0.6, 0.4), "White")                               # lintel
    m.box((0, 4.5, front - 0.36), (w, 0.36, 0.02), "DarkSteel")                         # sponsor band
    m.box((0, 5.0, (front - 2.0 + back) / 2), (w, 0.4, back - front + 2.0), "Concrete")  # first floor, balcony forward
    m.box((0, 5.75, front - 1.95), (w, 1.1, 0.03), "Glass")                             # glass balustrade
    rod(m, (-w / 2, 6.35, front - 1.95), (w / 2, 6.35, front - 1.95), 0.03, "Steel")
    for x in (-4.4, 0.0):
        rod(m, (x, 5.2, front - 1.95), (x, 6.35, front - 1.95), 0.025, "Steel")
    m.box((0, 6.7, front), (w, 3.0, 0.06), "Glass")                                     # curtain wall
    for i in range(7):
        x = -4.5 + i * 1.5
        m.box((x, 6.7, front - 0.05), (0.08, 3.0, 0.1), "DarkSteel")                     # mullions
    m.box((0, 6.7, (front + back) / 2 + 0.2), (w, 3.0, back - front - 0.4), "White")     # the floor's walls behind
    m.box((0, 8.4, (front - 3.0 + back) / 2), (w, 0.4, back - front + 3.0), "Roof")      # roof, overhanging
    m.box((0, 8.35, front - 3.05), (w, 0.6, 0.12), "White")                              # fascia
    return m


def stand_bay():
    """One bay of the main grandstand, 12 m wide and meant to stand in a row: twelve rows of
    seats on a concrete rake from a front wall, aisle steps with handrails up the middle, a
    back wall with a glazed hospitality strip along its top, and a sheeted roof on two steel
    trusses cantilevered from columns at the back, with a white fascia and a dark band."""
    m = Model("stand_bay")
    rows, tread, rise, first, front = 12, 0.85, 0.45, 1.8, -6.0
    m.box((0, 1.15, front - 0.15), (12, 2.3, 0.3), "Concrete")
    for r in range(rows):
        top, z = first + r * rise, front + (r + 0.5) * tread
        m.box((0, top / 2, z), (12, top, tread), "Concrete")
        for x0, x1 in ((-5.95, -0.6), (0.6, 5.95)):
            cx, span = (x0 + x1) / 2, x1 - x0
            m.box((cx, top + 0.42, z + 0.02), (span, 0.07, 0.4), "Seats")
            m.box((cx, top + 0.68, z + 0.24), (span, 0.46, 0.06), "Seats", rotate=12)
        m.box((0, top + rise / 2, z - tread / 4), (1.1, 0.04, tread / 2), "Concrete")     # a half step in the aisle
    back = front + rows * tread
    top = first + rows * rise
    for x in (-0.6, 0.6):
        rod(m, (x, first + 1.0, front + 0.2), (x, top + 1.0, back - 0.2), 0.025, "Steel")
    height = top + 3.2
    m.box((0, height / 2, back + 0.25), (12, height, 0.5), "Concrete")
    m.box((0, top + 1.8, back - 0.02), (12, 1.8, 0.06), "Glass")                          # hospitality boxes
    roof_y = height + 1.2
    for x in (-5.7, 5.7):
        m.box((x, roof_y / 2, back + 0.8), (0.4, roof_y, 0.4), "Steel")
        # A truss: top and bottom chords out over the seats, diagonals between.
        a_top, a_bot = (x, roof_y + 1.0, back + 0.8), (x, roof_y - 0.6, back + 0.8)
        tip = (x, roof_y, front - 0.8)
        rod(m, a_top, tip, 0.09, "Steel")
        rod(m, a_bot, tip, 0.09, "Steel")
        for i in range(1, 6):
            t0, t1 = (i - 1) / 6, i / 6
            p = lambda a, t: tuple(a[k] + (tip[k] - a[k]) * t for k in range(3))
            rod(m, p(a_bot, t0), p(a_top, t1), 0.05, "Steel")
    depth = back + 1.1 - (front - 0.8)
    m.box((0, roof_y + 0.05, (back + 1.1 + front - 0.8) / 2), (12, 0.12, depth), "Corrugated")
    m.box((0, roof_y - 0.1, front - 0.85), (12, 0.7, 0.1), "White")
    m.box((0, roof_y - 0.1, front - 0.91), (12, 0.3, 0.02), "DarkSteel")
    return m


BUILDINGS = {
    "pit_bay": pit_bay,
    "stand_bay": stand_bay,
}


# ---- landmarks ---------------------------------------------------------------------

def villa():
    """A neoclassical villa in the manner of Monza's Villa Reale: a main block 90 m long of
    three storeys and an attic, two wings forward from its ends round a courtyard that opens
    towards the track, ochre plaster over a stone base, stone cornices and window surrounds,
    a portico of six columns under a pediment in the middle, and tiled hipped roofs."""
    m = Model("villa")
    storey, base = 5.0, 1.2
    top = base + 3 * storey

    def block(x0, x1, z0, z1, windows_on):
        """A block from x0 to x1 and z0 to z1 (its front at z0), windows on the named faces."""
        cx, cz, w, d = (x0 + x1) / 2, (z0 + z1) / 2, x1 - x0, z1 - z0
        m.box((cx, base / 2, cz), (w + 0.4, base, d + 0.4), "Stone")                  # plinth
        m.box((cx, base + 3 * storey / 2, cz), (w, 3 * storey, d), "Plaster")
        for y in (base + storey - 0.2, base + 2 * storey - 0.2):                        # string courses
            m.box((cx, y, cz), (w + 0.2, 0.3, d + 0.2), "Stone")
        m.box((cx, top + 0.35, cz), (w + 0.8, 0.7, d + 0.8), "Stone")                  # cornice
        m.box((cx, top + 1.5, cz), (w, 1.6, d), "Plaster")                             # attic
        # A hipped roof: a ridge along the block's length.
        rise = min(w, d) * 0.28
        ridge = (w - d) / 2 if w > d else 0.0
        verts = [(x0 - 0.5, top + 2.3, z0 - 0.5), (x1 + 0.5, top + 2.3, z0 - 0.5), (x1 + 0.5, top + 2.3, z1 + 0.5), (x0 - 0.5, top + 2.3, z1 + 0.5),
                 (cx - ridge, top + 2.3 + rise, cz), (cx + ridge, top + 2.3 + rise, cz)]
        faces = [(0, 1, 5, 4), (1, 2, 5), (2, 3, 4, 5), (3, 0, 4), (0, 3, 2, 1)]
        m.add((verts, faces), "RoofTiles")
        # Windows: a dark pane set in, a stone surround and sill, on every storey.
        for face in windows_on:
            if face in ("front", "back"):
                z = z0 - 0.02 if face == "front" else z1 + 0.02
                out = -1 if face == "front" else 1
                span, along = w, lambda t: (x0 + t, z)
            else:
                x = x0 - 0.02 if face == "left" else x1 + 0.02
                out = -1 if face == "left" else 1
                span, along = d, lambda t: (x, z0 + t)
            count = int(span // 4.2)
            for i in range(count):
                t = (i + 0.5) * span / count
                for f in range(3):
                    y = base + f * storey + 1.0
                    hx, hz = along(t)
                    if face in ("front", "back"):
                        m.box((hx, y + 1.35, hz), (1.3, 2.6, 0.06), "Glass")
                        m.box((hx, y + 1.35, hz + out * 0.08), (1.7, 3.0, 0.12), "Stone")
                        m.box((hx, y - 0.05, hz + out * 0.14), (1.9, 0.14, 0.3), "Stone")
                    else:
                        m.box((hx, y + 1.35, hz), (0.06, 2.6, 1.3), "Glass")
                        m.box((hx + out * 0.08, y + 1.35, hz), (0.12, 3.0, 1.7), "Stone")
                        m.box((hx + out * 0.14, y - 0.05, hz), (0.3, 0.14, 1.9), "Stone")

    block(-45, 45, 0, 18, ("front", "back"))           # main block, its front on the courtyard
    block(-45, -29, -40, 0, ("right", "left"))          # wings reaching forward
    block(29, 45, -40, 0, ("left", "right"))
    # The portico: six columns, an entablature, a pediment, steps.
    for i in range(6):
        x = -9 + i * 3.6
        m.cylinder((x, base, -3.5), (x, top - 0.3, -3.5), 0.55, "Stone", sides=14)
    m.box((0, top + 0.1, -2.0), (21, 1.0, 4.0), "Stone")
    verts = [(-11, top + 0.6, -4.0), (11, top + 0.6, -4.0), (0, top + 4.6, -4.0),
             (-11, top + 0.6, 0.0), (11, top + 0.6, 0.0), (0, top + 4.6, 0.0)]
    m.add((verts, [(0, 1, 2), (3, 5, 4), (0, 3, 4, 1), (1, 4, 5, 2), (2, 5, 3, 0)]), "Stone")
    for k in range(4):
        m.box((0, 0.2 + k * 0.3, -5.5 + k * 0.5), (22, 0.3, 1.0), "Stone")
    return m


def banking():
    """A length of old concrete banking, in the manner of Monza's 1955 oval: a 60 degree arc
    of 200 m radius, 12 m wide, its surface tilted up to 9 m at its outer edge on concrete
    columns, a parapet along the top, easing down to the ground at both ends. Its origin is
    the middle of its inner edge, and the track lies on its inside (-z), as Monza's banking
    is seen: the arc's ends come round towards the track, and its steep face looks at it."""
    m = Model("banking")
    R, width, arc = 200.0, 12.0, math.radians(60)
    steps = 48
    rows = []
    for i in range(steps + 1):
        t = i / steps
        a = -arc / 2 + arc * t
        ease = min(1.0, math.sin(math.pi * t) * 2.2)          # full height through the middle
        lift_in, lift_out = 1.2 * ease, 9.0 * ease
        # The arc's centre is R in front of the origin, on the track's side.
        cx, cz = 0.0, -R
        inner = (cx + R * math.sin(a), cz + R * math.cos(a))
        outer = (cx + (R + width) * math.sin(a), cz + (R + width) * math.cos(a))
        rows.append((inner, outer, lift_in, lift_out, a))
    # Deck: surface on top, a 0.6 m slab.
    verts, faces = [], []
    for (ix, iz), (ox, oz), li, lo, _ in rows:
        verts += [(ix, li, iz), (ox, lo, oz), (ox, lo - 0.6, oz), (ix, li - 0.6, iz)]
    for i in range(steps):
        a, b = 4 * i, 4 * (i + 1)
        for j in range(4):
            j1 = (j + 1) % 4
            faces.append((a + j, a + j1, b + j1, b + j))
    faces += [(0, 1, 2, 3), (4 * steps, 4 * steps + 3, 4 * steps + 2, 4 * steps + 1)]
    m.add((verts, faces), "Concrete")
    # Parapet along the outer edge.
    verts, faces = [], []
    for (ix, iz), (ox, oz), li, lo, a in rows:
        nx, nz = math.sin(a), math.cos(a)
        verts += [(ox, lo - 0.6, oz), (ox, lo + 1.1, oz), (ox + nx * 0.3, lo + 1.1, oz + nz * 0.3), (ox + nx * 0.3, lo - 0.6, oz + nz * 0.3)]
    for i in range(steps):
        a, b = 4 * i, 4 * (i + 1)
        for j in range(4):
            faces.append((a + j, a + (j + 1) % 4, b + (j + 1) % 4, b + j))
    faces += [(0, 1, 2, 3), (4 * steps, 4 * steps + 3, 4 * steps + 2, 4 * steps + 1)]
    m.add((verts, faces), "Concrete")
    # Columns under both edges wherever the deck is off the ground.
    for i in range(1, steps, 2):
        (ix, iz), (ox, oz), li, lo, _ = rows[i]
        for (x, z), h in (((ix, iz), li), ((ox, oz), lo), (((ix + ox) / 2, (iz + oz) / 2), (li + lo) / 2)):
            if h > 1.4:
                m.box((x, (h - 0.6) / 2, z), (0.7, h - 0.6, 0.7), "Concrete")
    return m


# A horizon's look: peak height as a share of its radius, sharp crests or rolling ones, and
# its colours from foot to top (the last only above a ragged line at `snowline`, if any).
HORIZONS = {
    "alps": dict(height=0.24, sharp=True, low=(0.20, 0.26, 0.30), mid=(0.42, 0.45, 0.52), top=(0.86, 0.89, 0.95), snowline=0.55),
    "ridges": dict(height=0.13, sharp=True, low=(0.14, 0.2, 0.2), mid=(0.2, 0.26, 0.27), top=None, snowline=None),
    "dunes": dict(height=0.05, sharp=False, low=(0.7, 0.57, 0.42), mid=(0.8, 0.68, 0.52), top=None, snowline=None),
}


def horizon(name, path, radius=1000.0):
    """A range on the horizon, 140 degrees of it, at `radius` from its origin and facing it
    (its middle along -z, towards the circuit's start), in the look HORIZONS[name] gives: the
    Alps, the Ardennes' forested ridges, or dunes. Crests from ridged noise (or rolling ones)
    over massifs that rise and fall along it, heights a share of the radius, so scaled with the
    radius they stand at the same angle above the horizon. Its texture runs up with height,
    coloured already towards the haze."""
    look = HORIZONS[name]
    rng = np.random.default_rng(7)
    arc = math.radians(140)
    cols, depths = 360, [0.0, 0.25, 0.5, 0.75, 1.0]

    def ridged(u, octaves):
        """Ridged noise, 0 to about 1: sharp crests where each octave's wave peaks."""
        total, amp, norm = 0.0, 1.0, 0.0
        for f, ph in octaves:
            total += amp * (1.0 - abs(math.sin(u * f + ph))) ** 2
            norm += amp
            amp *= 0.62
        return total / norm

    def rolling(u, octaves):
        total, amp, norm = 0.0, 1.0, 0.0
        for f, ph in octaves[:3]:
            total += amp * (0.5 + 0.5 * math.sin(u * f * 0.6 + ph))
            norm += amp
            amp *= 0.5
        return total / norm

    octaves = [(f, ph) for f, ph in zip((9.0, 23.0, 47.0, 97.0, 199.0), rng.uniform(0, 6.28, 5))]
    massifs = [(f, ph) for f, ph in zip((3.0, 7.0), rng.uniform(0, 6.28, 2))]
    verts, uvs = [], []
    for j, dd in enumerate(depths):
        r = radius * (1.0 + 0.18 * dd)
        for i in range(cols + 1):
            u = i / cols
            a = -arc / 2 + arc * u
            edge = min(1.0, math.sin(math.pi * u) * 3.0)              # the range tapers at its ends
            massif = 0.5 + 0.5 * sum(0.5 * math.sin(u * f + ph) for f, ph in massifs)
            ridge = (ridged if look["sharp"] else rolling)(u * 1.0 + dd * 0.21, octaves)
            h = radius * look["height"] * edge * (0.15 + 0.85 * massif) * (0.3 + 0.7 * ridge ** 1.1) * (0.0 if dd == 0.0 else (0.5 + 0.5 * dd))
            verts.append((radius * 0 + r * math.sin(a), h - radius * 0.01, -r * math.cos(a)))
            uvs.append((u * 8.0, h / (radius * look["height"])))
    faces = []
    n = cols + 1
    for j in range(len(depths) - 1):
        for i in range(cols):
            a, b = j * n + i, (j + 1) * n + i
            faces.append((a, b, b + 1, a + 1))
    m = Model(name)
    bv = [wheel.U(*v) for v in verts]
    faces = car.facing(bv, faces, lambda c: -Vector((c.x, c.y, 0.0)))   # towards the origin
    m.verts, m.faces, m.mats = bv, faces, [name.capitalize()] * len(faces)

    # The texture: u along the range (8 repeats), v up it by height fraction.
    w, hgt = 1024, 256
    v = np.linspace(0, 1, hgt)[:, None] * np.ones((1, w))
    uu = np.linspace(0, 1, w)[None, :] * np.ones((hgt, 1))
    streak = 0.06 * np.sin(uu * 2 * math.pi * 23 + rng.uniform(0, 6)) + 0.05 * np.sin(uu * 2 * math.pi * 57 + 1.3)
    rise = np.clip((v - 0.28 - streak * 0.5) / 0.1, 0, 1)
    col = np.array(look["low"]) * (1 - rise)[..., None] + np.array(look["mid"]) * rise[..., None]
    if look["snowline"] is not None:
        snow = np.clip((v - look["snowline"] - streak) / 0.06, 0, 1)
        col = col * (1 - snow)[..., None] + np.array(look["top"]) * snow[..., None]
    rgba = np.concatenate([col, np.ones((hgt, w, 1))], axis=2).astype(np.float32)
    img = bpy.data.images.new(name, w, hgt, alpha=False)
    img.pixels.foreach_set(rgba.ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    return m, uvs


def gable(m, x0, x1, z0, z1, eave, ridge, material, overhang=0.6):
    """A gable roof along x, eaves at `eave`, ridge at `ridge`, as a closed wedge."""
    cz = (z0 + z1) / 2
    xa, xb, za, zb = x0 - overhang, x1 + overhang, z0 - overhang, z1 + overhang
    verts = [(xa, eave, za), (xb, eave, za), (xb, eave, zb), (xa, eave, zb), (xa, ridge, cz), (xb, ridge, cz),
             (xa, eave - 0.25, za), (xb, eave - 0.25, za), (xb, eave - 0.25, zb), (xa, eave - 0.25, zb)]
    faces = [(0, 1, 5, 4), (2, 3, 4, 5), (0, 4, 3), (1, 2, 5), (6, 9, 8, 7), (0, 6, 7, 1), (1, 7, 8, 2), (2, 8, 9, 3), (3, 9, 6, 0)]
    m.add((verts, faces), material)


def windows(m, x0, x1, z, y, count, w=1.2, h=1.5, out=-1, frame="White"):
    """A row of windows on a face at z, facing -z (out=-1) or +z."""
    for i in range(count):
        x = x0 + (i + 0.5) * (x1 - x0) / count
        m.box((x, y, z + out * 0.02), (w + 0.2, h + 0.2, 0.04), frame)    # the frame, and the pane proud of it
        m.box((x, y, z + out * 0.05), (w, h, 0.03), "Glass")


def chalet():
    """An Ardennes mountain chalet: a stone ground floor, a timber upper floor with a balcony
    round the front, a steep slate gable roof with deep eaves, and a chimney."""
    m = Model("chalet")
    w, d = 16.0, 11.0
    m.box((0, 1.8, 0), (w, 3.6, d), "Stone")
    m.box((0, 5.4, 0), (w, 3.6, d), "Timber")
    windows(m, -w / 2, w / 2, -d / 2, 1.8, 4, frame="Timber")
    windows(m, -w / 2, w / 2, -d / 2, 5.4, 4, frame="White")
    windows(m, -w / 2, w / 2, d / 2, 5.4, 4, out=1, frame="White")
    m.box((0, 3.75, -d / 2 - 0.9), (w + 0.4, 0.2, 1.8), "Timber")                 # balcony
    m.box((0, 4.35, -d / 2 - 1.75), (w + 0.4, 1.0, 0.08), "Timber")               # its rail
    for i in range(9):
        m.box((-w / 2 + i * w / 8, 1.9, -d / 2 - 1.7), (0.18, 3.7, 0.18), "Timber")   # posts under it
    gable(m, -w / 2, w / 2, -d / 2, d / 2, 7.2, 12.5, "Slate", overhang=1.4)
    m.box((4.5, 12.0, 2.0), (1.0, 3.0, 1.0), "Stone")                              # chimney
    return m


def viaduct():
    """A stone railway viaduct across a valley: eight round arches 16 m apart on tapering piers,
    27 m high, a 6 m deck with parapets. Its long side faces the track."""
    m = Model("viaduct")
    span, piers, height, depth = 16.0, 9, 27.0, 6.0
    length = span * (piers - 1)
    spring = height - 10.0
    for i in range(piers):
        x = -length / 2 + i * span
        m.box((x, spring / 2, 0), (3.2, spring, depth + 1.2), "Stone")                 # pier, wider at the foot
        m.box((x, spring * 0.15, 0), (4.2, spring * 0.3, depth + 2.2), "Stone")
    for i in range(piers - 1):
        x0 = -length / 2 + i * span + 1.6
        x1 = x0 + span - 3.2
        r, cx = (x1 - x0) / 2, (x0 + x1) / 2
        # The spandrel over each arch: the wall between the arch's curve and the deck.
        steps = 12
        ring = [(cx - r * math.cos(math.pi * k / steps), spring + r * math.sin(math.pi * k / steps)) for k in range(steps + 1)]
        outline = ring + [(x1, height - 1.0), (x0, height - 1.0)]
        verts = [(x, y, z) for z in (-depth / 2, depth / 2) for x, y in outline]
        n = len(outline)
        faces = [tuple(range(n)), tuple(range(n, 2 * n))] + [(j, (j + 1) % n, n + (j + 1) % n, n + j) for j in range(n)]
        m.add((verts, faces), "Stone")
    m.box((0, height - 0.5, 0), (length + 3.2, 1.0, depth + 0.6), "Stone")              # deck
    for z in (-depth / 2, depth / 2):
        m.box((0, height + 0.6, z), (length + 3.2, 1.2, 0.5), "Stone")                  # parapets
    return m


def sakhir_tower():
    """A desert circuit's tower in the manner of Sakhir's: a tall sand-coloured shaft of offices
    whose top flares out into a wide glazed viewing floor under a flat oversailing roof."""
    m = Model("sakhir_tower")
    for i, (w, y0, y1) in enumerate(((14.0, 0.0, 30.0), (12.0, 30.0, 40.0))):
        m.box((0, (y0 + y1) / 2, 0), (w, y1 - y0, w), "Sandstone")
    for k in range(1, 10):
        y = k * 3.6
        for out, z in ((-1, -7.0), (1, 7.0)):
            m.box((0, y - 1.0, z + out * 0.02), (10.0, 1.6, 0.05), "Glass")
    # The flared top: a truncated pyramid widening from 12 m to 26 m, glazed all round.
    y0, y1 = 40.0, 46.0
    verts = [(-6, y0, -6), (6, y0, -6), (6, y0, 6), (-6, y0, 6), (-13, y1, -13), (13, y1, -13), (13, y1, 13), (-13, y1, 13)]
    faces = [(0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7), (0, 3, 2, 1), (4, 5, 6, 7)]
    m.add((verts, faces), "Glass")
    m.box((0, y1 + 3.0, 0), (26.0, 6.0, 26.0), "Glass")                                 # viewing floor
    for k in range(9):
        c = -12 + k * 3
        for x, z in ((c, -13.05), (c, 13.05), (-13.05, c), (13.05, c)):
            m.box((x, y1 + 3.0, z), (0.25, 6.0, 0.25), "White")                          # mullions
    m.box((0, y1 + 6.4, 0), (32.0, 0.8, 32.0), "White")                                 # the roof, oversailing
    m.box((0, y1 + 7.6, 0), (6.0, 1.6, 6.0), "White")
    return m


def fort():
    """A desert fort of sandstone: a square curtain wall with a gate, round towers at the
    corners, crenellations along the top, and a keep inside."""
    m = Model("fort")
    half, h, t = 22.0, 9.0, 1.6
    for side in range(4):
        a = math.pi / 2 * side
        ux, uz = math.cos(a), math.sin(a)
        cx, cz = -uz * half, ux * half
        m.box((cx, h / 2, cz), (abs(ux) * 2 * half + abs(uz) * t, h, abs(uz) * 2 * half + abs(ux) * t), "Sandstone")
        for k in range(15):
            s_ = -half + 1.5 + k * 3.0
            m.box((cx + ux * s_, h + 0.6, cz + uz * s_), (1.4 if ux else t + 0.2, 1.2, t + 0.2 if ux else 1.4), "Sandstone")
    for x in (-half, half):
        for z in (-half, half):
            m.cylinder((x, 0, z), (x, h + 4.0, z), 3.5, "Sandstone", sides=16)
            for k in range(10):
                a = 2 * math.pi * k / 10
                m.box((x + 3.2 * math.cos(a), h + 4.6, z + 3.2 * math.sin(a)), (1.2, 1.2, 1.2), "Sandstone")
    m.box((0, 3.0, -half - 0.85), (6.0, 6.0, 0.1), "Timber")                            # the gate
    m.box((0, 7.0, 4.0), (14.0, 14.0, 14.0), "Sandstone")                               # keep
    for k in range(5):
        m.box((-6 + k * 3, 14.6, 4.0 - 7.0), (1.4, 1.2, 1.4), "Sandstone")
    return m


def ferris_wheel():
    """A seaside Ferris wheel 60 m across: two white rims on spokes to a hub, turned on two
    A-frame legs, with 24 coloured gondolas hanging from the rim."""
    m = Model("ferris_wheel")
    R, hub_y, gap = 30.0, 34.0, 3.0
    n = 48
    for z in (-gap, gap):
        ring = [(R * math.cos(2 * math.pi * k / n), hub_y + R * math.sin(2 * math.pi * k / n), z) for k in range(n)]
        for k in range(n):
            m.cylinder(ring[k], ring[(k + 1) % n], 0.35, "White", sides=6)
        for k in range(0, n, 2):
            m.cylinder((0, hub_y, z), ring[k], 0.12, "White", sides=4)
    m.cylinder((0, hub_y, -gap - 1.0), (0, hub_y, gap + 1.0), 1.4, "DarkSteel", sides=12)   # hub
    for z in (-gap - 1.0, gap + 1.0):
        for x in (-14.0, 14.0):
            m.cylinder((x, 0, z * 2.5), (0, hub_y, z), 0.7, "White", sides=8)               # legs
    colours = ("Red", "Blue", "Yellow", "White")
    for k in range(24):
        a = 2 * math.pi * k / 24
        x, y = R * math.cos(a), hub_y + R * math.sin(a) - 2.4
        m.box((x, y, 0), (2.4, 2.6, 2.6), colours[k % 4])
        m.box((x, y + 1.5, 0), (2.8, 0.3, 3.0), "White")
    m.box((0, 0.6, 0), (40.0, 1.2, 14.0), "Concrete")                                   # base
    return m


def lighthouse():
    """A lighthouse: a tapering white tower with red bands, a gallery with a rail, a glazed
    lantern and a dark cap."""
    m = Model("lighthouse")
    h, r0, r1 = 28.0, 3.2, 2.2
    bands = 7
    for k in range(bands):
        y0, y1 = h * k / bands, h * (k + 1) / bands
        ra, rb = r0 + (r1 - r0) * k / bands, r0 + (r1 - r0) * (k + 1) / bands
        sides = 20
        verts = [(ra * math.cos(2 * math.pi * j / sides), y0, ra * math.sin(2 * math.pi * j / sides)) for j in range(sides)] + \
                [(rb * math.cos(2 * math.pi * j / sides), y1, rb * math.sin(2 * math.pi * j / sides)) for j in range(sides)]
        faces = [(j, (j + 1) % sides, sides + (j + 1) % sides, sides + j) for j in range(sides)] + [tuple(range(sides)), tuple(range(sides, 2 * sides))]
        m.add((verts, faces), "Red" if k % 2 else "White")
    m.cylinder((0, h, 0), (0, h + 0.4, 0), r1 + 1.0, "DarkSteel", sides=20)               # gallery
    for j in range(16):
        a = 2 * math.pi * j / 16
        m.cylinder(((r1 + 0.9) * math.cos(a), h + 0.4, (r1 + 0.9) * math.sin(a)), ((r1 + 0.9) * math.cos(a), h + 1.4, (r1 + 0.9) * math.sin(a)), 0.05, "DarkSteel", sides=4)
    m.cylinder((0, h + 0.4, 0), (0, h + 3.4, 0), r1 * 0.8, "Glass", sides=16)             # lantern
    m.cylinder((0, h + 3.4, 0), (0, h + 4.6, 0), r1 * 0.9, "DarkSteel", sides=16)
    m.cylinder((0, 0, 0), (0, 3.0, 0), r0 + 3.0, "White", sides=20)                     # keeper's house, round
    return m


def hangar():
    """A wartime airfield hangar: a corrugated arched roof 40 m across and 70 m long, brick-
    and-concrete end walls, and big sliding doors at the front."""
    m = Model("hangar")
    span, length, rise = 40.0, 70.0, 16.0
    steps = 20
    arc = [(-span / 2 * math.cos(math.pi * k / steps), rise * math.sin(math.pi * k / steps)) for k in range(steps + 1)]
    verts, faces = [], []
    for x, y in arc:
        verts += [(x, y, -length / 2), (x, y, length / 2)]
    for k in range(steps):
        a, b = 2 * k, 2 * (k + 1)
        faces.append((a, b, b + 1, a + 1))
    # A thin shell: the same surface 0.3 m in, so it is closed and faces the right way.
    inner = [(x * 0.985, y * 0.98, z) for x, y, z in verts]
    base = len(verts)
    faces += [(base + f[0], base + f[3], base + f[2], base + f[1]) for f in faces]
    faces += [(0, base, base + 1, 1), (2 * steps, 2 * steps + 1, base + 2 * steps + 1, base + 2 * steps)]
    faces += [(2 * k, base + 2 * k, base + 2 * (k + 1), 2 * (k + 1)) for k in range(steps)]
    faces += [(2 * k + 1, 2 * (k + 1) + 1, base + 2 * (k + 1) + 1, base + 2 * k + 1) for k in range(steps)]
    m.add((verts + inner, faces), "Corrugated")
    # End walls: the back solid, the front with doors, under the arch.
    for z, doors in ((length / 2 - 0.2, False), (-length / 2 + 0.2, True)):
        outline = arc
        verts = [(x, y, z - 0.2) for x, y in outline] + [(x, y, z + 0.2) for x, y in outline]
        n = len(outline)
        faces = [tuple(range(n)), tuple(range(n, 2 * n))] + [(j, (j + 1) % n, n + (j + 1) % n, n + j) for j in range(n)]
        m.add((verts, faces), "Concrete")
        if doors:
            for i in range(4):
                m.box((-12 + i * 8, 5.5, z - 0.4), (7.6, 11.0, 0.3), "DarkSteel")
            m.box((0, 12.5, z - 0.35), (20.0, 2.0, 0.1), "Glass")
    return m


def control_tower():
    """An airfield control tower of the 1940s: a two-storey white block with long windows,
    a railed roof terrace, and a glazed control room on top."""
    m = Model("control_tower")
    w, d = 16.0, 10.0
    m.box((0, 3.5, 0), (w, 7.0, d), "White")
    for y in (1.8, 5.3):
        windows(m, -w / 2, w / 2, -d / 2, y, 6, w=2.0, h=1.6)
    m.box((0, 7.15, 0), (w + 1.0, 0.3, d + 1.0), "Concrete")
    for x in (-w / 2 - 0.4, w / 2 + 0.4):
        m.cylinder((x, 7.3, -d / 2 - 0.4), (x, 7.3, d / 2 + 0.4), 0.05, "DarkSteel", sides=4)
    m.cylinder((-w / 2 - 0.4, 8.3, -d / 2 - 0.4), (w / 2 + 0.4, 8.3, -d / 2 - 0.4), 0.05, "DarkSteel", sides=4)
    m.box((2.0, 8.8, 1.0), (7.0, 3.0, 6.0), "Glass")                                    # control room
    m.box((2.0, 10.45, 1.0), (7.8, 0.3, 6.8), "White")
    m.cylinder((5.0, 10.6, 3.5), (5.0, 16.0, 3.5), 0.08, "DarkSteel", sides=6)           # mast
    return m


def neon_tower(colour):
    """A night city's tower, 90 m of dark glass on a square plan, with glowing bands at every
    tenth floor, a glowing strip up each corner, and a lit crown."""
    m = Model(f"neon_tower_{'pink' if colour == 'NeonPink' else 'cyan'}")
    w, h = 22.0, 90.0
    m.box((0, h / 2, 0), (w, h, w), "Glass")
    for k in range(1, 9):
        m.box((0, k * 10.0, 0), (w + 0.3, 0.5, w + 0.3), colour)
    for x in (-w / 2, w / 2):
        for z in (-w / 2, w / 2):
            m.box((x, h / 2, z), (0.5, h, 0.5), colour)
    m.box((0, h + 3.0, 0), (w * 0.7, 6.0, w * 0.7), "Glass")
    m.box((0, h + 6.2, 0), (w * 0.72, 0.5, w * 0.72), colour)
    m.cylinder((0, h + 6.4, 0), (0, h + 22.0, 0), 0.3, "DarkSteel", sides=6)
    return m


LANDMARKS = {
    "villa": villa,
    "banking": banking,
    "chalet": chalet,
    "viaduct": viaduct,
    "sakhir_tower": sakhir_tower,
    "fort": fort,
    "ferris_wheel": ferris_wheel,
    "lighthouse": lighthouse,
    "hangar": hangar,
    "control_tower": control_tower,
    "neon_tower_pink": lambda: neon_tower("NeonPink"),
    "neon_tower_cyan": lambda: neon_tower("NeonCyan"),
}


# ---- output ------------------------------------------------------------------------

def export(obj, out):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    path = os.path.join(out, f"{obj.name}.fbx")
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"MESH"}, apply_scale_options="FBX_SCALE_UNITS",
        axis_forward="-Z", axis_up="Y", bake_space_transform=True, mesh_smooth_type="FACE",
        use_tspace=False, path_mode="STRIP", embed_textures=False, add_leaf_bones=False, bake_anim=False)
    print("SCENERY exported", path, sum(len(p.vertices) - 2 for p in obj.data.polygons), "triangles")


def render(obj, path):
    """The model on a grey floor, from its front three quarters, framed to its size."""
    scene = bpy.context.scene
    for o in list(scene.collection.objects):
        if o is not obj and o.type == "MESH" and o.name != "Floor":
            o.hide_render = True
    obj.hide_render = False
    if "Floor" not in bpy.data.objects:
        world = bpy.data.worlds.new("World")
        scene.world = world
        wheel.node_tree(world).nodes.get("Background").inputs["Color"].default_value = (0.6, 0.66, 0.75, 1.0)
        sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
        sun.data.energy = 3.0
        sun.rotation_euler = (math.radians(40), 0, math.radians(-150))
        scene.collection.objects.link(sun)
        floor = bpy.data.meshes.new("Floor")
        floor.from_pydata([wheel.U(-400, 0, -400), wheel.U(400, 0, -400), wheel.U(400, 0, 400), wheel.U(-400, 0, 400)], [], [(0, 1, 2, 3)])
        fo = bpy.data.objects.new("Floor", floor)
        fo.data.materials.append(wheel.principled("Grass", (0.1, 0.2, 0.06), 0.0, 0.9))
        scene.collection.objects.link(fo)
        cam = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
        scene.collection.objects.link(cam)
        scene.camera = cam
        scene.render.resolution_x, scene.render.resolution_y = 1200, 800
        scene.cycles.samples = 32
        scene.cycles.use_denoising = True
    lo = Vector((min(v.co.x for v in obj.data.vertices), min(v.co.y for v in obj.data.vertices), min(v.co.z for v in obj.data.vertices)))
    hi = Vector((max(v.co.x for v in obj.data.vertices), max(v.co.y for v in obj.data.vertices), max(v.co.z for v in obj.data.vertices)))
    centre, size = (lo + hi) * 0.5, (hi - lo).length
    cam = scene.camera
    cam.data.lens = 40
    if obj.name in HORIZONS:
        # From where the circuit will be: the range's middle, at a driver's height.
        cam.location = wheel.U(0, 2, 0)
        cam.rotation_euler = (wheel.U(0, 40, -1000) - cam.location).to_track_quat("-Z", "Y").to_euler()
        cam.data.lens = 24
    else:
        # From in front (Unity -z is Blender +y) and to one side, a little above.
        cam.location = centre + Vector((-0.7, 1.1, 0.45)).normalized() * size * 1.5
        cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
    cam.data.clip_end = size * 10 + 100
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument("--out")
    parser.add_argument("--preview")
    parser.add_argument("--only")
    args = parser.parse_args(argv)
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj)
    wheel.setup_cycles()
    models = dict(TRACKSIDE, **LANDMARKS, **BUILDINGS)
    for h in HORIZONS:
        models[h] = None
    only = args.only.split(",") if args.only else list(models)
    out = args.out or os.path.dirname(args.preview)
    os.makedirs(out, exist_ok=True)
    chain_link(os.path.join(out, "Chainlink.png"))
    for name in only:
        if name in HORIZONS:
            model, uvs = horizon(name, os.path.join(out, f"{name.capitalize()}.png"))
            obj = model.build(uvs)
        else:
            obj = models[name]().build()
        if args.out:
            export(obj, args.out)
        if args.preview:
            render(obj, f"{args.preview}-{name}.png")


if __name__ == "__main__":
    main()
