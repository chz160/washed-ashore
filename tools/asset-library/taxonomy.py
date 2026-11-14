"""Shared vocabulary across sources, and the Bells Bend relevance rules.

normalize_category() maps a source's own category (kept verbatim in the record as
category_original) onto one shared category. normalize_tags() folds synonyms and drops noise.
relevance() scores an asset for scope A/B/C. Rules are plain keyword lists so they are easy to
review and tune; the team overrides any single asset by editing its asset.json
(set bells_bend_relevance_edited: true so a rebuild keeps the edit).
"""
from __future__ import annotations

import re

# ---------------------------------------------------------------------------
# Shared categories. Ordered: the first rule whose keywords hit wins.
# Matched against the lower-cased "category text" (source category path, plus the
# ambientCG id prefix split into words), never against free tags, so the mapping is stable.
# ---------------------------------------------------------------------------
CATEGORY_RULES: list[tuple[str, tuple[str, ...]]] = [
    # lighting
    ("hdri/studio", ("studio", "backdrop")),
    ("hdri/interior", ("interiors", "indoor environment")),
    ("hdri/sky", ("pure skies", "sky hdri", "night sky")),
    ("hdri/outdoor", ("hdri", "streets & town", "fields & countryside", "mountains & hills",
                      "parks & gardens", "coast & water", "abandoned & ruins", "forest & woodland",
                      "desert & arid", "industrial", "environment")),
    # vegetation models
    ("vegetation/tree", ("nature/trees", "tree stump", "tree end")),
    ("vegetation/deadwood", ("logs, branches", "logs", "stick set", "3d stick")),
    ("vegetation/plant", ("nature/plants", "flower set", "leaf set", "nature/ground cover")),
    ("rock/model", ("nature/rocks", "rock brush")),
    # surfaces: ground and nature
    ("vegetation/surface", ("grass", "moss", "foliage", "scattered leaves", "leaf", "pine needles",
                            "wood chips", "organic", "bark", "thatched")),
    ("ground/gravel", ("gravel", "pebble")),
    ("ground/soil", ("ground", "terrain", "mud", "soil", "sand", "dirt", "clay", "footsteps",
                     "tire tracks")),
    ("paving", ("paving", "pathway", "cobble")),
    ("rock/surface", ("stone", "rock", "granite", "marble", "onyx", "travertine", "terrazzo", "cliff",
                      "lava", "ivory")),
    ("snow-ice", ("snow", "ice")),
    # surfaces: built
    ("decal/markings", ("road lines", "graffiti", "asphalt damage")),
    ("road/asphalt", ("asphalt", "road", "manhole", "tactile paving", "paving edge", "rails")),
    ("concrete", ("concrete",)),
    ("brick-block", ("brick", "block")),
    ("plaster-facade", ("plaster", "facade", "wallpaper", "office ceiling", "painted plaster")),
    ("roofing", ("roofing", "roof")),
    ("tiles-ceramic", ("tiles", "ceramic", "porcelain", "glazed terracotta", "tatami")),
    ("metal/painted", ("painted metal",)),
    ("metal/rust", ("rust",)),
    ("metal", ("metal", "corrugated steel", "diamond plate", "sheet metal", "grate", "pipe", "chainmail",
               "screw", "foil", "solar panel")),
    ("wood/painted", ("painted wood",)),
    ("wood", ("wood", "plank", "chipboard", "fence", "door", "cork", "wicker")),
    ("fabric-leather", ("fabric", "leather", "textiles", "carpet", "rope", "net")),
    ("paper-card", ("paper", "card", "cardboard", "tape", "sticker", "painting", "sign")),
    ("plastic-rubber", ("plastic", "rubber", "foam", "styrofoam", "sponge", "chewing gum", "payment card")),
    ("glass", ("glass",)),
    # overlays
    ("imperfection", ("surface imperfections", "fingerprints", "smear", "scratches", "chip", "leaking",
                      "wrinkles", "paint")),
    # model groups (Poly Haven model taxonomy top level)
    ("prop/furniture", ("furniture",)),
    ("prop/tools", ("tools & equipment", "weapons")),
    ("prop/containers", ("containers & storage",)),
    ("prop/decor", ("decor & art", "leisure", "apparel")),
    ("prop/electronics", ("electronics", "lighting", "office")),
    ("prop/food", ("food", "bread", "pear", "apple", "pastry", "cookie", "avocado", "cake", "cucumber",
                   "kiwi", "lemon", "mango", "melon", "pizza", "candy", "cold cuts", "christmas")),
    ("prop/industrial", ("industrial & infrastructure", "architecture")),
    ("prop/vehicles", ("vehicles",)),
    ("prop/nature-misc", ("wildlife", "shell")),
]


