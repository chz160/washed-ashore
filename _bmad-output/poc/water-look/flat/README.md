# Flattened look shots (w-qa)

Copies of the PNGs in `_bmad-output/poc/water-look/`, saved as opaque RGB.

- Method: Python/Pillow. The RGB channels are copied byte for byte (checked equal after saving); the alpha channel is dropped, which is the same as alpha = 255.
- Why: the originals are RGBA, and 37-62% of their pixels have alpha < 255 (the water shader's premultiplied transmittance ends up in the capture's alpha). Viewers that composite alpha show the water washed out.
- ~~The originals are kept unchanged in the parent folder.~~ **Amended 2026-10-07 ~00:10Z:** after these copies were made, w-artist flattened all 28 look PNGs in the parent folder (19 top-level + 9 in `prev-slot4/` and `prev-slot8/`) **in place** with PIL `convert('RGB').save()`. The RGBA originals no longer exist on disk; their hashes are kept in the "Original" column below. RGB is unchanged: w-qa checked that all 19 top-level parent files are now byte-identical to the copies in this folder (same sha256), so no measurement is affected (all qa metrics are RGB-only). Friction-log row recorded.

| File | Original sha256 (16) | Flat sha256 (16) |
|---|---|---|
| w5-bank.png | bfd9e6c9a22692a6 | ed40a83dfffa653e |
| w5-bluff-top.png | 0512a64fbccd506a | f79af5a84bed1a4c |
| w5-drift-phase0.10.png | 6aba502ec9a0cd23 | e23b285f8b6a7858 |
| w5-drift-phase0.25.png | bb568e0acdb8fa88 | 94bac452a5817a8a |
| w5-drift-phase0.40.png | 1d7879776d4e7330 | 91f2ba632fb98abe |
| w5-haze-climb-16m.png | 6c0d4b904364e66a | 10db12c4b8ebd08e |
| w5-haze-climb-27m.png | f55cfe7e850f4de9 | 5a4534e7dabcfd7c |
| w5-haze-climb-2m.png | 8e34b1541e7265b5 | 74401f3e284f4d71 |
| w5-haze-climb-8m.png | c3a32d17517c2380 | 37aec8dca167d9c7 |
| w5-haze-pan+4.png | 340e859cf7287c59 | 9f3aa03ebf0e4f34 |
| w5-haze-pan-12.png | 8a9a9deaba125d61 | f1a6a27c702a83da |
| w5-haze-pan-4.png | e1f4e5f38a07404b | 848d70a2746403ce |
| w5-lip.png | 848a8139c48323d4 | 845c7091c9577b5c |
| w5-north-line-east-channel.png | 153c2ff6ead85be9 | 4d15f377c483ee8d |
| w5-shallow-edge-stretch3.6.png | 9c0fdcebe589d36f | 2968e433100d4368 |
| w5-shallow-edge-stretch4.2.png | 7f63cdb1bf0cd99d | 5b2de08316a689a3 |
| w5-shallow-edge.png | bd0f50570c28d320 | 58046995ffb24085 |
| w5-water-level.png | 30c05f5fe0db9ed7 | 0175e431a2222047 |
| w5-west-neck-step.png | f1d8e2bb6ebee0ec | 4a7334d05bdbe233 |
