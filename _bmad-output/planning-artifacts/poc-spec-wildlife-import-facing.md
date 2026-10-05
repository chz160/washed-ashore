# POC Spec: Wildlife Import-Time Facing Fix

Owner: systems-designer | Greenlight: Noah (approach A chosen) | Platform: **Windows only**
Supersedes: the WL-BUG-7 runtime workaround (`modelYawOffset: 180`).

## Player problem

None is visible today, because the runtime yaw hides it. The problem is in the data. The Quaternius animal FBXs face -Z, so every system that reads the model frame has to know about a magic 180. Fix the asset at import so that "forward is +Z" holds for models, prefabs and clips. The loop this serves is wildlife wander, flee and watch, which depends on correct facing.

Measured head direction in the model frame today (`TestResults/qa-wl/qa-final-wildlife-a4.json`):

| Species | Head dir (x, z) |
|---|---|
| Deer | (0.26, -0.97) |
| Fox | (0.12, -0.99) |
| Stag | (0.32, -0.95) |
| Wolf | (-0.02, -1.00) |

## Approach (A, chosen)

Add an `AssetPostprocessor` in `Assets/Editor/`. Amendment 1 below supersedes the original path scope: the postprocessor now detects facing per asset. When it decides to flip, it applies an exact 180° yaw about the root's Y axis. `AnimalArmature` below means the armature root node; for Quaternius it is literally `AnimalArmature`.

- **OnPostprocessModel**: premultiply the `AnimalArmature` rest `localRotation` by `(0,1,0,0)` and map `localPosition` (x,y,z) to (-x,y,-z). Rotating the skinned-mesh node the same way is optional, for consistency and bounds.
- **OnPostprocessAnimation**: for every clip, remap the `AnimalArmature` curves.
  - Quaternion: x' = z, y' = w, z' = -x, w' = -y. This is a curve swap plus negation, so it is exact for keys and tangents.
  - Position: x' = -x, z' = -z.
  - Scale: unchanged.
  - If any clip keys Euler curves (`localEulerAnglesRaw`) instead of `m_LocalRotation`, the postprocessor must handle them or log an error. It must never silently skip them.
- Bump the postprocessor's `GetVersion()` so a reimport is forced deterministically.

### Amendment 1: detection, not a blind flip (lead, at Noah's request)

More vendor packs are coming, so the postprocessor detects facing per asset. The vendor survey is in progress at `_bmad-output/poc/vendor-facing-survey.md`.

- **(a) Detect.** Measure the head bone's position in root space at rest pose, relative to the root, and take the normalized XZ direction. The rules:
  - Flip 180° only when z < -0.5.
  - Leave the model untouched when z > +0.5.
  - Anything else is ambiguous: log a warning naming the asset path and the measured (x, z), and leave the model unchanged.
- **(b) Scope.** Any model with an armature and a recognisable head bone, not only `Quaternius/Animals/`.
  - The head bone is matched by name (case-insensitive `head`, from a candidate list kept in one place).
  - The flipped node is the armature root, i.e. the root's child that is the skeleton's top bone. It is not hard-coded to `AnimalArmature`.
  - Models with no head bone are skipped silently.
- **(c) Idempotent.** The decision is computed once per import from the unmodified source hierarchy. The model pass and the clip pass use that same decision, so they can never disagree. Re-importing twice gives identical results and never double-flips.
- **(d) Report.** A callable facing report, such as an editor menu item or a static method usable from batchmode, lists every rigged model in the project. For each it gives the asset path, head bone, measured (x, z), decision (flip, keep, ambiguous or no-head), and whether the flip was applied.

### Amendment 2: unpacked prefabs (techart finding)

The `Animal_*.prefab` files are fully unpacked copies, not nested FBX prefabs. `AnimalArmature` stores its own rotation, {0.7071,0,0,0.7071}, and the Animator's `m_Avatar` is 0. Reimporting the FBX therefore fixes the model and the clips, but not the prefab's rest pose.

- **(e) Prefab sync.** An editor step in the same file brings unpacked prefabs into line.
  - It is callable from a menu item and from batchmode, and it runs after the FBX reimport.
  - It applies the same 180° yaw to the armature root and to the mesh node.
  - Its decision uses the same detection rule as (a), measured in the prefab's own hierarchy, so it is idempotent: once flipped, the head is at +Z and the step does nothing.
  - "The rotation equals the vendor value" may be used as an extra guard, not as the only one.
  - `Wildlife_*.prefab` variants inherit the change. They must not hold their own overrides of the armature or mesh rotation; if they do, the step logs an error.
