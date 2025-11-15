## Weather simulator setup
- Added `Assets/Scripts/Weather2D.cs` plus meta implementing the 2D temperature/humidity/wind simulation with texture output and interactive brushes.
- Updated `Assets/Scenes/SampleScene.unity` to include a `Weather Controller` GameObject that hosts the `Weather2D` MonoBehaviour so the simulation runs when the scene loads.
- Added dual input-path handling in `Weather2D` so pointer and brush input work under both the old `Input` manager and the new Input System package.
- Expanded `Weather2D` with immediate-mode slider UI controlling global wind speed, wind direction, and simulation timescale, plus new wind/time-scale parameters applied to the simulation each frame.
- Extended `Weather2D` with cloud-specific fields and microphysics: separate cloud advection/diffusion buffers, humidity→cloud condensation, cloud evaporation, and ground forcing (heat, moisture, updraft) to spur convection. Updated visualization to render a sky gradient, ground strip, and clouds with density-driven shading, plus camera background tweaks for the new look.
- Added an in-game demo selector (IMGUI) wired to `ApplyDemoScenario`, currently offering a “Cloud Merger” preset that seeds two counter-rotating storm cells and converging winds so they collide mid-sky. Scenario resets clear the simulation arrays, re-seed base fields, inject Gaussian cloud/humidity blobs, and retune local wind to teach how pressure and vorticity drive cloud interactions.

