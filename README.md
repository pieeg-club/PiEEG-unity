<div align="center">

# PiEEG-unity

**Unity plugin for [PiEEG-server](https://github.com/pieeg-club/PiEEG-server)** — stream live EEG into Unity and build no-code neuro-reactive avatars.

[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](LICENSE)
[![Unity](https://img.shields.io/badge/Unity-2022.3%2B-black?logo=unity)](https://unity.com/)
[![VRChat](https://img.shields.io/badge/VRChat-Avatars%203.0-09a5d6)](https://vrchat.com/)
[![Discord](https://img.shields.io/discord/1059637443548987462?color=5865F2&logo=discord&logoColor=white&label=Discord)](https://discord.gg/neJ45FR6Sv)

</div>

---

PiEEG-unity turns a live brain signal into avatar animation **without touching the Animator**. Map an
EEG band to a blendshape or a shader property in a visual routing table, tune it against the real
signal in the editor, then bake it into your VRChat avatar non-destructively.

## What's inside

| Layer | What it does | Works without VRChat? |
| --- | --- | --- |
| **Neuro Binder** (`Window ▸ PiEEG ▸ Neuro Binder`) | UI Toolkit routing table: pick a telemetry source (`EEG_Alpha`, `EEG_Focus_Ratio`, …), drag a SkinnedMeshRenderer or Material as the target, shape the response with an AnimationCurve. | ✅ |
| **Live Preview** | Connects to the server on an `EditorApplication.update` hook, runs the same FFT as the server, and drives your avatar in **Edit mode** so you tune curves while wearing the headset. | ✅ |
| **Neuro Reactor** (runtime) | `[ExecuteAlways]` MonoBehaviour that drives the bindings at runtime for any standalone/XR Unity build. | ✅ (not for VRChat — VRChat strips custom scripts) |
| **VRChat SDK Automator** | **Build Mappings** generates clips + a 1D blend tree in an isolated FX controller and merges parameters + controller via **Modular Avatar**. Your base FX is never mutated. | VRChat-only |

The band-power math (radix-2 FFT, Hanning window, rolling-max normalization, δ/θ/α/β/γ) is a faithful
port of the server's `spectral.py` OSC bridge, so **what you preview is what OSC delivers**.

## Requirements

- **Unity 2022.3 LTS** (VRChat's supported version; also fine for non-VRChat projects).
- A reachable **[PiEEG-server](https://github.com/pieeg-club/PiEEG-server)** (`ws://raspberrypi.local:1616`).
- **VRChat path only:** the **VRChat Avatars SDK (Avatars 3.0)** + **[Modular Avatar](https://modular-avatar.nadena.dev/)**.
  The VRChat assembly compiles *only* when both are installed (gated by assembly `defineConstraints`),
  so the package imports cleanly in plain Unity projects too.

## Install

**Window → Package Manager → + → Install package from git URL…**

```
https://github.com/pieeg-club/PiEEG-unity.git?path=/Packages/com.pieeg.unity
```

No third-party runtime dependencies (uses `System.Net.WebSockets.ClientWebSocket`).

## Quick start — VRChat

1. Select your avatar (the GameObject with a **VRC Avatar Descriptor**).
2. Add a **PiEEG ▸ Neuro Binder** component (`Add Component`), or open **Window ▸ PiEEG ▸ Neuro Binder**
   and it adopts the current selection.
3. **Add a mapping:** choose a **Source** (e.g. `EEG_Alpha`), set **Target** to *Blendshape* and pick a
   SkinnedMeshRenderer + blendshape (or *Material Float* + a shader property), then shape the **Response
   Curve**.
4. Start the PiEEG-server, press **Start Live Preview**, and watch the avatar react while you tune.
5. Press **Build Mappings**. This writes `Assets/PiEEG/Generated/<Avatar>/PiEEG_FX.controller` and adds a
   `PiEEG Neuro` child with Modular Avatar components. Upload as usual — nothing in your base FX changed.

On the server, run the OSC/VRChat bridge so it emits `/avatar/parameters/EEG_<Band>` (0..1). The
parameter names the Automator registers match exactly.

## Quick start — any Unity project (no VRChat)

Add a **Neuro Binder** + **Neuro Reactor** to a GameObject, wire bindings the same way, and the Reactor
applies them at runtime (and in Edit mode). Use this for desktop, mobile, or XR experiences.

## Raw stream API

The low-level client is still here if you want frames directly:

```csharp
using PiEEG.Unity;
using UnityEngine;

public class BrainDemo : MonoBehaviour
{
    public PiEEGStream stream;
    void OnEnable()  => stream.OnFrame += HandleFrame;
    void OnDisable() => stream.OnFrame -= HandleFrame;
    void HandleFrame(PiEEGFrame frame)
    {
        float ch0 = frame.channels[0]; // µV; frame.t = unix s, frame.n = sample index
        transform.localScale = Vector3.one * (1f + Mathf.Abs(ch0) * 0.001f);
    }
}
```

## Testing

Open **Window ▸ General ▸ Test Runner ▸ EditMode** and run **SpectralTests** — these are hardware-free:
they feed synthetic sine waves and assert the band-power pipeline (`FftEngine`, `BandPowerAnalyzer`,
`RollingNormalizer`, `NeuroBinding`) behaves like the server. End-to-end (with hardware) is the
**Quick start** flow above.

## License

MIT — see [LICENSE](LICENSE).
