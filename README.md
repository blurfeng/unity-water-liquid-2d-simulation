![](Documents/samples_1.gif)

<p align="center">
  <img alt="GitHub Release" src="https://img.shields.io/github/v/release/blurfeng/unity-water-liquid-2d-simulation?color=blue">
  <img alt="GitHub Downloads (all assets, all releases)" src="https://img.shields.io/github/downloads/blurfeng/unity-water-liquid-2d-simulation/total?color=green">
  <img alt="GitHub Repo License" src="https://img.shields.io/github/license/blurfeng/unity-water-liquid-2d-simulation?color=blueviolet">
  <img alt="GitHub Repo Issues" src="https://img.shields.io/github/issues/blurfeng/unity-water-liquid-2d-simulation?color=yellow">
</p>

<p align="center">
  🌍
  中文 |
  <a href="./README_EN.md">English</a> |
  <a href="./README_JA.md">日本語</a>
</p>

<p align="center">
  📥
  <a href="#使用-upm">安装</a> |
  <a href="#下载安装包">下载</a>
</p>

# Liquid 2D Simulation - 2D流体模拟
Liquid 2D Simulation 是一款面向 `Unity` 的 2D 流体模拟系统，开箱即用，能够快速实现逼真的流体效果。  
它搭载**自研的流体粒子物理系统**（SPH 双密度求解），**不依赖 Unity 的物理系统**；通过 GPU 模式可以**轻松达到数万规模的粒子**，并保持高效运行。  
借助丰富的配置参数，你可以自由地创建水、岩浆、石油、泡沫、沙等各种不同质感的流体表现。

