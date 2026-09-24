"""Turn a roughness map into URP's metallic-smoothness map.

    Tools/.venv/bin/python Tools/smoothness_map.py rough.jpg out.png

URP Lit reads smoothness from the alpha channel of its metallic map, and texture libraries
ship roughness, so this writes RGBA with no metal (RGB 0) and alpha = 1 - roughness.
"""

import sys

import numpy as np
from PIL import Image


def main(rough_path, out_path):
    rough = np.asarray(Image.open(rough_path).convert("L"), dtype=np.float32) / 255.0
    out = np.zeros(rough.shape + (4,), np.uint8)
    out[..., 3] = np.round((1.0 - rough) * 255.0).astype(np.uint8)
    Image.fromarray(out, "RGBA").save(out_path, optimize=True)
    print(f"roughness {rough.min():.2f} to {rough.max():.2f}, mean {rough.mean():.2f} -> {out_path}")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