## 2025-11-14 – Study of 2D-Weather-Sandbox
- This WebGL sim models a 2D, hydrostatic-free troposphere column where every cell tracks velocities, pressure, potential temperature, vapor/cloud water, precipitation proxy, soil moisture, smoke, snow depth, and wall metadata, all advanced through GPU shaders each timestep [`shaders/common.glsl`](2D-Weather-Sandbox/shaders/common.glsl#L34).
- Core atmosphere loop: pressure solve enforces divergence-free flow, velocities integrate pressure gradients plus drag/wind forcing, and an advection pass back-traces each cell, updates scalar fields, and applies condensation/evaporation latent heating tied to saturation mixing ratios [`shaders/fragment/pressureShader.frag`](2D-Weather-Sandbox/shaders/fragment/pressureShader.frag#L16), [`shaders/fragment/velocityShader.frag`](2D-Weather-Sandbox/shaders/fragment/velocityShader.frag#L32), [`shaders/fragment/advectionShader.frag`](2D-Weather-Sandbox/shaders/fragment/advectionShader.frag#L71).
- Clouds & storms come from two coupled layers of complexity:
  - Microphysics: excess vapor becomes cloud water with configurable condensation rates; latent heat feeds back on buoyancy; soil, snow, and surface types feed moisture/temperature fluxes; GUI exposes most constants so storm structure can be tuned without code edits [`2D-Weather-Sandbox/app.js`](2D-Weather-Sandbox/app.js:347).
  - Precipitation: thousands of droplets run in a transform-feedback vertex shader that spawns parcels whenever cloud water exceeds thresholds, differentiates rain/snow/hail, handles riming/melting/evaporation, deposits water/ice on the ground, and even probabilistically sparks lightning once cloud water + precipitation exceed a density threshold [`shaders/vertex/precipitationShader.vert`](2D-Weather-Sandbox/shaders/vertex/precipitationShader.vert#L5).
- Radiation & visuals: a dedicated lighting shader casts sun/IR rays, attenuates by cloud/smoke density, models greenhouse gases, and feeds reflected light buffers used for the realistic renderer, keeping convective towers shaded and sun-heated [`shaders/fragment/lightingShader.frag`](2D-Weather-Sandbox/shaders/fragment/lightingShader.frag#L38).
- Environmental forcing: real-world soundings can be scraped, interpolated to simulation resolution, and enforced as nudging terms on temperature, dew point, and wind so storms form under plausible thermodynamic profiles [`2D-Weather-Sandbox/app.js`](2D-Weather-Sandbox/app.js:150).
- To recreate the *basics* of their simulator for our own project, stage the work:
  1. Build the 2D staggered-grid fluid core (pressure → velocity → advection) on the GPU or compute shader first; without that infrastructure, nothing else behaves believably.
  2. Layer in thermodynamics: keep potential temperature and total water (vapor) fields, convert to real T, apply saturation functions for condensation/evaporation, and inject latent heat locally just as their advection shader does.
  3. Represent cloud water explicitly so updraft cores carry LWC you can visualize, and add simple precipitation sinks (e.g., threshold-based fallout) before implementing their full droplet system.
  4. When ready for storms, implement a particle/parcel system inspired by their transform-feedback approach so rain/snow parcels can advect/fall independently and feedback mass/heat when they evaporate or reach the ground.
  5. Add external forcing hooks (surface heating, sea/land moisture flux, simple radiation gradient, or sounding nudging) to keep CAPE/CIN budgets realistic enough to sustain organized convection before investing in advanced rendering.
- Takeaways for cloud & storm fidelity: most of the realism comes from coupling latent heating, precipitation mass budgets, and radiative forcing to the fluid loop. Our MVP can copy their ordering of operations (pressure→velocity→advection→microphysics→precipitation) and gradually dial up complexity (lightning, hail density, greenhouse gases) once the base loop is performant.

## 2025-11-14 – Weather2D milestone 1 WIP
- Replaced the old CPU grid with a GPU compute pipeline (`WeatherFluid.compute`) that runs pressure→velocity→advection per frame and renders dye to a display texture. `Weather2D` now manages RenderTextures, demo presets, GUI buttons, and optional debug logging.
- The solver currently injects impulses (base thermal source + mouse + looping bursts) but densities/velocities decay instantly in runtime builds, leaving only a brief flash. Debug instrumentation shows average/max densities and speeds are reported as zeros/NaNs, confirming the data disappears almost immediately.
- Added a GPU stats buffer, inspector-driven logging, and `ComputeStats` kernel so we can log average/max density & velocity per frame. However, without further sandbox approvals we can’t run Unity CLI tests to inspect textures automatically; everything must be validated inside the editor.
- Action items for next session (after approvals): keep investigating why `_densityA/_velocityA` drop to zero (probable culprit is projection/advection writing zeros), add sanity asserts in the compute shader via a small debug buffer, and rerun the Unity scene with logging enabled to capture non-zero stats once the issue is fixed.

## 2025-11-15 – WeatherFluid projection fix
- Added the first Edit Mode regression tests under `Assets/Tests/EditMode/WeatherFluidTests.cs` to exercise injection, advection, and the full source-driven loop. The `BaseSourceSustainsEnergyAcrossMultipleSteps` test reproduces the instant dissipation by asserting on both average density and average speed after multiple timesteps.
- The stats logging from that test showed projection blowing up velocities (avg speed jumping from 0.0002 to 1e10 between steps) which then advected the dye out of bounds. The divergence and gradient kernels were scaling derivatives by `_SimSize`, effectively over-applying the pressure correction on normalized UV-space velocities.
- Updated `WeatherFluid.compute` so `ComputeDivergence` and `SubtractGradient` use a simple 0.5 central difference factor (no `_SimSize` multiplier). With the new tests the average density now ramps from ~5e-4 to ~3e-3 over six steps, velocities stay in the 1e-3 range, and `Unity -batchmode -runTests -testPlatform EditMode` passes locally (`Logs/editmode-results.xml`).

## 2025-11-15 – Humidity & cloud microphysics
- Goal: bring back humidity→cloud coupling plus a precipitation sink and ensure every new behavior is covered by deterministic Edit Mode tests.
- Compute shader changes:
  - Added `MoistureMicrophysics` kernel working in-place on humidity, cloud, and velocity textures. Supersaturation condenses at `_CondensationRate`, evaporation kicks in when humidity falls below `_SaturationThreshold`, precipitation drains cloud mass at `_PrecipitationRate`, and condensation injects buoyancy via `_LatentHeatBuoyancy`.
  - Expanded stats reduction to track humidity, cloud, speeds, and total water so we can assert on mass budgets. Visualization now blends humidity gradients with a separate cloud tint using `_CloudGain/_CloudBlend`.
- Weather2D orchestration:
  - Introduced dedicated humidity/cloud render targets, advection passes, and new inspector controls for the microphysics and visualization parameters.
  - Debug logging now reports humidity + cloud averages/maxima. The compute stats buffer grew to 8 floats, so I switched the async readback to raw `float[]` parsing.
- Tests (all in `WeatherFluidTests`):
  - Existing injection/energy tests now read the richer stats struct.
  - `CondensationCreatesCloudAndBuoyancy` seeds supersaturated humidity and checks for cloud growth plus upward velocity from latent heat.
  - `EvaporationReturnsHumidityWhenDry` initializes dry air with cloud water and expects humidity recovery + cloud depletion.
  - `PrecipitationReducesTotalWaterOverTime` runs multiple microphysics steps with precipitation cranked up and asserts that the combined humidity+cloud reservoir shrinks appreciably.
- Workflow notes:
  - Used the compute shader’s `_ClearTexture` kernel in tests for deterministic initial conditions (e.g., uniform humidity fields) instead of CPU readbacks.
  - Each microphysics test tweaks saturation/condensation knobs via a helper so the numbers stay stable rather than brittle magic constants.
  - After implementing the kernels I iterated on the tests first (they failed with zero cloud growth/decay) which helped diagnose missing cloud advection + precipitation order before validating the scene in the editor.

## 2025-11-15 – Cloud Merger demo polish
- Added a third preset (`Cloud Merger`) to `EnsureDemoScenarios()`. Two mirrored Gaussian bursts inject humidity/velocity once at startup to form colliding towers; no loop bursts are scheduled so the evolution is purely from the initial conditions + microphysics.
- DemoScenario now supports `timeScale` (per-scenario delta-time multiplier) and `disableBaseSource` (skips the perpetual ground plume). Weather2D caches those flags when applying a scenario and multiplies the frame dt by the requested timescale. The Cloud Merger preset ships with `timeScale = 0.6` for a slower, readable evolution and `disableBaseSource = true` so the convection decays naturally after the collision.

## 2025-11-15 – Rendering polish, precipitation, and sounding hooks
- **Rendering:** The visualize kernel now blends a sky/ground gradient, humidity tint, and cloud shading (lit vs. shadowed) with adjustable inspector colors. Clouds pop against a brighter dome while humidity still paints the boundary layer.
- **Precipitation loop:** `MoistureMicrophysics` writes per-cell precipitation amounts and the stats kernel now reports avg/max precipitation. Weather2D gathers those stats synchronously each frame to (a) modulate an optional `ParticleSystem` emitter via `precipitationEmissionGain`, and (b) feed condensation water back into the fluid by injecting a small humidity pulse near the surface proportional to recent rain. This satisfies “particle system with feedback” without a full particle simulation yet.
- **Surface forcing:** All base injections (including rain feedback) can optionally be modulated by a surface moisture map. Set `enableSurfaceForcing` plus a greyscale texture and the compute shader scales the thermal plume intensity by the map value.
- **Sounding profiles:** Added a `SoundingProfile` ScriptableObject so we can capture saturation threshold, condensation/evap rates, base plume parameters, and even a surface moisture texture. Weather2D can auto-apply a profile on start or via context menu, letting us stage different environmental soundings quickly.
- **Tests updated:** The Edit Mode suite now binds the new precipitation texture and reads the expanded stats buffer (humidity/cloud/velocity/precipitation totals), keeping the numerical assertions relevant after the architecture change.