## 🙏 致谢
流体物理求解器的核心算法主要参考了 [SebLague/Fluid-Sim](https://github.com/SebLague/Fluid-Sim)，感谢 SebLague。

## 📜 目录
- [简介](#简介)
  - [项目特性](#项目特性)
- [💻 环境要求](#-环境要求)
- [🌳 分支](#-分支)
- [🌱 快速开始](#-快速开始)
  - [1.安装插件](#1安装插件)
  - [2.添加 Renderer Feature](#2添加-renderer-feature)
  - [3.创建流体粒子描述符](#3创建流体粒子描述符)
  - [4.创建粒子生成器](#4创建粒子生成器)
- [🌊 Renderer Feature 设置指南](#-renderer-feature-设置指南)
  - [配置 Rendering Layer](#配置-rendering-layer)
  - [Cover Color 覆盖颜色](#cover-color-覆盖颜色)
  - [Opacity 不透明度](#opacity-不透明度)
  - [Blur 模糊](#blur-模糊)
  - [Distort 扭曲](#distort-扭曲)
  - [Edge 边缘](#edge-边缘)
  - [Pixel 像素化](#pixel-像素化)
- [💧 流体粒子设置指南](#-流体粒子设置指南)
  - [Sprite 贴图](#sprite-贴图)
  - [粒子尺寸与渲染](#粒子尺寸与渲染)
  - [Material 物理材质](#material-物理材质)
  - [Mix Colors 混合颜色](#mix-colors-混合颜色)
- [🧱 场景碰撞与阻挡](#-场景碰撞与阻挡)
  - [Liquid2DCollider 碰撞器](#liquid2dcollider-碰撞器)
- [🤝 两路耦合（流体 ↔ 刚体）](#-两路耦合流体--刚体)
  - [Liquid2DRigidbodyBridge 桥接组件](#liquid2drigidbodybridge-桥接组件)
- [🧲 力场](#-力场)
- [💀 死亡区域与边界](#-死亡区域与边界)
- [⛲ 粒子生成器设置指南](#-粒子生成器设置指南)
  - [控制喷射](#控制喷射)
  - [摆动与路径移动](#摆动与路径移动)
- [🚀 性能优化指南（物理）](#-性能优化指南物理)
  - [Liquid2DPhysicsConfig 组件](#liquid2dphysicsconfig-组件)
  - [选择 CPU / GPU 求解模式](#选择-cpu--gpu-求解模式)
  - [限制粒子数量与关键调参](#限制粒子数量与关键调参)
- [📋 待办事项列表](#-待办事项列表)


## 简介
使用本流体粒子系统，你可以快速实现2D流体的模拟，包括水、岩浆、石油、泡沫、沙等不同质感的流体。  
本系统使用**自研的流体粒子物理求解器**（SPH 双密度），**不再依赖 Unity 的物理系统**。粒子是纯数据，没有每个粒子对应的 GameObject，因此可以高效地模拟大量粒子。  
求解支持 **CPU / GPU 双模式**：CPU 模式基于 Job System + Burst；GPU 模式使用 Compute Shader，数据常驻 GPU，可以**轻松达到数万规模的粒子**并保持高效运行（实测约 2.2 万粒子在编辑器模式下仍保持 100FPS 左右）。  
渲染方面，通过 `Render Graph` 框架，只需一个主相机，并通过 `GPU Instance` 方式渲染流体粒子。与传统的单独相机渲染到 Render Target 的方式相比，渲染效率大幅提升。  
渲染方式类似 SDF 的融合效果，表现出流体的自然效果。  
实际过程中，通过粒子纹理的透明度叠加和裁剪实现粒子融合效果。相比于严格的 SDF 方法，这种方式在性能和效果上达到了更好的平衡，并且不会随着粒子数量的增加而降低性能。  
![](Documents/mix_1.gif)

### 项目特性
| 特性                              | 描述                                                                                         |
| --------------------------------- | -------------------------------------------------------------------------------------------- |
| 自研 SPH 物理                     | 纯数据的 SPH 双密度求解器，不依赖 Unity 物理系统，无每粒子 GameObject，万级粒子依然流畅。          |
| CPU / GPU 双模式                  | CPU 基于 Job System + Burst；GPU 使用 Compute Shader 数据常驻，规模更大。平台不支持时自动回退 CPU。 |
| 丰富的流体材质                    | 可配置粘性、表面张力、摩擦、反弹、重力缩放、浮力密度等，内置水/熔岩/泡沫/沙预设。                   |
| 场景交互                          | 自研碰撞器阻挡流体、两路刚体耦合（冲走 / 浮力漂浮）、力场（吸引 / 排斥 / 旋流）、死亡区域回收。       |
| 多色彩空间混色                    | 不同颜色流体相遇时混色，支持 Oklab / RYB / LinearRgb 三种混色算法。                                |
| URP 2D / Render Graph             | 基于 URP 2D，使用新的 Render Graph 框架进行渲染，性能大幅提升。                                    |
| GPU Instance                      | 使用 GPU Instance 方式渲染粒子，可以一次渲染大量粒子，支持更多粒子数量。                          |
| Volume 运行时修改                  | 支持在运行时通过 Volume 修改流体粒子的渲染效果。                                                |

## 💻 环境要求
- `Unity 6000.2` 或更新的版本
- 2022.3分支支持 `Unity 2022.3` 版本，但是此分支的更新慢于主分支
- URP 2D 渲染管线。Unity 6 版本使用 Render Graph 框架进行渲染
- 与着色器兼容的平台
- GPU 求解模式需要平台支持 `Compute Shader`；不支持时会自动回退到 CPU 模式

### 设备
用于项目开发的设备信息。  
![](Documents/device.png)

## 🌳 分支
- **main** - 主分支，基于 Unity 6 版本。
- **2022.3** - Unity 2022.3 版本分支。如果你需要在更旧的版本上使用此系统，可以查看此分支。更新会慢于主分支。

## 🌱 快速开始
按你喜欢的方式安装插件，然后你可以直接查看演示场景学习如何使用此系统。  
或者，按以下步骤一步步操作。
### 1.安装插件
#### 使用 UPM
```
https://github.com/blurfeng/unity-water-liquid-2d-simulation.git?path=Assets/Plugins/Liquid2DSimulation
```
通过 UPM 安装插件到你的项目。如果你需要演示场景，使用下面的方式导入。
1. 打开 `Window -> Package Manager`。  
![](Documents/qs_1_1.png)

2. 点击左上角的 `+` 号，选择 `Install package from git URL...`。  
![](Documents/qs_1_2.png)

3. 粘贴上面的 URL，点击 `Install` 按钮。  
![](Documents/qs_1_3.png)

4. 等待安装完成后，你会在 `Packages` 里看到 `Liquid2DSimulation` 包。你可以导入 Samples 文件夹来查看演示场景。  
![](Documents/qs_1_4.png)

5. 导入 Samples 文件夹后，你可以在 `Assets/Samples/Liquid 2D Simulation/./Samples` 目录下看到演示场景。  
![](Documents/qs_1_5.png)

#### 下载安装包
使用安装包将插件安装到你的项目中。  
在 [Releases](https://github.com/blurfeng/unity-water-liquid-2d-simulation/releases) 页面下载最新的安装包。  
然后将包导入到你的项目中。

> [!TIP]
> 插件包含了 Samples 文件夹，里面有演示场景。你可以直接从这里开始学习如何使用此系统。  
> 或者按下面的步骤一步步操作来将流体粒子系统添加到你的场景中。  
![](Documents/qs_2_1.png)

### 2.添加 Renderer Feature
演示场景中已经添加好了 Renderer Feature。  
如果你想在自己的场景中使用此系统，你需要在当前的 Renderer 2D Data 中添加 Liquid2D Feature。  
![](Documents/rf_1.png)

### 3.创建流体粒子描述符
流体粒子通过 `Liquid2DParticleDescriptor`（`ScriptableObject`）来定义。  
在 `Project` 窗口中右键，选择 `Create -> Liquid2D -> Particle Descriptor` 创建一个描述符资产。  
![](Documents/lp_1.png)

你需要配置描述符的参数来定义流体粒子的外观与行为：
- `Radius`：粒子物理半径（世界单位），决定粒子间距与邻居搜索范围。
- `RenderScale`：渲染倍率，绘制的可视大小 = `Radius × 2 × RenderScale`，通常远大于物理半径以获得 metaball 融合效果。
- `RenderSettings`：渲染设置，包括 `Sprite` 贴图、`Material` 材质、`Color` 颜色（支持 HDR）、`NameTag` 名称标签。
- `Material`：物理材质，定义粘性、表面张力、摩擦、反弹、重力缩放、浮力密度等（见 [Material 物理材质](#material-物理材质)）。
- `MixSettings`：混色设置（见 [Mix Colors 混合颜色](#mix-colors-混合颜色)）。

在插件的 `./Liquid2DSimulation/Resources/Materials/` 和 `./Liquid2DSimulation/Resources/Textures/` 目录下提供了材质和纹理，你可以直接使用。

### 4.创建粒子生成器
粒子生成器负责在运行时将描述符喷射成流体粒子。  
在场景中创建一个空物体，添加 `Liquid 2D/Gameplay/Liquid 2D Spawner` 组件（即 `Liquid2DSpawner`）。  
你也可以直接使用插件 `./Liquid2DSimulation/Resources/Prefabs/` 目录下的 `Liquid2DSpawner` 预制体（通过 UPM 安装时位于 `Packages/Liquid 2D Simulation/Resources/Prefabs/`），建议从它创建变体再修改参数。  
![](Documents/ls_1.png)

在 `Liquid2DSpawner` 的 `Liquid Particles` 列表中添加一个或多个 `Liquid2DParticleConfig`，每项引用一个**描述符**，并可设置**权重**与**生命周期**。生成时会按权重随机选择一个描述符。  
详见 [粒子生成器设置指南](#-粒子生成器设置指南)。

> [!TIP]
> 到这里为止，流体粒子系统已经可以工作了。  
> 但你通常还需要：用 [Liquid2DCollider 碰撞器](#liquid2dcollider-碰撞器) 阻挡流体流动，并设置渲染的阻挡 / 遮挡层让流体被场景物体正确地遮挡。

## 🌊 Renderer Feature 设置指南
`Liquid2DFeature` Renderer Feature 用于渲染流体粒子，并最终实现流体效果。  
以下主要讲解重要的特性或参数，更详细的参数可以直接查看 Inspector 面板的 Tooltip。

### 配置 Rendering Layer
在 Liquid Feature 中使用了 Rendering Layer 来区分指定可以阻挡或遮挡流体粒子的物体。
#### 添加阻挡层 Rendering Layer
1. 打开 `Edit -> Project Settings -> Tags and Layers`。
2. 在 `Rendering Layers` 中添加一个新的层，比如 `LiquidObstructor`。
![](Documents/rl_4.png)
3. 在你的阻挡物的 Sprite Renderer 组件中，找到 `Additional Settings -> Rendering Layer Mask`，并选择你刚刚创建的 `LiquidObstructor` 层。
4. 在 Liquid2DRenderer2D 的 Liquid2DFeature 上，找到 `Obstructor Rendering Layer Mask`，并选择你刚刚创建的 `LiquidObstructor` 层。

> [!TIP]
> 在 GitHub 的项目中，我配置好了正确的 Rendering Layer Mask。  
> 但你将插件导入你的项目时，项目中并不存在这些 Rendering Layer。  
> 但是在演示场景中，你会发现阻挡物很好地阻挡了流体粒子。这是因为原本已经配置了正确的 Rendering Layer Mask。  
> 因为引擎的缓存和机制，它们依旧能够正常工作。但是在你的项目中，这些 Rendering Layer 实际上并不存在。  
> 在演示场景的 Liquid2DRenderer2D 的 Liquid2DFeature 上，Obstructor Rendering Layer Mask 配置显示为 `Unnamed Layer 1`。  
> ![](Documents/rl_2.png)

> [!IMPORTANT]
> 这里的 Rendering Layer 只影响**渲染层面的遮挡顺序**（流体画在物体前面还是后面），**并不会真正阻挡流体的流动**。  
> 真正在物理上阻挡流体流动，是靠 [Liquid2DCollider 碰撞器](#liquid2dcollider-碰撞器)。两者通常需要配合使用：用碰撞器挡住流体，用 Rendering Layer 让物体正确地遮挡或被流体覆盖。

#### 阻挡
`阻挡`指的是在渲染上可以挡住流体粒子的物体，比如挡板、管道、容器、地形等。  
你需要在 `Renderer Feature` 的 `ObstructorRenderingLayerMask` 中配置用于阻挡的层级。  
![](Documents/rl_5.png)

然后在场景中所有需要阻挡流体粒子的物体的 `Sprite Renderer` 组件的 `Additional Settings` 中配置 `Rendering Layer Mask`。  
![](Documents/rl_3.png)

否则，你会发现流体粒子会覆盖在这些对象的上面。  
![](Documents/rl_1.png)

因为流体粒子的渲染顺序是 `RenderPassEvent.AfterRenderingTransparents`，在透明物体渲染之后。  
所以如果你没有正确配置阻挡层，流体粒子会渲染在不透明和透明物体的上面。

#### 遮挡
`遮挡`指的是可以覆盖在流体粒子上面，但不会阻挡流体粒子流动的物体，比如玻璃瓶的正面、地形的正面等。  
遮挡的配置流程和阻挡类似。  
你需要在 `Renderer Feature` 的 `OccluderRenderingLayerMask` 中配置用于遮挡的层级。  
然后在场景中所有需要遮挡流体粒子的物体的 `Sprite Renderer` 组件的 `Additional Settings` 中配置 `Rendering Layer Mask`。  
但遮挡的物体不会阻挡流体粒子流动（通过物理设置），覆盖在流体粒子之上。  
![](Documents/occ_1.gif)

### Cover Color 覆盖颜色
如果你设置了 `Cover Color`，并且这个颜色的透明度为1（这里透明度代表覆盖强度），那么这个颜色将完全覆盖原有的颜色。

### Opacity 不透明度
通过设置 `Opacity Mode` 和 `Opacity Value` 参数，你可以控制流体的整体不透明度。  
Default 模式不会改变粒子本身的透明度。模糊后看起来内部颜色更加不透明，边缘会更加透明。  
Multiply 模式会将不透明度和粒子本身的透明度相乘。  
Replace 模式会将不透明度直接应用到粒子上。这也会覆盖粒子本身的透明度以及模糊后的透明度。  
使用覆盖颜色和不透明度设置，你可以得到均匀的流体颜色。  
![](Documents/coverColorAndOpacity_1.png)

### Blur 模糊
模糊效果可以让粒子间的融合更自然。  
如果你的粒子贴图本身融合效果已经很好，可以关闭模糊来提升性能。  
模糊的迭代次数和偏移量决定模糊的强度。更多的迭代次数和较小的偏移量可以获得更好的模糊效果。  
由于使用模糊的方式而不是 SDF 方式，所以粒子数量不会对性能产生影响。  
![](Documents/blur_1.gif)
#### 关于模糊和背景
因为模糊的原理是对像素进行采样并混合，所以背景的颜色会影响模糊的效果，最终让流体的边缘看起来接近背景颜色。  
![](Documents/blur_2.png)

可以通过算法减弱这种情况但无法完全消除。因此，提供了 `blurBgColor` 和 `blurBgColorIntensity` 参数来调节边缘的颜色。  
建议使用和粒子整体接近的颜色作为边缘颜色，强度设置为0.5-0.8之间，这样能获得更自然的效果。  
你也可以打开 `ignoreBgColor` 参数来忽略背景颜色的影响（实际上无法完全忽略背景颜色），这会增加一些性能开销。  
如果你设置了 `Cover Color` 并完全覆盖了背景色，就不会有流体粒子边缘融合背景色的情况了，因为粒子本身的颜色和模糊产生的颜色将被完全覆盖。

### Distort 扭曲
通过扭曲效果模拟流体的折射。如果流体是透明的，可以看到背后的画面被扭曲的效果。  
如果流体是不透明的，可以关闭此效果提升性能。  
![](Documents/distort_1.gif)

### Edge 边缘
通过 `Edge Intensity` 和 `Edge Color` 参数，你可以控制流体边缘的颜色和宽度。  
这能够强调流体的边缘，使其更明显，模拟菲涅尔的边缘效果，或者制作发光的边缘。  
![](Documents/edge_1.gif)

### Pixel 像素化
通过开启像素化效果，你可以让流体粒子呈现像素化的效果。  
这可以使流体适用于像素风格的游戏。  
![](Documents/pixel_1.gif)
#### PixelBg 像素化背景
像素化背景可以让流体覆盖的区域也呈现像素化效果，使风格更统一。


## 💧 流体粒子设置指南
`Liquid2DParticleDescriptor` 描述符定义了一类流体粒子的全部属性，是组成流体的基本单元。  
以下主要讲解重要的特性或参数，更详细的参数可以直接查看 Inspector 面板的 Tooltip。

### Sprite 贴图
为流体粒子配置合适的 Sprite 非常重要，它决定了流体粒子的融合效果，最终决定流体整体的视觉效果。  
贴图的重点在于对透明度的设计。推荐使用类似 SDF 效果的贴图，中心透明度高，边缘透明度低。  
如果你想要更柔和的边缘，可以使用高斯模糊处理贴图。如果你想要更锐利的边缘，可以使用硬边的贴图。
#### 贴图透明度
粒子间透明度的叠加和裁剪是实现粒子融合的关键。  
与模糊效果相配合，可以实现更自然的流体效果。当然如果贴图本身的融合已经很好，也可以关闭模糊效果来提升性能。  
需要注意的是，粒子贴图并不在乎透明范围是否超出了贴图的边界，超出反而能让粒子更早地融合。  
假设你使用一个圆形的贴图，贴图的边界是透明的，那么粒子在接触时会有一段距离才开始融合。  
但假设贴图边界是 0.6 的透明度，那么粒子在接触时就会开始融合。  
![](Documents/sp_1.png)

### 粒子尺寸与渲染
在纯数据模型中，流体粒子**不再使用 Unity 碰撞器**。粒子的尺寸由描述符上的两个参数决定：
- `Radius`：**物理半径**（世界单位）。它决定粒子间的物理间距、邻居搜索范围与堆积密度，是 SPH 求解的基本尺度。
- `RenderScale`：**渲染倍率**。绘制的可视直径 = `Radius × 2 × RenderScale`。

通常 `RenderScale` 会设置得远大于物理半径（默认 4），让相邻粒子的贴图大幅重叠，从而获得平滑的 metaball 融合效果。  
如果可视尺寸太小，你会看到粒子间的缝隙；太大则流体会显得过于「黏糊」。配合 Sprite 贴图的透明度设计来调整融合手感。  
![](Documents/co_1.png)

### Material 物理材质
描述符上的 `Material`（`Liquid2DParticleMaterial`）定义了这一类粒子的物理表现。通过差异化参数，可以模拟水、熔岩、泡沫、沙等不同介质。  
重点参数（更多细节见 Inspector 的 Tooltip）：
- `Mass`：质量。影响受力响应与喷射初速（初速 = 冲量 / 质量）。
- `Viscosity`：粘性。越大越粘稠、流动越缓。水低，熔岩高。
- `Cohesion`：内聚力 / 表面张力。越大越易结团、成液滴。泡沫高，沙为 0。
- `Friction`：摩擦。与碰撞体接触时的切向阻尼，沙用较高值堆出休止角。
- `Restitution`：反弹系数。与碰撞体碰撞时的法向反弹强度。
- `GravityScale`：重力缩放。1 = 正常下落，0 = 悬浮，**负值 = 上浮**（适合泡沫）。
- `Density`：流体质量密度，**专用于阿基米德浮力对比**（物体密度小于此值则漂浮）。与堆积密度相互独立。
- `TargetDensityScale` / `NearPressureMultiplierScale`：分别缩放全局的目标密度与近压力，用于微调单类粒子的堆积松紧与短距离斥力。

材质内置了 **Water（水）/ Lava（熔岩）/ Foam（泡沫）/ Sand（沙）** 几种预设，可作为起点再微调：水低粘低张力；熔岩高粘高质量高密度；泡沫高张力、低重力甚至上浮；沙高摩擦、零张力。  
![](Documents/pm_1.png)

### Mix Colors 混合颜色
通过启用描述符 `MixSettings` 中的 `Mix Colors`，你可以让不同颜色的流体粒子间的颜色进行混合。  
两个粒子都需要开启 `Mix Colors`，才会在相遇时混合，并改变自身的颜色。  
![](Documents/mc_1.gif)

根据配置，你可以控制混合的速度等行为：
- `MixColorsSpeed`：混合速度（0 = 不混合，1 = 瞬间混合）。
- `MixColorsWithMovement`：是否根据粒子运动加速混色（运动越快混色越快）。
- `MixColorsWhenStationary`：静止时是否混色。默认关闭，使静止粒子跳过混色以节省开销；需要静止也混色时手动开启。

![](Documents/mc_2.png)

> 混色由**求解器内部的邻居查询**完成（即复用 SPH 已经构建的空间结构在相邻粒子间混色），**不再依赖 Unity 物理引擎的接触回调**，因此也不需要再开启 `Reuse Collision Callbacks`。  
> 混色使用的色彩算法由 `Liquid2DPhysicsConfig` 的 `ColorMixMode` 全局控制，支持三种：
> - `Oklab`（默认）：感知均匀色彩空间混合，颜色过渡自然。
> - `Ryb`：RYB 颜料色轮混合（蓝 + 黄 = 绿），模拟真实颜料。
> - `LinearRgb`：线性 RGB 算术平均（蓝 + 黄 = 灰）。

## 🧱 场景碰撞与阻挡
新系统使用**自研的碰撞器**让场景物体阻挡流体流动，取代了原来的 Unity `Collider2D`。

### Liquid2DCollider 碰撞器
在挡板、管道、容器、地形等需要阻挡流体的物体上，添加对应的碰撞器组件即可（菜单 `Liquid 2D/Colliders/...`）：
- `Liquid2DBoxCollider`：矩形盒（支持旋转 OBB），字段 `size`。
- `Liquid2DCircleCollider`：圆形，字段 `radius`。
- `Liquid2DCapsuleCollider`：胶囊，字段 `length`、`radius`。
- `Liquid2DPolygonCollider`：多边形，字段 `points`、`closed`。`closed = true` 为实心凸多边形；`closed = false` 为双面薄壁折线，适合复杂地形。

碰撞器尺寸会受物体的缩放影响，移动 / 旋转物体时碰撞器会跟随。  
此外还提供了 `Liquid2DEdgeCollider` / `Liquid2DCustomCollider` / `Liquid2DMeshCollider` 等，可桥接 Unity 的 `EdgeCollider2D` / `CustomCollider2D` 等已有碰撞器形状。  
![](Documents/collider_1.png)

> [!TIP]
> 每个碰撞器都有一个可选的 `nameTag`：留空时作用于**全部**粒子；填写后**只阻挡匹配该标签的粒子组**。  
> 这让你可以做出「某些流体能穿过某些物体」之类的效果。

## 🤝 两路耦合（流体 ↔ 刚体）
流体不仅能被场景物体阻挡，还能反过来推动场景中的 `Rigidbody2D`，实现冲走、漂浮等双向交互效果。

### Liquid2DRigidbodyBridge 桥接组件
在一个带有 `Rigidbody2D` 的物体上，挂上 `Liquid2DCollider`（任意形状）和 `Liquid2DRigidbodyBridge` 组件，这个碰撞器就会成为**动态碰撞器**，把流体施加的反作用力桥接到刚体上。  
![](Documents/coupling_1.gif)

主要参数：
- **冲走（相对速度阻力）**
  - `dragCoefficient`：阻力系数，越大越容易被流体冲走。流体静止时该力≈0，不会无端推动物体。
  - `applyTorque`：在接触质心处施力以产生力矩（使物体随流体翻滚）。
  - `maxSpeedChange`：单步速度变化上限，防止瞬间被加速过猛。
- **浮力（阿基米德原理）**
  - `useBuoyancy`：启用浮力。
  - `bodyVolume`：物体体积（2D 面积），为 0 时会自动根据子碰撞器形状计算。
  - `fullSubmersionContacts`：视为「完全浸没」所需的接触粒子数。
  - `submergedLinearDrag` / `submergedAngularDrag`：浸没时的线性 / 角阻尼。

> [!TIP]
> 浮力只统计物体**下方**接触的流体粒子，避免顶部流体把轻质物体「顶飞」。  
> 物体密度（`Rigidbody2D` 的质量 / 体积）与流体材质的 `Density` 对比决定漂浮还是下沉。

## 🧲 力场
力场可以对范围内的流体粒子施加额外的力，用于制作吸引器、排斥器、漩涡、抽水口等效果。  
添加 `Liquid 2D/Physics/Force Fields/Liquid 2D Radial Force Field` 组件（`Liquid2DRadialForceField`），以物体位置为中心持续作用。  
![](Documents/forcefield_1.gif)

主要参数：
- `radius`：力场作用半径。
- `strength`：力场强度（> 0 吸引，< 0 排斥）。
- `swirlStrength`：切向旋流强度（> 0 逆时针，< 0 顺时针）。
- `velocityDamping`：范围内速度衰减系数。
- `gravityAttenuation`：力场内重力衰减（让粒子更容易被力场主导）。
- `falloff` / `forceMode`：力随距离的衰减指数与模式（`Falloff` / `Constant`）。
- `nameTag`：留空作用全部粒子，填写则只作用匹配的粒子组。

> [!TIP]
> 插件还提供了 `Liquid2DMouseInteractor`，它是力场的一个子类，可以用鼠标实时吸引 / 排斥流体，方便调试与互动。

## 💀 死亡区域与边界
用 `Liquid 2D/Gameplay/Liquid 2D Dead Zone` 组件（`Liquid2DDeadZone`）在场景中划定一块区域来**回收粒子**，取代旧的 Unity 物理触发器方案，避免流出屏幕的粒子无限堆积。  
主要参数：
- `shape`：区域形状（Box / Circle / Capsule / Polygon），配合 `size` / `radius` / `length` / `points`。
- `boundsMode`：`false` 销毁**进入**区域的粒子；`true` 销毁**离开**区域（区域外）的粒子，可当作活动边界使用。
- `nameTag`：只回收匹配标签的粒子组。

此外还提供了 `Liquid2DBounds` 碰撞器，可用作把流体限制在某个范围内的封闭边界。

## ⛲ 粒子生成器设置指南
`Liquid2DSpawner` 粒子生成器用于生成流体粒子，类似一个水管或喷泉。  
以下主要讲解重要的特性或参数，更详细的参数可以直接查看 Inspector 面板的 Tooltip。

### 控制喷射
通过参数你可以控制喷射粒子的流量和力度：
- `flowRate` / `flowRateFactor`：流量（粒子 / 秒）及其调整系数。
- `ejectForce` / `ejectForceFactor` / `ejectForceRandomRange`：喷射力大小、系数与随机范围。
- `nozzleWidth`：喷嘴宽度，批量生成时会在宽度内分层均匀分布。
- `sizeRandomRange`：粒子尺寸随机范围。

#### 生成粒子描述符列表
你可以在 `Liquid Particles` 中配置多个 `Liquid2DParticleConfig`，每项包含一个**描述符**、**权重**和可选的**生命周期**覆盖。每次生成粒子时会根据权重随机选择一个描述符。  
![](Documents/ls_2.png)

这允许你使用更多不同的粒子来模拟更复杂的流体，比如岩浆。它们通常是不均匀的红色、橙色和黄色的混合粒子。  
![](Documents/ls_3.gif)

关于岩浆的配置技巧，这里我将 `Cutoff` 设置为 0.14，这样流体会保留更多透明度低的部分。  
然后启用 `Distort` 效果。这样你会看到岩浆流体边缘接近透明的部分背景被折射扭曲，类似热气腾腾的效果。

### 摆动与路径移动
为了模拟移动的水管 / 喷泉，生成器支持喷口摆动和沿路径移动：
- `swingAngleRange` / `swingSpeed`：喷射方向的摆动角度范围与速度。
- `moveEnabled` / `moveMode`（`Once` / `Loop` / `PingPong`）/ `moveSpeed` / `waypoints`：让生成器沿一组路径点移动。


## 🚀 性能优化指南（物理）
以下措施可帮助你在不同规模下获得最佳性能。

### Liquid2DPhysicsConfig 组件
`Liquid2DPhysicsConfig` 组件（菜单 `Liquid 2D/Systems/Liquid 2D Physics Config`）集中管理求解器的全局参数、求解模式、粒子上限、物理步长等，并在 `Awake` 时自动应用。将它挂载到场景中任意常驻对象上即可。  
更详细的参数说明见各字段的 Inspector Tooltip。

### 选择 CPU / GPU 求解模式
组件的 `Mode` 字段决定求解平台。**除非有明确理由，建议始终使用 GPU 模式。**
- **GPU 模式（默认，推荐）**：Compute Shader 驱动，粒子数据常驻 GPU，性能远优于 CPU 模式，可轻松达到数万规模粒子。平台不支持 Compute Shader 时会自动回退到 CPU 模式。
- **CPU 模式**：Job System + Burst 驱动，适合确实无法使用 Compute Shader 的平台，或需要可靠读取 CPU 端粒子数据的调试场景。

> [!WARNING]
> GPU 模式下，粒子数据常驻 GPU，**CPU 端的数据默认是陈旧的**，因此 `GetPosition` / `GetVelocity` / 调试 Gizmos 等基于 CPU 数据的功能不可靠。  
> 如确需在 GPU 模式下读取这些数据，可开启 `GpuReadbackToStore`，但这会带来**同步的 GPU→CPU 停顿**，开销较大，**生产环境请保持关闭**。  
> 渲染（Renderer Feature）直接读取 GPU 缓冲，不受此影响。

### 粒子数量与关键调参
- **`MaxParticlesPerTag`（每 nameTag 存活上限）**：GPU 模式下粒子数量几乎没有上限（仅受显存约束）；只有在目标机型上测试时感到性能不足，才需要考虑通过此参数限制存活数量（0 表示不限制）。超出上限时自动回收该组下最旧的粒子。建议配合 [死亡区域](#-死亡区域与边界) 回收逃逸粒子来保持场景干净。
- **`Substeps`（子步进数）**：每个固定物理步细分的子步数，越大越稳定（高速 / 高压力下不易穿透或爆炸），但开销也越大。
- **`MaxSpeed`（最大速度）**：限制粒子速度上限，防止压力爆炸导致速度失控（0 = 不限制，建议为喷射力的 3–5 倍）。
- **`SmoothingRadius (H)`、`TargetDensity`、`PressureMultiplier`、`NearPressureMultiplier`、`ViscosityStrength` 等全局 SPH 参数**：决定流体的整体可压缩性、弹性与粘稠度，影响稳定性与开销，细节见 Tooltip。
- **`overrideFixedTimestep` / `fixedTimestep`**：可统一覆盖物理频率（`Time.fixedDeltaTime`）。降低物理频率能减少每秒求解次数，但会降低物理平滑度与高速稳定性。


## 📋 待办事项列表
- **物理系统**
  - 流体粒子间更丰富的相互作用：进一步模拟粘性、张力、温度等，表现石油、蜂蜜、烟雾等更多介质。
  - 更多力场与交互类型（方向力场、风、爆炸冲击等）。
- **渲染 / 工具**
  - GPU 模式下与 CPU 模式在混色等细节上的进一步对齐。
  - 更完善的编辑器调试与可视化工具。
