# Changelog

## [1.3.0] - 2026-08-02
### Added
- **Color Mode (Simple / Gradient).** Each particle descriptor can now choose how its base color is produced. `Simple` keeps the previous flat color; `Gradient` samples an HDR gradient per particle from a physical scalar (speed / density / impact) every frame, driving dynamic looks like flow, foam, and translucent wave crests. See the README "Color Mode" section for the full parameter set.
### Fixed
- Fixed a color-space mismatch: particle colors uploaded via `SetVectorArray` / `ComputeBuffer` were not sRGB→linear converted in linear-color-space projects (unlike `SetColor`), so they rendered darker than material colors like the edge color. Particle colors are now converted at the GPU-upload boundary to match.

## [1.2.0] - 2026-07-03
### Added
- **Unity 2022.3 support (single-source).** One `Runtime` / `Editor` codebase now targets both Unity 6 (URP 17 / Render Graph) and Unity 2022.3 (URP 14) through `#if UNITY_6000_0_OR_NEWER` version macros. On 2022.3 the render pass runs on the classic imperative URP pipeline (no Render Graph) with an identical result. The `2022.3` branch is installed via the `#2022.3` git-URL suffix; the code is shared across both engines, only the Samples differ per version.
- **`RenderingLayerMask` support on Unity 2022.3.** Unity 2022.3 has no built-in `RenderingLayerMask` struct, so a compatible shim plus a custom PropertyDrawer was added: it reads the configured rendering-layer names from URP Global Settings and shows the same checkbox mask popup as Unity 6. On 2022.3 the layer names are edited under `Project Settings → Graphics → URP Global Settings → Rendering Layers (3D)`; usage is otherwise identical to Unity 6.
### Changed
- Colliders now accept **multiple** `nameTag`s: the single-tag field became a `List<string>`, and a collider blocks any particle group matching **any** tag in its list (empty list = all particles). Matching uses a per-collider group bitmask (`Liquid2DColliderData.GroupId` → `GroupMask`) rebuilt once per frame, so the per-particle solver cost is unchanged; the CPU (Burst) and GPU (compute) paths are both updated. The distinct-group count is capped at 32 (int mask); a collider tag resolving beyond that is ignored and logs a one-time warning.
- **Data-breaking:** the collider's legacy single `nameTag` field was removed with no migration path — colliders that had a tag set in existing scenes/prefabs load as an empty list (i.e. affect all particles) and must be re-tagged.
- Lowered the declared minimum dependencies to the Unity 2022.3 baseline: `unity` `2022.3` and `com.unity.render-pipelines.universal` `14.0.12` (previously `6000.2` / URP `17.2.0`). Assembly definition references were switched from GUIDs to explicit names.
- The sample UI (`HandlerUI`) now creates its `EventSystem` with the input module matching the active input backend (new Input System or legacy), so it no longer relies on the legacy Input Manager being enabled.

## [1.1.0] - 2026-07-02
### Added
- `Liquid2DSimulation.ClearAll()` to instantly remove every fluid particle at runtime.
### Changed
- Parallelized the GPU counting-sort prefix sum: replaced the serial single-thread `PrefixSum` with a work-efficient, bank-conflict-free multi-block Blelloch scan (span reduced to O(log n)). Measured ~100 → ~150 FPS at 20k particles (GPU frame time ~10ms → ~6.7ms)on Unity Editor. A serial fallback and an editor-only A/B verification kernel are kept for correctness.
- Amortized-O(1) slot freeing: `FreeSlot` no longer does an O(n) list removal; it now uses version-aware tombstones plus a head pointer with periodic in-place compaction, so per-group churn no longer scales with group size.
- Features whose `nameTag` matches no live particles now early-out of the full-screen render chain (skipping grab / blur / composite).
- Throttled spawns to a maximum of 256 particles per `FixedUpdate` (overflow dropped) to prevent a single-frame avalanche after a stall or under a very high flow rate.
- Weighted-random selection now uses proper zero-weight semantics (sums weights first; a non-positive total returns default).
- Reduced per-frame allocations: reuse the CPU hash-grid and contact buffers across substeps, build the draw matrix directly (skipping `Matrix4x4.TRS`), allocate kill flags only when destroy zones exist, and gate contact sampling on the dynamic-body count.
- Removed dead code: the unreferenced public `StringParameter` / `UIntParameter` Volume types, unused `Equals` / `GetHashCode` overrides, and the write-only `_EdgeStart` shader property.
- The debug particle-draw tool now also works in player builds.
- Clear the last frame of fluid rendering when play mode stops.
### Fixed
- Concave mesh colliders no longer leak particles: closed solid-mesh outlines are convex-decomposed via ear-clip triangulation (each triangle sent to the solver as its own polygon), so concave shapes contain fluid correctly.
- Collider velocity tracking is now gap-aware — it detects skipped fixed steps (e.g. after a hitch) and treats them as a restart instead of producing a huge phantom velocity.
- Fix a `Mesh` leak: the in-shader quad mesh created per Feature is now destroyed on `Dispose`.
- Wrap `SphCpuSolver.Step` in try/finally so `TempJob` buffers are always released even if a job throws (editor safety system / NaN).
- Move the GPU `MixColor` budget after the contact check to match CPU behavior; add a `count > 0` guard to the GPU grow full-reupload; add `Liquid2DRigidbodyBridge.InvalidateBodyCache()`.
- Hardening: the Spawner no longer starts an ejection burst in edit mode via `OnValidate`; `Loader`'s default load null-checks and logs; force dispatch uses Unity-Object null semantics to safely handle destroyed MonoBehaviours; synchronous GPU→CPU debug readbacks are now editor-only.
- Fix a compute-shader warning by using unsigned division in the `AddBlockOffsets` kernel (`Liquid2DSph.compute`).

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