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
- **The build gets too big.** `deploy-web.ps1` prints a warning when the
  compressed `Web.data` file goes over 100 MB. Large asset imports, such as a
  new wildlife or vegetation pack, can get there before the north opens.

## Background

The web build packs everything the scene references into one file,
`Build/Web.data.unityweb`. On 2026-10-06 that file was 56.6 MB after Brotli
compression, which is over the 25 MiB per-file limit of Cloudflare Pages. Paid
plans don't raise that limit, and Workers static assets have the same one.

The short-term fix is in `deploy-web.ps1`. Any file over 25 MiB goes to the R2
bucket `washed-ashore-game-data`, and `index.html` loads it from there. This
gets around the limit, but it doesn't fix these problems:

- Every player downloads the whole file before the game starts.
- Any change to the build means every returning player downloads all of it
  again.
- The file grows with every asset we add.

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
- The data file shrinks to the engine and a small starting scene.
- The browser keeps bundles it has already downloaded and fetches only the ones
  that changed.
- Each group's packing mode controls bundle size, so every file can stay under
  the Pages limit. The remote load path can also point at the R2 bucket we
  already have.

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
  `Addressables.Release`. A missed one leaks memory, and WebGL has little to
  spare.
- **Direct references undo the split.** Anything a built-in scene references
  directly still goes into the data file. Those fields must become
  `AssetReference` fields, or the content must move into scenes that load
  additively.
- **Shared assets get copied.** Materials, shaders and textures used by more
  than one group are copied into each of those bundles unless they have their
  own shared group. Use **Window > Asset Management > Addressables > Analyze**
  and run **Check Duplicate Bundle Dependencies**.
- **URP shader variants can be stripped.** Materials inside bundles can render
  pink in a build. Check them in a real web build, not only in the editor.
- **The deploy gets an extra step.** Addressables content must build before the
  player build (turn on **Build Addressables on Player Build**), and the bundles
  must be uploaded with it.
- **Some bugs only show up in a build.** In the editor, content loads from the
  asset database. Test with the **Use Existing Build** play mode script.
- **Prefab facing.** Bundled prefabs still use the import-time fix from
  `ModelFacingPostprocessor`. Run **Sync Prefab Facing** before building
  bundles, as usual.

## Rough effort

A few days: install and configure the package, convert spawners and direct
references, split `World.unity` into additive scenes, fix duplicate
dependencies and shader stripping, and add the bundle build and upload to
`deploy-web.ps1`. It gets more expensive the longer we wait, because every new
direct reference is one more thing to convert.
