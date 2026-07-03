# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

> [!IMPORTANT]
> **语言要求（最高优先级，全程生效）：必须始终使用中文与用户沟通。**
> 所有面向用户的输出——回复、解释、计划、提问、总结、待办列表、错误说明等——一律使用中文，且贯穿整个会话，不得中途切换回英文。
> （内部思考过程可以使用英文，但呈现给用户的任何内容都必须是中文。）
> 这条规则凌驾于任何默认行为之上，请务必严格遵守，不要遗忘。

## Collaboration Rules
- **始终使用中文回复（全程，不可遗忘）** — Always reply in Chinese for everything user-facing, throughout the entire session
- Before modifying code, briefly explain the approach first; do not jump straight to code
- When multiple implementation options exist, list them and let me choose rather than picking one yourself
- NEVER perform any git operation automatically (commit, push, branch, reset, etc.); the user handles these
- NEVER create Unity `.meta` files by hand; leave them to Unity's automatic generation. Only create/edit the source asset (e.g. the `.cs`), then let the editor import it. (When *moving/copying* an existing asset on disk, move its existing `.meta` alongside it to preserve the GUID — that is not "creating" a meta.)
- **Non-code assets down-porting risk**: copying non-code assets (scenes/prefabs/materials/`.asset`) from the Unity 6 repo into this 2022 project can break silently (version/GUID drift). Only copy a non-code asset after confirming it is truly compatible; otherwise ask the user to **create the asset in the 2022 editor first**, then edit it referencing the Unity 6 version. Copying `.cs`/`.compute`/`.hlsl`/`.shader` (text) + their `.meta` is safe.

## 移植状态 (Porting status) — READ FIRST

> [!IMPORTANT]
> **本仓库是 2D 流体系统从 Unity 6 向下移植到 Unity 2022.3 的版本，核心移植已完成。** 采用**完全移植**策略：直接从 Unity 6 移植代码，2022 的旧内容已作为参考被清除。**物理系统（CPU + GPU 路径）与渲染系统均已移植并在编辑器验证通过。**

- **真源（Unity 6，功能完整）：** `F:\ProjectUnity\unity-water-liquid-2d-simulation\Assets`（引擎 `6000.3`，URP `17.3.0`，Render Graph）。它的 `Assets/CLAUDE.md` 描述完整目标架构。移植以它为算法与设计的权威参考，但 **Render Graph 渲染管线相关的描述不适用于本仓库**（见下）。
- **本仓库（Unity 2022.3，移植目标）：** 引擎 `2022.3.62f3`，URP `14.0.12`（**无 Render Graph**，Compatibility Mode）。命名空间根 `Fs.Liquid2D`。
- **进度：**
  - ✅ **UPM 插件骨架已建**：`Assets/Plugins/Liquid2DSimulation/`（镜像 Unity 6 目录结构，含 `Runtime`/`Editor` asmdef + `package.json`）。
  - ✅ **物理系统已移植并验证**（纯数据 SPH，**CPU + GPU 两条路径均已在编辑器验证通过**）。已应用全部 2022 适配（见「Physics port notes」）。
  - ✅ **旧 2022 内容已删除**（`Assets/Source`、`Assets/Editor`、旧粒子预制体/材质/物理材质），`Settings/Renderer2D.asset` 的旧 Feature 引用已清空。
  - ✅ **渲染系统已移植**（URP 14 命令式重写，已在编辑器验证）：`Liquid2DPass` 由 Render Graph 改写为 `ScriptableRenderPass.Execute`+`CommandBuffer`+`RTHandle`+`DrawRenderers`；`Liquid2DFeature`/`Liquid2DRenderFeatureSettings`/`Liquid2DVolume`(+`Liquid2DVolumeDataParameter`)/8 个 shader/`Liquid2DDebugParticleDisplay`/2 个 editor(`Liquid2DParticleDescriptorEditor`/`Liquid2DEditorUtility`) 均已移植。渲染层选择机制采用 **nameTag（粒子↔Feature）+ URP Rendering Layers（障碍/遮挡）**，忠实照 Unity 6。
  - ✅ **GPU 路径已移植并验证**：GPU compute 求解器（`SphGpuSolver`+`Liquid2DSph.compute`）与 GPU 渲染路径均已在编辑器验证通过。`Liquid2DPass.ExecuteParticles` 已接通 GPU 分支——当 `Mode==Gpu` 且 `TryGetRenderBuffers` 成功时走 `ExecuteParticlesGpu`（`DrawProcedural` 直接读 5 个 `ComputeBuffer` + `Liquid2DParticleGpu` shader），否则回落 CPU `DrawMeshInstanced`。
