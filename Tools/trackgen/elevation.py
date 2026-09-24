"""Sample terrain elevation for the centreline from a public DEM service.

SRTM is 30 m horizontally and roughly +/-5 m vertically, so it is sampled well
below the centreline resolution and then smoothed and interpolated back up.
Sampling finer than the DEM buys nothing but noise.

The result is a usable base profile, not a finished one. Circuits whose character
lives in their elevation, Spa above all, still need the important features
sculpted by hand afterwards.
"""

import json
import time
from pathlib import Path

import numpy as np
import requests
from scipy.interpolate import CubicSpline
from scipy.ndimage import gaussian_filter1d

API = "https://api.opentopodata.org/v1/{dataset}"
BATCH = 100
RATE_LIMIT_S = 1.1


def sample(lats, lons, dataset="srtm30m", cache_path=None, verbose=True):
    """Return elevations in metres for the given coordinates, with nulls filled."""
    lats = np.asarray(lats, dtype=float)
    lons = np.asarray(lons, dtype=float)

    cache = {}
    cache_file = Path(cache_path) if cache_path else None
    if cache_file and cache_file.exists():
        cache = json.loads(cache_file.read_text())

    keys = [f"{la:.5f},{lo:.5f}" for la, lo in zip(lats, lons)]
    missing = [k for k in dict.fromkeys(keys) if k not in cache]

    if missing:
        if verbose:
            print(f"  elevation: {len(missing)} points to fetch from {dataset}")
        for start in range(0, len(missing), BATCH):
            chunk = missing[start:start + BATCH]
            url = API.format(dataset=dataset)
            resp = requests.get(url, params={"locations": "|".join(chunk)}, timeout=60)
            resp.raise_for_status()
            results = resp.json().get("results", [])
            for key, res in zip(chunk, results):
                cache[key] = res.get("elevation")
            if start + BATCH < len(missing):
                time.sleep(RATE_LIMIT_S)
        if cache_file:
            cache_file.parent.mkdir(parents=True, exist_ok=True)
            cache_file.write_text(json.dumps(cache))
    elif verbose:
        print(f"  elevation: all {len(keys)} points cached")

    z = np.array([cache.get(k) if cache.get(k) is not None else np.nan for k in keys])
    return _fill_gaps(z)


def _fill_gaps(z):
    """Interpolate over DEM voids so a few nulls cannot poison the profile."""
    bad = np.isnan(z)
    if bad.all():
        return np.zeros_like(z)
    if bad.any():
        idx = np.arange(len(z))
        z[bad] = np.interp(idx[bad], idx[~bad], z[~bad])
    return z


def smooth_closed(z, window_m, spacing_m):
    """Circular moving average, since the profile wraps at the start line."""
    w = max(int(round(window_m / max(spacing_m, 1e-6))), 1)
    if w % 2 == 0:
        w += 1
    if w <= 1:
        return z
    kernel = np.ones(w) / w
    padded = np.concatenate([z[-w:], z, z[:w]])
    return np.convolve(padded, kernel, mode="same")[w:-w]


def profile_for_centerline(frame, cx, cy, spacing_m, sample_every_m=25.0,
                           smooth_m=120.0, dataset="srtm30m", cache_path=None,
                           verbose=True):
    """Build a smooth elevation profile covering every centreline sample."""
    n = len(cx)
    step = max(int(round(sample_every_m / spacing_m)), 1)
    idx = np.arange(0, n, step)

    lon, lat = frame.to_wgs(cx[idx], cy[idx])
    z_coarse = sample(lat, lon, dataset=dataset, cache_path=cache_path, verbose=verbose)
    z_coarse = smooth_closed(z_coarse, smooth_m, spacing_m * step)

    # A light Gaussian on top of the box average, one coarse sample wide, rounds off what
    # the box leaves at its edges.
    z_coarse = gaussian_filter1d(z_coarse, 1.0, mode="wrap")

    # A periodic cubic spline back up to the centreline samples, not straight lines. Joined
    # with straight lines the slope changed abruptly every 25 m, and a car at 216 km/h met a
    # small ramp at every coarse sample: up to 4.6 g vertically at Monza and 7.3 g at Suzuka,
    # enough to take all four wheels off the ground on a circuit that is nearly flat. The
    # spline removes the kinks, and every climb stays within a metre of the linear profile.
    # Wrap one sample past the end so it closes cleanly at the start line.
    xp = np.concatenate([idx, [n]])
    fp = np.concatenate([z_coarse, [z_coarse[0]]])
    return CubicSpline(xp, fp, bc_type="periodic")(np.arange(n))
