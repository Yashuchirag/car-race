"""Turn a raw traced ring into a smooth centreline with usable curvature.

OpenStreetMap node spacing is irregular and carries digitising noise of roughly
half a metre. Differentiating that directly produces curvature spikes that are
pure noise, so the ring is fitted with a periodic smoothing spline and then
resampled at uniform arc length before any geometry is measured.
"""

import numpy as np
from scipy.interpolate import splev, splprep


def polyline_length(xy):
    """Total length of an open or closed polyline in metres."""
    return float(np.hypot(*np.diff(xy, axis=0).T).sum())


def close_ring(xy, tol=1.0):
    """Ensure the first and last point coincide, which splprep requires."""
    xy = np.asarray(xy, dtype=float)
    # Drop consecutive duplicates, which break the spline fit.
    keep = np.concatenate(([True], np.hypot(*np.diff(xy, axis=0).T) > 1e-6))
    xy = xy[keep]
    gap = float(np.hypot(*(xy[0] - xy[-1])))
    if gap > tol:
        xy = np.vstack([xy, xy[0]])
    else:
        xy[-1] = xy[0]
    return xy, gap


def fit_periodic_spline(xy, noise_m=0.5):
    """Fit a closed cubic smoothing spline.

    `noise_m` is the expected per-point digitising error. The spline is allowed a
    total squared residual of N * noise_m^2, which is the natural scale for that
    assumption and is far easier to reason about than splprep's raw `s`.
    """
    n = len(xy)
    smoothing = n * (noise_m ** 2)
    tck, _ = splprep([xy[:, 0], xy[:, 1]], s=smoothing, per=True, k=3)
    return tck


def sample_uniform(tck, spacing=2.0, dense_factor=40):
    """Sample a closed spline at uniform arc length.

    Returns a dict with position, arc length, heading, curvature and the unit
    left normal at every sample. Curvature uses the parameterisation-invariant
    form, so it is correct despite the spline being parameterised in u, not s.
    """
    u_dense = np.linspace(0.0, 1.0, dense_factor * 2000)
    xd, yd = splev(u_dense, tck)
    seg = np.hypot(np.diff(xd), np.diff(yd))
    s_dense = np.concatenate(([0.0], np.cumsum(seg)))
    total = s_dense[-1]

    n_samples = max(int(round(total / spacing)), 16)
    s_target = np.linspace(0.0, total, n_samples, endpoint=False)
    u_target = np.interp(s_target, s_dense, u_dense)

    x, y = splev(u_target, tck)
    dx, dy = splev(u_target, tck, der=1)
    ddx, ddy = splev(u_target, tck, der=2)

    speed = np.hypot(dx, dy)
    heading = np.arctan2(dy, dx)
    curvature = (dx * ddy - dy * ddx) / np.maximum(speed ** 3, 1e-12)

    return {
        "x": x,
        "y": y,
        "s": s_target,
        "heading": heading,
        "curvature": curvature,
        "normal_x": -np.sin(heading),
        "normal_y": np.cos(heading),
        "length": float(total),
        "spacing": float(total / n_samples),
    }


def total_turning(xy):
    """Integrated absolute heading change of a closed polyline, in radians.

    A simple closed curve always turns 2*pi in net terms, so net turning says
    nothing. The absolute total does: it counts a chicane's left and its right
    separately, which makes it sensitive to corners being smoothed away.
    """
    d = np.diff(np.asarray(xy, dtype=float), axis=0)
    head = np.arctan2(d[:, 1], d[:, 0])
    step = np.diff(np.concatenate([head, head[:1]]))
    return float(np.abs((step + np.pi) % (2 * np.pi) - np.pi).sum())


