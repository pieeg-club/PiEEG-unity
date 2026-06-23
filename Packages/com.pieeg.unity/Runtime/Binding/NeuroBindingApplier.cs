using System.Collections.Generic;
using UnityEngine;

namespace PiEEG.Unity
{
    /// <summary>
    /// Applies <see cref="NeuroBinding"/> outputs directly to scene targets (blendshape weights and
    /// material floats) and can capture/restore their original values. Used by both the editor live
    /// preview and the runtime <see cref="NeuroReactor"/> so what you tune is exactly what gets
    /// applied. Material edits use per-renderer <see cref="MaterialPropertyBlock"/>s so shared
    /// material assets are never mutated.
    /// </summary>
    public sealed class NeuroBindingApplier
    {
        struct BlendshapeKey
        {
            public SkinnedMeshRenderer Renderer;
            public int Index;
        }

        readonly Dictionary<SkinnedMeshRenderer, Dictionary<int, float>> _originalBlend =
            new Dictionary<SkinnedMeshRenderer, Dictionary<int, float>>();
        readonly Dictionary<Renderer, MaterialPropertyBlock> _blocks =
            new Dictionary<Renderer, MaterialPropertyBlock>();

        /// <summary>Applies one binding given its already-evaluated output strength in [0,1].</summary>
        public void Apply(NeuroBinding b, float output01)
        {
            if (b == null || !b.enabled) return;

            if (b.targetType == NeuroTargetType.Blendshape)
            {
                var smr = b.skinnedRenderer;
                if (smr == null || smr.sharedMesh == null) return;
                int idx = smr.sharedMesh.GetBlendShapeIndex(b.blendShapeName);
                if (idx < 0) return;

                CaptureBlend(smr, idx);
                smr.SetBlendShapeWeight(idx, Mathf.Clamp01(output01) * 100f);
            }
            else
            {
                var r = b.materialRenderer;
                if (r == null || string.IsNullOrEmpty(b.materialProperty)) return;

                float value = Mathf.Lerp(b.materialMin, b.materialMax, Mathf.Clamp01(output01));
                var mpb = GetBlock(r);
                r.GetPropertyBlock(mpb, b.materialIndex);
                mpb.SetFloat(b.materialProperty, value);
                r.SetPropertyBlock(mpb, b.materialIndex);
            }
        }

        void CaptureBlend(SkinnedMeshRenderer smr, int idx)
        {
            if (!_originalBlend.TryGetValue(smr, out var map))
            {
                map = new Dictionary<int, float>();
                _originalBlend[smr] = map;
            }
            if (!map.ContainsKey(idx))
                map[idx] = smr.GetBlendShapeWeight(idx);
        }

        MaterialPropertyBlock GetBlock(Renderer r)
        {
            if (!_blocks.TryGetValue(r, out var mpb))
            {
                mpb = new MaterialPropertyBlock();
                _blocks[r] = mpb;
            }
            return mpb;
        }

        /// <summary>Restores every blendshape weight and clears material property blocks we touched.</summary>
        public void Restore()
        {
            foreach (var kv in _originalBlend)
            {
                var smr = kv.Key;
                if (smr == null) continue;
                foreach (var w in kv.Value)
                    smr.SetBlendShapeWeight(w.Key, w.Value);
            }
            _originalBlend.Clear();

            foreach (var kv in _blocks)
            {
                var r = kv.Key;
                if (r == null) continue;
                // Empty block clears the per-renderer overrides back to the material defaults.
                r.SetPropertyBlock(null);
            }
            _blocks.Clear();
        }
    }
}
