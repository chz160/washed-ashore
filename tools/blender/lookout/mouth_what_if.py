"""What-if for the join mouths: the funnel swept at a smaller radius than wall_reach. Prints only; not part of the build.

    python -B tools/blender/lookout/mouth_what_if.py 0.30 0.28 0.27 0.26
The reachable span stays at checks.wall_reach; only the funnel's sweep radius changes. Same points, walls only, same caps.
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from mc39_cab import Cab                    # noqa: E402
from mc39_collision import Collision        # noqa: E402
from mc39_layout import Layout              # noqa: E402
from mc39_tower import Tower                # noqa: E402


def main():
    with open(os.path.join(HERE, "mc39_params.json"), encoding="utf-8") as fh:
        P = json.load(fh)
    L = Layout(P)
    T = Tower(P, L)
    T.frame(0); T.stairs(0); T.landings(0)
    K = Collision(P, L, T, Cab(P, L, T))
    radii = [float(a) for a in sys.argv[1:]] or [0.30, 0.28, 0.27, 0.26]
    res = {r: K.mouth_widths(funnel_reach=r) for r in radii}
    keys = sorted(res[radii[0]], key=lambda k: (99 if k[0] == "exit" else k[0], k[1]))
    for key in keys:
        name = "lane 9 (exit)" if key[0] == "exit" else f"flight {key[0]} {key[1]}"
        for side in ("left", "right"):
            base = res[radii[0]][key][side]
            line = f"{name:16s} {side:5s} R {base['reachable_span']:.3f} (ended by {base['reachable_span_ended_by'] or 'nothing within 1 m'})"
            for r in radii:
                x = res[r][key][side]
                line += f" | sweep {r:.2f}: W {x['half_width']:.3f}, {x['coverage'] * 100:.0f}%, {x['bound_by']}"
            print(line)


if __name__ == "__main__":
    main()
