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
Liquid 2D Simulation is a 2D fluid simulation system for `Unity`. It works out of the box and lets you quickly achieve realistic fluid effects.\
It is powered by a **custom-built fluid particle physics system** (SPH dual-density solver) and **does not rely on Unity's physics system**; with GPU mode it can **easily reach tens of thousands of particles** while staying highly efficient.\
With its rich set of configuration parameters, you can freely create water, lava, oil, foam, sand, and many other fluids with different textures and looks.

## 🙏 Acknowledgements
The core algorithm of the fluid physics solver mainly references [SebLague/Fluid-Sim](https://github.com/SebLague/Fluid-Sim). Thanks to SebLague.

## 📜 Table of Contents
- [Introduction](#introduction)
  - [Features](#features)
- [💻 Requirements](#-requirements)
- [🌳 Branches](#-branches)
- [🌱 Quick Start](#-quick-start)
  - [1. Install the Plugin](#1-install-the-plugin)
  - [2. Add the Renderer Feature](#2-add-the-renderer-feature)
  - [3. Create a Fluid Particle Descriptor](#3-create-a-fluid-particle-descriptor)
  - [4. Create a Particle Spawner](#4-create-a-particle-spawner)
- [🌊 Renderer Feature Setup Guide](#-renderer-feature-setup-guide)
  - [Configure Rendering Layers](#configure-rendering-layers)
  - [Cover Color](#cover-color)
  - [Opacity](#opacity)
  - [Blur](#blur)
  - [Distort](#distort)
  - [Edge](#edge)
  - [Pixel](#pixel)
- [💧 Fluid Particle Setup Guide](#-fluid-particle-setup-guide)
  - [Sprite](#sprite)
  - [Particle Size and Rendering](#particle-size-and-rendering)
  - [Material (Physics)](#material-physics)
  - [Mix Colors](#mix-colors)
- [🧱 Scene Collision and Blocking](#-scene-collision-and-blocking)
  - [Liquid2DCollider](#liquid2dcollider)
- [🤝 Two-Way Coupling (Fluid ↔ Rigidbody)](#-two-way-coupling-fluid--rigidbody)
  - [Liquid2DRigidbodyBridge Component](#liquid2drigidbodybridge-component)
- [🧲 Force Fields](#-force-fields)
- [💀 Dead Zones and Boundaries](#-dead-zones-and-boundaries)
- [⛲ Particle Spawner Setup Guide](#-particle-spawner-setup-guide)
  - [Controlling Ejection](#controlling-ejection)
  - [Swing and Path Movement](#swing-and-path-movement)
- [🚀 Performance Optimization Guide (Physics)](#-performance-optimization-guide-physics)
  - [Liquid2DPhysicsConfig Component](#liquid2dphysicsconfig-component)
  - [Choosing CPU / GPU Solve Mode](#choosing-cpu--gpu-solve-mode)
  - [Particle Count and Key Tuning](#particle-count-and-key-tuning)
- [📋 To-Do List](#-to-do-list)


## Introduction
With this fluid particle system you can quickly simulate 2D fluids, including water, lava, oil, foam, sand, and other media with different textures.\
The system uses a **custom-built fluid particle solver** (SPH dual-density) and **no longer relies on Unity's physics system**. Particles are pure data with no per-particle GameObject, so it can efficiently simulate large numbers of particles.\
Solving supports **both CPU and GPU modes**: CPU mode is based on the Job System + Burst; GPU mode uses Compute Shaders with data resident on the GPU, and can **easily reach tens of thousands of particles** while staying efficient (measured at around 22,000 particles still holding roughly 100 FPS in Editor mode).\
For rendering, the `Render Graph` framework requires only a single main camera and renders the fluid particles via `GPU Instancing`. Compared to the traditional approach of rendering to a separate camera's Render Target, rendering efficiency is greatly improved.\
The rendering approach produces a fusion effect similar to SDF, expressing the natural look of fluids.\
In practice, the particle fusion effect is achieved by stacking and clipping the alpha of the particle textures. Compared to a strict SDF method, this achieves a better balance between performance and visual quality, and performance does not degrade as the particle count grows.\
![](Documents/mix_1.gif)

### Features
| Feature                           | Description                                                                                                   |
| --------------------------------- | ------------------------------------------------------------------------------------------------------------- |
| Custom SPH physics                | A pure-data SPH dual-density solver, independent of Unity physics, with no per-particle GameObject; smooth even at tens of thousands of particles. |
| CPU / GPU dual mode               | CPU is based on the Job System + Burst; GPU uses Compute Shaders with resident data for larger scale. Automatically falls back to CPU when the platform doesn't support it. |
| Rich fluid materials              | Configurable viscosity, surface tension, friction, restitution, gravity scale, buoyancy density, and more, with built-in Water / Lava / Foam / Sand presets. |
| Scene interaction                 | Custom colliders block fluid, two-way rigidbody coupling (wash-away / buoyant float), force fields (attract / repel / swirl), and dead zones for recycling. |
| Multi color-space mixing          | Fluids of different colors mix when they meet, with three mixing algorithms: Oklab / RYB / LinearRgb.         |
| URP 2D / Render Graph             | Built on URP 2D, using the new Render Graph framework for rendering, greatly improving performance.           |
| GPU Instancing                    | Particles are rendered with GPU Instancing, rendering large numbers of particles in one pass and supporting higher particle counts. |
| Runtime tweaking via Volume       | Supports modifying the fluid particles' rendering effects at runtime through Volumes.                         |

## 💻 Requirements
- `Unity 6000.2` or newer
- The 2022.3 branch supports `Unity 2022.3`, but this branch updates more slowly than the main branch
- URP 2D rendering pipeline. Unity 6 uses the Render Graph framework for rendering
- A platform compatible with the shaders
- GPU solve mode requires platform support for `Compute Shaders`; it automatically falls back to CPU mode when unsupported


## 🌳 Branches
- **main** - The main branch, based on Unity 6.
- **2022.3** - The Unity 2022.3 branch. If you need to use this system on an older version, check out this branch. It updates more slowly than the main branch.

## 🌱 Quick Start
Install the plugin in whatever way you prefer, then you can look at the demo scenes to learn how to use the system.\
Or follow the steps below one by one.
### 1. Install the Plugin
#### Using UPM
```
https://github.com/blurfeng/unity-water-liquid-2d-simulation.git?path=Assets/Plugins/Liquid2DSimulation
```
Install the plugin into your project via UPM. If you need the demo scenes, import them as shown below.
1. Open `Window -> Package Manager`.\
![](Documents/qs_1_1.png)

2. Click the `+` in the top-left corner and select `Install package from git URL...`.\
![](Documents/qs_1_2.png)

3. Paste the URL above and click the `Install` button.\
![](Documents/qs_1_3.png)

4. After the installation finishes, you'll see the `Liquid2DSimulation` package under `Packages`. You can import the Samples folder to view the demo scenes.\
![](Documents/qs_1_4.png)

5. After importing the Samples folder, you'll find the demo scenes under `Assets/Samples/Liquid 2D Simulation/./Samples`.\
![](Documents/qs_1_5.png)

#### Download Package
Install the plugin into your project using a package.\
Download the latest package from the [Releases](https://github.com/blurfeng/unity-water-liquid-2d-simulation/releases) page.\
Then import the package into your project.

> [!TIP]
> The plugin includes a Samples folder with demo scenes. You can start learning how to use the system directly from there.\
> Or follow the steps below one by one to add the fluid particle system to your scene.\
![](Documents/qs_2_1.png)

### 2. Add the Renderer Feature
The Renderer Feature is already added in the demo scenes.\
If you want to use this system in your own scene, you need to add the Liquid2D Feature to your current Renderer 2D Data.\
![](Documents/rf_1.png)

### 3. Create a Fluid Particle Descriptor
Fluid particles are defined by a `Liquid2DParticleDescriptor` (a `ScriptableObject`).\
Right-click in the `Project` window and choose `Create -> Liquid2D -> Particle Descriptor` to create a descriptor asset.\
![](Documents/lp_1.png)

You configure the descriptor's parameters to define the appearance and behavior of the fluid particles:
- `Radius`: The particle's physical radius (world units), which determines particle spacing and the neighbor-search range.
- `RenderScale`: The render multiplier. The visual size drawn = `Radius × 2 × RenderScale`, usually much larger than the physical radius to achieve a metaball fusion effect.
- `RenderSettings`: Rendering settings, including the `Sprite`, `Material`, `Color` (HDR supported), and `NameTag`.
- `Material`: The physics material, defining viscosity, surface tension, friction, restitution, gravity scale, buoyancy density, and more (see [Material (Physics)](#material-physics)).
- `MixSettings`: Color-mixing settings (see [Mix Colors](#mix-colors)).

Materials and textures are provided under the plugin's `./Liquid2DSimulation/Resources/Materials/` and `./Liquid2DSimulation/Resources/Textures/` directories, which you can use directly.

### 4. Create a Particle Spawner
The particle spawner is responsible for ejecting descriptors into fluid particles at runtime.\
Create an empty object in the scene and add the `Liquid 2D/Gameplay/Liquid 2D Spawner` component (i.e. `Liquid2DSpawner`).\
You can also use the `Liquid2DSpawner` prefab under the plugin's `./Liquid2DSimulation/Resources/Prefabs/` directory (located at `Packages/Liquid 2D Simulation/Resources/Prefabs/` when installed via UPM); it's recommended to create a variant from it and then tweak the parameters.\
![](Documents/ls_1.png)

In the `Liquid Particles` list of `Liquid2DSpawner`, add one or more `Liquid2DParticleConfig` entries. Each references a **descriptor** and can specify a **weight** and **lifetime**. When spawning, a descriptor is chosen at random by weight.\
For details, see [Particle Spawner Setup Guide](#-particle-spawner-setup-guide).

> [!TIP]
> At this point, the fluid particle system is already working.\
> But you usually also want to: use [Liquid2DCollider](#liquid2dcollider) to block fluid flow, and set up the rendering blocking / occluding layers so the fluid is correctly occluded by scene objects.

## 🌊 Renderer Feature Setup Guide
The `Liquid2DFeature` Renderer Feature is used to render the fluid particles and ultimately produce the fluid effect.\
The following mainly explains the important features or parameters; for more detailed parameters, check the Tooltips in the Inspector panel.

### Configure Rendering Layers
The Liquid Feature uses Rendering Layers to distinguish which objects can block or occlude the fluid particles.
#### Add a Blocking Rendering Layer
1. Open `Edit -> Project Settings -> Tags and Layers`.
2. In `Rendering Layers`, add a new layer, e.g. `LiquidObstructor`.
![](Documents/rl_4.png)
3. On the Sprite Renderer of your blocker object, find `Additional Settings -> Rendering Layer Mask` and select the `LiquidObstructor` layer you just created.
4. On the Liquid2DFeature of the Liquid2DRenderer2D, find `Obstructor Rendering Layer Mask` and select the `LiquidObstructor` layer you just created.

> [!TIP]
> In the GitHub project, I have the correct Rendering Layer Masks configured.\
> But when you import the plugin into your project, those Rendering Layers don't exist.\
> Still, in the demo scenes you'll find that blockers block the fluid particles nicely. This is because the correct Rendering Layer Masks were already configured.\
> Due to the engine's caching and mechanics, they still work. But in your project, those Rendering Layers don't actually exist.\
> On the Liquid2DFeature of the demo scene's Liquid2DRenderer2D, the Obstructor Rendering Layer Mask shows as `Unnamed Layer 1`.\
> ![](Documents/rl_2.png)

> [!IMPORTANT]
> The Rendering Layers here only affect the **rendering-level occlusion order** (whether the fluid is drawn in front of or behind objects); they **do not actually block the fluid's flow**.\
> Physically blocking the fluid's flow is done by [Liquid2DCollider](#liquid2dcollider). The two usually need to be used together: use colliders to block the fluid, and use Rendering Layers to make objects correctly occlude or be covered by the fluid.

#### Blocking
`Blocking` refers to objects that can block the fluid particles at the rendering level, such as baffles, pipes, containers, terrain, etc.\
You need to configure the layer used for blocking in the `Renderer Feature`'s `ObstructorRenderingLayerMask`.\
![](Documents/rl_5.png)

Then, in the `Additional Settings` of the `Sprite Renderer` component of every object in the scene that should block fluid particles, configure the `Rendering Layer Mask`.\
![](Documents/rl_3.png)

Otherwise, you'll find that fluid particles draw over the top of those objects.\
![](Documents/rl_1.png)

Because the fluid particles are rendered at `RenderPassEvent.AfterRenderingTransparents`, after transparent objects are rendered.\
So if you don't configure the blocking layer correctly, fluid particles will render over both opaque and transparent objects.

#### Occlusion
`Occlusion` refers to objects that can cover the fluid particles but do not block their flow, such as the front of a glass bottle or the front of terrain.\
The configuration process for occlusion is similar to blocking.\
You need to configure the layer used for occlusion in the `Renderer Feature`'s `OccluderRenderingLayerMask`.\
Then, in the `Additional Settings` of the `Sprite Renderer` component of every object in the scene that should occlude fluid particles, configure the `Rendering Layer Mask`.\
But occluding objects do not block the fluid particles' flow (via the physics settings); they cover over the top of the fluid particles.\
![](Documents/occ_1.gif)

### Cover Color
If you set a `Cover Color` and that color's alpha is 1 (here alpha represents cover strength), then this color will completely cover the original color.

### Opacity
Through the `Opacity Mode` and `Opacity Value` parameters, you can control the overall opacity of the fluid.\
Default mode does not change the particles' own transparency. After blurring, the interior color looks more opaque while the edges look more transparent.\
Multiply mode multiplies the opacity by the particle's own transparency.\
Replace mode applies the opacity directly to the particles. This also overrides the particle's own transparency as well as the transparency produced by the blur.\
Using the cover color and opacity settings, you can get a uniform fluid color.\
![](Documents/coverColorAndOpacity_1.png)

### Blur
The blur effect makes the fusion between particles more natural.\
If your particle texture already fuses well on its own, you can turn off the blur to improve performance.\
The blur's iteration count and offset determine its strength. More iterations and a smaller offset produce a better blur.\
Because it uses blurring rather than SDF, the particle count does not affect performance.\
![](Documents/blur_1.gif)
#### About Blur and Background
Because blurring works by sampling and mixing pixels, the background color affects the blur result, ultimately making the fluid edges look close to the background color.\
![](Documents/blur_2.png)

The algorithm can reduce this but cannot fully eliminate it. Therefore, the `blurBgColor` and `blurBgColorIntensity` parameters are provided to adjust the edge color.\
It's recommended to use a color close to the overall fluid color as the edge color, with intensity set between 0.5 and 0.8 for a more natural result.\
You can also enable the `ignoreBgColor` parameter to ignore the background color's influence (in practice it cannot be fully ignored), which adds some performance overhead.\
If you set a `Cover Color` that fully covers the background color, there will be no fluid edge fusing with the background color, because the particle's own color and the color produced by the blur will be completely covered.

### Distort
The distort effect simulates the refraction of the fluid. If the fluid is transparent, you can see the scene behind it being distorted.\
If the fluid is opaque, you can turn off this effect to improve performance.\
![](Documents/distort_1.gif)

### Edge
Through the `Edge Intensity` and `Edge Color` parameters, you can control the color and width of the fluid's edge.\
This can emphasize the fluid's edge to make it more noticeable, simulate a Fresnel edge effect, or create a glowing edge.\
![](Documents/edge_1.gif)

### Pixel
By enabling the pixelation effect, you can make the fluid particles appear pixelated.\
This makes the fluid suitable for pixel-art-style games.\
![](Documents/pixel_1.gif)
#### PixelBg
PixelBg makes the area covered by the fluid pixelated as well, giving a more unified style.


## 💧 Fluid Particle Setup Guide
The `Liquid2DParticleDescriptor` descriptor defines all the properties of a category of fluid particle and is the basic building block of the fluid.\
The following mainly explains the important features or parameters; for more detailed parameters, check the Tooltips in the Inspector panel.

### Sprite
Configuring a suitable Sprite for the fluid particles is very important; it determines the fusion effect of the fluid particles, which ultimately determines the overall visual look of the fluid.\
The key to the texture lies in the design of its transparency. It's recommended to use an SDF-like texture with high transparency in the center and low transparency at the edges.\
If you want softer edges, you can apply a Gaussian blur to the texture. If you want sharper edges, use a hard-edged texture.
#### Texture Transparency
The stacking and clipping of transparency between particles is the key to achieving particle fusion.\
Combined with the blur effect, it can produce a more natural fluid look. Of course, if the texture already fuses well on its own, you can turn off the blur to improve performance.\
Note that the particle texture doesn't care whether the transparent region extends beyond the texture boundary; extending beyond it actually lets particles fuse sooner.\
Suppose you use a circular texture whose boundary is transparent; then particles will travel some distance after touching before they start to fuse.\
But suppose the texture boundary has a transparency of 0.6; then particles will start to fuse as soon as they touch.\
![](Documents/sp_1.png)

### Particle Size and Rendering
In the pure-data model, fluid particles **no longer use Unity colliders**. The size of a particle is determined by two parameters on the descriptor:
- `Radius`: The **physical radius** (world units). It determines the physical spacing between particles, the neighbor-search range, and the packing density, and is the fundamental scale of the SPH solve.
- `RenderScale`: The **render multiplier**. The visual diameter drawn = `Radius × 2 × RenderScale`.

`RenderScale` is usually set much larger than the physical radius (default 4), so that adjacent particles' textures overlap substantially, producing a smooth metaball fusion effect.\
If the visual size is too small, you'll see gaps between particles; too large, and the fluid will look overly "gloopy." Adjust it together with the Sprite's transparency design to tune the fusion feel.\
![](Documents/co_1.png)

### Material (Physics)
The `Material` (`Liquid2DParticleMaterial`) on the descriptor defines the physical behavior of this category of particle. By differentiating the parameters, you can simulate water, lava, foam, sand, and other different media.\
Key parameters (for more details, see the Inspector Tooltips):
- `Mass`: Mass. Affects the response to forces and the initial ejection speed (initial speed = impulse / mass).
- `Viscosity`: Viscosity. The larger it is, the thicker and slower-flowing the fluid. Low for water, high for lava.
- `Cohesion`: Cohesion / surface tension. The larger it is, the more easily particles clump together and form droplets. High for foam, 0 for sand.
- `Friction`: Friction. Tangential damping when in contact with a collider; sand uses a higher value to pile up at an angle of repose.
- `Restitution`: Restitution. The strength of the normal bounce when colliding with a collider.
- `GravityScale`: Gravity scale. 1 = normal fall, 0 = suspended, **negative = floats upward** (good for foam).
- `Density`: The fluid's mass density, **used specifically for Archimedean buoyancy comparison** (an object floats if its density is less than this value). Independent of the packing density.
- `TargetDensityScale` / `NearPressureMultiplierScale`: Scale the global target density and near-pressure respectively, used to fine-tune a single category of particle's packing tightness and short-range repulsion.

The material has several built-in presets — **Water / Lava / Foam / Sand** — which can serve as a starting point for further tweaking: water has low viscosity and low tension; lava has high viscosity, high mass, and high density; foam has high tension, low gravity or even floats; sand has high friction and zero tension.\
![](Documents/pm_1.png)

### Mix Colors
By enabling `Mix Colors` in the descriptor's `MixSettings`, you can let fluid particles of different colors mix their colors.\
Both particles must have `Mix Colors` enabled to mix when they meet and change their own colors.\
![](Documents/mc_1.gif)

Depending on the configuration, you can control the mixing speed and other behaviors:
- `MixColorsSpeed`: Mixing speed (0 = no mixing, 1 = instant mixing).
- `MixColorsWithMovement`: Whether to accelerate mixing based on particle movement (the faster the movement, the faster the mixing).
- `MixColorsWhenStationary`: Whether to mix when stationary. Off by default, so stationary particles skip mixing to save overhead; enable it manually when you need stationary particles to mix as well.

![](Documents/mc_2.png)

> Color mixing is done by the **solver's internal neighbor query** (i.e. reusing the spatial structure already built by the SPH to mix colors between adjacent particles), and **no longer relies on Unity's physics engine contact callbacks**, so there's no longer any need to enable `Reuse Collision Callbacks`.\
> The color algorithm used for mixing is controlled globally by `Liquid2DPhysicsConfig`'s `ColorMixMode`, with three options:
> - `Oklab` (default): Mixing in a perceptually uniform color space, with natural color transitions.
> - `Ryb`: RYB pigment color-wheel mixing (blue + yellow = green), simulating real pigments.
> - `LinearRgb`: Linear RGB arithmetic mean (blue + yellow = gray).

## 🧱 Scene Collision and Blocking
The new system uses **custom colliders** to let scene objects block the fluid's flow, replacing the original Unity `Collider2D`.

### Liquid2DCollider
On objects that need to block fluid, such as baffles, pipes, containers, and terrain, simply add the corresponding collider component (menu `Liquid 2D/Colliders/...`):
- `Liquid2DBoxCollider`: A rectangular box (supports rotated OBB), with the field `size`.
- `Liquid2DCircleCollider`: A circle, with the field `radius`.
- `Liquid2DCapsuleCollider`: A capsule, with the fields `length` and `radius`.
- `Liquid2DPolygonCollider`: A polygon, with the fields `points` and `closed`. `closed = true` is a solid convex polygon; `closed = false` is a double-sided thin-wall polyline, suitable for complex terrain.

The collider's size is affected by the object's scale, and the collider follows when the object moves / rotates.\
Additionally, `Liquid2DEdgeCollider` / `Liquid2DCustomCollider` / `Liquid2DMeshCollider`, etc. are provided, which can bridge existing Unity collider shapes such as `EdgeCollider2D` / `CustomCollider2D`.\
![](Documents/collider_1.png)

> [!TIP]
> Each collider has an optional `nameTag`: when left empty it applies to **all** particles; when filled in it **only blocks the particle group matching that tag**.\
> This lets you create effects like "certain fluids can pass through certain objects."

## 🤝 Two-Way Coupling (Fluid ↔ Rigidbody)
Fluid can not only be blocked by scene objects but can in turn push the `Rigidbody2D` in the scene, achieving two-way interactions such as wash-away and floating.

### Liquid2DRigidbodyBridge Component
On an object with a `Rigidbody2D`, attach a `Liquid2DCollider` (any shape) and a `Liquid2DRigidbodyBridge` component, and that collider becomes a **dynamic collider** that bridges the reaction force applied by the fluid to the rigidbody.\
![](Documents/coupling_1.gif)

Main parameters:
- **Wash-away (relative-velocity drag)**
  - `dragCoefficient`: Drag coefficient; the larger it is, the more easily it is washed away by the fluid. When the fluid is at rest, this force ≈ 0, so it won't push the object for no reason.
  - `applyTorque`: Apply force at the contact center of mass to produce torque (so the object tumbles with the fluid).
  - `maxSpeedChange`: The upper limit of the velocity change per step, preventing sudden over-acceleration.
- **Buoyancy (Archimedes' principle)**
  - `useBuoyancy`: Enable buoyancy.
  - `bodyVolume`: The object's volume (2D area); when 0, it is automatically computed from the child collider shapes.
  - `fullSubmersionContacts`: The number of contact particles required to be considered "fully submerged."
  - `submergedLinearDrag` / `submergedAngularDrag`: The linear / angular damping when submerged.

> [!TIP]
> Buoyancy only counts fluid particles in contact **below** the object, to avoid fluid on top "launching" a light object away.\
> The object's density (`Rigidbody2D`'s mass / volume) compared with the fluid material's `Density` determines whether it floats or sinks.

## 🧲 Force Fields
A force field can apply an additional force to fluid particles within its range, used to create attractors, repellers, vortices, drains, and similar effects.\
Add the `Liquid 2D/Physics/Force Fields/Liquid 2D Radial Force Field` component (`Liquid2DRadialForceField`), which acts continuously centered on the object's position.\
![](Documents/forcefield_1.gif)

Main parameters:
- `radius`: The force field's radius of effect.
- `strength`: The force field's strength (> 0 attracts, < 0 repels).
- `swirlStrength`: Tangential swirl strength (> 0 counterclockwise, < 0 clockwise).
- `velocityDamping`: The velocity damping coefficient within the range.
- `gravityAttenuation`: Gravity attenuation inside the force field (making particles more easily dominated by the field).
- `falloff` / `forceMode`: The falloff exponent and mode of the force over distance (`Falloff` / `Constant`).
- `nameTag`: When empty it acts on all particles; when filled in it only acts on the matching particle group.

> [!TIP]
> The plugin also provides `Liquid2DMouseInteractor`, a subclass of the force field that can attract / repel fluid in real time with the mouse, convenient for debugging and interaction.

## 💀 Dead Zones and Boundaries
Use the `Liquid 2D/Gameplay/Liquid 2D Dead Zone` component (`Liquid2DDeadZone`) to mark out a region in the scene to **recycle particles**, replacing the old Unity physics trigger approach and preventing particles that flow off-screen from piling up indefinitely.\
Main parameters:
- `shape`: The region shape (Box / Circle / Capsule / Polygon), with `size` / `radius` / `length` / `points`.
- `boundsMode`: `false` destroys particles that **enter** the region; `true` destroys particles that **leave** the region (outside it), which can be used as an active boundary.
- `nameTag`: Only recycles the particle group matching the tag.

Additionally, a `Liquid2DBounds` collider is provided, which can be used as a closed boundary to keep fluid within a certain range.

## ⛲ Particle Spawner Setup Guide
The `Liquid2DSpawner` particle spawner is used to generate fluid particles, similar to a water pipe or fountain.\
The following mainly explains the important features or parameters; for more detailed parameters, check the Tooltips in the Inspector panel.

### Controlling Ejection
Through the parameters you can control the flow and force of the ejected particles:
- `flowRate` / `flowRateFactor`: The flow rate (particles / second) and its adjustment factor.
- `ejectForce` / `ejectForceFactor` / `ejectForceRandomRange`: The ejection force magnitude, factor, and random range.
- `nozzleWidth`: The nozzle width; when batch-spawning, particles are distributed evenly in layers across the width.
- `sizeRandomRange`: The random range of particle size.

#### Spawn Particle Descriptor List
You can configure multiple `Liquid2DParticleConfig` entries in `Liquid Particles`, each containing a **descriptor**, a **weight**, and an optional **lifetime** override. Each time a particle is spawned, a descriptor is chosen at random according to the weights.\
![](Documents/ls_2.png)

This lets you use more diverse particles to simulate more complex fluids, such as lava. They are usually a non-uniform mix of red, orange, and yellow particles.\
![](Documents/ls_3.gif)

As for tips on configuring lava, here I set `Cutoff` to 0.14 so that the fluid retains more of the low-transparency parts.\
Then enable the `Distort` effect. This way you'll see the near-transparent parts of the lava fluid's edges refract and distort the background, similar to a steaming-hot effect.

### Swing and Path Movement
To simulate a moving water pipe / fountain, the spawner supports nozzle swing and movement along a path:
- `swingAngleRange` / `swingSpeed`: The swing angle range and speed of the ejection direction.
- `moveEnabled` / `moveMode` (`Once` / `Loop` / `PingPong`) / `moveSpeed` / `waypoints`: Make the spawner move along a set of waypoints.


## 🚀 Performance Optimization Guide (Physics)
The following measures help you get the best performance at different scales.

### Liquid2DPhysicsConfig Component
The `Liquid2DPhysicsConfig` component (menu `Liquid 2D/Systems/Liquid 2D Physics Config`) centrally manages the solver's global parameters, solve mode, particle cap, physics step, and more, and applies them automatically on `Awake`. Just attach it to any persistent object in the scene.\
For more detailed parameter explanations, see the Inspector Tooltip of each field.

### Choosing CPU / GPU Solve Mode
The component's `Mode` field decides the solve platform. **Unless you have a clear reason, it's recommended to always use GPU mode.**
- **GPU mode (default, recommended)**: Driven by Compute Shaders, with particle data resident on the GPU, performing far better than CPU mode and easily reaching tens of thousands of particles. When the platform doesn't support Compute Shaders, it automatically falls back to CPU mode.
- **CPU mode**: Driven by the Job System + Burst, suitable for platforms that truly cannot use Compute Shaders, or debugging scenarios where you need to reliably read CPU-side particle data.

> [!WARNING]
> In GPU mode, particle data is resident on the GPU and the **CPU-side data is stale by default**, so functions based on CPU data such as `GetPosition` / `GetVelocity` / debug Gizmos are unreliable.\
> If you really need to read this data in GPU mode, you can enable `GpuReadbackToStore`, but this incurs a **synchronous GPU→CPU stall** with significant overhead, so **keep it off in production**.\
> Rendering (the Renderer Feature) reads the GPU buffers directly and is unaffected by this.

### Particle Count and Key Tuning
- **`MaxParticlesPerTag` (the alive cap per nameTag)**: In GPU mode there is virtually no limit on particle count (constrained only by VRAM); only when you find performance insufficient while testing on the target hardware do you need to consider limiting the alive count via this parameter (0 means no limit). When the cap is exceeded, the oldest particles in that group are automatically recycled. It's recommended to combine this with [dead zones](#-dead-zones-and-boundaries) to recycle escaping particles and keep the scene clean.
- **`Substeps`**: The number of substeps each fixed physics step is subdivided into; the larger it is, the more stable (less likely to penetrate or explode under high speed / high pressure), but the more costly as well.
- **`MaxSpeed`**: Limits the upper bound of particle speed, preventing pressure explosions from causing runaway velocities (0 = no limit, recommended to be 3–5× the ejection force).
- **Global SPH parameters such as `SmoothingRadius (H)`, `TargetDensity`, `PressureMultiplier`, `NearPressureMultiplier`, `ViscosityStrength`**: These determine the fluid's overall compressibility, elasticity, and thickness, affecting stability and cost; see the Tooltips for details.
- **`overrideFixedTimestep` / `fixedTimestep`**: Can uniformly override the physics frequency (`Time.fixedDeltaTime`). Lowering the physics frequency reduces the number of solves per second, but lowers the physics smoothness and high-speed stability.


## 📋 To-Do List
- **Physics system**
  - Richer interactions between fluid particles: further simulating viscosity, tension, temperature, etc., to express more media such as oil, honey, and smoke.
  - More force field and interaction types (directional force fields, wind, explosion shockwaves, etc.).
- **Rendering / Tools**
  - Further alignment of details such as color mixing between GPU mode and CPU mode.
  - More complete editor debugging and visualization tools.
