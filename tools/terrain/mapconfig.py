"""Read MapConfig values straight from the Unity asset YAML (the single source of truth)."""
import re
from pathlib import Path

DEFAULT_ASSET = Path("Assets/World/MapConfig.asset")

_FLOAT_KEYS = ("horizontalScale", "verticalScale", "datumMeters", "poolElevationMeters",
               "northLineZ", "marginMeters", "northVistaMinMeters", "lakeDepthBelowWater",
               "shoreRampMeters", "landMinAboveWater", "bankLipAboveWater", "bankSlopeDeg",
               "bankBandMeters", "bankHighStart", "bankHighEnd", "wadeDepth", "oppositeBankSlopeDeg",
               "oppositeBankOffsetMax", "oppositeBankWavelengthMin", "oppositeBankWavelengthMax",
               "oppositeBankTaper", "bankSteepFadeStartDeg", "bankSteepExemptDeg",
               "bankBluffExclusionMeters", "bankBluffFadeMeters")
_INT_KEYS = ("tileSizeMeters", "heightmapResolution", "gateOpen", "oppositeBankEnabled", "oppositeBankSeed",
             "bankSteepExemptHighBanksOnly")
_STR_KEYS = ("manifestPath",)


class MapConfig:
    def __init__(self, values, path):
        self.path = str(path)
        self.__dict__.update(values)

    @property
    def water_level_y(self):
        return self.dem_to_unity_y(self.poolElevationMeters)

    def dem_to_unity_y(self, dem_m):
        return (dem_m - self.datumMeters) * self.verticalScale

    def as_dict(self):
        d = {k: getattr(self, k) for k in _FLOAT_KEYS + _INT_KEYS + _STR_KEYS}
        d["WaterLevelY"] = self.water_level_y
        return d


def load(project_root, asset=DEFAULT_ASSET):
    path = Path(project_root) / asset
    text = path.read_text(encoding="utf-8")
    raw = dict(re.findall(r"^  (\w+): ?(.*)$", text, flags=re.M))
    values = {}
    for k in _FLOAT_KEYS:
        values[k] = float(raw[k])
    for k in _INT_KEYS:
        values[k] = int(raw[k])
    for k in _STR_KEYS:
        values[k] = raw[k].strip()
    res = values["heightmapResolution"]
    if (res - 1) & (res - 2) or res < 33:
        raise ValueError(f"{path}: heightmapResolution {res} is not 2^n+1")
    if (values["tileSizeMeters"] % (res - 1)) != 0:
        raise ValueError(f"{path}: tileSizeMeters must be a multiple of heightmapResolution-1")
    if not 0.6 <= values["verticalScale"] <= 0.85:
        raise ValueError(f"{path}: verticalScale {values['verticalScale']} outside the greenlit 0.6-0.85 band")
    if not 15 <= values["bankSlopeDeg"] <= 30:
        raise ValueError(f"{path}: bankSlopeDeg {values['bankSlopeDeg']} outside 15-30 (brief rev 3)")
    if not 0 <= values["bankBandMeters"] <= 12:
        raise ValueError(f"{path}: bankBandMeters {values['bankBandMeters']} over 12 needs the director "
                         "(ruling/bells-bend-shoreline-bank)")
    if not 20 <= values["oppositeBankSlopeDeg"] <= 30 or not             80 <= values["oppositeBankWavelengthMin"] <= values["oppositeBankWavelengthMax"] <= 200:
        raise ValueError(f"{path}: oppositeBankSlopeDeg must be 20-30 and wavelengths within 80-200")
    if not values["bankHighEnd"] > values["bankHighStart"] or values["shoreRampMeters"] > 150:
        raise ValueError(f"{path}: need bankHighEnd > bankHighStart and shoreRampMeters <= 150")
    if values["lakeDepthBelowWater"] < 8:
        raise ValueError(f"{path}: lakeDepthBelowWater must be >= 8 (L3)")
    return MapConfig(values, path)
