"""The car's wheel, built in Blender: tyre, rim, brake disc and caliper.

Run headless from WSL (Blender is a Windows program, so paths are Windows paths):

    /mnt/d/Software/Blender/blender.exe -b --factory-startup \
        -P "$(wslpath -w Tools/blender/wheel.py)" -- \
        --out 'D:\\Dev\\CarRace\\Assets\\Art\\Wheel' --preview "$(wslpath -w Tools/out/wheel.png)"

Writes Wheel.fbx (meshes Tyre, Rim, Disc, Caliper), Tyre Normal.png and Rim AO.png to --out, and optionally a render to --preview and the scene to --blend.

Everything is laid out in Unity's frame for a right-hand wheel, hub at the origin:
x along the axle and outward, y up, z forward. U() converts to Blender's frame once, at
mesh creation, so the FBX exporter's -Z forward, Y up conversion puts it back: Blender's
-X is Unity's +X, since FBX to Unity also flips handedness.

The low poly tyre is what the game draws. The tread grooves, shoulder blocks and sidewall
lettering exist only on a dense copy and are baked into the tyre's normal map, which is
how game tyres get their detail without the triangles.
"""

import argparse
import math
import os
import sys

import bmesh
import bpy
from mathutils import Vector

RADIUS = 0.34           # CarConfig.ReferenceSportsCar tyre radius
HALF_WIDTH = 0.1275     # 255 mm section
BEAD_R = 0.2286         # 18 inch rim
HUB_X = 0.045           # mounting face, ET45
BRAND = "CARRACE"
SIZE_TEXT = "SPORT GT   255/45 R18 97Y"

TYRE_TEX = (4096, 1024)
RIM_AO_TEX = 1024


def U(x, y, z):
    """Unity (x right, y up, z forward) to Blender (x right, y forward, z up), mirrored."""
    return Vector((-x, -z, y))


# ---- mesh helpers ------------------------------------------------------------

def make_object(name, verts, faces, uvs=None, smooth_angle=None, triangulate=True):
    """verts in Unity coordinates; faces as index tuples, wound so their Blender normal
    faces outward (the helpers below take care of that). Game meshes are triangulated
    here, so the tangents the normal map is baked against are the ones Unity computes."""
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([U(*v) for v in verts], [], faces)
    if uvs is not None:
        layer = mesh.uv_layers.new(name="UVMap")
        for poly in mesh.polygons:
            for li in poly.loop_indices:
                layer.data[li].uv = uvs[mesh.loops[li].vertex_index]
    mesh.validate()
    if triangulate:
        bm = bmesh.new()
        bm.from_mesh(mesh)
        bmesh.ops.triangulate(bm, faces=bm.faces[:], quad_method="FIXED", ngon_method="BEAUTY")
        bm.to_mesh(mesh)
        bm.free()
    mesh.update()
    mesh.shade_smooth()
    if smooth_angle is not None:
        mesh.set_sharp_from_angle(angle=math.radians(smooth_angle))
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def lathe(profile, segments, seam=True):
    """A profile of (x, r) points turned about the axle. Walking along the profile, the
    visible side is on the left in the (x, r) plane, so each face is wound to put its normal
    along (-dr, dx). With seam, the first ring is repeated at the end so UVs can wrap."""
    rings = segments + 1 if seam else segments
    verts, uvs, faces = [], [], []
    lengths = [0.0]
    for (x0, r0), (x1, r1) in zip(profile, profile[1:]):
        lengths.append(lengths[-1] + math.hypot(x1 - x0, r1 - r0))
    n = len(profile)
    for s in range(rings):
        a = 2 * math.pi * s / segments
        for i, (x, r) in enumerate(profile):
            verts.append((x, r * math.cos(a), r * math.sin(a)))
            uvs.append((s / segments, lengths[i] / lengths[-1]))
    # Which way to wind depends only on the profile edge, so it is decided once per edge.
    flip = [orient(verts, (i, n + i, n + i + 1, i + 1), profile, i, 0, segments) for i in range(n - 1)]
    for s in range(segments):
        s1 = s + 1 if seam else (s + 1) % segments
        for i in range(n - 1):
            quad = (s * n + i, s1 * n + i, s1 * n + i + 1, s * n + i + 1)
            faces.append(tuple(reversed(quad)) if flip[i] else quad)
    return verts, faces, uvs


