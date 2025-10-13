![](Documents/samples_1.gif)

<p align="center">
  <img alt="GitHub Release" src="https://img.shields.io/github/v/release/blurfeng/unity-water-liquid-2d-simulation?color=blue">
  <img alt="GitHub Downloads (all assets, all releases)" src="https://img.shields.io/github/downloads/blurfeng/unity-water-liquid-2d-simulation/total?color=green">
  <img alt="GitHub Repo License" src="https://img.shields.io/github/license/blurfeng/unity-water-liquid-2d-simulation?color=blueviolet">
  <img alt="GitHub Repo Issues" src="https://img.shields.io/github/issues/blurfeng/unity-water-liquid-2d-simulation?color=yellow">
</p>

<p align="center">
  🌍
  <a href="./README.md">中文</a> |
  English |
  <a href="./README_JA.md">日本語</a>
</p>

<p align="center">
  📥
  <a href="#using-upm">Install</a> |
  <a href="#download-package">Download</a>
</p>

# Liquid 2D Simulation
Liquid 2D Simulation is a 2D fluid simulation system designed for `Unity`. \
Ready to use out of the box, it enables you to quickly achieve realistic physical fluid effects. \
With its extensive configuration options, you can freely create fluids with a variety of textures and behaviors.

## 📜 Table of Contents
- [Introduction](#introduction)
  - [Project Features](#project-features)
- [💻 System Requirements](#-system-requirements)
- [🌳 Branches](#-branches)
- [🌱 Quick Start](#-quick-start)
  - [1. Install Plugin](#1-install-plugin)
  - [2. Add Renderer Feature](#2-add-renderer-feature)
  - [3. Create Fluid Particle Prefab](#3-create-fluid-particle-prefab)
  - [4. Create Particle Spawner](#4-create-particle-spawner)
- [🌊 Renderer Feature Settings Guide](#-renderer-feature-settings-guide)
  - [Configure Rendering Layer](#configure-rendering-layer)
  - [Cover Color](#cover-color)
  - [Opacity](#opacity)
  - [Blur](#blur)
  - [Distort](#distort)
  - [Edge](#edge)
  - [Pixel](#pixel)
- [💧 Fluid Particle Settings Guide](#-fluid-particle-settings-guide)
  - [Sprite Texture](#sprite-texture)
  - [Collider](#collider)
  - [Rigidbody 2D](#rigidbody-2d)
  - [Mix Colors](#mix-colors)
- [⛲ Particle Spawner Settings Guide](#-particle-spawner-settings-guide)
  - [Control Spawning](#control-spawning)
- [📋 Todo List](#-todo-list)


## Introduction
Using this fluid particle system, you can quickly implement 2D fluid simulation, including water, magma, oil and other fluids with different textures.\
The project simulates 2D fluid effects through fluid particles and supports efficiently generating large numbers of particles, suitable for mobile platforms. However, too many rigidbodies remain a performance issue.\
By using the `Render Graph` framework, only one main camera is needed, and fluid particles are rendered using `GPU Instance`.\
Compared to traditional methods of rendering to Render Target with separate cameras, rendering efficiency is greatly improved.\
The rendering method is similar to SDF fusion effects, showing natural fluid effects.\
In the actual process, particle fusion effects are achieved through alpha blending and clipping of particle textures. Compared to strict SDF methods, this approach achieves a better balance between performance and effects, and performance doesn't decrease as particle count increases.\
![](Documents/mix_1.gif)

### Project Features
| Feature                             | Description                                                                                          |
| ----------------------------------- | ---------------------------------------------------------------------------------------------------- |
| URP 2D                              | Project based on URP2D.                                                                              |
| Render Graph                        | Uses the new Render Graph framework for rendering with significantly improved performance.           |
| GPU Instance                        | Uses GPU Instance to render particles, can render many particles at once, supporting more particles. |
| Runtime Volume Modification         | Supports modifying fluid particle rendering effects through Volume at runtime.                       |
| Physical Particle Simulation        | Simulates physical effects of fluid particles through rigidbodies for more natural performance.      |
| Particle Physics Calculation ⚠️TODO | A high-performance particle physics algorithm that can smoothly handle thousands of particles.       |


## 💻 System Requirements
- `Unity 6000.2` or newer version
- The 2022.3 branch supports `Unity 2022.3` version, but this branch updates slower than the main branch
- URP 2D rendering pipeline. Unity 6 version uses Render Graph framework for rendering
- Platforms compatible with shaders


## 🌳 Branches
- **main** - Main branch, based on Unity 6.
- **2022.3** - Unity 2022.3 version branch. If you need to use this system on older versions, check this branch. Updates will be slower than the main branch.


## 🌱 Quick Start
Install the plugin in your preferred way, then you can directly check the demo scene to learn how to use this system.\
Or follow the steps below step by step.
### 1. Install Plugin
#### Using UPM
```
https://github.com/blurfeng/unity-water-liquid-2d-simulation.git?path=Assets/Plugins/Liquid2DSimulation
```
Install the plugin to your project through UPM. If you need demo scenes, import using the method below.
1. Open `Window -> Package Manager`.\
![](Documents/qs_1_1.png)

2. Click the `+` in the top left corner and select `Install package from git URL...`.\
![](Documents/qs_1_2.png)

3. Paste the URL above and click the `Install` button.\
![](Documents/qs_1_3.png)

4. After installation completes, you'll see the `Liquid2DSimulation` package in `Packages`. You can import the Samples folder to view demo scenes.\
![](Documents/qs_1_4.png)

5. After importing the Samples folder, you can see demo scenes in the `Assets/Samples/Liquid 2D Simulation/./Samples` directory.\
![](Documents/qs_1_5.png)

#### Download Package
Install the plugin to your project using the installation package.\
Download the latest installation package from the [Releases](https://github.com/blurfeng/unity-water-liquid-2d-simulation/releases) page.\
Then import the package into your project.

> [!TIP]
> The plugin includes a Samples folder with demo scenes. You can start learning how to use this system directly from here.\
> Or follow the steps below step by step to add the fluid particle system to your scene.\
![](Documents/qs_2_1.png)

### 2. Add Renderer Feature
The demo scene already has the Renderer Feature added.\
If you want to use this system in your own scene, you need to add Liquid2D Feature to the current Renderer 2D Data.\
![](Documents/rf_1.png)

### 3. Create Fluid Particle Prefab
You can find the fluid particle prefab `Liquid2DParticle` in the `./Liquid2DSimulation/Runtime/Resources/Prefabs/` directory.\
It's recommended to create a variant prefab from this prefab, then modify materials and parameters to create the fluid particles you want.\
You can also directly create your own fluid particle prefab and add the `Liquid2DParticle` component, `Circle Collider 2D` component, and `Rigidbody 2D` component.\
![](Documents/lp_1.png)

You need to configure the parameters of the `Liquid2DParticle` component to adjust the behavior of fluid particles. Including Sprite texture, material, color, and fluid layer, etc.\
Materials and textures are provided in the `./Liquid2DSimulation/Runtime/Resources/Materials/` and `./Liquid2DSimulation/Runtime/Resources/Textures` directories of the plugin, which you can use directly.

### 4. Create Particle Spawner
You can find the particle spawner prefab `LiquidSpawner` in the `./Liquid2DSimulation/Runtime/Resources/Prefabs/` directory.\
It's recommended to create a variant prefab from this prefab, then modify parameters to create the particle spawner you want.\
You can also directly create your own particle spawner prefab and add the `Liquid2DSpawner` component.\
![](Documents/ls_1.png)

> [!TIP]
> Up to this point, the fluid particle system is ready to work.\
> However, you still need to configure obstruction and occlusion layers so that fluid particles are correctly blocked and occluded by objects in the scene.


## 🌊 Renderer Feature Settings Guide
`Liquid2DFeature` Renderer Feature is used to render fluid particles and ultimately achieve fluid effects.\
The following mainly explains important features or parameters. More detailed parameters can be viewed directly in the Inspector panel tooltips.

### Configure Rendering Layer
Liquid Feature uses Rendering Layer to distinguish which objects can block or occlude fluid particles.
#### Add Obstructor Rendering Layer
1. Open `Edit -> Project Settings -> Tags and Layers`.
2. Add a new layer in `Rendering Layers`, such as `LiquidObstructor`.
![](Documents/rl_4.png)
3. In your obstructor's Sprite Renderer component, find `Additional Settings -> Rendering Layer Mask` and select the `LiquidObstructor` layer you just created.
4. In Liquid2DRenderer2D's Liquid2DFeature, find `Obstructor Rendering Layer Mask` and select the `LiquidObstructor` layer you just created.

> [!TIP]
> In the GitHub project, I have configured the correct Rendering Layer Mask.\
> However, when you import the plugin into your project, these Rendering Layers do not exist in the project.\
> But in the demo scene, you'll find that obstructors block fluid particles well. This is because the correct Rendering Layer Mask was originally configured.\
> Due to engine caching and mechanisms, they can still work normally. But in your project, these Rendering Layers don't actually exist.\
> In the demo scene's Liquid2DRenderer2D's Liquid2DFeature, the Obstructor Rendering Layer Mask configuration shows as `Unnamed Layer 1`.\
> ![](Documents/rl_2.png)

#### Obstruction
`Obstruction` refers to objects that can hinder fluid particle flow, such as barriers, pipes, containers, terrain, etc.\
You need to configure the layers used for obstruction in the `Renderer Feature`'s `ObstructorRenderingLayerMask`.\
![](Documents/rl_5.png)

Then configure `Rendering Layer Mask` in the `Additional Settings` of the `Sprite Renderer` component for all objects in the scene that need to obstruct fluid particles.\
![](Documents/rl_3.png)

Otherwise, you'll find that fluid particles will overlay on top of these objects.\
![](Documents/rl_1.png)

Because the rendering order of fluid particles is `RenderPassEvent.AfterRenderingTransparents`, after transparent objects are rendered.\
So if you don't configure the obstruction layer correctly, fluid particles will render on top of opaque and transparent objects.

#### Occlusion
`Occlusion` refers to objects that can cover fluid particles but don't block fluid particle flow, such as the front of glass bottles, the front of terrain, etc.\
The configuration process for occlusion is similar to obstruction.\
You need to configure the layers used for occlusion in the `Renderer Feature`'s `OccluderRenderingLayerMask`.\
Then configure `Rendering Layer Mask` in the `Additional Settings` of the `Sprite Renderer` component for all objects in the scene that need to occlude fluid particles.\
But occluding objects don't block fluid particle flow (through physics settings), they cover over the fluid particles.\
![](Documents/occ_1.gif)

### Cover Color
If you set `Cover Color` and this color's alpha is 1 (here alpha represents coverage intensity), then this color will completely cover the original color.

### Opacity
By setting `Opacity Mode` and `Opacity Value` parameters, you can control the overall opacity of the fluid.\
Default mode doesn't change the particle's own opacity. After blurring, the internal color looks more opaque and edges become more transparent.\
Multiply mode multiplies the opacity with the particle's own opacity.\
Replace mode directly applies the opacity to particles. This also overrides the particle's own opacity and post-blur opacity.\
Using cover color and opacity settings, you can get uniform fluid color.\
![](Documents/coverColorAndOpacity_1.png)

### Blur
Blur effects make particle fusion more natural.\
If your particle texture already has good fusion effects, you can turn off blur to improve performance.\
The blur iteration count and offset determine blur intensity. More iterations and smaller offsets can achieve better blur effects.\
Since blur is used instead of SDF, particle count doesn't affect performance.\
![](Documents/blur_1.gif)
#### About Blur and Background
Because blur works by sampling and blending pixels, the background color affects the blur effect, making fluid edges look close to the background color.\
![](Documents/blur_2.png)

Algorithms can reduce this situation but can't completely eliminate it. Therefore, `blurBgColor` and `blurBgColorIntensity` parameters are provided to adjust edge color.\
It's recommended to use colors close to the particle's overall color as edge color, with intensity set between 0.5-0.8 for more natural effects.\
You can also enable the `ignoreBgColor` parameter to ignore background color influence (actually can't completely ignore background color), which adds some performance overhead.\
If you set `Cover Color` and completely cover the background color, there won't be fluid particle edge blending with background color, because the particle's own color and blur-generated color will be completely covered.

### Distort
Simulates fluid refraction through distortion effects. If the fluid is transparent, you can see the distorted effect of the background.\
If the fluid is opaque, you can turn off this effect to improve performance.\
![](Documents/distort_1.gif)

### Edge
Through `Edge Intensity` and `Edge Color` parameters, you can control the color and width of fluid edges.\
This can emphasize fluid edges, make them more obvious, simulate Fresnel edge effects, or create glowing edges.\
![](Documents/edge_1.gif)

### Pixel
By enabling the pixelation effect, you can make fluid particles show pixelated effects.\
This can make fluids suitable for pixel-style games.\
![](Documents/pixel_1.gif)
#### PixelBg
Pixelated background can make the area covered by fluid also show pixelated effects, making the style more unified.


## 💧 Fluid Particle Settings Guide
`Liquid2DParticle` Fluid particles are the basic units that compose fluids.\
The following mainly explains important features or parameters. More detailed parameters can be viewed directly in the Inspector panel tooltips.

### Sprite Texture
Configuring appropriate Sprite for fluid particles is very important, it determines the fusion effect of fluid particles and ultimately determines the overall visual effect of the fluid.\
The key to the texture is the alpha design. It's recommended to use textures with SDF-like effects, high alpha in the center and low alpha at edges.\
If you want softer edges, you can use Gaussian blur to process textures. If you want sharper edges, you can use hard-edge textures.
#### Texture Alpha
Alpha blending and clipping between particles is the key to achieving particle fusion.\
Combined with blur effects, more natural fluid effects can be achieved. Of course, if the texture's own fusion is already good, you can also turn off blur effects to improve performance.\
Note that particle textures don't care if the transparent range exceeds the texture boundaries - exceeding actually allows particles to fuse earlier.\
Suppose you use a circular texture where the texture boundary is transparent, then particles will have a distance before they start fusing when they touch.\
But suppose the texture boundary has 0.6 alpha, then particles will start fusing when they touch.\
![](Documents/sp_1.png)

### Collider
Colliders and rigidbodies are key to achieving physical effects of fluid particles. It's recommended to use circular colliders, which better represent the physical effects of fluid particles.\
Generally, the collider size needs to be smaller than the texture size, so that fluid particles can fuse better visually, otherwise you'll see gaps between particles.\
![](Documents/co_1.png)

### Rigidbody 2D
Uses Unity's physics system to simulate physical effects of fluid particles.\
The rigidbody's mass, linear drag, gravity scale and other parameters all affect the behavior of fluid particles.\
You can adjust these parameters to simulate different types of fluids, such as water, magma, oil, etc.\
Physics materials also affect the behavior of fluid particles. By adjusting friction and bounciness parameters, different fluid effects can be simulated.\
![](Documents/pm_1.png)
![](Documents/pm_2.png)

### Mix Colors
By enabling `Mix Colors`, you can make colors mix between different colored fluid particles.\
Both particles need to have `Mix Colors` enabled to mix when they come into contact and change their own colors.\
![](Documents/mc_1.gif)

Based on configuration, you can control the speed and range of mixing.\
![](Documents/mc_2.png)

## ⛲ Particle Spawner Settings Guide
`LiquidSpawner` is used to generate fluid particles, like a pipe or fountain.\
The following mainly explains important features or parameters. More detailed parameters can be viewed directly in the Inspector panel tooltips.

### Control Spawning
Through parameters you can control the flow rate and intensity of spawned particles.
#### Spawn Particle Prefab List
You can configure multiple particle prefabs. Each time a particle is generated, a prefab is randomly selected based on weight.\
![](Documents/ls_2.png)

This allows you to use more different particles to simulate more complex fluids, such as magma. They are usually non-uniform mixtures of red, orange and yellow particles.\
![](Documents/ls_3.gif)

For magma configuration tips, I set the `Cutoff` to 0.14 here, so the fluid retains more parts with low transparency.\
Then enable the `Distort` effect. This way you'll see the background near the transparent parts of the magma fluid edges being refracted and distorted, creating an effect similar to rising heat.


## 📋 Todo List
- **Physics System**
  - Optimization: When generating a large number of particles, Unity's physics system can quickly become a performance bottleneck. It is necessary to optimize the physics system, calculate fluid physics manually, or use the DOTS framework to improve performance.
  - Inter-fluid particle physical interactions: Through simulating physical effects like viscosity and tension, represent different types of fluids, such as oil, honey, foam, etc.
