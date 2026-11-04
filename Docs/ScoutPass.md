# Scout mountain pass

> 2026-10-17 update: the saved ScoutPass scene now has a complete 360-degree arena and orbit camera. See `Scout360.md` for current controls and validation. The sections below also describe the original base-map build.

Applied on 2026-10-17 to the story node `scout` (산길 척후병).

## Scene and routing

- Scene: `Assets/Scenes/ScoutPass.unity`, enabled in Editor Build Settings.
- `RunConfig.nodeBattleScenes` maps story node IDs to scene names. Only `scout` is configured.
- `SceneRoutes.LoadBattle()` resolves that mapping from the pending node, with GameScene as the fallback.
- Existing save IDs, battle rules, shared fighter animations, walls, webcam components and the combat HUD are retained.
- The original GameScene was not overwritten by this map work.

## Art

`Assets/Environments/ScoutPass` contains all runtime models, textures, materials, meshes and prefabs.
Four Tripo props provided by the user were copied from Blender: low boulder, cliff rock, bent pine and timber fence.
LOD0 triangle budgets are 14k / 22k / 44k / 16k; LOD1 is 32% and LOD2 is 9% of those budgets. These are per asset, not total scene counts.
Repeated props have LODGroups. Base color textures use a maximum of 2048 pixels and normal maps 1024 pixels, with compression and mipmaps.
The original 7,733,065-triangle meshes remain in the external source backup; they are not imported into the project.

The fighting floor, surrounding scenery and distant mountains are now 3D. The former planar backdrop is removed from the saved scene; the sky uses separately drifting clouds. The late-afternoon mountain panorama and gravel diffuse texture were generated for this scene with the conversation's image-generation tool. No Tripo generation request was made by this integration.
The checkpoint, curved roof, flag and grass are generated meshes; the supply cart reuses the project's existing Naganeupseong asset.

`ScoutPassWind` deforms the two flag meshes. `Scout Pine Wind` moves the higher parts of the tree meshes. Terrain shading blends the main lane and curving path continuously; none of these visuals changes collision geometry.

## Editor tools

- `Tools > VisionStriker > Build Scout Mountain Pass` rebuilds the original base layout. Then use `Tools > VisionStriker > Build Scout 360 Arena` to expand it again. `ScoutPassBuilder.BuildAndPreview` performs both automatically. Save other open work first. This overwrites generated scene placement, so do not use it after hand-editing the scene unless that is intended.
- `ScoutPassVerification.Run` is an explicitly invoked editor-only check. It backs up and restores the existing run save.
- Optional arguments: `-scoutReport <directory>` and `-scoutPreview` for a short visual capture.
- The builder and verification utilities live under `Assets/Editor` and are excluded from player builds.

## Validation

Unity 6000.4.5f1 play-mode verification passed 34 checks including shader/texture presence, cloth deformation, both boundary walls, real attack damage, scout-specific scene routing, stage initialization, victory-to-map and defeat-to-start behavior. Final scenery changes were rendered again with no runtime errors. Original player progress was restored after testing.
This is functional and visual validation, not a device performance benchmark. Existing unrelated MCP connectivity logs and the project's existing HitVfx warning are separate from the new environment.

External review and source backup: `C:\Users\sbpar\Documents\VisionStriker_ScoutPass_20261017`.
Runtime assets do not depend on that folder. It contains the original Blender source, optimized Blender library, actual Unity screenshots, a six-second video and detailed Korean work notes.
