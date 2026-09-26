"""Compose the leaf textures the generated trees wear, from CC0 leaf photographs.

    Tools/.venv/bin/python Tools/foliage_textures.py LEAVES_DIR OUT_DIR

LEAVES_DIR holds ambientCG leaf atlases unzipped, one folder each (LeafSet024/,
LeafSet014/, LeafSet019/, 1K PNG). A tree seen from a car is made of cards a metre or two
across, so each texture is a whole twig: leaves cut out of an atlas by its opacity map,
laid along drawn twigs, the ones drawn first darker since they sit deeper in the clump.
The palm frond is drawn outright, since no atlas has one. Every texture is 1024 square,
its twig running from the bottom middle (where the card is fixed) to the top, and the
colour of the nearest leaf is spread into the transparent pixels so that mipmaps do not
fringe the leaves with black.

Writes broadleaf_a.png, broadleaf_b.png, conifer.png and palm.png.
"""

import math
import random
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

SIZE = 1024
TWIG = (78, 62, 46, 255)


def leaves(folder, name, turn_degrees):
    """Each leaf of an atlas as an RGBA sprite, tip up, stem at the bottom."""
    colour = Image.open(folder / name / f"{name}_1K-PNG_Color.png").convert("RGB")
    alpha = np.asarray(Image.open(folder / name / f"{name}_1K-PNG_Opacity.png").convert("L"))
    labels, count = ndimage.label(alpha > 100)
    sprites = []
    for index, box in enumerate(ndimage.find_objects(labels), start=1):
        height, width = box[0].stop - box[0].start, box[1].stop - box[1].start
        if height * width < alpha.size * 0.004:
            continue
        rgba = colour.crop((box[1].start, box[0].start, box[1].stop, box[0].stop)).convert("RGBA")
        mask = np.where(labels[box] == index, alpha[box], 0).astype(np.uint8)
        rgba.putalpha(Image.fromarray(mask))
        sprites.append(rgba.rotate(turn_degrees, expand=True, resample=Image.BICUBIC))
    return sprites


def place(canvas, sprite, at, angle, length, shade, rng):
    """The sprite scaled to length, its stem at `at`, pointing `angle` degrees
    anticlockwise from up, its colour times shade."""
    scale = length / sprite.height
    leaf = sprite.resize((max(1, int(sprite.width * scale)), max(1, int(length))), Image.BICUBIC)
    r, g, b, a = leaf.split()
    tint = [shade * rng.uniform(0.95, 1.05), shade, shade * rng.uniform(0.9, 1.0)]
    leaf = Image.merge("RGBA", [c.point(lambda v, k=k: min(255, int(v * k))) for c, k in zip((r, g, b), tint)] + [a])
    turned = leaf.rotate(angle, expand=True, resample=Image.BICUBIC)
    theta = math.radians(angle)
    half = leaf.height / 2
    base = (half * math.sin(theta), half * math.cos(theta))
    x = at[0] - turned.width / 2 - base[0]
    y = at[1] - turned.height / 2 - base[1]
    layer = Image.new("RGBA", canvas.size)
    layer.paste(turned, (int(x), int(y)), turned)
    canvas.alpha_composite(layer)


def twig_points(start, angle, length, step):
    """Points along a straight twig from start, `angle` degrees anticlockwise from up."""
    theta = math.radians(angle)
    n = max(1, int(length / step))
    return [(start[0] - math.sin(theta) * length * k / n, start[1] - math.cos(theta) * length * k / n)
            for k in range(n + 1)]


def broadleaf(sprites, seed, shade):
    """A twig of leaves: a main stem up the middle, side twigs off it, leaves along all
    of them pointing outward, filling a rough circle."""
    rng = random.Random(seed)
    canvas = Image.new("RGBA", (SIZE, SIZE))
    draw = ImageDraw.Draw(canvas)
    twigs = [((512, 1010), rng.uniform(-6, 6), 820, 9)]
    for k in range(7):
        y = 900 - k * 105
        side = 1 if k % 2 == 0 else -1
        length = 330 - k * 30 + rng.uniform(-40, 40)
        twigs.append(((512 + rng.uniform(-10, 10), y), side * rng.uniform(40, 62), length, 5))
    for start, angle, length, width in twigs:
        points = twig_points(start, angle, length, 20)
        draw.line(points, fill=TWIG, width=width)
    order = []
    for start, angle, length, _ in twigs:
        for k, p in enumerate(twig_points(start, angle, length, 34)[1:]):
            for side in (-1, 1):
                order.append((p, angle + side * rng.uniform(35, 75)))
        order.append((twig_points(start, angle, length, 34)[-1], angle + rng.uniform(-15, 15)))
    rng.shuffle(order)
    for n, (p, angle) in enumerate(order):
        depth = n / len(order)
        place(canvas, rng.choice(sprites), p, angle, rng.uniform(120, 170),
              shade * (0.62 + 0.45 * depth), rng)
    return canvas