- **留白约定：** 未定型/未验证处用 `【留白 / TODO：…】` 标注，勿当既定事实。

## Project overview

A Unity 2022.3 URP **2D liquid simulation** system, ported down from the Unity 6 version and shipped as the UPM-style package `com.blurfeng.liquid-2d-simulation` under **`Assets/Plugins/Liquid2DSimulation/`** (mirrors the Unity 6 layout: `Runtime/` + `Editor/` asmdefs + `package.json`). The surrounding project (scenes, `Settings/` URP assets, `Texture/`) is a **dev/demo harness**.

- Engine: `2022.3.62f3` (`ProjectSettings/ProjectVersion.txt`); URP `14.0.12` (`Packages/manifest.json`).
- **Physics packages installed** (`Packages/packages-lock.json`): `com.unity.burst` **1.8.21**, `com.unity.collections` **1.2.4** (the **1.x** line — NOT 2.x), `com.unity.mathematics` **1.2.6**. Input System is NOT installed; the project uses the legacy Input Manager (`activeInputHandler: 0`).
- Package version is `0.1.0` (`Assets/Plugins/Liquid2DSimulation/package.json`) to signal work-in-progress; `unity: 2022.3`, URP dep `14.0.12`.
- Namespace root `Fs.Liquid2D`; runtime asmdef `Liquid2DSimulation`, editor asmdef `Liquid2DSimulation.Editor`.

## Build / run / test

- **No CLI build.** Open in the Unity Editor (`2022.3.62f3`). "Compilation" = the editor compiling on focus; "build verification" = opening in Unity and checking the Console.
- **`.sln` / `*.csproj` are Unity-generated** — never hand-edit them. **No test suite** yet.
- **Editor-must-be-closed for disk restructures.** Bulk file moves/deletes and `.asset` edits are safest with the editor closed; adding/moving `.cs` while it is open can desync `.meta`. Deletions are handled gracefully. Check `Temp/UnityLockfile` presence as a hint the editor is open.
- **Physics is play-mode only.** `Liquid2DSimulation.Instance` is null in edit mode; the sim auto-creates a hidden singleton and runs in `FixedUpdate`. `Liquid2DDebugGizmos` only draws in Play.
- **Scene setup (both physics + rendering are verified working via this)**:
  1. Create a particle type asset: `Create ▸ Liquid2D/Particle Descriptor`. Set `Radius` + physics `Material`. For **rendering**, fill `RenderSettings`: `Sprite` (e.g. `Texture/Circle`), `Material` (a material using shader **`Custom/URP/2D/Liquid2DParticle`**), and `NameTag` = `"Liquid2D"` (matching the feature) or empty. (For gizmo-only physics validation, `RenderSettings` may stay empty.)
  2. In a scene, add: a `Liquid2DPhysicsConfig` with **`mode = Cpu`** (⚠ default is `Gpu` — see gotchas), a `Liquid2DBounds` (container), a `Liquid2DSpawner` (one `liquidParticles` entry → the descriptor), and optionally `Liquid2DDebugGizmos` (Scene-view physics discs).
  3. Add the **`Liquid2DFeature`** to `Settings/Renderer2D.asset` (Add Renderer Feature). Its Blur/Effect shaders auto-populate via `Shader.Find`.
  4. Enter Play → **Game view** shows the composited metaball fluid; **Scene view** shows the debug gizmos.
- **【留白 / TODO：SampleScene 仍指向已删除的旧预制体/旧 Volume（有 missing script）；应改造成一个纯数据 + 渲染的正式 demo 场景。】**

