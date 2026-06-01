<div align="center">

# PiEEG-unity

**Unity plugin for [PiEEG-server](https://github.com/pieeg-club/PiEEG-server)** — stream live EEG into Unity over WebSocket.

[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](LICENSE)
[![Unity](https://img.shields.io/badge/Unity-2021.3%2B-black?logo=unity)](https://unity.com/)
[![Status](https://img.shields.io/badge/status-WIP%20%E2%80%94%20bare%20minimum-orange)]()
[![Discord](https://img.shields.io/discord/1059637443548987462?color=5865F2&logo=discord&logoColor=white&label=Discord)](https://discord.gg/neJ45FR6Sv)

</div>

---

> ## ⚠️ Work in progress — bare minimum
>
> This is a **v0.0.1 bootstrap**. It does **one thing**: connect to a PiEEG-server over WebSocket and hand you raw EEG frames in C#. That's it.
>
> **Not yet included** (planned, contributions welcome):
> - Band powers (δ/θ/α/β/γ) computed in-engine
> - Smoothed / normalized signals (alpha level, focus, relaxation as `float` in [0,1])
> - Per-band `UnityEvent<float>` for inspector wiring
> - Demo scene / sample assets
> - Typed wrappers for server commands (recording, filter, OSC, LSL)
> - Authentication flow (`--auth` mode)
> - `.meta` files / verified UPM publish
>
> If you need any of the above today, use the raw WebSocket API directly — it's documented in the [PiEEG-server README](https://github.com/pieeg-club/PiEEG-server#websocket-api).
>
> Born from a [Discord discussion](https://discord.gg/neJ45FR6Sv) with the community about whether a Unity SDK was worth shipping. Consensus: yes, but start small. So here we are.

## Install

In Unity, open **Window → Package Manager → + → Install package from git URL…** and paste:

```
https://github.com/pieeg-club/PiEEG-unity.git?path=/Packages/com.pieeg.unity
```

Requires Unity 2021.3 LTS or newer. No third-party dependencies (uses `System.Net.WebSockets.ClientWebSocket`).

## Quick start

Add a `PiEEGStream` component to any GameObject, set **Url** to `ws://raspberrypi.local:1616`, leave **Connect On Start** ticked, and wire the `On Frame Event` UnityEvent in the inspector.

Or in code:

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
        // frame.channels is µV per channel (8 or 16 elements).
        // frame.t = unix seconds, frame.n = monotonic sample index.
        float ch0 = frame.channels[0];
        transform.localScale = Vector3.one * (1f + Mathf.Abs(ch0) * 0.001f);
    }
}
```

That's the whole API surface for v0.0.1.

## Roadmap

See the "Not yet included" list above. Issues and PRs welcome — especially for band-power computation and a proper demo scene.

## License

MIT — see [LICENSE](LICENSE).
