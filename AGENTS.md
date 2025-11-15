# Repository Guidelines

## Project Structure & Module Organization
- `Assets/`, `Packages/`, `ProjectSettings/`: Unity project hosting the playable weather sandbox. Place new C# systems in `Assets/Scripts/` and prefabs in `Assets/Prefabs/`. Keep art/audio under `Assets/Resources/` where Unity can bundle them.
- `2D-Weather-Sandbox/`: Browser-based reference simulator (HTML, JS, GLSL). Shader sources live under `shaders/`; gameplay logic is in `app.js`. Use this directory for benchmarking or borrowing algorithms; avoid mixing Unity code here.
- `codex-notes.md`, `Logs/`, `UserSettings/`: Developer notes and Unity-generated files. Treat `Logs/`, `Temp/`, `Library/` as disposable.

## Build, Test, and Development Commands
- `open -a "Unity" .` – launches the Unity Editor at the repo root so you can iterate on the main scene (`Assets/Scenes/SampleScene.unity`).
- `Unity -projectPath . -quit -runTests -testResults ./Logs/editmode-results.xml` – CLI run of Edit Mode tests for CI.
- `npx http-server 2D-Weather-Sandbox -o index.html` – serves the reference WebGL simulator locally for quick comparisons. Any static server will work; keep it isolated from Unity assets.

## Coding Style & Naming Conventions
- Unity C#: prefer PascalCase classes/fields, camelCase locals, 2-space indentation (matching existing `Weather2D.cs`). Group serialized fields at the top with `[Header]` labels for inspector clarity.
- JavaScript/GLSL in `2D-Weather-Sandbox/`: follow existing 2-space indent and descriptive function names (`updateSetupSliders`, `lightingShader.frag`). Keep uniforms/constants in SCREAMING_SNAKE_CASE to match shader includes.
- Avoid mixing Unity GUIDs; new assets must live inside `Assets/` with accompanying `.meta` files committed.

## Testing Guidelines
- Unity: add Edit Mode tests under `Assets/Tests/EditMode/` and Play Mode tests under `Assets/Tests/PlayMode/`. Name files `<Feature>Tests.cs` and methods `Test_<Scenario>_<ExpectedOutcome>()`.
- Web simulator: sanity-check shader or JS changes by loading through the local server and watching the browser console; no automated harness exists yet, so document manual steps in `codex-notes.md`.

## Commit & Pull Request Guidelines
- Commits: use imperative summaries (“Add latent-heat hook to Weather2D”) and keep Unity-generated noise out of the diff. Batch related asset and script changes together.
- Pull requests: include a short description, reproduction steps, and screenshots/GIFs when visual behavior changes. Link issue numbers (e.g., “Fixes #123”) and note any manual test coverage (Unity playthrough steps, browser scenarios). Provide before/after metrics if you tweak performance-critical shaders.

## Developer Notes
- Keep a rich developer log in the indicated developer notes file. Date each entry. Add each new entry to the bottom of the file.