"""Verification plot for a generated track.

Look at this before importing anything into the engine. A wrong stitch, a
smoothing setting that has cut the corners, or a nonsense speed profile are all
obvious here and all expensive to find later.
"""

import matplotlib
matplotlib.use("Agg")

import matplotlib.pyplot as plt  # noqa: E402
import numpy as np  # noqa: E402
from matplotlib.collections import LineCollection  # noqa: E402


def plot_track(track, out_path):
    c = track["centerline"]
    r = track["racing_line"]
    cx, cy = np.array(c["x"]), np.array(c["y"])
    h = np.array(c["heading"])
    wl, wr = np.array(c["width_left"]), np.array(c["width_right"])
    nx, ny = -np.sin(h), np.cos(h)
    speed_kmh = np.array(r["speed_ms"]) * 3.6

    fig = plt.figure(figsize=(17, 9), facecolor="white")
    grid = fig.add_gridspec(3, 2, width_ratios=[1.45, 1], hspace=0.42, wspace=0.18)

    # --- track map -------------------------------------------------------
    ax = fig.add_subplot(grid[:, 0])
    left = np.stack([cx + wl * nx, cy + wl * ny], axis=1)
    right = np.stack([cx - wr * nx, cy - wr * ny], axis=1)
    ax.fill(np.concatenate([left[:, 0], right[::-1, 0]]),
            np.concatenate([left[:, 1], right[::-1, 1]]),
            color="#d8d8d8", zorder=1)
    for edge in (left, right):
        ax.plot(edge[:, 0], edge[:, 1], color="#4a4a4a", lw=1.0, zorder=2)
    ax.plot(cx, cy, color="#ffffff", lw=0.8, ls=(0, (7, 7)), zorder=3)

    pts = np.stack([np.array(r["x"]), np.array(r["y"])], axis=1).reshape(-1, 1, 2)
    segs = np.concatenate([pts[:-1], pts[1:]], axis=1)
    lc = LineCollection(segs, cmap="RdYlGn", linewidths=2.6, zorder=4)
    lc.set_array(speed_kmh[:-1])
    ax.add_collection(lc)
    fig.colorbar(lc, ax=ax, label="racing line speed (km/h)",
                 orientation="horizontal", location="bottom",
                 fraction=0.045, pad=0.09)

    ax.plot(cx[0], cy[0], "o", ms=11, mfc="#1f77b4", mec="white", mew=2, zorder=6)
    ax.annotate("start / finish", (cx[0], cy[0]), textcoords="offset points",
                xytext=(12, 12), fontsize=9, fontweight="bold")
    for corner in track["corners"]:
        i = int(corner["apex_s"] / track["sample_spacing_m"]) % len(cx)
        ax.annotate(str(corner["number"]), (cx[i], cy[i]), fontsize=7,
                    color="#b00020", ha="center", va="center", zorder=7)

    lap = track["estimated_lap_time_s"]
    ax.set_title(
        f"{track['name']}   ({track['source_layout']} layout)\n"
        f"{track['length_m']:.0f} m, {track['length_error_pct']:+.2f}% vs official   |   "
        f"{track['reference_car']['name']} estimate {int(lap // 60)}:{lap % 60:06.3f}",
        fontsize=12, fontweight="bold")
    ax.set_aspect("equal")
    ax.set_xlabel("east (m)")
    ax.set_ylabel("north (m)")
    ax.grid(alpha=0.25)

    s = np.array(c["s"])

    # --- elevation --------------------------------------------------------
    ax1 = fig.add_subplot(grid[0, 1])
    z = np.array(c["z"])
    ax1.fill_between(s, z.min(), z, color="#8d6e63", alpha=0.45)
    ax1.plot(s, z, color="#5d4037", lw=1.3)
    ax1.set_ylabel("elevation (m)")
    ax1.set_title(f"elevation profile   (range {z.max() - z.min():.1f} m)", fontsize=10)
    ax1.grid(alpha=0.25)

    # --- curvature --------------------------------------------------------
    ax2 = fig.add_subplot(grid[1, 1], sharex=ax1)
    k = np.array(c["curvature"])
    ax2.axhline(0, color="#999", lw=0.8)
    ax2.fill_between(s, 0, k, where=k >= 0, color="#1565c0", alpha=0.6, label="left")
    ax2.fill_between(s, 0, k, where=k < 0, color="#c62828", alpha=0.6, label="right")
    ax2.set_ylabel("curvature (1/m)")
    ax2.set_title("centreline curvature", fontsize=10)
    ax2.legend(fontsize=8, loc="upper right")
    ax2.grid(alpha=0.25)

    # --- speed ------------------------------------------------------------
    ax3 = fig.add_subplot(grid[2, 1], sharex=ax1)
    ax3.plot(s, speed_kmh, color="#2e7d32", lw=1.4)
    ax3.fill_between(s, 0, speed_kmh, color="#2e7d32", alpha=0.15)
    ax3.set_ylabel("speed (km/h)")
    ax3.set_xlabel("distance along lap (m)")
    ax3.set_title("racing line speed profile", fontsize=10)
    ax3.grid(alpha=0.25)

    fig.savefig(out_path, dpi=110, bbox_inches="tight")
    plt.close(fig)
