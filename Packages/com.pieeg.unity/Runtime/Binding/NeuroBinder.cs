using System.Collections.Generic;
using UnityEngine;

namespace PiEEG.Unity
{
    /// <summary>
    /// Authoring data for a neuro-reactive avatar: the connection settings plus the routing table of
    /// <see cref="NeuroBinding"/>s. Add this to the root of your avatar, open
    /// <c>Window ▸ PiEEG ▸ Neuro Binder</c>, and wire EEG bands to blendshapes / materials.
    ///
    /// <para>This component is pure data — it holds no runtime logic. In the VRChat workflow it is an
    /// <i>authoring-only</i> component (VRChat strips custom MonoBehaviours at upload; the actual
    /// gimmick is the generated clips + 1D blend tree merged via Modular Avatar). For non-VRChat
    /// Unity projects, pair it with a <see cref="NeuroReactor"/> to drive the bindings at runtime.</para>
    /// </summary>
    [AddComponentMenu("PiEEG/Neuro Binder")]
    [DisallowMultipleComponent]
    [HelpURL("https://github.com/pieeg-club/PiEEG-unity")]
    public class NeuroBinder : MonoBehaviour
    {
        [Tooltip("PiEEG-server WebSocket URL used by the live preview and the runtime reactor. " +
                 "Default port is 1616. The VRChat OSC path does not use this — VRChat's native " +
                 "OSC client receives data from the server's OSC bridge directly.")]
        public string serverUrl = "ws://raspberrypi.local:1616";

        [Tooltip("Hardware sample rate (Hz). Must match the device the server reports.")]
        public int sampleRate = EegBands.DefaultSampleRate;

        [Range(0f, 0.95f)]
        [Tooltip("Preview/runtime EMA smoothing. 0 = raw (matches OSC output exactly).")]
        public float smoothing = 0.5f;

        [Tooltip("The routing table. Each row maps an EEG parameter to a visual output.")]
        public List<NeuroBinding> bindings = new List<NeuroBinding>();

        /// <summary>Adds a fresh binding with sensible defaults and returns it.</summary>
        public NeuroBinding AddBinding()
        {
            var b = new NeuroBinding();
            bindings.Add(b);
            return b;
        }
    }
}