def split_camel(s: str) -> str:
    s = re.sub(r"^3D", "3D ", s)
    return re.sub(r"(?<=[a-z])(?=[A-Z])", " ", s).replace("H D R I", "HDRI")


def acg_category(asset_id: str) -> str:
    """ambientCG has no per-asset category field; the category is the id without its number."""
    return re.sub(r"\d+[A-Z]?$", "", asset_id)


def normalize_category(category_text: str) -> str:
    t = category_text.lower()
    is_hdri = "hdri" in t
    for norm, keys in CATEGORY_RULES:
        if norm.startswith("hdri/") != is_hdri:
            continue
        if _hits(t, keys):
            return norm
    return "hdri/outdoor" if is_hdri else "misc"


# ---------------------------------------------------------------------------
# Tags
# ---------------------------------------------------------------------------
TAG_SYNONYMS = {
    "rusty": "rust", "rusted": "rust", "corroded": "rust", "corrosion": "rust",
    "mossy": "moss", "muddy": "mud", "leaves": "leaf", "leafs": "leaf", "trees": "tree",
    "rocks": "rock", "rocky": "rock", "stones": "stone", "pebbles": "pebble", "bricks": "brick",
    "planks": "plank", "boards": "plank", "tiles": "tile", "wooden": "wood", "metallic": "metal",
    "grassy": "grass", "weeds": "weed", "plants": "plant", "branches": "branch", "roots": "root",
    "logs": "log", "cliffs": "cliff", "boulders": "boulder", "old": "worn", "weathered": "worn",
    "damaged": "broken", "cracks": "cracked", "crack": "cracked", "overcast sky": "overcast",
    "cloudy": "overcast", "sunset": "dusk", "evening": "dusk", "rural": "countryside",
    "woods": "forest", "woodland": "forest", "river bank": "riverbank", "dried": "dry",
}
_NOISE = re.compile(r"^\d+$")


def normalize_tags(tags, extra=()) -> list[str]:
    out = []
    for t in list(tags) + list(extra):
        t = str(t).strip().lower().replace("_", " ")
        if not t or _NOISE.match(t) or re.match(r"^[a-z]+\d{3}[a-z]?$", t):  # drop "concrete049"
            continue
        t = TAG_SYNONYMS.get(t, t)
        if t not in out:
            out.append(t)
    return out


# ---------------------------------------------------------------------------
# Bells Bend relevance (scope A = high + medium, B = A + low, C = everything)
# ---------------------------------------------------------------------------
EXCLUDE = ("desert", "arid", "dune", "cactus", "palm", "tropical", "jungle", "savanna", "arctic",
           "glacier", "iceberg", "polar", "sci-fi", "scifi", "futuristic", "fantasy", "alien",
           "spaceship", "lava", "coral", "bamboo", "christmas")

# Species, ground and landform words that make an asset a direct Bells Bend fit.
HIGH_NATURE = {
    "vegetation": ("oak", "hickory", "maple", "sycamore", "poplar", "tulip tree", "cedar", "juniper",
                   "willow", "dead tree", "fallen", "log", "stump", "shrub", "bush", "briar", "bramble",
                   "blackberry", "kudzu", "vine", "ivy", "creeper", "grass", "broomsedge", "weed",
                   "thistle", "dandelion", "fern", "leaf litter", "forest floor", "moss", "bark",
                   "branch", "twig", "root", "deadwood", "scattered leaves", "dry leaves", "leaf"),
    "ground/rock": ("mud", "riverbank", "river bed", "wet ground", "gravel", "limestone", "bluff",
                    "boulder", "cliff", "forest ground", "dirt", "soil", "rocky ground", "path", "trail",
                    "clay", "puddle", "forest"),
}
# Condition words that make a built surface a ruin (25 years after the collapse).
RUIN = ("rust", "rusty", "corroded", "peeling", "chipped", "rotten", "rotting", "weathered", "worn", "old",
        "dirty", "cracked", "broken", "damaged", "abandoned", "leaking", "stained", "moss", "mossy",
        "overgrown", "debris", "rubble", "graffiti", "decayed", "ruin")