def orient(verts, quad, profile, i, s, segments):
    (x0, r0), (x1, r1) = profile[i], profile[i + 1]
    a = 2 * math.pi * (s + 0.5) / segments
    nx, nr = -(r1 - r0), (x1 - x0)
    want = U(nx, nr * math.cos(a), nr * math.sin(a))
    p = [U(*verts[k]) for k in quad]
    have = (p[1] - p[0]).cross(p[3] - p[0])
    if have.length < 1e-12:
        have = (p[2] - p[1]).cross(p[3] - p[1])
    return have.dot(want) < 0


def merge(parts):
    """Several (verts, faces) into one."""
    verts, faces = [], []
    for v, f in parts:
        base = len(verts)
        verts.extend(v)
        faces.extend(tuple(k + base for k in face) for face in f)
    return verts, faces


def closed_outward(verts, faces):
    """Rewinds a closed mesh so every normal faces out, by bmesh's own test."""
    bm = bmesh.new()
    bv = [bm.verts.new(U(*v)) for v in verts]
    for f in faces:
        bm.faces.new([bv[k] for k in f])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.verts.index_update()
    out = [tuple(v.index for v in f.verts) for f in bm.faces]
    bm.free()
    return out


def sweep(path, section, cap=True):
    """Rings of section points (each a function of the path index) joined into a tube,
    capped at both ends, returned with outward faces."""
    verts, faces = [], []
    m = len(section(0))
    for k in range(len(path)):
        verts.extend(section(k))
    for k in range(len(path) - 1):
        for j in range(m):
            j1 = (j + 1) % m
            faces.append((k * m + j, k * m + j1, (k + 1) * m + j1, (k + 1) * m + j))
    if cap:
        faces.append(tuple(range(m)))
        last = (len(path) - 1) * m
        faces.append(tuple(last + j for j in range(m)))
    return verts, closed_outward(verts, faces)


def rounded_rect(half_a, half_b, corner, steps=3):
    """A rounded rectangle's outline as (a, b) points, counter-clockwise."""
    pts = []
    for ca, cb, start in ((1, 1, 0), (-1, 1, 90), (-1, -1, 180), (1, -1, 270)):
        cx, cy = ca * (half_a - corner), cb * (half_b - corner)
        for t in range(steps + 1):
            ang = math.radians(start + 90 * t / steps)
            pts.append((cx + corner * math.cos(ang), cy + corner * math.sin(ang)))
    return pts


def resample(points, spacing):
    """A polyline resampled at even arc length, keeping its end points."""
    out = [points[0]]
    carry = 0.0
    for (x0, y0), (x1, y1) in zip(points, points[1:]):
        seg = math.hypot(x1 - x0, y1 - y0)
        d = spacing - carry
        while d < seg:
            t = d / seg
            out.append((x0 + (x1 - x0) * t, y0 + (y1 - y0) * t))
            d += spacing
        carry = seg - (d - spacing)
    out.append(points[-1])
    return out


def catmull(points, per):
    """A Catmull-Rom curve through the points, `per` samples a span."""
    ext = [points[0]] + list(points) + [points[-1]]
    out = []
    for i in range(1, len(ext) - 2):
        p0, p1, p2, p3 = (Vector(ext[j]) for j in (i - 1, i, i + 1, i + 2))
        for t in range(per):
            t /= per
            q = 0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t
                       + (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t)
            out.append((q.x, q.y))
    out.append(points[-1])
    return out


# ---- tyre --------------------------------------------------------------------

def tyre_half():
    """The tyre's outer surface from the +x bead to the crown, as control points."""
    w = HALF_WIDTH
    return [(w * 0.88, BEAD_R), (w * 0.97, BEAD_R + 0.012), (w * 1.0, BEAD_R + 0.045),
            (w * 0.985, RADIUS - 0.03), (w * 0.93, RADIUS - 0.012), (w * 0.8, RADIUS - 0.002),
            (w * 0.55, RADIUS), (0.0, RADIUS)]


def tyre_profile(spacing):
    half = catmull(tyre_half(), 6)
    full = [(-x, r) for x, r in half] + [(x, r) for x, r in reversed(half[:-1])]
    return resample(full, spacing)


_SIDEWALL = []


