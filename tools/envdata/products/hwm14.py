"""Upper-atmosphere winds from HWM14 above a measured sounding (environment plan E4.2).

The official HWM14 Fortran (Drob et al. 2015, Earth and Space Science 2:301; release 123114 as
archived with its three data files at Zenodo DOI 10.5281/zenodo.240890) is compiled with gfortran
into a small driver at build time. Every build first reproduces NRL's published check profile
(day 323 of 1993, 11.66667 UT, Jicamarca, ap 35: e.g. 90 km 11.112 / 28.726 m/s, 200 km -23.171 /
-29.150 m/s meridional/zonal) to 1 mm/s, then writes the profile over the launch site at the
launch instant with the GFZ 3-hour ap of that interval, in the same columns as a sounding
(altitude, wind-from direction, speed) so ProfileWind can stack it above the sounding.
"""
from __future__ import annotations

import io
import math
import os
import subprocess
import tempfile
import zipfile

PROFILES = {
    # id: (latitude, longitude, year, day of year, UT seconds, GFZ ap 3-hour slot, description)
    "hwm_as506": (28.6084, -80.6043, 1969, 197, 13 * 3600 + 32 * 60, (1969, 7, 16, 4),
                  "HWM14 over LC-39A at the AS-506 launch, 1969-07-16 13:32 UT"),
}
CHECK = {90: (11.112, 28.726), 100: (26.755, 6.700), 110: (-40.442, 1.402), 120: (-15.840, -16.838),
         150: (-26.765, -29.464), 200: (-23.171, -29.150)}
DRIVER = """program driver
  implicit none
  integer :: iyd, ios
  real(4) :: sec, alt, glat, glon, stl, f107a, f107, ap(2), w(2)
  do
    read(*,*,iostat=ios) iyd, sec, alt, glat, glon, ap(2)
    if (ios /= 0) exit
    ap(1) = -1.0
    f107a = 150.0
    f107 = 150.0
    stl = mod(sec/3600.0 + glon/15.0 + 48.0, 24.0)
    call hwm14(iyd, sec, alt, glat, glon, stl, f107a, f107, ap, w)
    write(*,'(2es20.10)') w(1), w(2)
  end do
end program driver
"""


def _compile(archive: bytes) -> str:
    folder = tempfile.mkdtemp(prefix="envdata_hwm14_")
    with zipfile.ZipFile(io.BytesIO(archive)) as z:
        for name in z.namelist():
            if name.endswith(("hwm14.f90", ".bin", ".dat")):
                with open(os.path.join(folder, name.split("/")[-1]), "wb") as handle:
                    handle.write(z.read(name))
    with open(os.path.join(folder, "driver.f90"), "w") as handle:
        handle.write(DRIVER)
    subprocess.run(["gfortran", "-O1", "-std=legacy", "-o", "hwm14drv", "hwm14.f90", "driver.f90"],
                   cwd=folder, check=True, capture_output=True)
    return folder


def _run(folder: str, lines: list[str]) -> list[tuple[float, float]]:
    out = subprocess.run([os.path.join(folder, "hwm14drv")], cwd=folder, input="\n".join(lines) + "\n",
                         capture_output=True, text=True, check=True).stdout.split("\n")
    return [tuple(float(v) for v in line.split()) for line in out if line.strip()]


def _ap(text: str, year: int, month: int, day: int, slot: int) -> float:
    for line in text.splitlines():
        f = line.split()
        if f and not line.startswith("#") and (int(f[0]), int(f[1]), int(f[2])) == (year, month, day):
            return float(f[15 + slot])  # ap1..ap8 start at column 15
    raise ValueError("no GFZ row for the launch date")


def build_series(product_id: str, sources: dict):
    lat, lon, year, doy, sec, (y, m, d, slot), description = PROFILES[product_id]
    folder = _compile(sources["zenodo_pyhwm14"])
    check = _run(folder, [f"93323 42000.012 {alt} -11.95 -76.77 35" for alt in CHECK])
    for (alt, (mer, zon)), (w_mer, w_zon) in zip(CHECK.items(), check):
        if abs(w_mer - mer) > 1e-3 or abs(w_zon - zon) > 1e-3:
            raise ValueError(f"HWM14 build does not reproduce NRL's check profile at {alt} km")
    ap = _ap(sources["gfz_kp_ap_f107"].decode("ascii"), y, m, d, slot)
    altitudes = list(range(20, 151))
    winds = _run(folder, [f"{year % 100:02d}{doy:03d} {sec} {a} {lat} {lon} {ap}" for a in altitudes])
    rows = []
    for alt, (north, east) in zip(altitudes, winds):
        speed = math.hypot(north, east)
        wind_from = (math.degrees(math.atan2(-east, -north)) + 360.0) % 360.0
        rows.append([alt * 1000.0, float("nan"), float("nan"), wind_from, speed, float("nan")])
    header = {"product": product_id, "description": description, "model": "HWM14 release 123114",
              "time_scale": "UTC", "frame": "local ENU", "station_latitude_deg": lat, "station_longitude_deg": lon,
              "ap_3h": ap, "check_profile": "NRL checkhwm14 day 323/1993 reproduced to 1 mm/s",
              "columns": ["altitude_m", "geopotential_m", "pressure_pa", "wind_from_deg", "wind_speed_mps", "temperature_k"],
              "units": ["m", "m", "Pa", "deg", "m/s", "K"]}
    return header, rows