## Architecture

Two decoupled halves meet at a data hand-off: a **pure-data SPH physics** sim (ported, present) and a **URP rendering pipeline** (not yet ported). The renderer will read particle state out of the sim buffers, never off Transforms.

### Physics — pure-data SPH (`Runtime/Source/Systems/Physics/`) — PORTED

No per-particle GameObjects. Particles are rows in Structure-of-Arrays `NativeArray` buffers advanced by an SPH **dual-density** solver. (Faithful copy of the Unity 6 sources.)

- **`Liquid2DSimulation.cs`** — runtime singleton hub (`[DefaultExecutionOrder(-100)]`, lazily creates a hidden GameObject; **play-mode only**). Owns the `Liquid2DParticleStore`, the solver, descriptor/material tables, per-`nameTag` groups. Drives solving in **`FixedUpdate`**. Public API: `Spawn` / `Despawn` / `GetPosition` / `GetVelocity` / `IsAlive` / `SetColor`. Static config knobs: `Params`, `Mode`, `ColorMixMode`, `MaxParticlesPerTag`, `GpuReadbackToStore`. Render hand-off (for the future renderer): `TryGetRenderData` (CPU SoA + active indices), `TryGetRenderBuffers` (GPU `ComputeBuffer`s), `GetRenderableAliveCount`.
- **`Liquid2DParticleStore.cs`** — SoA store (positions/velocities/colors/radii/typeId/groupId/alive/…) as parallel `NativeArray`s. Stable slots; a handle's `Index`==slot, freeing bumps a `version` for staleness detection.
- **`Solvers/`** — `ILiquid2DSolver` is the CPU/GPU seam. **`SphCpuSolver` + `SphJobs` + `Liquid2DHashGrid`** = Job System + Burst dual-density SPH over a hand-rolled counting-sort hash grid (no `NativeParallelHashMap`). **`SphGpuSolver` + `Shaders/Resources/Liquid2DSph.compute`** = GPU-resident compute path. `SolverParams` = global solver params.
- **`Liquid2DParticleDescriptor.cs`** — `ScriptableObject` that **replaces the particle prefab**: defines a type's `Radius`, `RenderScale`, `DefaultLifetime`, `RenderSettings`, `MixSettings`, physics `Material`. Registered into the sim for a `typeId`.
- **Colliders / ForceFields / DeadZones / Bounds** — scene-authored MonoBehaviours flattened to Burst buffers each frame via static registries. `Liquid2DRigidbodyBridge` (`ILiquid2DForceReceiver`) is the two-way coupling to a `Rigidbody2D` (wash-away + buoyancy).
- **`Liquid2DPhysicsConfig.cs`** (in `Systems/`) — optional scene component pushing solver params / `mode` / cap / fixed timestep into the static `Liquid2DSimulation` fields on `Awake`.
- **Gameplay** (`Systems/Gameplay/`) — `Liquid2DSpawner` / `Liquid2DRegionSpawner` (spawn from a `Liquid2DParticleConfig` wrapping a descriptor), `Liquid2DDeadZone`, `Liquid2DMouseInteractor` (legacy+new input, compiled via `ENABLE_LEGACY_INPUT_MANAGER`), `AutoRotator`, `Random/`.
- **`Systems/Debug/Liquid2DDebugGizmos.cs`** — Scene-view gizmo visualizer reading the sim SoA (physics-only validation tool; Play-mode only; `#if UNITY_EDITOR`).

### Rendering — URP 14 imperative `ScriptableRenderPass` (`Runtime/Source/Systems/`) — PORTED

Consumes the sim hand-off (`TryGetRenderData` CPU SoA — the active path; `TryGetRenderBuffers` GPU — deferred) instead of Transforms, and replicates the Unity 6 look.