- AC1 is refined: prefab YAML changes are allowed only when this scripted step produces them. Hand edits are still an automatic FAIL. Running the step twice must leave the files unchanged in `git diff`.
- AC2 is extended: the rest-pose check is measured on the prefab **and** on the FBX model, with the Animator not evaluating.

Baseline before the fix, measured by techart in root space as head minus Body, normalized in XZ:
- **Head direction:** Deer, Stag, Fox and Wolf all have head z = -1.00, both at rest and at mid-clip in Idle, Walk and Gallop.
- **Lowest non-armature bone Y at rest:** Deer 0.0006, Stag 0.0006, Fox 0.0101, Wolf 0.0165. After the fix these must stay within ±0.005; a 180° yaw must not change height.

### Amendment 3: detection rule and scope (lead, from the vendor survey)

The survey (`_bmad-output/poc/vendor-facing-survey.md`) shows that the head-bone rule misreads bipeds. This amendment supersedes the rule in (a) and the scope in (b). (c) through (e) are unchanged and use the new rule.

- **Detection order.** This applies to rigged FBX models only, measured in root space at rest pose.
  1. **Primary, the L/R rule.** Take mean X(L) - mean X(R), where L bones are named `*.L`, `*_L` or `Left*` and R bones are the mirrored names. Use L-only mean X only when the rig has no R bones. If it is greater than +ε, the model faces -Z, so flip it. If it is less than -ε, the model already faces +Z, so keep it. Unity's +Z-forward puts the model's left at -X.
     - **Mandated form:** when both L and R bones exist, the measure is mean(L.x) - mean(R.x). Only when there are no R bones is it mean(L.x). The report records which form was used.
     - **ε** = 0.05 × max(size X, size Z) of the axis-aligned bounding box of every bone Transform under the armature root, `_end` tips included, measured in root space at rest. The 0.05 factor is tunable.
     - qa's measurement shows the L-only form would leave Fox and Wolf inside ε. The L-R form clears ε by 1.8× or more for all 12 animals.
     - **Side matching** is done on the bone name after stripping any `:` or `|` prefix. All Transforms under the armature root are considered.
       - L: the name ends with `.L`, `_L`, `.l` or `_l`, or it starts with `Left` followed by an uppercase letter, digit, `_`, `.` or the end of the name. This avoids false matches such as `Leftover`.
       - R: the mirrored patterns.
       - An `_end` tip counts as its parent's side: a trailing `_end` is stripped before matching, so `FF.L_end` counts as L. This is the same bone set ε uses. It was ruled in IF-BUG-2; the effect is about 4% on the measure and no decision changes.
  2. **Fallback 1:** head minus body or hips on Z, with the thresholds from (a). Use it only when the L/R rule is not clear, for example a quadruped with no L/R naming.
     - **Upright guard:** if the head's horizontal (XZ) offset is less than 0.2 × its distance from the root, the result is ambiguous, so warn and leave the model unchanged. A biped's head sits almost directly above the root and would otherwise give an arbitrary direction.
  3. **Fallback 2:** if neither rule is clear, warn with the asset path and the measured values, and leave the model unchanged.
  - If the L/R rule decides and the head rule clearly disagrees, log a warning, but L/R still wins.
- **Scope.** FBX only. glTF and GLB files are excluded; the Robin GLB already faces +Z.
  - Rigs with `animationType` Human are skipped with a log line. Unity's "Based Upon Body Orientation" may already correct them; that is a follow-up, not this pod.
  - Guns and props are out of scope; guns get a socket offset later.
- **Report (d)** adds these columns: L/R mean X, the rule that decided, and the skip reason (non-FBX, Humanoid, or no rig).

**Fallback (B):** a prefab wrapper with a rotated child. Use it only if A fails AC2 or AC5 after 3 attempts. It needs lead sign-off before switching.

## Tunable parameters

