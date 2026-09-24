"""Measure a Radiance .hdr sky for the Unity scene: where the sun is, its colour and
brightness against the sky, and the colour at the horizon.

    Tools/.venv/bin/python Tools/sky_analysis.py path/to/sky_1k.hdr

The 1k version of a Poly Haven sky is enough; the numbers are averages. The direction uses
the formula of Unity's Skybox/Panoramic shader at rotation 0,

    u = 0.5 - atan2(z, x) / 2pi,    v = 1 - acos(y) / pi,

so a directional light pointed along -direction lights the scene from where the sun is drawn.
The intensity printed is the sun's illuminance over pi: URP's direct lighting has no pi in it,
while its ambient light from the sky is irradiance over pi, so that is the intensity that puts
the sun and the sky at their real ratio with the sky material at exposure 1.
"""

import math
import sys

import numpy as np


def read_hdr(path):
    """Radiance RGBE with the new run length encoding, the only kind Poly Haven ships."""
    data = open(path, "rb").read()
    i = data.index(b"\n\n") + 2
    j = data.index(b"\n", i)
    size = data[i:j].split()
    if size[0] != b"-Y" or size[2] != b"+X":
        raise ValueError(f"unexpected orientation {size}")
    height, width = int(size[1]), int(size[3])
    p = j + 1
    rgbe = np.zeros((height, width, 4), np.uint8)
    for row in range(height):
        if data[p] != 2 or data[p + 1] != 2:
            raise ValueError("not new-style run length encoded")
        p += 4
        for channel in range(4):
            x = 0
            while x < width:
                n = data[p]
                p += 1
                if n > 128:
                    n -= 128
                    rgbe[row, x:x + n, channel] = data[p]
                    p += 1
                else:
                    rgbe[row, x:x + n, channel] = np.frombuffer(data[p:p + n], np.uint8)
                    p += n
                x += n
    exponent = rgbe[..., 3].astype(np.int32)
    scale = np.ldexp(1.0, exponent - 136)
    return np.where(exponent[..., None] > 0, rgbe[..., :3] * scale[..., None], 0.0)


def main(path):
    rgb = read_hdr(path)
    height, width, _ = rgb.shape
    lum = rgb @ [0.2126, 0.7152, 0.0722]

    lat = ((np.arange(height) + 0.5) / height * math.pi)[:, None]
    lon = ((0.5 - (np.arange(width) + 0.5) / width) * 2 * math.pi)[None, :]
    dirs = np.stack([np.sin(lat) * np.cos(lon), np.cos(lat) * np.ones_like(lon),
                     np.sin(lat) * np.sin(lon)], -1)
    solid_angle = (2 * math.pi / width) * (math.pi / height) * np.sin(lat) * np.ones_like(lon)

    # The sun: the luminance weighted centroid of pixels above half the peak.
    mask = lum > lum.max() * 0.5
    sun = (dirs[mask] * lum[mask, None]).sum(0)
    sun /= np.linalg.norm(sun)
    colour = rgb[mask].mean(0)
    colour /= colour.max()

    # Its illuminance: the light above the surrounding sky within 3 degrees of it.
    angle = np.degrees(np.arccos(np.clip(dirs @ sun, -1, 1)))
    background = np.median(lum[(angle > 5) & (angle < 15)])
    disc = angle < 3
    excess = (lum[disc] - background).clip(0) * solid_angle[disc]
    sun_normal = (excess * (dirs[disc] @ sun)).sum()
    up = dirs[..., 1] > 0
    sky_ground = (lum[up] * solid_angle[up] * dirs[up][:, 1]).sum() - (excess * dirs[disc][:, 1]).sum()

    horizon = rgb[int(height * 0.5 * (1 - 5 / 90)):height // 2].reshape(-1, 3).mean(0)

    elevation = math.degrees(math.asin(sun[1]))
    print(f"towards the sun (Unity x, y, z)  ({sun[0]:.4f}, {sun[1]:.4f}, {sun[2]:.4f}), {elevation:.1f} deg up")
    print(f"sun colour                       {np.round(colour, 3)}")
    print(f"sun intensity for URP            {sun_normal / math.pi:.2f}")
    print(f"sun / sky on level ground        {sun_normal * sun[1] / sky_ground:.2f}")
    print(f"horizon colour, linear           {np.round(horizon, 3)}")


if __name__ == "__main__":
    main(sys.argv[1])