- **`Liquid2DFeature.cs`** — `ScriptableRendererFeature`; `Shader.Find`s the Blur/Effect shaders, builds their materials, constructs `Liquid2DPass`, `EnqueuePass`. Carries `NameTag`. **Ported verbatim** (not Render Graph).
- **`Liquid2DPass.cs`** — `ScriptableRenderPass` at `RenderPassEvent.AfterRenderingTransparents`. **Rewritten** from Unity 6's Render Graph pass into imperative URP 14: RTs allocated in `OnCameraSetup` via `RenderingUtils.ReAllocateIfNeeded` (persistent `RTHandle`s, released in `Dispose`), everything else built into one `CommandBuffer` in `Execute`. Per frame: GrabAsBg (**two-target MRT** via `cmd.SetRenderTarget(RenderTargetIdentifier[], depth)`) → draw particles from sim (`cmd.DrawMeshInstanced`, per-descriptor nameTag gate, frustum cull, ≤1023/batch) → iterative Kawase blur with core-keep (`Blitter.BlitCameraTexture` + `CombineTwo`) → obstructor/occluder rendering-layer textures (`context.DrawRenderers` — **must flush the `cmd` first**) → composite via `Liquid2DEffect` (cutoff/cover/edge/opacity/pixel/distort keywords) → debug-display overlay. Scene-view camera draws particles straight to camera color and returns.
- **`Liquid2DRenderFeatureSettings.cs`** — the serialized effect settings + nested `Blur`/`Distort`/`Edge`/`Pixel` + the `CopyFrom` merge (single source of truth for the per-frame Volume merge). `Obstructor/OccluderRenderingLayerMask` are **`uint`** (Unity 6 used the `RenderingLayerMask` struct, which doesn't exist in 2022.3 — see notes).
- **`Volumes/Liquid2DVolume.cs`** — `VolumeComponent` overriding feature settings at runtime; `Liquid2DPass.UpdateSettings()` merges by `nameTag` each frame via `CopyFrom`.
- **Shaders (`Runtime/Source/Shaders/`)** — 8 shaders (`Liquid2DParticle`(`Gpu`)/`Liquid2DParticleDisplay`/`Liquid2DBlur`/`Liquid2DEffect`/`CombineTwo`/`GrabAsBg`/`Clone`) + `ShaderLibrary/MathUtils.hlsl`, resolved by `Shader.Find("Custom/URP/2D/...")`. Ported verbatim (URP 14-compatible). ⚠ `Liquid2DParticleDisplay.shader` declares the name `Custom/URP/2D/Liquid2DDebugParticleDisplay`.
- **`Systems/Debug/Liquid2DDebugParticleDisplay.cs`** — optional `DrawProcedural`-based particle overlay drawn by the pass (`ExecuteDraw(CommandBuffer)`; Unity 6 used `RasterCommandBuffer`).

## Physics port notes

- **Source of the port:** a faithful file-tree copy of Unity 6's `Runtime/Source/Systems/Physics/**` + solvers + `Liquid2DPhysicsConfig` + shared data types (`Liquid2DColorMixMode`/`Liquid2DParticleMixSettings`/`Liquid2DParticleRenderSettings`) + `Localization/` + `Systems/Debug/Liquid2DDebugGizmos` + `Systems/Gameplay/**` + `Shaders/Resources/Liquid2DSph.compute`, with `.meta` preserved (GUIDs match Unity 6). Editor: only `Liquid2DRigidbodyBridgeEditor` + `Liquid2DSpawnerEditor` (the rendering-dependent editors were deferred).
- **The ONLY Unity 6 → 2022.3 code changes needed** (verified by a full scan):
  - `Liquid2DRigidbodyBridge.cs` — `Rigidbody2D.linearVelocity` (Unity 6 rename) → **`velocity`**. (`angularVelocity` is unchanged.)
  - `Liquid2DRegionSpawner.cs` — `Color.aquamarine` (Unity 6-only named color) → **`new Color(0.498f, 1f, 0.831f)`**.
  - Everything else (Collections 1.2.4 / Mathematics 1.2.6 / Burst 1.8 / the compute shader / C# 9) compiles unchanged.
- **⚠ `Liquid2DSimulation.Mode` defaults to `Gpu`** (and `Liquid2DPhysicsConfig.mode` defaults to `Gpu`). Since GPU is not yet validated, **set `mode = Cpu`** for now. In CPU mode the CPU store is always current, so `GetPosition`/`Liquid2DDebugGizmos` work directly; in GPU mode they need `gpuReadbackToStore = true` (a synchronous GPU→CPU stall).
- **asmdef:** `Runtime/Liquid2DSimulation.asmdef` references only `Unity.Burst` / `Unity.Collections` / `Unity.Mathematics` — the physics layer uses **no URP type** (`TryGetRenderBuffers` returns core `ComputeBuffer`). When the renderer is added it will need URP references (likely a separate rendering asmdef or new references here).
- **GPU path** (`SphGpuSolver` + `Liquid2DSph.compute`) is ported and **validated in-editor** under `Mode=Gpu`; the Blelloch parallel prefix-sum kernel (512 threads/group) works. A serial fallback remains as an escape hatch: `SphGpuSolver.DebugUseSerialPrefixSum` (a `public static readonly bool = false` compile-time constant, ~line 87) — flip to `true` in code to switch to the serial prefix-sum kernel if the parallel one ever regresses.

## Rendering port notes (DONE)

The Render Graph → URP 14 rewrite is complete. Key Unity 6 → URP 14.0.12 API mappings used in `Liquid2DPass.cs` (apply the same when extending it):

- `RecordRenderGraph(RenderGraph, ContextContainer)` → `Execute(ScriptableRenderContext, ref RenderingData)` + `OnCameraSetup`.
- `frameData.Get<Universal*Data>()` / `resourceData.activeColorTexture` → `renderingData.cameraData` + `renderingData.cameraData.renderer.cameraColorTargetHandle` (read the color target only in `OnCameraSetup`/`Execute`, cache it; never in the ctor/`AddRenderPasses`).
- `renderGraph.CreateTexture(TextureDesc)` (transient) → persistent `RTHandle` fields + `RenderingUtils.ReAllocateIfNeeded(ref h, desc, ...)` in `OnCameraSetup` (**URP 14 name — URP 15+ renamed it `ReAllocateHandleIfNeeded`**), released in `Dispose`.
- `builder.SetRenderAttachment(th, idx)` → `cmd.SetRenderTarget(...)` / `CoreUtils.SetRenderTarget(cmd, rt, ClearFlag, color)`; the **two-target MRT** (GrabAsBg) → `cmd.SetRenderTarget(RenderTargetIdentifier[]{rt0,rt1}, rt0)`.
- `renderGraph.AddBlitPass` → `Blitter.BlitCameraTexture(cmd, src, dst)`.
- `renderGraph.CreateRendererList` + `cmd.DrawRendererList` → `context.DrawRenderers(cullResults, ref drawSettings, ref filteringSettings)` — **`context`, not `cmd`, so flush (`context.ExecuteCommandBuffer(cmd); cmd.Clear();`) before calling it**. Build `drawSettings` via the inherited `protected CreateDrawingSettings(tag, ref renderingData, sort)`.
- `RasterCommandBuffer` (RG) → classic `CommandBuffer`.
- **`RenderingLayerMask` struct → `uint`** — the struct is 2023.1+/URP 16+ only; `FilteringSettings.renderingLayerMask` is `uint` in URP 14. (Inspector shows a raw number; a nicer mask popup drawer is a future polish.)
- **Ported verbatim** (no API change): the 8 shaders, `DrawMeshInstanced`/`DrawProcedural`+`MaterialPropertyBlock`, the sim→renderer hand-off, the Volume `CopyFrom` merge.
- **Correctness invariants preserved** (each was a deliberate Unity 6 fix): core-keep even/odd RT parity; clear obstructor/occluder to transparent every frame; occluder gated on `renderingLayerMask != 0`; `_PIXEL_BG` keyword set unconditionally; distortion disabled on opaque-Replace; `SetKeyword` state-change guard.
- **GPU render path is wired + validated**: `Liquid2DPass.ExecuteParticles` takes the GPU branch (`ExecuteParticlesGpu`) when `Liquid2DSimulation.Mode == Gpu && TryGetRenderBuffers(...)` succeeds — per descriptor (nameTag gate) it binds the 5 `ComputeBuffer`s (`PositionsBuf`/`ColorsBuf`/`RadiiBuf`/`TypeIdsBuf`/`ActiveIdxBuf`) + sprite texture + `RenderScale` on a `MaterialPropertyBlock` and draws via `cmd.DrawProcedural(..., MeshTopology.Triangles, 6, count, mpb)` with the shared `Liquid2DParticleGpu` material; otherwise it falls back to the CPU `DrawMeshInstanced` path.

## Conventions / gotchas

- **"Render Graph" belongs to Unity 6, not here.** Anything referencing `RecordRenderGraph`/`TextureHandle`/`RasterCommandBuffer` cannot be used in this URP 14 repo.
- **Trilingual docs (keep the style):** public API XML comments in Chinese / English / Japanese; inspector tooltips via `LocalizationTooltipAttribute(zh, en, ja)` (ported, in `Runtime/Source/Localization/`). `[Header(...)]` uses English only.
- **`.meta` handling** — never hand-create; preserve on move/copy to keep GUIDs.
- **Burst/native cleanup:** every `NativeArray`/`NativeList`/`ComputeBuffer` must be disposed; TempJob buffers freed each frame (see the `try/finally` around `_solver.Step` and `OnDestroy` in `Liquid2DSimulation.cs`).
- **GPU-mode staleness:** under `Mode == Gpu` the CPU `store` is stale except on buffer-grow frames — `GetPosition`/gizmos are unreliable unless `GpuReadbackToStore` is on.
- Editor-only code is guarded by `#if UNITY_EDITOR` (runtime files like `Liquid2DDebugGizmos` guard their `UnityEditor.Handles` usage this way).

## Unity coding conventions

- **Inspector fields stay private.** Any serialized field exposed in the Inspector is declared `private` with `[SerializeField]` — never `public`. Applies to `MonoBehaviour`, `ScriptableObject`, and nested `[Serializable]` classes. Expose a property instead of widening visibility.
  - ⚠️ **Existing divergence (from the Unity 6 source, kept faithfully):** `Liquid2DParticleDescriptor`, `Liquid2DParticleMaterial`, `Liquid2DParticleRenderSettings` use `public` serialized fields (PascalCase). Match the surrounding file's style when editing; prefer private+`[SerializeField]` for genuinely new code.
- **Public field naming: PascalCase.** Public fields use PascalCase (`Radius`, `RenderScale`). Private fields use `_camelCase` with a leading underscore.

### C# identifier naming

Follow the official .NET naming guidelines
(https://learn.microsoft.com/zh-cn/dotnet/csharp/fundamentals/coding-style/identifier-names):

- **PascalCase** — types, methods, properties, events, namespaces, enum members, `public`/`protected`/`internal` fields and constants.
- **Interfaces** — `I` prefix + PascalCase (`ILiquid2DSolver`, `ILiquid2DForceReceiver`).
- **Private fields — always `_camelCase` with a leading underscore**, regardless of `static`/`readonly`/`const` (incl. `Shader.PropertyToID` caches). Accessible constants use PascalCase.
- **Method parameters & locals** — `camelCase`. **Generic type params** — `T` prefix (`T`, `TValue`).
- ⚠️ **Serialization caveat:** renaming a serialized field (incl. first-letter case) breaks saved YAML — migrate assets or add `[FormerlySerializedAs("oldName")]`.
- ⚠️ **Shader/compute identifier exception:** strings passed to `Shader.PropertyToID(...)` must match the shader/compute declarations exactly (e.g. in `Liquid2DSph.compute`, buffers PascalCase like `Positions`, scalars camelCase like `dt`/`h`). C# naming does not apply to them.

### Unity Object null checks

For types deriving from `UnityEngine.Object` (MonoBehaviour, ScriptableObject, Material, GameObject, Sprite, …), prefer the implicit-bool form `if (obj)` / `if (!obj)` over `!= null` / `== null` — Unity overloads these and the bool form correctly treats a destroyed object as "null". (Plain C# objects still use `is null` / `!= null`.)
