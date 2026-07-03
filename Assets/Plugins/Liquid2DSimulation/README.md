![](https://raw.githubusercontent.com/blurfeng/unity-water-liquid-2d-simulation/main/Documents/samples_1.gif)

<p align="center">
  <img alt="GitHub Release" src="https://img.shields.io/github/v/release/blurfeng/unity-water-liquid-2d-simulation?color=blue">
  <img alt="GitHub Repo License" src="https://img.shields.io/badge/license-MIT-blueviolet">
  <img alt="GitHub Repo Issues" src="https://img.shields.io/github/issues/blurfeng/unity-water-liquid-2d-simulation?color=yellow">
</p>

<p align="center">
  🌍
  <a href="#中文">中文</a> |
  <a href="#english">English</a> |
  <a href="#日本語">日本語</a>
</p>

# Liquid 2D Simulation

> Unity URP 2D 流体模拟 · 2D fluid simulation for Unity URP · Unity URP 向け 2D 流体シミュレーション

本文件是随 UPM 包发行的精简说明。**完整的分步图文文档在 GitHub 仓库**：
This file is the concise readme shipped with the UPM package. **The full step-by-step docs live in the GitHub repository**:
このファイルは UPM パッケージに同梱される簡易説明です。**完全なステップバイステップのドキュメントは GitHub リポジトリ**にあります：

📖 [中文](https://github.com/blurfeng/unity-water-liquid-2d-simulation/blob/main/README.md) ·
[English](https://github.com/blurfeng/unity-water-liquid-2d-simulation/blob/main/README_EN.md) ·
[日本語](https://github.com/blurfeng/unity-water-liquid-2d-simulation/blob/main/README_JA.md)

---

## 中文

Liquid 2D Simulation 是一款面向 `Unity` 的 2D 流体模拟系统，开箱即用，能够快速实现逼真的流体效果。
它搭载**自研的流体粒子物理系统**（SPH 双密度求解），**不依赖 Unity 的物理系统**；通过 GPU 模式可以**轻松达到数万规模的粒子**，并保持高效运行。
借助丰富的配置参数，你可以自由地创建水、岩浆、石油、泡沫、沙等各种不同质感的流体表现。
同时支持 `Unity 6` 与 `Unity 2022.3` 两个引擎版本（更旧的版本未经测试）。

### 特性
| 特性                | 描述                                                                                         |
| ------------------- | -------------------------------------------------------------------------------------------- |
| 自研 SPH 物理       | 纯数据的 SPH 双密度求解器，不依赖 Unity 物理系统，无每粒子 GameObject，万级粒子依然流畅。          |
| CPU / GPU 双模式    | CPU 基于 Job System + Burst；GPU 使用 Compute Shader 数据常驻，规模更大。平台不支持时自动回退 CPU。 |
| 丰富的流体材质      | 可配置粘性、表面张力、摩擦、反弹、重力缩放、浮力密度等，内置水/熔岩/泡沫/沙预设。                   |
| 场景交互            | 自研碰撞器阻挡流体、两路刚体耦合（冲走 / 浮力漂浮）、力场（吸引 / 排斥 / 旋流）、死亡区域回收。       |
| 多色彩空间混色      | 不同颜色流体相遇时混色，支持 Oklab / RYB / LinearRgb 三种混色算法。                                |
| URP 2D / Render Graph | 基于 URP 2D 渲染。Unity 6 使用新的 Render Graph 框架；Unity 2022.3 改用旧的命令式 URP 管线，效果一致。 |
| GPU Instance        | 使用 GPU Instance 方式渲染粒子，可一次渲染大量粒子。                                            |
| Volume 运行时修改   | 支持在运行时通过 Volume 修改流体粒子的渲染效果。                                                |

### 环境要求
- `Unity 6000.2` 或更新版本；**`Unity 2022.3` 也已完整支持**（源码单源设计，同一套代码通过版本宏支持两个引擎版本）。
- URP 2D 渲染管线（Unity 6 走 Render Graph，Unity 2022.3 走命令式 URP 14，效果一致）。
- GPU 求解模式需要平台支持 `Compute Shader`；不支持时自动回退 CPU。

### 快速上手
1. 将 `Liquid2DFeature` 添加到 URP 的 **2D Renderer Data**。
2. 在 `Project` 窗口右键 `Create → Liquid2D → Particle Descriptor` 创建**流体粒子描述符**，配置半径、渲染、材质与混色。
3. 场景中新建空物体添加 `Liquid2DSpawner`（或使用 `Resources/Prefabs/` 下的预制体），在 `Liquid Particles` 列表引用描述符即可喷射流体。

> 想直接看演示，可在 `Window → Package Manager` 中选中本包，从 **Samples** 导入演示场景。

> 碰撞阻挡、两路刚体耦合、力场、性能调优（CPU/GPU、粒子上限）等详细配置见 [完整文档](https://github.com/blurfeng/unity-water-liquid-2d-simulation/blob/main/README.md)。

### 致谢与许可
流体求解器核心算法主要参考 [SebLague/Fluid-Sim](https://github.com/SebLague/Fluid-Sim)，感谢 SebLague。
基于 MIT 许可证发行，详见 [LICENSE](https://github.com/blurfeng/unity-water-liquid-2d-simulation)。

---

## English

Liquid 2D Simulation is a 2D fluid simulation system for `Unity`. It works out of the box and lets you quickly achieve realistic fluid effects.
It is powered by a **custom-built fluid particle physics system** (SPH dual-density solver) and **does not rely on Unity's physics system**; with GPU mode it can **easily reach tens of thousands of particles** while staying highly efficient.
With its rich set of configuration parameters, you can freely create water, lava, oil, foam, sand, and many other fluids with different textures and looks.
It supports both `Unity 6` and `Unity 2022.3` (older versions are untested).

### Features
| Feature               | Description                                                                                                   |
| --------------------- | ------------------------------------------------------------------------------------------------------------- |
| Custom SPH physics    | A pure-data SPH dual-density solver, independent of Unity physics, with no per-particle GameObject; smooth even at tens of thousands of particles. |
| CPU / GPU dual mode   | CPU is based on the Job System + Burst; GPU uses Compute Shaders with resident data for larger scale. Automatically falls back to CPU when unsupported. |
| Rich fluid materials  | Configurable viscosity, surface tension, friction, restitution, gravity scale, buoyancy density, and more, with built-in Water / Lava / Foam / Sand presets. |
| Scene interaction     | Custom colliders block fluid, two-way rigidbody coupling (wash-away / buoyant float), force fields (attract / repel / swirl), and dead zones for recycling. |
| Multi color-space mixing | Fluids of different colors mix when they meet, with three algorithms: Oklab / RYB / LinearRgb.          |
| URP 2D / Render Graph | Built on URP 2D. Unity 6 uses the new Render Graph framework; Unity 2022.3 uses the classic imperative URP pipeline, with the same result. |
| GPU Instancing        | Particles are rendered with GPU Instancing, drawing large numbers of particles in one pass.                    |
| Runtime tweaking via Volume | Modify the fluid particles' rendering effects at runtime through Volumes.                                |

### Requirements
- `Unity 6000.2` or newer; **`Unity 2022.3` is also fully supported** (single-source: one codebase supports both engine versions via version macros).
- URP 2D rendering pipeline (Unity 6 uses Render Graph; Unity 2022.3 uses the imperative URP 14 pipeline — same result).
- GPU solve mode requires platform support for `Compute Shaders`; it automatically falls back to CPU otherwise.

### Quick Start
1. Add `Liquid2DFeature` to your URP **2D Renderer Data**.
2. In the `Project` window, `Create → Liquid2D → Particle Descriptor` to make a **fluid particle descriptor**, then configure radius, rendering, material, and mixing.
3. Add a `Liquid2DSpawner` to a GameObject in the scene (or use the prefab under `Resources/Prefabs/`), and reference the descriptor in its `Liquid Particles` list to eject fluid.

> To see it running right away, select this package in `Window → Package Manager` and import the **Samples** demo scenes.

> For colliders, two-way rigidbody coupling, force fields, and performance tuning (CPU/GPU, particle caps), see the [full documentation](https://github.com/blurfeng/unity-water-liquid-2d-simulation/blob/main/README_EN.md).

### Acknowledgements & License
The fluid solver's core algorithm mainly references [SebLague/Fluid-Sim](https://github.com/SebLague/Fluid-Sim). Thanks to SebLague.
Released under the MIT License — see [LICENSE](https://github.com/blurfeng/unity-water-liquid-2d-simulation).

---

## 日本語

Liquid 2D Simulation は `Unity` 向けの 2D 流体シミュレーションシステムです。すぐに使え、リアルな流体表現を素早く実現できます。
**自社開発の流体パーティクル物理システム**（SPH デュアル密度ソルバー）を搭載し、**Unity の物理システムに依存しません**。GPU モードでは**数万規模のパーティクル**を簡単に扱いながら、高い効率を維持できます。
豊富な設定パラメータにより、水・マグマ・石油・泡・砂など、さまざまな質感の流体表現を自由に作成できます。
`Unity 6` と `Unity 2022.3` の両方に対応しています（それより古いバージョンは未検証です）。

### 特徴
| 特徴                  | 説明                                                                                          |
| --------------------- | --------------------------------------------------------------------------------------------- |
| 自社開発の SPH 物理   | 純粋データの SPH デュアル密度ソルバー。Unity 物理に依存せず、パーティクルごとの GameObject も不要で、数万規模でも滑らか。 |
| CPU / GPU 両モード    | CPU は Job System + Burst ベース。GPU は Compute Shader でデータを常駐させ、より大規模に対応。非対応時は自動で CPU にフォールバック。 |
| 豊富な流体マテリアル  | 粘性・表面張力・摩擦・反発・重力スケール・浮力密度などを設定可能。水 / マグマ / 泡 / 砂のプリセットを内蔵。 |
| シーンインタラクション | 自社製コライダーによる流体ブロック、双方向の剛体カップリング（押し流し / 浮力）、フォースフィールド（吸引 / 反発 / 渦）、デッドゾーンによる回収。 |
| 複数色空間での混色    | 異なる色の流体が出会うと混色。Oklab / RYB / LinearRgb の 3 種類に対応。                          |
| URP 2D / Render Graph | URP 2D をベースに描画。Unity 6 は新しい Render Graph、Unity 2022.3 は従来の命令型 URP パイプラインで実装しますが、表現は同じです。 |
| GPU Instance          | GPU Instance 方式でパーティクルを描画し、一度に大量のパーティクルを描画。                        |
| Volume による実行時変更 | 実行時に Volume を通じて流体パーティクルの描画効果を変更可能。                                  |

### 動作環境
- `Unity 6000.2` 以降；**`Unity 2022.3` も完全対応**（単一ソース設計。同一コードがバージョンマクロで両エンジンに対応）。
- URP 2D レンダーパイプライン（Unity 6 は Render Graph、Unity 2022.3 は命令型 URP 14。表現は同じ）。
- GPU ソルブモードはプラットフォームの `Compute Shader` サポートが必要。非対応時は自動的に CPU へフォールバック。

### クイックスタート
1. `Liquid2DFeature` を URP の **2D Renderer Data** に追加します。
2. `Project` ウィンドウで `Create → Liquid2D → Particle Descriptor` から**流体パーティクルディスクリプタ**を作成し、半径・描画・マテリアル・混色を設定します。
3. シーンの GameObject に `Liquid2DSpawner` を追加（または `Resources/Prefabs/` のプレハブを使用）し、`Liquid Particles` リストでディスクリプタを参照して流体を噴射します。

> すぐに動作を確認するには、`Window → Package Manager` で本パッケージを選択し、**Samples** のデモシーンをインポートしてください。

> コライダー、双方向カップリング、フォースフィールド、パフォーマンス調整（CPU/GPU、パーティクル上限）などの詳細は [完全なドキュメント](https://github.com/blurfeng/unity-water-liquid-2d-simulation/blob/main/README_JA.md) を参照してください。

### 謝辞とライセンス
流体ソルバーのコアアルゴリズムは主に [SebLague/Fluid-Sim](https://github.com/SebLague/Fluid-Sim) を参考にしました。SebLague に感謝します。
MIT ライセンスで公開しています。[LICENSE](https://github.com/blurfeng/unity-water-liquid-2d-simulation) を参照してください。