def fit_best_spline(xy, raw_length, spacing=2.0,
                    candidates=(0.5, 1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 8.0),
                    length_tol_pct=0.5, min_radius_floor_m=8.0, verbose=True):
    """Choose the least smoothing that leaves the curvature usable.

    Too little smoothing and the spline overshoots through the tight clusters of
    nodes mappers use for slow corners, inventing radii tighter than the traced
    polygon. Too much and it erases real corners: at Monza a smoothing of 8 m
    raised the tightest radius from 8.7 m to 54.7 m, flattening both chicanes,
    while changing total length by only 0.36% so no length check would notice.

    So the rule is least smoothing, not most: of the fits holding the traced
    length, take the one with the smallest smoothing whose tightest radius
    clears `min_radius_floor_m`. Smoothing beyond that point only destroys
    corners. If nothing clears the floor, fall back to the largest minimum
    radius and say so, because the response is not monotonic and the best
    available fit may still carry a spike.
    """
    trials = []
    for noise in candidates:
        try:
            tck = fit_periodic_spline(xy, noise_m=noise)
            geom = sample_uniform(tck, spacing=spacing)
        except Exception as exc:  # noqa: BLE001 - a failed fit is just a rejected candidate
            trials.append({"noise_m": noise, "error": type(exc).__name__})
            continue
        peak = float(np.max(np.abs(geom["curvature"])))
        trials.append({
            "noise_m": noise,
            "length_m": round(geom["length"], 1),
            "length_error_pct": round(100.0 * (geom["length"] - raw_length) / raw_length, 3),
            "min_radius_m": round(1.0 / max(peak, 1e-9), 1),
            "_tck": tck, "_geom": geom,
        })

    usable = [t for t in trials
              if "_geom" in t and abs(t["length_error_pct"]) <= length_tol_pct]
    if not usable:
        raise RuntimeError(
            "no smoothing level held the traced length to "
            f"{length_tol_pct}%. Tried: "
            + ", ".join(f"{t['noise_m']}m->{t.get('length_error_pct', 'fit failed')}"
                        for t in trials))

    clear = [t for t in usable if t["min_radius_m"] >= min_radius_floor_m]
    if clear:
        best = min(clear, key=lambda t: t["noise_m"])
        why = f"least smoothing clearing the {min_radius_floor_m:.0f} m floor"
    else:
        best = max(usable, key=lambda t: t["min_radius_m"])
        why = (f"nothing cleared the {min_radius_floor_m:.0f} m floor, "
               "so took the largest minimum radius")

    if verbose:
        print(f"  smoothing: noise={best['noise_m']} m, {why} "
              f"(tightest radius {best['min_radius_m']} m, "
              f"length {best['length_error_pct']:+.3f}% vs traced, "
              f"{len(usable)} usable of {len(trials)})")
    summary = [{k: v for k, v in t.items() if not k.startswith("_")} for t in trials]
    return best["_tck"], best["_geom"], best["noise_m"], summary


def detect_corners(s, curvature, max_radius=400.0, min_length=15.0):
    """Group the centreline into corners, wrapping across the start line.

    A corner is a contiguous run where the turn radius stays under `max_radius`.
    Returned entries carry the apex, so kerb and marker placement can key off them.
    """
    n = len(s)
    total = float(s[-1] + (s[1] - s[0]))
    threshold = 1.0 / max_radius
    active = np.abs(curvature) > threshold
    if not active.any():
        return []

    # Rotate so index 0 sits on a straight, which keeps runs contiguous.
    shift = int(np.argmin(np.abs(curvature)))
    rolled = np.roll(active, -shift)
    sign = np.sign(np.roll(curvature, -shift))

    corners = []
    i = 0
    while i < n:
        if not rolled[i]:
            i += 1
            continue
        j = i
        while j + 1 < n and rolled[j + 1] and sign[j + 1] == sign[i]:
            j += 1

        idx = (np.arange(i, j + 1) + shift) % n
        length = float(len(idx) * (total / n))
        if length >= min_length:
            k = curvature[idx]
            apex = idx[int(np.argmax(np.abs(k)))]
            corners.append({
                "s_start": float(s[idx[0]]),
                "s_end": float(s[idx[-1]]),
                "length_m": round(length, 1),
                "direction": "left" if k[np.argmax(np.abs(k))] > 0 else "right",
                "min_radius_m": round(float(1.0 / np.max(np.abs(k))), 1),
                "apex_s": float(s[apex]),
            })
        i = j + 1

    corners.sort(key=lambda c: c["s_start"])
    for n_i, c in enumerate(corners, start=1):
        c["number"] = n_i
    return corners
