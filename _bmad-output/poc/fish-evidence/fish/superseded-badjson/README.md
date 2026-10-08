# Superseded fish result files with malformed JSON

Moved here unedited in slot D (team-lead's call: quarantine, don't delete, don't hand-edit). The active-file audit
(`FishJsonTests.ResultFiles_OnDisk_AllParse`) covers `TestResults/fish*.json(l)` only, not this folder.

| File | Written by | sha256 | Bug |
|---|---|---|---|
| fish-calib-info.json | FishInfoRun (slot C1/C2 information run, seeds 404/505/606) | 9c941d23faae247eb59bcd511603ff823d5b5fb8d31655cdb4d3099151374bf5 | `"raisedPerMin":F3` in 3 runs: an interpolated `{x:F3}}}` printed the literal format instead of the number |

The source is fixed. Every fish result file now goes through `WashedAshore.Fish.FishJson.WriteFile`, which refuses
to write anything that doesn't parse, and an EditMode source lint rejects the `{x:F1}}` pattern.
