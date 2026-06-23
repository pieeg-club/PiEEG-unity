# Changelog

## [0.2.0] - 2026-06-23

### Added — VRChat Neuro-Animation Studio
- **Neuro Binder** (`Window ▸ PiEEG ▸ Neuro Binder`): a UI Toolkit routing table that maps EEG
  bands (`EEG_Alpha`, …) to blendshapes and material floats with per-row response curves — no
  Animator wiring required.
- **Live in-editor preview**: connects to the PiEEG-server WebSocket on an `EditorApplication.update`
  hook, computes band powers with the same FFT math as the server OSC bridge, and drives the avatar
  in Edit mode so creators can tune curves while wearing the hardware. Originals are restored on stop.
- **VRChat SDK Automator** (`PiEEG.Unity.Editor.VRChat`): one-click **Build Mappings** generates
  rest/active clips, an isolated FX `AnimatorController` with a 1D blend tree per mapping, and wires
  the float parameters + controller into the avatar via **Modular Avatar** (Merge Animator +
  Parameters) — non-destructive, the base FX controller is never mutated. Compiles only when the
  VRChat Avatars SDK and Modular Avatar are installed (gated by assembly `defineConstraints`).
- **Spectral core** (runtime): `FftEngine` (radix-2 FFT matching `spectral.py`), `BandPowerAnalyzer`,
  `RollingNormalizer`, and `EegBands`.
- **`NeuroBinder`** authoring component + **`NeuroReactor`** general-purpose runtime driver
  (`[ExecuteAlways]`) for non-VRChat Unity projects (Built-in/URP/HDRP, desktop, XR).
- Code split into three assemblies (`PiEEG.Unity.Runtime`, `PiEEG.Unity.Editor`,
  `PiEEG.Unity.Editor.VRChat`) so unrelated edits don't trigger full recompiles.

### Changed
- Minimum Unity bumped to **2022.3 LTS** (VRChat's current supported version).

## [0.0.1] - 2026-06-01
- Initial bootstrap. `PiEEGClient` (WebSocket), `PiEEGFrame`, `PiEEGStream` MonoBehaviour.