- L/R bone name patterns (`*.L`, `*_L`, `Left*`) and ε (default 0.05 of the skeleton's XZ extent).
- Head and hips name candidates for fallback 1 (default `head`, case-insensitive).
- Fallback 1 thresholds: flip when z < -0.5, keep when z > +0.5, ambiguous in between.
- Yaw angle: fixed at 180. It is not a tunable, because exactness depends on it.
- Prey `angularSpeed` 600 is tuning, not part of the workaround. **It stays unchanged.**

## Acceptance criteria (for qa-lead)

| # | Criterion | Verification |
|---|---|---|
| AC1 | The fix is the postprocessor in `Assets/Editor/`. A reimport of the Animals folder (or Reimport All) reproduces it. There are no hand-edited FBX, `.meta` or prefab files, and nothing exists only in a scratchpad. | Code review plus `git diff`: only the new .cs file (and its .meta) under `Assets/Editor/`, plus the removals from AC3. A reimport followed by AC2 still passes. |
| AC1a | Detection works and is safe (Amendments 1 and 3). All 12 Quaternius animals, farm animals included, are detected as flip by the **L/R rule** (their L bones are at +X). qa's census: these 12 are the only rigged models with a head bone among 80 model assets, all at rest z = -1.000, baseline in `TestResults/qa-if/`. There is no real keep or ambiguous model, so synthetic fixtures are **required**. They are built in code or kept under `Assets/Tests/`, never in shipped content, which means detection must be callable on a bare hierarchy. Fixture 1: a model facing +Z with its L bones at -X stays unflipped. Fixture 2: a model with no L/R bones and a sideways head (±X) logs a warning naming the asset and stays unchanged. A Humanoid-rig model is skipped with a log line. Two consecutive reimports produce identical head directions, with no double-flip. No asset outside the flip set changes orientation. | An EditMode test covers flip, keep, ambiguous and idempotence. The facing report (d) is attached as JSON or a table in `TestResults/`. The before and after reports are diffed so that only flip-decision assets changed. |
| AC2 | The head direction in the model frame has z > +0.9 for Deer, Stag, Fox and Wolf at rest, and when sampled at mid-clip (normalized time 0.5) for Idle, Walk and Gallop/Run. Farm animals are nice-to-have and are reported, not gating. | An EditMode or PlayMode probe writes per-species and per-clip values to `TestResults/` JSON. |
| AC3 | The workaround is fully removed: the `modelYawOffset` field (`WildlifeTuning.cs:17-19`), its values in `WildlifeTuning.asset`, the setter in `WildlifeBriefDefaults.cs:35-37`, and the term in `WildlifeAgent.cs:338`. `ExtraModelYaw` may stay as a test-only hook. | A grep for `modelYawOffset` across `Assets/` returns 0 hits. |
| AC4 | The facing test measures the **head bone** direction against the movement direction. The flipped positive control (`ExtraModelYaw` = 180) still **fails**. | `WildlifeBehaviourTests` (~164 and ~183) and `WildlifeA4Probe`. QA confirms that the control's assertion fires. |
| AC5 | No regressions: wildlife PlayMode 18/18, WorldWalkTests 5/5 and EditMode **42/42**: the 11 pre-existing tests plus the 31 AC1a cases in `Assets/Tests/EditMode/ModelFacingDetectionTests.cs`. The lead confirmed this. The 11 pre-existing tests must all still exist and pass. Feet don't clip or float beyond the existing A4 probe thresholds. `maxClipRunSec` and `maxFloatRunSec` stay at 0, and facing share is at least the A4 baseline (Deer 0.998, Fox 1.0, Stag 1.0, Wolf 0.993). Renderer bounds still contain the mesh, with no culling pop. | Full test run results, plus a re-run of the A4 JSON diffed against `qa-final-wildlife-a4.json`. |
| AC6 | The Windows build exits 0. A launch smoke of at least 60 s logs 0 errors in `Player.log`. | Build log plus Player.log, attached by build-engineer or qa. |
| AC7 | Optional: Noah visually confirms in the Windows build, relayed by the lead session. | Not gating. |

## Automatic FAILs

- The same blocker survives 3 attempts. Stop and escalate to the lead; do not loop.
- Any WebGL build, W7 work or browser check.
- The FBX files, their `.meta` files or `Animal_*.prefab` are edited by hand to fake the result.
- Any residual runtime 180° compensation, under any name, outside the test-only `ExtraModelYaw`.
- The positive control passes, which would mean the test is blind.
- Prey `angularSpeed` or other tuning is changed to mask a facing error.
- Clips are silently skipped, or Euler-curve clips are left unremapped.
- A +Z or ambiguous model gets flipped, or any model double-flips on reimport.
- A path or asset-name allowlist is used instead of detection. Quaternius-only scoping fails Amendment 1.

## Ownership

- technical-artist: the postprocessor, AC1 and AC2, and the AC1a EditMode fixture test (producer's call).
- gameplay-engineer: workaround removal and test updates (AC3, AC4).
- qa-lead: AC2 and AC4-AC6, and the verdict.
- executive-producer: tracking.
