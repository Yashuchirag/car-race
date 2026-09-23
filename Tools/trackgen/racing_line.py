"""Minimum-curvature racing line and the speed profile that follows from it.

The line is solved as a bounded linear least-squares problem: every solve node
gets one lateral offset, and the objective is the summed squared second
difference of the resulting path, which is a discrete stand-in for curvature.
Box bounds keep the car inside the track.

Minimum curvature is not exactly the minimum lap time, because a real fast line
trades a little corner radius for a better exit onto a long straight, and that
trade depends on the car. It is close enough to drive well and to serve as the
AI target, and it needs no vehicle model to compute.
"""

from dataclasses import dataclass, asdict

import numpy as np
from scipy.interpolate import CubicSpline
from scipy.optimize import lsq_linear
from scipy.sparse import coo_matrix

G = 9.81
RHO = 1.225


@dataclass
class Car:
    """Reference car used only to derive a speed profile and a lap-time estimate."""
    name: str = "GT3"
    mass: float = 1300.0        # kg
    mu: float = 1.45            # peak tyre friction coefficient
    cl_a: float = 3.00          # downforce coefficient times frontal area
    cd_a: float = 1.10          # drag coefficient times frontal area
    power_w: float = 375_000.0  # engine power in watts, about 500 hp
    v_max: float = 80.0         # m/s, about 288 km/h
    width: float = 2.00         # m
    brake_frac: float = 0.95    # share of grip usable under braking
    traction_frac: float = 0.55 # share of grip at the driven axle


def optimise(cx, cy, nx, ny, width_left, width_right, car, margin=0.6,
             solve_spacing=8.0, sample_spacing=2.0, verbose=True):
    """Solve for the lateral offset at every centreline sample.

    `nx, ny` is the unit left normal, so a positive offset moves the line left.

    The solve runs on a coarser grid than the centreline and the result is
    interpolated back up with a periodic cubic spline. Bounded least squares
    costs iterations per active constraint, and a hairpin pins a long run of
    samples hard against the track edge, so solving every sample is slow.

    The interpolant has to be smooth. Interpolating the offsets linearly puts a
    curvature kink at every node, which measured 6.9% slower on the test circuit
    against a full-resolution solve; the cubic came within 0.6% at six times the
    speed. Pass solve_spacing=sample_spacing to skip the approximation entirely.
    """
    stride = max(int(round(solve_spacing / max(sample_spacing, 1e-6))), 1)
    n_full = len(cx)

    if stride > 1:
        sel = np.arange(0, n_full, stride)
        coarse, report = _solve(cx[sel], cy[sel], nx[sel], ny[sel],
                               width_left[sel], width_right[sel], car, margin)
        # Smooth periodic interpolation back onto the full centreline.
        alpha = CubicSpline(np.append(sel, n_full),
                            np.append(coarse, coarse[0]),
                            bc_type="periodic")(np.arange(n_full))
    else:
        sel = np.arange(n_full)
        alpha, report = _solve(cx, cy, nx, ny, width_left, width_right, car, margin)

    # Interpolation can nudge a value a hair past the bound, so reclamp.
    clearance = car.width / 2.0 + margin
    hi = np.maximum(width_left - clearance, 0.0)
    lo = -np.maximum(width_right - clearance, 0.0)
    alpha = np.clip(alpha, lo, hi)

    report["solve_nodes"] = int(len(sel))
    report["max_offset_m"] = round(float(np.max(np.abs(alpha))), 2)
    if verbose:
        print(f"  racing line: {report['solve_nodes']} solve nodes, "
              f"max offset {report['max_offset_m']} m, "
              f"{report['nodes_on_track_edge_pct']}% of nodes on the edge")
    return alpha, report


def _solve(cx, cy, nx, ny, width_left, width_right, car, margin):
    """Bounded least squares for the offsets on whatever grid it is given."""
    n = len(cx)
    i = np.arange(n)
    im1 = (i - 1) % n
    ip1 = (i + 1) % n

    # Rows alternate the x and y components of the path's second difference.
    rows = np.concatenate([2 * i, 2 * i, 2 * i, 2 * i + 1, 2 * i + 1, 2 * i + 1])
    cols = np.concatenate([im1, i, ip1, im1, i, ip1])
    vals = np.concatenate([nx[im1], -2 * nx[i], nx[ip1],
                           ny[im1], -2 * ny[i], ny[ip1]])
    a_mat = coo_matrix((vals, (rows, cols)), shape=(2 * n, n)).tocsr()

    b_vec = np.empty(2 * n)
    b_vec[0::2] = cx[im1] - 2 * cx[i] + cx[ip1]
    b_vec[1::2] = cy[im1] - 2 * cy[i] + cy[ip1]

    clearance = car.width / 2.0 + margin
    hi = np.maximum(width_left - clearance, 0.0)
    lo = -np.maximum(width_right - clearance, 0.0)
    # Guard against a corridor narrower than the car.
    collapsed = hi < lo
    hi[collapsed] = 0.0
    lo[collapsed] = 0.0

    sol = lsq_linear(a_mat, -b_vec, bounds=(lo, hi),
                     lsq_solver="lsmr", max_iter=60, tol=1e-6, verbose=0)
    alpha = sol.x

    at_edge = int(np.sum((alpha > hi - 1e-3) | (alpha < lo + 1e-3)))
    report = {
        "nodes_on_track_edge": at_edge,
        "nodes_on_track_edge_pct": round(100.0 * at_edge / n, 1),
        "optimiser_status": int(sol.status),
    }
    return alpha, report


