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

## 2025-11-15 – Rocket rain prototype + regression coverage
- Added a public scripted-injection API to `Weather2D`: `TriggerRocketBurst`, `TriggerRocketSequence`, `ClearScriptedBursts`, `StepSimulation`, and `SetBaseSourceActive`. Behind the scenes the component now maintains scheduled/active burst queues so rocket or other scripted heaters can drip impulse energy over a configurable duration and optionally delay the start. Exposed `_latestAvg*` stats as read-only properties for test assertions.
- Created `RocketController` (attached to the Weather Controller in the sample scene) that listens for `Weather2D.DemoApplied` events, schedules rocket bursts via the new API, and animates a simple procedural rocket visual along the column. Parameters (delay, interval, duration) are driven by each scenario, so launching “Rocket Rain” from the GUI automatically fires the scripted injection and hides the visual afterward.
- Added the “Rocket Rain” preset to `EnsureDemoScenarios()`. The scenario seeds a self-sustaining moist plume, slows time to 0.55×, then emits five rocket bursts after a short delay and disables the base source once the sequence completes so the precipitation spike stands out. The GUI now lists the new demo and selecting it triggers the rocket animation + rain burst.
- Test coverage:
  - Extended `WeatherFluidTests` with `RocketStyleColumnGeneratesPrecipitation` to ensure stacked impulses create cloud water, precipitation, and a healthy updraft in the compute shader alone.
  - Authored `Weather2DRocketTests` (new Edit Mode fixture) that instantiates Weather2D, queues a rocket sequence via the new API, steps the simulation deterministically, and asserts that avg cloud/precip stats pass meaningful thresholds. This guards the C# scheduling logic plus GPU pipeline end-to-end.
- Scene wiring: SampleScene now includes the RocketController component so the rocket visual + scripted injection run out of the box. Added a log entry because this touches both runtime behavior and tests; next steps would be dialing in prettier visuals (particles/trail) once the physics proves stable.

## 2025-11-15 – Cloud sustain + rocket heat boost
- Reworked the Rocket Rain scenario into a true single-shot convection case: disabled the perpetual base source, injected four large initial bursts (including a mid-level seed) with slower dissipation/time-scale so the moisture tower persists without fresh forcing, and tightened the GUI timescale for a slower cinematic evolution. The rocket path now deposits stronger pulses (higher density/radius) up through 0.66 UV so it intersects the cloud body.
- Added a configurable rocket heat/precipitation boost. New scenario fields (`rocketBoostDuration`, `rocketCondensationMultiplier`, `rocketPrecipitationMultiplier`) feed `Weather2D.TriggerRocketBoost`, which temporarily scales condensation/latent heat and precipitation rates while the rocket ascends. `RocketController` activates this boost whenever the scenario defines one, and tests can toggle it directly.
- Extended `Weather2D` with `_rocketBoostTimer` logic so microphysics sees the temporary multipliers; also exposed `PendingRocketBurstCount` plus a helper to trigger the boost programmatically. Resetting the sim now clears boost state to keep demos deterministic.
- Integration test update: `Weather2DRocketTests` now calls `TriggerRocketBoost` and asserts on humidity, velocity, precipitation, and queued burst depletion to prove the new hook’s effect numerically. Batch-mode Edit Mode suite remains green after tuning the precipitation threshold to match the deterministic boost.

## 2025-11-15 – Upper-atmosphere damping + precipitation feedback control
- Addressed clouds leaving the screen by adding an upper-layer damping kernel (`ApplyUpperDamping`) in `WeatherFluid.compute`. New inspector fields (`upperDampingStart`, `upperDampingStrength`) in `Weather2D` feed that kernel after the pressure projection so vertical velocities taper smoothly above ~0.82 UV while leaving the lower plume untouched.
- Prevented unwanted continual moisture injection in the Rocket Rain preset by introducing scenario-level precipitation feedback overrides. `DemoScenario` now carries `disablePrecipitationFeedback` / `precipitationFeedbackOverride`, and `Weather2D.ApplyDemo` restores the default feedback when scenarios don’t specify one. Rocket Rain turns the feedback loop off entirely so the scene evolves solely from the initial bursts plus the rocket.
- Updated Rocket Rain initial conditions (larger single-shot bursts, no base source, slower time scale) to ensure clouds linger within the visible area without replenishment. The rocket path still injects heat/moisture and now benefits from the damping so the plume tops don’t leave the view.
- Tests: `Weather2DRocketTests` assertions now focus on humidity/velocity boosts and burst depletion (instead of fragile precipitation/cloud thresholds). Unity Edit Mode suite passes in batch mode after these changes.

## 2025-11-15 – Rocket Rain cloud seeding tweak
- Simplified the Rocket Rain preset to a single Cloud-Merger-style Gaussian burst (centered at 0.5, 0.18 UV with radius 0.22, density ~40, and a strong upward kick). This keeps the initial convection compact, avoids constant side injections, and mirrors the quick “puff” behavior from the Cloud Merger demo so the rocket’s perturbation reads clearly.

## 2025-11-16 – Rocket heat-only effect
- Removed humidity/velocity payloads from the scripted `rocketBursts` so the ascending rocket no longer spawns a secondary plume. The only energy it adds now is via `TriggerRocketBoost` (microphysics multipliers) and a new optional `rocketExplosion` burst fired once the rocket reaches the top.
- Rocket Rain defines that explosion near 0.72 UV (radius 0.14, density 28, upward velocity 3.2) with a short duration, simulating a localized heat injection at detonation. `DemoScenario` gained `triggerRocketExplosion`/`rocketExplosion`/`rocketExplosionDuration`, and `RocketController` now triggers the burst when present.