NATURAL_CATS = ("vegetation/", "ground/", "rock/")
BUILT_CATS = ("concrete", "metal", "wood", "brick-block", "plaster-facade", "roofing", "road/asphalt",
              "paving", "glass", "imperfection", "decal/markings")
# Built categories that suit the setting even when clean (roads, concrete, sheet steel, overlays).
BUILT_MEDIUM_CATS = ("concrete", "road/asphalt", "roofing", "metal/rust", "metal/painted", "imperfection",
                     "decal/markings", "wood/painted")
RURAL_PROPS = ("barn", "shed", "farm", "fence", "tractor", "barrel", "crate", "bucket", "tire", "pallet",
               "jerrycan", "gas can", "shovel", "axe", "tool", "rope", "ladder", "chain", "pipe", "sign",
               "bench", "mailbox", "lamp", "lantern", "bottle", "jar", "box")
HDRI_HIGH_SETTING = ("forest", "woodland", "field", "countryside", "meadow", "rural", "farm", "river",
                     "park", "hill", "road", "abandoned", "ruin")
HDRI_HIGH_WEATHER = ("overcast", "clear", "partly cloudy", "partly_cloudy", "dusk", "sunset",
                     "morning", "sunrise", "afternoon", "fog")


def _hits(text: str, words) -> list[str]:
    return [w for w in words if re.search(r"(?<![a-z])" + re.escape(w) + r"s?(?![a-z])", text)]


def relevance(rec: dict) -> tuple[str, str]:
    """Return (high|medium|low|none, reason) from type, shared category, name and tags."""
    cat = rec["category"]
    strong = " ".join([rec["name"], rec.get("category_original", ""), cat]).lower()
    tags = " | ".join(rec.get("tags", []) + rec.get("tags_original", [])).lower()
    allt = strong + " | " + tags

    ex = _hits(allt, EXCLUDE)
    if ex:
        return "none", "excluded: " + ", ".join(ex[:4])

    if rec["type"] == "HDRI":
        if cat in ("hdri/studio", "hdri/interior"):
            return "low", "indoor/studio HDRI: look-dev only"
        setting = _hits(allt, HDRI_HIGH_SETTING)
        weather = _hits(allt, HDRI_HIGH_WEATHER)
        if setting and weather:
            return "high", f"outdoor HDRI: {', '.join(setting[:2])}; {', '.join(weather[:2])}"
        if setting or (cat == "hdri/sky" and weather):
            return "medium", "outdoor HDRI: " + ", ".join((setting + weather)[:3])
        return "low", "outdoor HDRI: night, urban or unspecified"

    if cat == "snow-ice":
        return "low", "snow/ice: rare in Middle Tennessee"

    nature = [(g, h) for g, w in HIGH_NATURE.items() for h in _hits(allt, w)]
    ruin = _hits(allt, RUIN)

    if cat.startswith(NATURAL_CATS) or rec["type"] == "plant":
        if nature:
            return "high", "nature: " + ", ".join(dict.fromkeys(h for _, h in nature[:4]))
        return "medium", "natural surface/model: " + cat

    if cat.startswith(BUILT_CATS):
        if ruin:
            return "high", f"ruin surface ({cat}): " + ", ".join(ruin[:4])
        if nature and any(g == "vegetation" for g, _ in nature):
            return "high", f"overgrown ({cat}): " + ", ".join(h for _, h in nature[:3])
        if cat.startswith(BUILT_MEDIUM_CATS):
            return "medium", f"settlement surface ({cat})"
        return "low", f"general surface ({cat})"

    if rec["type"] in ("model", "plant") or cat.startswith("prop/"):
        props = _hits(allt, RURAL_PROPS)
        if props and (ruin or cat in ("prop/tools", "prop/containers", "prop/industrial")):
            return "medium", "rural/settlement prop: " + ", ".join((props + ruin)[:3])
        return "low", f"general prop ({cat})"

    if ruin:
        return "medium", f"worn {cat}: " + ", ".join(ruin[:3])
    return "low", f"general purpose ({cat})"
