# Follow-up: scum small-end (p10) miss and look-shot alpha

w-artist, 2026-10-06. Post-POC follow-up only; nothing here is landed. The rulings are `ruling/water-look-final` and `ruling/water-scum-small-end`.

## 1. Scum p10 sliver miss (recorded deviation)

**What w-qa measured.** w-qa ran `_bmad-output/poc/water-qa/scum_measure.py` on the landed `WaterRipple.png` bytes, using the landed values: tiles 13/16 m, stretch 4.0, coverage 0.33, `_ScumBand` (-3.5, -2.8, -1.4, -0.9), jitter 0.6.

| Measure | Result | Bar |
|---|---|---|
| Moment aspect | 4.66-4.69 | median 4-6 (pass) |
| Coverage | 0.32-0.33 | pass |
| Edge ramp | 59-70% of width | pass |
| Length p10 | 0.32 m | ≥ 0.375 (fail) |
| Width p10 | 0.05 m | ≥ 0.075 (fail) |

My reproduction is in `scum-measure-landed-soft011.txt`.

**Cause.** The shader fades scum out at the band edges by multiplying the threshold field by a smooth band term. Measuring `band × thr > 0.5` therefore slices patches along the band edges into thin slivers. Those slivers fill the bottom decile of length and width. My earlier simulation (`scum-sim.py`) left the band out, so it didn't see them.

**Things that did not fix it under the current metric** (all on the landed bytes):

| Change | Result |
|---|---|
| Coarser tiles (17/21, 18/22) | width p10 0.06; length p90 2.34-2.46 |
| Wider band ramps + jitter 0.3 | width p10 0.07; length p90 2.75-3.08 (fails p90) |
| Band shifting the threshold instead of multiplying | more small patches (length median 0.56) |

**Proposed fix (not landed).**
- **Metric:** grade whole patches. A patch is a connected component of `thr > 0.5` that reaches the band (band ≥ 0.5). The band fades opacity only, which is what the shader already does.
- **Values:**
  - `_FoamTiling` 13/16 → 11.5/14.2
  - `_ScumBand` → (-4.0, -2.6, -1.5, -0.6), so the opacity fade at the band edges is gradual
- **Result on the landed bytes** (`scum-measure-wholepatch.py` / `.txt`):

| Measure | Median | p10-p90 |
|---|---|---|
| Length | 0.96 m | 0.52-2.40 |
| Width | 0.14 m | 0.10-0.22 |
| Moment aspect | 4.70 | |
| Coverage | 0.32 | |

  - All bars pass. That variant's edge-ramp figure isn't meaningful, because its keep-mask cuts the 0.3-0.5 fringe.
- **Staged copy:** `scratchpad/wa-stage/.../BellsBendWater.shader.pending` (property defaults only).
- **Also noted by w-director:** culling fragments below a minimum area (post-POC polish).

## 2. Look-shot alpha

`BellsBendWaterLook.Shot()` read the render target as RGBA32. The water's premultiplied output (`Blend One SrcAlpha`) leaves transmittance in the target's alpha, so 37-62% of the pixels had alpha < 255, and viewers that honour alpha showed the water washed out.
- **Existing PNGs:** all 28 in `water-look/` and `water-look/prev-*/` were flattened to RGB offline. The colour values are unchanged, because the RGB in the capture is already the final composite.
- **Script fix (staged, not landed):** capture as RGB24. It needs a slot whenever the look script next lands.
- **w-tools' player probe:** `WaterLookShots.cs` should get the same treatment if it reads RGBA.
