# Mirrors BellsBendWater.shader's path/Fresnel rule for a fish at depth d below the surface.
import math
EXT = (2.2, 2.4, 3.2); MINCOS = 0.15; F0 = 0.02; FLOOR = 0.02

def alpha(eye_h, horiz, d):
    L = math.hypot(eye_h, horiz); vy = eye_h / L
    F = F0 + (1 - F0) * (1 - vy) ** 5
    path = d / max(vy, MINCOS)
    T = [math.exp(-e * path) for e in EXT]
    return (1 - F) * sum(T) / 3, F, vy

def maxdist(eye_h, d):
    best = None
    for i in range(0, 8001):
        x = i * 0.05
        if alpha(eye_h, x, d)[0] >= FLOOR: best = x
    return best

for d100 in range(5, 300, 5):
    d = d100 / 100
    if alpha(1e6, 0, d)[0] < FLOOR:
        print('vertical cutoff ~', d, 'm'); break
for d in (0.3, 0.6, 1.0, 1.5):
    for h, name in ((1.7, 'shore 1.7'), (26.6, 'bluff 26.6')):
        row = [f'{x}m:{alpha(h, x, d)[0]:.3f}' for x in (0, 2, 4, 6, 10, 20, 30, 50)]
        print(f'd={d} {name}: maxdist={maxdist(h, d)}  ', ' '.join(row))
