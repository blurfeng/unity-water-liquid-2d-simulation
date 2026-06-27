# Changelog

## [1.0.8] - 2026-06-27
### Added
- Colliders now support Push and Submerge interaction modes. Push shoves fluid particles away (for fluid containers); Submerge lets fluid cover the collider for more natural floating/sinking of objects in water.
### Changed
- Updated the demo sample scenes (DevScene, WaterAndMagma).
### Fixed
- Fix custom shaders (CombineTwo / GrabAsBg / Clone / Liquid2DParticleGpu) being stripped at build time, causing a black screen in player builds.
- Fix shader keyword variants (opacity / edge / pixel / distort / occluder / ignore-bg-color) being stripped at build time, causing editor/build visual mismatch (e.g. transparent water turning opaque). Runtime-toggled keywords now use multi_compile.
- Fix the Volume override for Distort.Magnitude not being merged; refactored Volume merging into a unified CopyFrom entry point (per-field copy into existing nested instances, no per-frame allocation).
- Fix the core-keep blur picking the wrong texture on odd iteration counts.
- Fix pixelation breaking under RenderTexture / split-screen / Render Scale by using the camera render target size.
- Fix the _PIXEL_BG keyword lingering on the shared material after pixelation is turned off.
- Fix GPU residual particles by resetting the render count to zero when emptied.
- Fix a GPU→CPU teleport when switching solvers by reading back GPU state before the switch.
- Fix a latent bug where the ScatterSpawn sentinel could overwrite slot 0.
- Fix a build compile error.

## [1.0.7] - 2026-06-22
### Changed
- Major release: replaced Unity's physics with a self-developed SPH (dual-density) fluid particle solver. Particles are now pure data (no per-particle GameObject), supporting tens of thousands of particles with CPU (Job System + Burst) and GPU (Compute Shader) modes.
- Introduced the Liquid2DParticleDescriptor (ScriptableObject) to define particle types, replacing the particle prefab.
- Added two-way coupling between fluid and rigidbodies (Liquid2DRigidbodyBridge): wash-away and buoyancy/float.
- Added force fields to apply directional/area forces to particles.

## [0.9.5] - 2025-11-15
### Changed
- The SpawnOne method for generating liquid particles provides an onSpawned callback to retrieve the spawned particle.
### Fixed
- Fix a bug where liquid particles were destroyed using Unity’s built-in Destroy instead of the custom destruction method provided by the loader (such as a pool).

## [0.9.4] - 2025-11-08
### Changed
- In the liquid Spawner, change the SpawnOne method (which generates a single particle) to public.

## [0.9.3] - 2025-11-03
### Changed
- Prompt when modifying the liquid particle NameTag. Updated the comparison rules for NameTag.
- Updated the liquid spawner : added options to adjust the flow rate factor and jet force factor. The diagram size in the Scene view is now fixed relative to the camera zoom.
- Added a new scene Minisize to demonstrate how to adjust liquid particle-related prefabs and parameters when the camera size is smaller.
- Updated the example images in the README.
- Other updates and adjustments, with improved code comments.

## [0.9.2] - 2025-10-22
### Fixed
- Fix the issue where the liquid2DLayerMask could not be modified when loading the plugin package via UPM. Remove liquid2DLayerMask and unify the identification of the Renderer Feature, liquid particles, and Volume using nameTag.

## [0.9.1] - 2025-10-21
### Fixed
- Fix the issue where the GUIDs in the .meta files of IRandomData and Random are identical to those in the .meta files of the Random utility scripts under Fs.Utility, causing a conflict.

## [0.9.0] - 2025-10-13
### Changed
- Liquid particle color mixing.
- Added occluder rendering feature, allowing you to set layers that occlude the liquid.
- Added demo sample "WaterMix" for color mixing. "Milk" for occluder rendering and pixel effects.

## [0.8.0] - 2025-09-24
- Initial release.