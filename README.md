# Realtime Fluid Simulation

A high-performance GPU-based fluid simulation framework for **Unity 2023.2.3f1**. The project showcases particle-based and grid-based methods rendered in real time using compute shaders and modern screen-space techniques.

## Features

- 3D SPH-style particle simulation rendered with Marching Cubes.
- 2D and 3D grid fluid solvers.
- Screen-space fluid surface reconstruction and smoothing.
- Ray-marched volume rendering.
- Indirect instanced particle rendering (GPU driven).
- Optimised compute shaders for counting, scanning, hashing and sorting.
- Modular helpers for spawning, rendering and diagnostics.

## Quick Start

1. Install **Unity 2023.2.3f1** (or newer).
2. Clone or download this repository.
3. Open the project with the Unity Hub.
4. Load one of the demonstration scenes in `Assets/Scenes/`:
   - `MarchingCubes.unity` – 3D particle fluid rendered with Marching Cubes.
   - `Particles.unity` – Raw particle render.
   - `Pressure (2D).unity` – 2D solver demo.
   - `Raymarch.unity` – Volume rendering demo.
5. Press **Play** to run the simulation.

## Folder Overview

```
Assets/
  Scripts/               # C# wrappers and helpers
    Helpers/             # GPU utilities (scan, sort, hashing, etc.)
    Rendering/           # Renderers & shaders
    Simulation/          # 2D & 3D solvers, math libraries
  Scenes/                # Demo scenes
```

## Building for Other Platforms

The project targets desktop GPU APIs (DirectX11/12, Metal, Vulkan). For mobile or WebGL, additional optimisation and shader variants will be required.

## Contributing

Pull requests are welcome! Please follow the existing code style:

- C#
  - Public fields: `camelCase`
  - Private fields: `_underscore`
  - Methods: `PascalCase`
- HLSL/Compute/Shader
  - Variables & functions: `camelCase`
  - Structs: `PascalCase`
  - Static constants: `UPPERCASE`

## License

This project is released under the MIT License. See `LICENSE` for details.