def sidewall_x(r):
    """The outer (+x) sidewall's x at radius r, from the same curve the tyre is made of."""
    if not _SIDEWALL:
        half = catmull(tyre_half(), 12)
        _SIDEWALL.extend(half[:len(half) * 2 // 3])
    return min(_SIDEWALL, key=lambda p: abs(p[1] - r))[0]


GROOVES = (-0.07, -0.03, 0.03, 0.07)    # circumferential grooves' centres, m
GROOVE_HALF, GROOVE_DEPTH = 0.005, 0.006
SHOULDER_BLOCKS, SLOT_FRACTION, SLOT_DEPTH = 60, 0.22, 0.0045


def tread_depth(x, a):
    """How far the dense tyre's surface is pushed in at (x across, a round), m."""
    d = 0.0
    for g in GROOVES:
        t = abs(x - g) / GROOVE_HALF
        if t < 1.0:
            d = max(d, GROOVE_DEPTH * math.sqrt(1.0 - t * t))
    # Slots across the shoulders, fading in from the outer grooves.
    edge = (abs(x) - 0.078) / 0.012
    if edge > 0.0:
        phase = (a / (2 * math.pi) * SHOULDER_BLOCKS) % 1.0
        if phase < SLOT_FRACTION:
            d = max(d, SLOT_DEPTH * min(edge, 1.0) * math.sin(math.pi * phase / SLOT_FRACTION))
    return d


def dense_tyre():
    profile = tyre_profile(0.0008)
    segments = 960
    verts, faces, _ = lathe(profile, segments, seam=False)
    n = len(profile)
    for s in range(segments):
        a = 2 * math.pi * s / segments
        for i, (x, r) in enumerate(profile):
            if r > RADIUS - 0.006:
                d = tread_depth(x, a)
                if d > 0.0:
                    k = s * n + i
                    rr = r - d
                    verts[k] = (x, rr * math.cos(a), rr * math.sin(a))
    return make_object("Tyre Dense", verts, faces, triangulate=False)


def lettering(text, size, radius_mid, angle_mid, raise_m=0.0014):
    """Raised letters on the outer sidewall, their baseline on an arc, reading clockwise
    as you face the wheel from outside: the top of the wheel reads left to right.

    Rounded edges, not square ones: a letter's flat top has the sidewall's own normal, so
    in the normal map a letter is only its edges, and a square edge bakes to one pixel."""
    curve = bpy.data.curves.new("Text", type="FONT")
    curve.body = text
    curve.size = size
    curve.extrude = raise_m * 0.5
    curve.bevel_depth = raise_m * 0.5
    curve.bevel_resolution = 3
    curve.align_x = "CENTER"
    curve.align_y = "CENTER"
    curve.space_character = 1.15
    obj = bpy.data.objects.new("Text", curve)
    bpy.context.scene.collection.objects.link(obj)
    bpy.context.view_layer.update()
    depsgraph = bpy.context.evaluated_depsgraph_get()
    mesh = bpy.data.meshes.new_from_object(obj.evaluated_get(depsgraph))
    bpy.data.objects.remove(obj)
    bpy.data.curves.remove(curve)

    # Letters centred on the arc; glyph coordinates are along the text, up, and out.
    xs = [v.co.x for v in mesh.vertices]
    ys = [v.co.y for v in mesh.vertices]
    u0, h0 = (min(xs) + max(xs)) * 0.5, (min(ys) + max(ys)) * 0.5
    verts = []
    for v in mesh.vertices:
        u, h, z = v.co.x - u0, v.co.y - h0, v.co.z     # z is -raise_m..raise_m
        a = angle_mid + u / radius_mid
        r = radius_mid + h
        x = sidewall_x(r) + z
        verts.append((x, r * math.cos(a), r * math.sin(a)))
    faces = [tuple(p.vertices) for p in mesh.polygons]
    bpy.data.meshes.remove(mesh)
    return verts, closed_outward(verts, faces)


def tyre_letters():
    mid = (BEAD_R + RADIUS) * 0.5 + 0.004
    parts = []
    for base in (0.0, math.pi):
        parts.append(lettering(BRAND, 0.034, mid, base))
        parts.append(lettering(SIZE_TEXT, 0.012, mid, base + math.pi / 2))
    verts, faces = merge(parts)
    return make_object("Tyre Letters", verts, faces, triangulate=False)


def game_tyre():
    verts, faces, uvs = lathe(tyre_profile(0.016), 96)
    return make_object("Tyre", verts, faces, uvs, smooth_angle=60)


# ---- rim ---------------------------------------------------------------------

SPOKE_PAIRS = 5


def rim_barrel():
    profile = [(0.100, 0.247), (0.118, 0.249), (0.125, 0.245), (0.129, 0.237), (0.128, 0.229),
               (0.122, 0.222), (0.110, 0.217), (0.090, 0.2155), (0.0, 0.215), (-0.100, 0.215),
               (-0.116, 0.218), (-0.125, 0.226), (-0.128, 0.236)]
    verts, faces, _ = lathe(profile, 96, seam=False)
    return verts, faces


def rim_hub():
    h = HUB_X
    profile = [(h - 0.035, 0.084), (h - 0.004, 0.088), (h + 0.001, 0.086), (h + 0.004, 0.078),
               (h + 0.006, 0.040), (h + 0.009, 0.033), (h + 0.011, 0.031), (h + 0.018, 0.028),
               (h + 0.024, 0.019), (h + 0.027, 0.008), (h + 0.028, 0.0005)]
    verts, faces, _ = lathe(profile, 64, seam=False)
    return verts, faces


def rim_spokes():
    """Five pairs of spokes, each pair a V from the hub to the lip, dished: the hub sits
    deep and the spokes rise to the lip, as on a concave wheel."""
    r0, r1 = 0.072, 0.226
    x_hub, x_lip = HUB_X + 0.004, 0.118
    steps = 16
    parts = []
    for pair in range(SPOKE_PAIRS):
        centre = 2 * math.pi * pair / SPOKE_PAIRS
        for side in (-1, 1):
            def frame(k):
                t = k / steps
                r = r0 + (r1 - r0) * t
                spread = math.radians(1.8 + 5.2 * t ** 0.8)
                a = centre + side * spread
                x = x_hub + (x_lip - x_hub) * (1 - (1 - t) ** 2.2)
                return t, r, a, x

            def section(k):
                t, r, a, x = frame(k)
                half_w = 0.018 - 0.006 * t
                depth = 0.028 - 0.008 * t
                radial = Vector((0, math.cos(a), math.sin(a)))
                tangent = Vector((0, -math.sin(a), math.cos(a)))
                centre_pt = Vector((x - depth * 0.5, 0, 0)) + radial * r
                ring = []
                for ta, tb in rounded_rect(half_w, depth * 0.5, 0.004, 3):
                    p = centre_pt + tangent * ta + Vector((tb, 0, 0))
                    ring.append((p.x, p.y, p.z))
                return ring

            parts.append(sweep(range(steps + 1), section))
    return merge(parts)


def rim_nuts():
    parts = []
    for k in range(SPOKE_PAIRS):
        a = 2 * math.pi * (k + 0.5) / SPOKE_PAIRS
        cy, cz = 0.056 * math.cos(a), 0.056 * math.sin(a)
        rings = [(HUB_X + 0.002, 0.0105), (HUB_X + 0.017, 0.0105), (HUB_X + 0.021, 0.0085)]

        def section(i, rings=rings, cy=cy, cz=cz):
            x, rad = rings[i]
            return [(x, cy + rad * math.cos(j * math.pi / 3), cz + rad * math.sin(j * math.pi / 3))
                    for j in range(6)]

        parts.append(sweep(range(len(rings)), section))
    return merge(parts)


def game_rim():
    verts, faces = merge([rim_barrel(), rim_hub(), rim_spokes(), rim_nuts()])
    obj = make_object("Rim", verts, faces, smooth_angle=50)
    smart_uv(obj)
    return obj


def smart_uv(obj):
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.004)
    bpy.ops.object.mode_set(mode="OBJECT")


# ---- brakes ------------------------------------------------------------------

def game_disc():
    """A vented disc on an aluminium-style hat. The vent shows as a dark slot round the edge."""
    ring = [(-0.004, 0.117), (-0.036, 0.117), (-0.036, 0.178), (-0.028, 0.178), (-0.027, 0.1745),
            (-0.013, 0.1745), (-0.012, 0.178), (-0.004, 0.178), (-0.004, 0.117)]
    hat = [(-0.004, 0.117), (-0.002, 0.100), (0.030, 0.097), (0.037, 0.090), (0.038, 0.030),
           (0.038, 0.0005)]
    rv, rf, _ = lathe(ring, 72, seam=False)
    hv, hf, _ = lathe(hat, 72, seam=False)
    verts, faces = merge([(rv, rf), (hv, hf)])
    return make_object("Disc", verts, faces, smooth_angle=40)


def game_caliper():
    """A four piston caliper straddling the disc behind and above the hub, where it sits on
    a front wheel. It hangs on the steering pivot, so it steers but does not spin."""
    centre = math.radians(-58)      # angle from the top, toward the rear
    span = math.radians(27)
    steps = 18
    x_in, x_out = -0.052, 0.030

    def section(k):
        t = k / steps
        a = centre - span + 2 * span * t
        # Rounded ends: the body narrows radially toward each end.
        end = math.sin(math.pi * t) ** 0.35
        r_mid, half_r = 0.160, 0.034 * max(end, 0.55)
        ring = []
        for ta, tb in rounded_rect((x_out - x_in) * 0.5, half_r, 0.010, 3):
            x = (x_in + x_out) * 0.5 + ta
            r = r_mid + tb
            ring.append((x, r * math.cos(a), r * math.sin(a)))
        return ring

    verts, faces = sweep(range(steps + 1), section)
    return make_object("Caliper", verts, faces, smooth_angle=45)


# ---- baking ------------------------------------------------------------------

def bake_target(obj, name, size, colour_space):
    image = bpy.data.images.new(name, size[0], size[1], alpha=False,
                                float_buffer=False, is_data=colour_space == "Non-Color")
    mat = bpy.data.materials.new(name + " Bake")
    nodes = node_tree(mat).nodes
    node = nodes.new("ShaderNodeTexImage")
    node.image = image
    nodes.active = node
    obj.data.materials.clear()
    obj.data.materials.append(mat)
    return image


def bake(kind, low, high=None, margin=8):
    bpy.ops.object.select_all(action="DESELECT")
    for h in high or []:
        h.select_set(True)
    low.select_set(True)
    bpy.context.view_layer.objects.active = low
    settings = dict(type=kind, margin=margin, use_clear=True)
    if high:
        settings.update(use_selected_to_active=True, cage_extrusion=0.008, max_ray_distance=0.02)
    if kind == "NORMAL":
        settings.update(normal_space="TANGENT")
    bpy.ops.object.bake(**settings)


def save(image, path):
    image.filepath_raw = path
    image.file_format = "PNG"
    image.save()


def node_tree(block):
    if block.node_tree is None:
        block.use_nodes = True
    return block.node_tree


# ---- scene -------------------------------------------------------------------

def setup_cycles():
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 64
    scene.cycles.device = "CPU"
    try:
        prefs = bpy.context.preferences.addons["cycles"].preferences
        for backend in ("OPTIX", "CUDA"):
            try:
                prefs.compute_device_type = backend
                prefs.get_devices()
                if any(d.type == backend for d in prefs.devices):
                    for d in prefs.devices:
                        d.use = d.type == backend
                    scene.cycles.device = "GPU"
                    break
            except TypeError:
                continue
    except KeyError:
        pass
    print("WHEEL cycles device", scene.cycles.device)


def principled(name, colour, metallic, roughness, normal=None, ao=None):
    mat = bpy.data.materials.new(name)
    nodes, links = node_tree(mat).nodes, node_tree(mat).links
    bsdf = nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*colour, 1.0)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    if ao is not None:
        tex = nodes.new("ShaderNodeTexImage")
        tex.image = ao
        mix = nodes.new("ShaderNodeMix")
        mix.data_type = "RGBA"
        mix.blend_type = "MULTIPLY"
        # The Mix node's colour sockets come after its float and vector ones.
        mix.inputs[0].default_value = 1.0
        mix.inputs[6].default_value = (*colour, 1.0)
        links.new(tex.outputs["Color"], mix.inputs[7])
        links.new(mix.outputs[2], bsdf.inputs["Base Color"])
    if normal is not None:
        tex = nodes.new("ShaderNodeTexImage")
        tex.image = normal
        tex.image.colorspace_settings.name = "Non-Color"
        nm = nodes.new("ShaderNodeNormalMap")
        links.new(tex.outputs["Color"], nm.inputs["Color"])
        links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
    return mat


