#if PIEEG_HAS_VRC_SDK && PIEEG_HAS_MODULAR_AVATAR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace PiEEG.Unity.Editor.VRChat
{
    /// <summary>
    /// Turns a <see cref="NeuroBinding"/> + response curve into procedural AnimationClips and a 1D
    /// blend tree layer. The response curve is sampled at its keyframes (plus the 0 and 1 endpoints)
    /// so an arbitrary threshold/falloff shape is reproduced faithfully as a piecewise-linear blend
    /// tree — for the common linear case that's just the "0" (resting) and "1" (active) clips the
    /// blueprint calls for.
    /// </summary>
    internal static class NeuroClipFactory
    {
        const float Epsilon = 1e-4f;

        /// <summary>
        /// Samples a response curve into ascending, de-duplicated <c>(threshold, output)</c> pairs
        /// over the domain [0,1]. Always includes both endpoints so the property is fully driven
        /// even on write-defaults-off avatars.
        /// </summary>
        public static List<(float threshold, float output)> SampleCurve(AnimationCurve curve)
        {
            var thresholds = new List<float> { 0f, 1f };
            if (curve != null)
            {
                foreach (var k in curve.keys)
                {
                    float t = Mathf.Clamp01(k.time);
                    thresholds.Add(t);
                }
            }
            thresholds.Sort();

            var result = new List<(float, float)>();
            float last = float.NegativeInfinity;
            foreach (var t in thresholds)
            {
                if (t - last < Epsilon) continue; // skip duplicates
                last = t;
                float output = curve != null ? Mathf.Clamp01(curve.Evaluate(t)) : t;
                result.Add((t, output));
            }

            if (result.Count < 2)
            {
                result.Clear();
                result.Add((0f, curve != null ? Mathf.Clamp01(curve.Evaluate(0f)) : 0f));
                result.Add((1f, curve != null ? Mathf.Clamp01(curve.Evaluate(1f)) : 1f));
            }
            return result;
        }

        /// <summary>Builds the <see cref="EditorCurveBinding"/> for a binding's target property.</summary>
        public static EditorCurveBinding BindingFor(NeuroBinding b, string relativePath)
        {
            if (b.targetType == NeuroTargetType.Blendshape)
            {
                return new EditorCurveBinding
                {
                    path = relativePath,
                    type = typeof(SkinnedMeshRenderer),
                    propertyName = "blendShape." + b.blendShapeName,
                };
            }

            var rendererType = b.materialRenderer != null ? b.materialRenderer.GetType() : typeof(Renderer);
            return new EditorCurveBinding
            {
                path = relativePath,
                type = rendererType,
                propertyName = "material." + b.materialProperty,
            };
        }

        /// <summary>Maps a curve output (0..1) to the concrete property value for the target.</summary>
        public static float OutputValue(NeuroBinding b, float output01)
        {
            return b.targetType == NeuroTargetType.Blendshape
                ? Mathf.Clamp01(output01) * 100f
                : Mathf.Lerp(b.materialMin, b.materialMax, Mathf.Clamp01(output01));
        }

        /// <summary>Creates a one-keyframe constant clip and stores it as a sub-asset of the controller.</summary>
        public static AnimationClip CreateConstantClip(
            AnimatorController controller, string name, EditorCurveBinding binding, float value)
        {
            var clip = new AnimationClip { name = name };
            var curve = new AnimationCurve(new Keyframe(0f, value));
            AnimationUtility.SetEditorCurve(clip, binding, curve);
            AssetDatabase.AddObjectToAsset(clip, controller);
            return clip;
        }

        /// <summary>
        /// Adds a full-weight layer to <paramref name="controller"/> containing a single state whose
        /// motion is a Simple1D blend tree driven by <paramref name="parameter"/>.
        /// </summary>
        public static void AddBlendTreeLayer(
            AnimatorController controller, string layerName, string parameter,
            IReadOnlyList<(float threshold, Motion motion)> children)
        {
            EnsureFloatParameter(controller, parameter);

            controller.AddLayer(layerName);
            var layers = controller.layers;
            int idx = layers.Length - 1;
            layers[idx].defaultWeight = 1f;

            var stateMachine = layers[idx].stateMachine;

            var tree = new BlendTree
            {
                name = layerName,
                blendType = BlendTreeType.Simple1D,
                blendParameter = parameter,
                useAutomaticThresholds = false,
            };
            AssetDatabase.AddObjectToAsset(tree, controller);
            foreach (var (threshold, motion) in children)
                tree.AddChild(motion, threshold);

            var state = stateMachine.AddState(layerName);
            state.motion = tree;
            state.writeDefaultValues = true; // Modular Avatar re-aligns this to the avatar's WD mode.
            stateMachine.defaultState = state;

            controller.layers = layers; // persist defaultWeight change
        }

        static void EnsureFloatParameter(AnimatorController controller, string name)
        {
            foreach (var p in controller.parameters)
                if (p.name == name) return;
            controller.AddParameter(name, AnimatorControllerParameterType.Float);
        }
    }
}
#endif