def menger_curvature(x, y, stride=1):
    """Signed curvature from the circle through three points `stride` apart.

    The racing line is not uniformly spaced once lateral offsets are applied, so
    a finite-difference formula assuming even spacing would be wrong here.

    A wider stride measures curvature over a longer baseline, which rejects
    digitising noise without biasing genuine corners: three points on a circle
    return that circle's radius however far apart they sit. Keep the baseline
    well under the shortest real corner, a few metres, or true corners get
    flattened too.
    """
    n = len(x)
    w = max(int(stride), 1)
    i = np.arange(n)
    p0 = np.stack([x[(i - w) % n], y[(i - w) % n]], axis=1)
    p1 = np.stack([x, y], axis=1)
    p2 = np.stack([x[(i + w) % n], y[(i + w) % n]], axis=1)

    v1, v2 = p1 - p0, p2 - p1
    cross = v1[:, 0] * v2[:, 1] - v1[:, 1] * v2[:, 0]
    a = np.hypot(*v1.T)
    b = np.hypot(*v2.T)
    c = np.hypot(*(p2 - p0).T)
    return 2.0 * cross / np.maximum(a * b * c, 1e-12)


def _long_accel(v, k, car, braking):
    """Longitudinal acceleration still available at this speed and curvature."""
    fz = car.mass * G + 0.5 * RHO * car.cl_a * v * v
    a_grip = car.mu * fz / car.mass
    frac = np.clip((v * v * k) / max(a_grip, 1e-6), 0.0, 1.0)
    ellipse = np.sqrt(max(1.0 - frac * frac, 0.0))
    a_drag = 0.5 * RHO * car.cd_a * v * v / car.mass

    if braking:
        return car.brake_frac * a_grip * ellipse + a_drag
    a_traction = car.traction_frac * a_grip * ellipse
    a_power = car.power_w / (car.mass * max(v, 5.0))
    return max(min(a_traction, a_power) - a_drag, 0.0)


def speed_profile(ds, kappa, car, passes=4):
    """Fastest speed at every sample, limited by grip, power, drag and braking.

    Starts from the pure cornering limit, then alternates a backward pass for
    braking and a forward pass for acceleration until the closed loop settles.
    """
    n = len(ds)
    k = np.abs(kappa)

    # m v^2 k = mu (m g + 0.5 rho Cl A v^2), solved for v.
    denom = car.mass * k - 0.5 * car.mu * RHO * car.cl_a
    with np.errstate(divide="ignore", invalid="ignore"):
        v_corner = np.where(denom > 1e-9,
                            np.sqrt(car.mu * car.mass * G / np.maximum(denom, 1e-9)),
                            car.v_max)
    v = np.minimum(v_corner, car.v_max)

    for _ in range(passes):
        for i in range(n - 1, -1, -1):
            j = (i + 1) % n
            a = _long_accel(v[i], k[i], car, braking=True)
            v[i] = min(v[i], np.sqrt(v[j] ** 2 + 2.0 * a * ds[i]))
        for i in range(n):
            j = (i - 1) % n
            a = _long_accel(v[j], k[j], car, braking=False)
            v[i] = min(v[i], np.sqrt(max(v[j] ** 2 + 2.0 * a * ds[j], 0.0)))

    v = np.maximum(v, 5.0)
    v_mid = 0.5 * (v + np.roll(v, -1))
    lap_time = float(np.sum(ds / v_mid))
    return v, lap_time


def car_presets():
    """Reference cars, so a layout can be sanity-checked against real lap times."""
    return {
        "gt3": Car(name="GT3"),
        "f1": Car(name="F1", mass=798.0, mu=1.75, cl_a=6.0, cd_a=1.5,
                  power_w=760_000.0, v_max=94.0, width=2.0, traction_frac=0.62),
        "road": Car(name="Road sports", mass=1500.0, mu=1.05, cl_a=0.3, cd_a=0.75,
                    power_w=300_000.0, v_max=78.0, width=1.9, traction_frac=0.50),
    }


def car_to_dict(car):
    return asdict(car)