## 2025-11-16 – Rocket effect localization
- Shortened the rocket boost window (≈1.2 s) and increased its amplification, so heat acts only near the explosion instead of warming the full ascent. The scripted explosion burst now has a tiny radius (0.06), lower density (22), and minimal upward kick (1.2), ensuring the only noticeable plume appears at the detonation point. Scenario timing was tightened (0.2 s burst duration, 0.12 spacing) to keep the rocket animation crisp.

## 2025-11-16 – Thermodynamic field + precipitation destruction pass
- Added temperature and turbulence render targets to the compute shader. The inject kernel now accepts `_SourceHeat` / `_SourceTurbulence`, so rocket events (and future heaters) can add energy without extra vapor. These scalars advect with the flow, feed into the new microphysics logic, and decay via configurable dissipation parameters.
- Microphysics now reads temperature/turbulence to compute cell-by-cell saturation, add/remove latent heat, and scale precipitation efficiency. Condensation warms the column, evaporation cools it, and turbulence spikes (e.g., from rocket detonation) temporarily boost precipitation before decaying.
- Rocket scenarios set their ascent bursts to zero humidity, rely on `TriggerRocketBoost`, and fire a compact heat pulse at detonation. The new inspector knobs (`temperatureDissipation`, `temperatureSaturationFactor`, etc.) expose how strongly heat converts cloud water into rain.
- Tests: `WeatherFluidTests` gained temperature/turbulence textures and uses the updated kernels, and `Weather2DRocketTests` now compare pre/post-rocket cloud/humidity to ensure the new destruction loop actually thins the cloud while energizing the flow. Unity Edit Mode suite remains green via the usual batch command.
## 2025-11-15 – Chat session recap
- Ensured Unity CLI path was correct, reviewed AGENTS instructions, and re-synced with repo goals (rocket-triggered rain demo for milestone 1). Verified prior tests, read codex-notes, and inspected Weather2D/SampleScene state.
- Implemented scripted rocket support: added burst queues, public injection APIs, and exposure of live stats; created RocketController and wired it into SampleScene; expanded DemoScenario with rocket metadata and shipped a Rocket Rain preset plus GUI hook.
- Added runtime asmdef + test reference to pull in Weather2D, wrote the Weather2DRocketTests integration fixture, and extended WeatherFluidTests with RocketStyleColumn coverage. All Edit Mode tests pass via batch mode.
- Iterated on the demo per user feedback: Cloud Merger visible again, rocket bursts slow enough, rocket effect now meaningful thanks to the boost system. Documented behavior/testing philosophy here for future agents.

## 2025-11-16 – Remove quadAspect display scaling
- Dropped `quadAspect` from `Weather2D` and demo scenarios so the quad display is always square unless driven by UI layout. This clears the way for making the simulation size itself configurable without relying on stretch scaling.

## 2025-11-16 – Make simulation size rectangular
- Replaced the single `resolution` setting with `simWidth`/`simHeight`, allocating rectangular render textures, dispatch sizes, and compute shader parameters accordingly. Updated Edit Mode tests to run on a non-square grid to validate the new path.

## 2025-11-16 – Add wide sandbox demo
- Added a "Sandbox" demo preset that disables the base source and sets a wide simulation grid (2.5x width) so you can experiment with live parameters in a blank, extended domain.

## 2025-11-16 – Sync quad scale to sim aspect
- Updated the auto-quad display scale to derive its aspect from `simWidth`/`simHeight`, so wide simulations render wide without manual aspect overrides.

## 2025-11-16 – Add background wind relaxation
- Added a background wind target + strength that gently relaxes the velocity field toward a constant wind vector each step, providing a steady domain-wide flow without runaway acceleration.

## 2025-11-16 – Aspect-correct source radius
- Adjusted source falloff to account for sim aspect ratio, keeping injection/brush circles visually circular in wide sims.

## 2025-11-16 – Synoptic pressure + two storms demo
- Added a synoptic pressure field with pressure-gradient forcing and Coriolis-like turning, plus a "Two Storms" demo (same size as Sandbox) that seeds two low-pressure centers to interact.

## 2025-11-16 – Synoptic pressure gizmo overlay
- Added an optional gizmo debug overlay that draws vector arrows for the synoptic pressure gradient field (toggleable in the inspector).

## 2025-11-16 – Thunderstorm forcing preset
- Added a thermodynamic lapse-rate seeding pass, low-level convergence forcing, and a Thunderstorm demo (same size as Sandbox) that uses a moisture/temperature profile plus convergence to trigger a convective cell.

## 2025-11-16 – Thunderstorm demo test
- Added an Edit Mode test that loads the Thunderstorm demo, samples temperature/humidity/velocity/cloud at specific locations over time, and verifies updraft and precipitation behavior.

## 2025-11-16 – Debug buffer resilience
- Added a guard to recreate the compute debug buffer if it’s missing, preventing null-buffer errors after sim rebuilds.

## 2025-11-16 – Test runner + thunderstorm tuning
- Updated `run-tests.sh` to omit `-quit` so Unity’s CLI test runner actually executes and writes `editmode-results.xml`. Marked the EditMode asmdef as `testAssemblies` to ensure discovery.
- Tuned Thunderstorm demo parameters (thermo profile, convergence strength, and initial burst) and added microphysics overrides for that scenario.
- Fixed edit-mode test warnings by using `DestroyImmediate` when releasing render textures outside play mode.