def conifer(sprites, seed):
    """A bough seen from above: sprays of needles either side of a stem, longest at the
    base, so the card tapers to its tip."""
    rng = random.Random(seed)
    canvas = Image.new("RGBA", (SIZE, SIZE))
    ImageDraw.Draw(canvas).line([(512, 1015), (512, 60)], fill=TWIG, width=7)
    sprays = []
    for layer in range(2):
        for k in range(13):
            y = 960 - k * 68 + rng.uniform(-12, 12)
            reach = 1.0 - k / 14
            for side in (-1, 1):
                sprays.append((layer, (512, y), side * rng.uniform(38, 58), 180 + 300 * reach * rng.uniform(0.85, 1.1)))
    sprays.append((1, (512, 120), rng.uniform(-8, 8), 190))
    for n, (layer, p, angle, length) in enumerate(sprays):
        place(canvas, rng.choice(sprites), p, angle, length, (0.6 if layer == 0 else 0.85) * rng.uniform(0.9, 1.05), rng)
    return canvas


def palm(seed):
    """A palm frond: a stem up the middle and narrow leaflets off both sides, angled to
    the tip, longest in the middle, drawn as polygons with a midrib, yellowing at the ends."""
    rng = random.Random(seed)
    canvas = Image.new("RGBA", (SIZE, SIZE))
    draw = ImageDraw.Draw(canvas)
    for k in range(46):
        t = k / 45
        y = 990 - t * 930
        length = 470 * math.sin(math.pi * (0.1 + 0.85 * t)) * rng.uniform(0.85, 1.05)
        for side in (-1, 1):
            angle = math.radians(rng.uniform(35, 50))
            droop = rng.uniform(0.05, 0.2)
            dx, dy = side * math.sin(angle), -math.cos(angle)
            tip = (512 + dx * length, y + dy * length + droop * length)
            width = rng.uniform(14, 20)
            nx, ny = -dy * side, dx * side
            mid = (512 + dx * length * 0.45, y + dy * length * 0.45 + droop * length * 0.2)
            green = np.array([66, 104, 38]) * rng.uniform(0.8, 1.15)
            yellow = np.array([150, 140, 60])
            end = rng.uniform(0.0, 0.35)
            colour = tuple(int(v) for v in green * (1 - end) + yellow * end) + (255,)
            draw.polygon([(512, y), (mid[0] + nx * width, mid[1] + ny * width), tip,
                          (mid[0] - nx * width, mid[1] - ny * width)], fill=colour)
            draw.line([(512, y), mid, tip], fill=tuple(int(v * 0.8) for v in colour[:3]) + (255,), width=2)
    draw.line([(512, 1015), (512, 50)], fill=(96, 92, 50, 255), width=10)
    return canvas


def bleed(canvas):
    """Transparent pixels take the colour of the nearest opaque one."""
    pixels = np.asarray(canvas).copy()
    empty = pixels[..., 3] < 8
    _, (iy, ix) = ndimage.distance_transform_edt(empty, return_indices=True)
    pixels[..., :3] = pixels[iy, ix, :3]
    return Image.fromarray(pixels, "RGBA"), 1.0 - empty.mean()


def main(leaves_dir, out_dir):
    leaves_dir, out_dir = Path(leaves_dir), Path(out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)
    beech = leaves(leaves_dir, "LeafSet024", 0)
    lime = leaves(leaves_dir, "LeafSet014", 0)
    needles = leaves(leaves_dir, "LeafSet019", 90)
    print(f"leaves cut out: {len(beech)} beech, {len(lime)} lime, {len(needles)} needle sprays")
    made = {
        "broadleaf_a": broadleaf(beech, 1, 0.9),
        "broadleaf_b": broadleaf(lime, 2, 0.72),
        "conifer": conifer(needles, 3),
        "palm": palm(4),
    }
    for name, canvas in made.items():
        image, cover = bleed(canvas)
        image.save(out_dir / f"{name}.png", optimize=True)
        print(f"{name}.png: {cover:.0%} covered")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
