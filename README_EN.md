<p align="center">
  🌍
  <a href="./README.md">中文</a> |
  English |
  <a href="./README_JA.md">日本語</a>
</p>

# Liquid 2D Simulation — Unity 2022.3 Branch

This is the **Unity 2022.3 branch** (URP 14) of [Liquid 2D Simulation](https://github.com/blurfeng/unity-water-liquid-2d-simulation).

> 📖 **For the full feature overview, usage guide, and parameter reference, see the README on the main branch:**
> [中文](https://github.com/blurfeng/unity-water-liquid-2d-simulation/blob/main/README.md) ·
> [English](https://github.com/blurfeng/unity-water-liquid-2d-simulation/blob/main/README_EN.md) ·
> [日本語](https://github.com/blurfeng/unity-water-liquid-2d-simulation/blob/main/README_JA.md)

## What this branch is / differences from main
This branch makes the plugin run on `Unity 2022.3` (URP 14). The **source is single-source**: the same `Runtime` / `Editor` code supports both Unity 6 and Unity 2022.3 via version macros (`#if UNITY_6000_0_OR_NEWER`) and is merged down from main's `dev` branch, so it is **functionally identical to main and used in the same way**. The only differences from main are the underlying implementation and configuration entry points:

| | main | This branch (2022.3) |
| --- | --- | --- |
| Engine | `Unity 6` (URP 17) | `Unity 2022.3` (URP 14) |
| Rendering | Render Graph | Classic imperative URP pipeline (no Render Graph), same result |
| Rendering Layer config | `Project Settings → Tags and Layers → Rendering Layers` | `Project Settings → Graphics → URP Global Settings → Rendering Layers (3D)`, same usage |
| Samples | Unity 6 version | Adapted for 2022.3 |
| Update cadence | Source of truth | Merged from main, slightly behind |

## Install via UPM
In Package Manager, click `+` → `Install package from git URL...` and paste:
```
https://github.com/blurfeng/unity-water-liquid-2d-simulation.git?path=Assets/Plugins/Liquid2DSimulation#2022.3
```
> If you **don't need the demo scenes (Samples)**, you can also use the main branch link directly (single-source, shared across both versions — just drop the trailing `#2022.3`). Use the `#2022.3` link above only when you want the Samples already adapted for 2022.3.

## Pull updates from dev into this branch (for maintainers)
This repo does not modify the source; the source is merged single-source from the main repo. The following commands pull the latest `Runtime` / `Editor` / `package.json` from the main repo's `dev` branch into this 2022.3 branch:
```bash
git fetch origin
git checkout origin/dev -- \
  Assets/Plugins/Liquid2DSimulation/Runtime \
  Assets/Plugins/Liquid2DSimulation/Editor \
  Assets/Plugins/Liquid2DSimulation/package.json \
  Assets/Plugins/Liquid2DSimulation/package.json.meta \
  Assets/Plugins/Liquid2DSimulation/CHANGELOG.md \
  Assets/Plugins/Liquid2DSimulation/CHANGELOG.md.meta \
  Assets/Plugins/Liquid2DSimulation/README.md \
  Assets/Plugins/Liquid2DSimulation/README.md.meta
```
