<p align="center">
  🌍
  中文 |
  <a href="./README_EN.md">English</a> |
  <a href="./README_JA.md">日本語</a>
</p>

# Liquid 2D Simulation — Unity 2022.3 分支

这是 [Liquid 2D Simulation](https://github.com/blurfeng/unity-water-liquid-2d-simulation) 的 **Unity 2022.3 版本分支**（URP 14）。

> 📖 **完整的功能介绍、使用指南与参数说明请查看 main 主分支的 README：**
> [中文](https://github.com/blurfeng/unity-water-liquid-2d-simulation/blob/main/README.md) ·
> [English](https://github.com/blurfeng/unity-water-liquid-2d-simulation/blob/main/README_EN.md) ·
> [日本語](https://github.com/blurfeng/unity-water-liquid-2d-simulation/blob/main/README_JA.md)

## 此分支的作用 / 与 main 的区别
本分支让插件在 `Unity 2022.3`（URP 14）上运行。**源码为单源设计**：同一套 `Runtime` / `Editor` 源码通过版本宏（`#if UNITY_6000_0_OR_NEWER`）同时支持 Unity 6 与 Unity 2022.3，从 main 的 `dev` 分支合并而来，**功能与 main 一致、使用方式相同**。与 main 的区别仅在于底层实现与配置入口：

| | main 主分支 | 本分支（2022.3） |
| --- | --- | --- |
| 引擎 | `Unity 6`（URP 17） | `Unity 2022.3`（URP 14） |
| 渲染 | Render Graph | 旧的命令式 URP 管线（无 Render Graph），效果一致 |
| Rendering Layer 配置 | `Project Settings → Tags and Layers → Rendering Layers` | `Project Settings → Graphics → URP Global Settings → Rendering Layers (3D)`，用法相同 |
| 演示场景（Samples） | Unity 6 版 | 针对 2022.3 适配 |
| 更新节奏 | 源码真源 | 从 main 合并，略慢于主分支 |

## 使用 UPM 安装
在 Package Manager 中点击 `+` → `Install package from git URL...`，粘贴以下链接：
```
https://github.com/blurfeng/unity-water-liquid-2d-simulation.git?path=Assets/Plugins/Liquid2DSimulation#2022.3
```
> 若你**不需要演示场景（Samples）**，也可直接使用 main 主分支链接（源码单源、两版通用，去掉末尾的 `#2022.3` 即可）；需要已适配 2022.3 的 Samples 时，再用上面的 `#2022.3` 链接。

## 从 dev 拉取更新到本分支（维护者用）
本仓库原则上不改源码，源码只从主仓库单源合并。以下命令把主仓库 `dev` 分支上最新的 `Runtime` / `Editor` / `package.json` 拉取到本 2022.3 分支：
```bash
git fetch origin
git checkout origin/dev -- \
  Assets/Plugins/Liquid2DSimulation/Runtime \
  Assets/Plugins/Liquid2DSimulation/Editor \
  Assets/Plugins/Liquid2DSimulation/package.json \
  Assets/Plugins/Liquid2DSimulation/package.json.meta \
  Assets/Plugins/Liquid2DSimulation/CHANGELOG.md \
  Assets/Plugins/Liquid2DSimulation/CHANGELOG.md.meta
```
