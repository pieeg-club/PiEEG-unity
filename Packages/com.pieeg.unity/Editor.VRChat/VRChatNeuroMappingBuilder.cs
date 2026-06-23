#if PIEEG_HAS_VRC_SDK && PIEEG_HAS_MODULAR_AVATAR
using System.Collections.Generic;
using System.Text;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace PiEEG.Unity.Editor.VRChat
{
    /// <summary>
    /// Registers the VRChat backend with the core editor at load time. Because this whole assembly
    /// only compiles when both the VRChat Avatars SDK and Modular Avatar are installed, the core UI
    /// transparently falls back to "install the SDK" guidance when they are absent.
    /// </summary>
    [InitializeOnLoad]
    internal static class VRChatBuilderRegistrar
    {
        static VRChatBuilderRegistrar()
        {
            NeuroMappingBuilderRegistry.Register(new VRChatNeuroMappingBuilder());
        }
    }

    /// <summary>
    /// The SDK Automator. Non-destructively turns a <see cref="NeuroBinder"/> routing table into a
    /// working neuro-reactive VRChat avatar:
    /// <list type="number">
    /// <item>generates rest/active AnimationClips per mapping (curve-sampled),</item>
    /// <item>builds an isolated FX <see cref="AnimatorController"/> with one full-weight 1D blend-tree
    /// layer per mapping, driven by the matching <c>EEG_*</c> float,</item>
    /// <item>injects the float parameters into the avatar via a Modular Avatar Parameters component
    /// (so VRChat exposes them to OSC), and</item>
    /// <item>merges the controller at build time via Modular Avatar Merge Animator — the user's base
    /// FX controller is never mutated.</item>
    /// </list>
    /// Everything is parented under a single "PiEEG Neuro" GameObject that can be deleted to fully
    /// remove the gimmick.
    /// </summary>
    public sealed class VRChatNeuroMappingBuilder : INeuroMappingBuilder
    {
        const string GeneratedRoot = "Assets/PiEEG/Generated";
        const string ChildObjectName = "PiEEG Neuro";

        public string DisplayName => "VRChat · Modular Avatar";

        public bool CanBuild(NeuroBinder binder, out string reason)
        {
            if (binder == null) { reason = "No Neuro Binder."; return false; }

            var avatar = binder.GetComponentInParent<VRCAvatarDescriptor>();
            if (avatar == null)
            {
                reason = "No VRC Avatar Descriptor found on this object or its parents. " +
                         "Add the Neuro Binder to your avatar root.";
                return false;
            }

            int valid = 0;
            var sb = new StringBuilder();
            foreach (var b in binder.bindings)
            {
                if (b == null || !b.enabled) continue;
                if (!b.IsValid(out string r)) { sb.AppendLine($"• {b.DisplayName}: {r}"); continue; }

                var target = TargetTransform(b);
                if (target == null || !IsDescendantOf(target, avatar.transform))
                {
                    sb.AppendLine($"• {b.DisplayName}: target is not under the avatar.");
                    continue;
                }
                valid++;
            }

            if (valid == 0)
            {
                reason = "No valid mappings to build." +
                         (sb.Length > 0 ? "\n\n" + sb : "");
                return false;
            }

            reason = sb.Length > 0 ? "Some mappings were skipped:\n\n" + sb : null;
            return true;
        }

        public NeuroBuildResult Build(NeuroBinder binder)
        {
            var avatar = binder.GetComponentInParent<VRCAvatarDescriptor>();
            if (avatar == null) return NeuroBuildResult.Fail("No VRC Avatar Descriptor found.");

            var created = new List<string>();
            string folder = EnsureFolder(GeneratedRoot + "/" + Sanitize(avatar.name));
            string controllerPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/PiEEG_FX.controller");

            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.layers = new AnimatorControllerLayer[0]; // start from a clean slate
            created.Add(controllerPath);

            // name → networkSynced (true wins if any binding wants sync)
            var parameters = new Dictionary<string, bool>();
            int built = 0;

            foreach (var b in binder.bindings)
            {
                if (b == null || !b.enabled || !b.IsValid(out _)) continue;
                var target = TargetTransform(b);
                if (target == null || !IsDescendantOf(target, avatar.transform)) continue;

                string path = AnimationUtility.CalculateTransformPath(target, avatar.transform);
                var ecBinding = NeuroClipFactory.BindingFor(b, path);
                string param = b.ParameterName;
                string layerName = UniqueLayerName(controller, $"PiEEG {b.DisplayName}");

                var samples = NeuroClipFactory.SampleCurve(b.responseCurve);
                var children = new List<(float, Motion)>(samples.Count);
                for (int i = 0; i < samples.Count; i++)
                {
                    float value = NeuroClipFactory.OutputValue(b, samples[i].output);
                    string clipName = $"{layerName} [{samples[i].threshold:0.00}]";
                    var clip = NeuroClipFactory.CreateConstantClip(controller, clipName, ecBinding, value);
                    children.Add((samples[i].threshold, clip));
                }

                NeuroClipFactory.AddBlendTreeLayer(controller, layerName, param, children);

                parameters[param] = parameters.TryGetValue(param, out bool s) ? (s || b.synced) : b.synced;
                built++;
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            // ── Modular Avatar wiring (non-destructive) ──
            var root = avatar.transform;
            var existing = root.Find(ChildObjectName);
            if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);

            var go = new GameObject(ChildObjectName);
            Undo.RegisterCreatedObjectUndo(go, "Build PiEEG Neuro Mappings");
            go.transform.SetParent(root, false);

            var merge = Undo.AddComponent<ModularAvatarMergeAnimator>(go);
            merge.animator = controller;
            merge.layerType = VRCAvatarDescriptor.AnimLayerType.FX;
            merge.pathMode = MergeAnimatorPathMode.Absolute; // we animate existing avatar objects
            merge.matchAvatarWriteDefaults = true;
            merge.deleteAttachedAnimator = false;

            var maParams = Undo.AddComponent<ModularAvatarParameters>(go);
            foreach (var kv in parameters)
            {
                maParams.parameters.Add(new ParameterConfig
                {
                    nameOrPrefix = kv.Key,
                    syncType = ParameterSyncType.Float, // registers in Expression Parameters → OSC can write it
                    localOnly = !kv.Value,              // unsynced params still receive OSC, just not networked
                    saved = false,
                    defaultValue = 0f,
                    hasExplicitDefaultValue = true,
                });
            }

            EditorUtility.SetDirty(go);
            EditorSceneMarkDirty(go);

            string msg =
                $"Built {built} mapping(s) and {parameters.Count} parameter(s).\n" +
                $"Controller: {controllerPath}\n" +
                $"Added \"{ChildObjectName}\" with Modular Avatar Merge Animator + Parameters.\n\n" +
                "Upload as usual — VRChat's OSC will drive the EEG_* parameters from the server bridge.";
            return NeuroBuildResult.Ok(msg, created);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        static Transform TargetTransform(NeuroBinding b)
        {
            if (b.targetType == NeuroTargetType.Blendshape)
                return b.skinnedRenderer != null ? b.skinnedRenderer.transform : null;
            return b.materialRenderer != null ? b.materialRenderer.transform : null;
        }

        static bool IsDescendantOf(Transform t, Transform root)
        {
            for (var c = t; c != null; c = c.parent)
                if (c == root) return true;
            return false;
        }

        static string UniqueLayerName(AnimatorController controller, string baseName)
        {
            var used = new HashSet<string>();
            foreach (var l in controller.layers) used.Add(l.name);
            if (!used.Contains(baseName)) return baseName;
            int i = 2;
            while (used.Contains($"{baseName} {i}")) i++;
            return $"{baseName} {i}";
        }

        static string Sanitize(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name)
                sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
            string s = sb.ToString().Trim('_');
            return string.IsNullOrEmpty(s) ? "Avatar" : s;
        }

        static string EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return path;
            var parts = path.Split('/');
            string current = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
            return current;
        }

        static void EditorSceneMarkDirty(GameObject go)
        {
            if (go != null && !Application.isPlaying)
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
        }
    }
}
#endif
