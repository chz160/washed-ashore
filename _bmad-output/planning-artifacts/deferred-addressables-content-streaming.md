---
title: 'Deferred: split game content with Addressables'
status: deferred
decided: 2026-10-06
---

# Deferred: split game content with Addressables

**Status:** Deferred. We are not doing this yet, but we plan to.

## Do this before either of these happens

- **We open the northern region.** That is, before anything sets
  `MapConfig.gateOpen` to true or adds playable land north of the north line.
  The north is the first natural streaming boundary, so build it as content that
  loads separately rather than adding it to `World.unity`.
- **The build gets too big.** The old size trigger (web `Web.data` over 100 MB)
  lapsed with the web build. The new trigger is set in #12. Large asset imports,
  such as a new wildlife or vegetation pack, can get there before the north
  opens.

## Background

The player build packs everything the scene references into the built data
files, so nothing loads separately. (This note began as a web-build problem: on
2026-10-06 the web data file was 56.6 MB after compression. The web build is
retired as of 2026-10-14, see `_bmad-output/poc/web-retired.md`; the reasons still hold for
desktop.) A single block of content causes these problems:

- Every player downloads the whole build before the game starts.
- Any change to the build can mean every returning player downloads a large
  patch.
- The build grows with every asset we add.

## What Addressables changes

[Addressables](https://docs.unity3d.com/Packages/com.unity.addressables@latest)
(`com.unity.addressables`, not installed yet) moves content out of the data file
into bundle files that load while the game runs.

- Assets and scenes are marked as addressable and sorted into groups. Each group
  builds into one or more `.bundle` files, plus a catalog that says where
  everything is.
- Code loads content by name, for example
  `Addressables.LoadSceneAsync("North", LoadSceneMode.Additive)` or
  `Addressables.InstantiateAsync("Crow")`.
- The data files shrink to the engine and a small starting scene.
- The launcher or updater can fetch only the bundles that changed.
- Each group's packing mode controls bundle size, and bundles can be shipped
  with the build or hosted remotely.

## Suggested first split

| Group | Contents | Loads |
| --- | --- | --- |
| Core | Player, UI, shared materials and shaders | At startup (stays in the data file) |
| Bells Bend | The current playable land as an additive scene | At startup |
| North | The northern region as an additive scene | When the player nears the gate |
| Wildlife | Animal prefabs, meshes and clips | When their spawners first need them |
| Birds | Robin and crow prefabs | Same as Wildlife |
| Audio | Ambient and music clips | When first played |

## Costs and risks

- **Loading becomes asynchronous.** Spawners, region loading and anything else
  that pulls in content must wait for its load to finish.
- **Every load needs a release.** Each load needs a matching
  `Addressables.Release`. A missed one leaks memory.
- **Direct references undo the split.** Anything a built-in scene references
  directly still goes into the data file. Those fields must become
  `AssetReference` fields, or the content must move into scenes that load
  additively.
- **Shared assets get copied.** Materials, shaders and textures used by more
  than one group are copied into each of those bundles unless they have their
  own shared group. Use **Window > Asset Management > Addressables > Analyze**
  and run **Check Duplicate Bundle Dependencies**.
- **URP shader variants can be stripped.** Materials inside bundles can render
  pink in a build. Check them in a real player build, not only in the editor.
- **The build gets an extra step.** Addressables content must build before the
  player build (turn on **Build Addressables on Player Build**), and the bundles
  must ship with it for each target (Windows, Linux, macOS).
- **Some bugs only show up in a build.** In the editor, content loads from the
  asset database. Test with the **Use Existing Build** play mode script.
- **Prefab facing.** Bundled prefabs still use the import-time fix from
  `ModelFacingPostprocessor`. Run **Sync Prefab Facing** before building
  bundles, as usual.

## Rough effort

A few days: install and configure the package, convert spawners and direct
references, split `World.unity` into additive scenes, fix duplicate
dependencies and shader stripping, and add the bundle build to the player build
scripts. It gets more expensive the longer we wait, because every new
direct reference is one more thing to convert.
