using System;
using UnityEngine;

namespace PiEEG.Unity
{
    /// <summary>What an EEG source drives on the avatar.</summary>
    public enum NeuroTargetType
    {
        /// <summary>A blendshape weight on a <see cref="SkinnedMeshRenderer"/> (0–100).</summary>
        Blendshape = 0,

        /// <summary>A float shader property on a <see cref="Renderer"/>'s material.</summary>
        MaterialFloat = 1,
    }

    /// <summary>
    /// One row in the Neuro-Binder routing table: maps an incoming EEG parameter (e.g.
    /// <c>EEG_Alpha</c>) through a response curve to a visual output (a blendshape or a material
    /// float). This is the single source of truth shared by three consumers:
    /// <list type="bullet">
    /// <item>the in-editor live preview (applies it directly for WYSIWYG tuning),</item>
    /// <item>the VRChat SDK Automator (bakes it into clips + a 1D blend tree),</item>
    /// <item>the general-purpose <see cref="NeuroReactor"/> runtime (non-VRChat builds).</item>
    /// </list>
    /// Scene object references are stored here (on a scene <see cref="NeuroBinder"/> component),
    /// which is why the binder is a <c>MonoBehaviour</c> and not a project asset.
    /// </summary>
    [Serializable]
    public class NeuroBinding
    {
        [Tooltip("Enable/disable this row without deleting it.")]
        public bool enabled = true;

        [Tooltip("Friendly label shown in the routing table and used to name generated assets.")]
        public string label = "";

        [Header("Source")]
        [Tooltip("EEG parameter name as emitted by the server OSC bridge, e.g. EEG_Alpha. " +
                 "This is also the VRChat animator/expression parameter name that OSC writes to.")]
        public string sourceId = "EEG_Alpha";

        [Tooltip("Override the VRChat parameter name. Leave empty to use Source Id verbatim " +
                 "(required for OSC to deliver data — it must match what the bridge sends).")]
        public string parameterNameOverride = "";

        [Header("Response")]
        [Tooltip("Maps the normalised source value (x: 0..1) to the output strength (y: 0..1). " +
                 "Use this to set a threshold and falloff, e.g. flat until x=0.7 then ramp to 1.")]
        public AnimationCurve responseCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Header("Target")]
        public NeuroTargetType targetType = NeuroTargetType.Blendshape;

        // ── Blendshape target ──
        public SkinnedMeshRenderer skinnedRenderer;
        public string blendShapeName = "";

        // ── Material-float target ──
        public Renderer materialRenderer;
        [Tooltip("Index of the material slot on the renderer.")]
        public int materialIndex = 0;
        [Tooltip("Shader property name, e.g. _EmissionStrength.")]
        public string materialProperty = "";
        [Tooltip("Output range the curve's 0..1 maps onto for the material property.")]
        public float materialMin = 0f;
        public float materialMax = 1f;

        [Header("VRChat")]
        [Tooltip("Network-sync this parameter so remote players see the reaction. " +
                 "Costs 8 bits of the avatar's synced parameter budget.")]
        public bool synced = true;

        /// <summary>The effective VRChat / OSC parameter name for this binding.</summary>
        public string ParameterName =>
            string.IsNullOrWhiteSpace(parameterNameOverride) ? sourceId : parameterNameOverride.Trim();

        /// <summary>A human-friendly name, falling back to the source/target if no label is set.</summary>
        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(label)) return label;
                string target = targetType == NeuroTargetType.Blendshape
                    ? blendShapeName
                    : materialProperty;
                return string.IsNullOrEmpty(target) ? sourceId : $"{sourceId} → {target}";
            }
        }

        /// <summary>Evaluates the response curve, clamped to [0,1] on both axes.</summary>
        public float Evaluate(float source01)
            => Mathf.Clamp01(responseCurve.Evaluate(Mathf.Clamp01(source01)));

        /// <summary>True if this binding has a usable target wired up.</summary>
        public bool IsValid(out string reason)
        {
            if (string.IsNullOrWhiteSpace(ParameterName))
            {
                reason = "Source/parameter name is empty.";
                return false;
            }
            if (targetType == NeuroTargetType.Blendshape)
            {
                if (skinnedRenderer == null) { reason = "No Skinned Mesh Renderer assigned."; return false; }
                if (string.IsNullOrWhiteSpace(blendShapeName)) { reason = "No blendshape selected."; return false; }
            }
            else
            {
                if (materialRenderer == null) { reason = "No Renderer assigned."; return false; }
                if (string.IsNullOrWhiteSpace(materialProperty)) { reason = "No material property set."; return false; }
            }
            reason = null;
            return true;
        }
    }
}
