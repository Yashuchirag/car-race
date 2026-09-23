"""Conversion between WGS84 and a local metric frame centred on one circuit.

A transverse Mercator projection centred on the circuit keeps scale distortion
below a millimetre across a 10 km extent, which is far finer than the source
geometry, so local coordinates can be treated as exact metres.
"""

import numpy as np
import pyproj


class LocalFrame:
    """East/north metres relative to an origin latitude and longitude."""

    def __init__(self, lat0: float, lon0: float):
        self.lat0 = lat0
        self.lon0 = lon0
        local = pyproj.CRS.from_proj4(
            f"+proj=tmerc +lat_0={lat0} +lon_0={lon0} "
            "+k=1 +x_0=0 +y_0=0 +ellps=WGS84 +units=m +no_defs"
        )
        self._to_local = pyproj.Transformer.from_crs("EPSG:4326", local, always_xy=True)
        self._to_wgs = pyproj.Transformer.from_crs(local, "EPSG:4326", always_xy=True)

    def to_local(self, lon, lat):
        """Longitude/latitude in degrees to east/north in metres."""
        x, y = self._to_local.transform(np.asarray(lon), np.asarray(lat))
        return np.asarray(x), np.asarray(y)

    def to_wgs(self, x, y):
        """East/north in metres to longitude/latitude in degrees."""
        lon, lat = self._to_wgs.transform(np.asarray(x), np.asarray(y))
        return np.asarray(lon), np.asarray(lat)


def bbox_around(lat: float, lon: float, radius_m: float):
    """Square bounding box of the given half-width, as (south, west, north, east)."""
    dlat = radius_m / 111_320.0
    dlon = radius_m / (111_320.0 * max(np.cos(np.radians(lat)), 1e-6))
    return lat - dlat, lon - dlon, lat + dlat, lon + dlon