def render_preview(path, tyre, rim, disc, caliper):
    scene = bpy.context.scene
    world = bpy.data.worlds.new("World")
    world.color = (0.55, 0.6, 0.68)
    scene.world = world
    bg = node_tree(world).nodes.get("Background")
    bg.inputs["Color"].default_value = (0.55, 0.6, 0.68, 1.0)
    bg.inputs["Strength"].default_value = 0.7

    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 3.5
    sun.rotation_euler = (math.radians(50), 0, math.radians(-140))
    scene.collection.objects.link(sun)

    floor = bpy.data.meshes.new("Floor")
    floor.from_pydata([U(-3, -RADIUS, -3), U(3, -RADIUS, -3), U(3, -RADIUS, 3), U(-3, -RADIUS, 3)],
                      [], [(0, 1, 2, 3)])
    floor_obj = bpy.data.objects.new("Floor", floor)
    floor_obj.data.materials.append(principled("Asphalt", (0.09, 0.09, 0.09), 0.0, 0.85))
    scene.collection.objects.link(floor_obj)

    # From outside and ahead of a right-hand wheel, a little above the hub.
    cam = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
    cam.data.lens = 60
    cam.location = U(1.35, 0.25, 0.75)
    direction = U(0.02, 0.0, 0.0) - cam.location
    cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    scene.collection.objects.link(cam)
    scene.camera = cam

    scene.render.resolution_x, scene.render.resolution_y = 1600, 1000
    scene.render.filepath = path
    scene.cycles.use_denoising = True
    bpy.ops.render.render(write_still=True)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", required=True)
    parser.add_argument("--preview")
    parser.add_argument("--blend")
    args = parser.parse_args(argv)
    os.makedirs(args.out, exist_ok=True)

    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj)
    setup_cycles()

    tyre = game_tyre()
    rim = game_rim()
    disc = game_disc()
    caliper = game_caliper()
    dense = dense_tyre()
    letters = tyre_letters()
    print("WHEEL triangles", {o.name: sum(len(p.vertices) - 2 for p in o.data.polygons)
                              for o in (tyre, rim, disc, caliper, dense)})

    # Tyre detail from the dense copy. The rim's ambient occlusion with the tyre and disc
    # in place, which spin with it; the caliper does not, so it stays out of the bakes.
    caliper.hide_render = True
    tyre_normal = bake_target(tyre, "Tyre Normal", TYRE_TEX, "Non-Color")
    bake("NORMAL", tyre, [dense, letters])
    save(tyre_normal, os.path.join(args.out, "Tyre Normal.png"))

    dense.hide_render = letters.hide_render = True
    rim_ao = bake_target(rim, "Rim AO", (RIM_AO_TEX, RIM_AO_TEX), "Non-Color")
    bake("AO", rim, margin=4)
    save(rim_ao, os.path.join(args.out, "Rim AO.png"))
    caliper.hide_render = False

    tyre.data.materials.clear()
    tyre.data.materials.append(principled("Tyre", (0.035, 0.035, 0.038), 0.0, 0.75, tyre_normal))
    rim.data.materials.clear()
    rim.data.materials.append(principled("Rim", (0.8, 0.81, 0.83), 1.0, 0.22, ao=rim_ao))
    disc.data.materials.append(principled("Disc", (0.42, 0.42, 0.43), 1.0, 0.45))
    caliper.data.materials.append(principled("Caliper", (0.7, 0.03, 0.02), 0.0, 0.3))

    bpy.ops.object.select_all(action="DESELECT")
    for obj in (tyre, rim, disc, caliper):
        obj.select_set(True)
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(args.out, "Wheel.fbx"), use_selection=True,
        object_types={"MESH"}, apply_scale_options="FBX_SCALE_UNITS",
        axis_forward="-Z", axis_up="Y", bake_space_transform=True,
        mesh_smooth_type="FACE", use_tspace=False, use_mesh_modifiers=True,
        path_mode="STRIP", embed_textures=False, add_leaf_bones=False, bake_anim=False)
    print("WHEEL exported", os.path.join(args.out, "Wheel.fbx"))

    if args.preview:
        bpy.data.objects.remove(dense)
        bpy.data.objects.remove(letters)
        render_preview(args.preview, tyre, rim, disc, caliper)
        print("WHEEL preview", args.preview)
    if args.blend:
        bpy.ops.wm.save_as_mainfile(filepath=args.blend)


main()
