using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace PiEEG.Unity.Editor
{
    /// <summary>
    /// The Neuro-Binder: a no-code routing table that maps incoming EEG bands to avatar visuals
    /// (blendshapes / material floats) without ever touching the Animator by hand. Tune response
    /// curves with a live in-editor preview of the real signal, then press <b>Build Mappings</b> to
    /// have the SDK Automator generate the parameters, clips, and blend tree (VRChat backend).
    /// </summary>
    public sealed class NeuroBinderWindow : EditorWindow
    {
        const string Uxmlless = "PiEEG Neuro Binder";

        NeuroBinder _binder;
        SerializedObject _so;
        readonly NeuroLivePreview _preview = new NeuroLivePreview();

        // UI refs rebuilt per target.
        ScrollView _rowsContainer;
        VisualElement _statusPill;
        Label _statusLabel;
        Button _previewButton;
        VisualElement _metersRow;
        readonly Dictionary<string, VisualElement> _bandMeterFills = new Dictionary<string, VisualElement>();
        readonly List<(NeuroBinding binding, VisualElement fill, Label value)> _liveRowBars =
            new List<(NeuroBinding, VisualElement, Label)>();

        [MenuItem("Window/PiEEG/Neuro Binder")]
        public static void Open()
        {
            var w = GetWindow<NeuroBinderWindow>();
            w.titleContent = new GUIContent(Uxmlless);
            w.minSize = new Vector2(420, 480);
            w.Show();
        }

        /// <summary>Opens the window already focused on a specific binder.</summary>
        public static void OpenFor(NeuroBinder binder)
        {
            Open();
            var w = GetWindow<NeuroBinderWindow>();
            w.SetTarget(binder);
        }

        void OnEnable()
        {
            _preview.StateChanged += OnPreviewStateChanged;
            rootVisualElement.style.paddingTop = 6;
            rootVisualElement.style.paddingBottom = 6;
            rootVisualElement.style.paddingLeft = 8;
            rootVisualElement.style.paddingRight = 8;
            BuildShell();

            if (_binder == null)
                TryAdoptSelection();
            else
                SetTarget(_binder);

            EditorApplication.update += RepaintMeters;
        }

        void OnDisable()
        {
            EditorApplication.update -= RepaintMeters;
            _preview.StateChanged -= OnPreviewStateChanged;
            _preview.Stop();
        }

        void OnSelectionChange()
        {
            if (_binder == null) TryAdoptSelection();
        }

        void TryAdoptSelection()
        {
            if (Selection.activeGameObject == null) return;
            var b = Selection.activeGameObject.GetComponentInParent<NeuroBinder>();
            if (b != null) SetTarget(b);
        }

        // ── Shell ──────────────────────────────────────────────────────────

        void BuildShell()
        {
            var root = rootVisualElement;
            root.Clear();

            var title = new Label("🧠  Neuro Binder")
            {
                style = { fontSize = 15, unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 2 }
            };
            root.Add(title);
            root.Add(new Label("Route EEG bands to blendshapes & materials — no Animator wiring.")
            {
                style = { fontSize = 11, opacity = 0.7f, marginBottom = 6, whiteSpace = WhiteSpace.Normal }
            });

            // Target selector
            var targetField = new ObjectField("Avatar / Binder")
            {
                objectType = typeof(NeuroBinder),
                allowSceneObjects = true,
                value = _binder,
            };
            targetField.RegisterValueChangedCallback(e => SetTarget(e.newValue as NeuroBinder));
            root.Add(targetField);

            _bodyHost = new VisualElement();
            root.Add(_bodyHost);
        }

        VisualElement _bodyHost;

        void SetTarget(NeuroBinder binder)
        {
            if (_preview.IsRunning) _preview.Stop();
            _binder = binder;
            _so = binder != null ? new SerializedObject(binder) : null;
            BuildBody();
        }

        void BuildBody()
        {
            _bodyHost.Clear();
            _bandMeterFills.Clear();
            _liveRowBars.Clear();

            if (_binder == null)
            {
                _bodyHost.Add(new HelpBox(
                    "Select a GameObject with a Neuro Binder component, or add one to your avatar root " +
                    "(Add Component ▸ PiEEG ▸ Neuro Binder).", HelpBoxMessageType.Info));
                var addBtn = new Button(AddBinderToSelection) { text = "Add Neuro Binder to selected GameObject" };
                addBtn.style.marginTop = 4;
                _bodyHost.Add(addBtn);
                return;
            }

            // Connection settings
            var conn = new Foldout { text = "Connection", value = true };
            var url = new TextField("Server URL") { bindingPath = "serverUrl" };
            url.tooltip = "PiEEG-server WebSocket URL used by the live preview. Default port 1616.";
            var rate = new IntegerField("Sample Rate (Hz)") { bindingPath = "sampleRate" };
            var smooth = new Slider("Smoothing", 0f, 0.95f) { bindingPath = "smoothing" };
            conn.Add(url);
            conn.Add(rate);
            conn.Add(smooth);
            _bodyHost.Add(conn);

            // Live preview controls
            BuildPreviewBar();

            // Routing table header
            var tableHeader = new Label("Routing table")
            {
                style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 8, marginBottom = 2 }
            };
            _bodyHost.Add(tableHeader);

            _rowsContainer = new ScrollView(ScrollViewMode.Vertical)
            {
                style = { flexGrow = 1, minHeight = 120 }
            };
            _bodyHost.Add(_rowsContainer);
            RebuildRows();

            var addRow = new Button(AddBinding) { text = "＋ Add Mapping" };
            addRow.style.marginTop = 4;
            _bodyHost.Add(addRow);

            // Build footer
            BuildFooter();

            _bodyHost.Bind(_so);
        }

        void BuildPreviewBar()
        {
            var box = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    marginTop = 6, paddingTop = 6, paddingBottom = 6, paddingLeft = 6, paddingRight = 6,
                    borderTopLeftRadius = 4, borderTopRightRadius = 4,
                    borderBottomLeftRadius = 4, borderBottomRightRadius = 4,
                    backgroundColor = new Color(0f, 0f, 0f, 0.15f),
                }
            };

            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            _previewButton = new Button(TogglePreview) { text = "▶ Live Preview" };
            _previewButton.style.marginRight = 8;
            row.Add(_previewButton);

            _statusPill = new VisualElement
            {
                style =
                {
                    width = 10, height = 10, borderTopLeftRadius = 5, borderTopRightRadius = 5,
                    borderBottomLeftRadius = 5, borderBottomRightRadius = 5,
                    marginRight = 5, backgroundColor = new Color(0.5f, 0.5f, 0.5f),
                }
            };
            row.Add(_statusPill);
            _statusLabel = new Label("Idle") { style = { fontSize = 11, opacity = 0.85f } };
            row.Add(_statusLabel);
            box.Add(row);

            // Band meters
            _metersRow = new VisualElement
            {
                style = { flexDirection = FlexDirection.Row, marginTop = 6, display = DisplayStyle.None }
            };
            foreach (var band in EegBands.All)
            {
                var col = new VisualElement { style = { flexGrow = 1, marginRight = 4, alignItems = Align.Center } };
                var track = new VisualElement
                {
                    style =
                    {
                        height = 40, width = 14, justifyContent = Justify.FlexEnd,
                        backgroundColor = new Color(0f, 0f, 0f, 0.3f),
                        borderTopLeftRadius = 3, borderTopRightRadius = 3,
                        borderBottomLeftRadius = 3, borderBottomRightRadius = 3,
                    }
                };
                var fill = new VisualElement
                {
                    style =
                    {
                        height = 0, width = 14, backgroundColor = BandColor(band.Name),
                        borderBottomLeftRadius = 3, borderBottomRightRadius = 3,
                    }
                };
                track.Add(fill);
                col.Add(track);
                col.Add(new Label(band.Name.Substring(0, 1)) { style = { fontSize = 9, opacity = 0.7f } });
                _metersRow.Add(col);
                _bandMeterFills[band.DefaultParameter] = fill;
            }
            box.Add(_metersRow);

            _bodyHost.Add(box);
        }

        void BuildFooter()
        {
            var footer = new VisualElement { style = { marginTop = 8 } };

            if (NeuroMappingBuilderRegistry.HasBuilder)
            {
                var builder = NeuroMappingBuilderRegistry.Active;
                var buildBtn = new Button(BuildMappings)
                {
                    text = $"⚙  Build Mappings  ·  {builder.DisplayName}",
                };
                buildBtn.style.height = 30;
                buildBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
                footer.Add(buildBtn);
                footer.Add(new Label("Non-destructive: generates an isolated sub-animator and merges it at build time.")
                {
                    style = { fontSize = 10, opacity = 0.6f, marginTop = 2, whiteSpace = WhiteSpace.Normal }
                });
            }
            else
            {
                footer.Add(new HelpBox(
                    "No avatar SDK backend detected. Install the VRChat Avatars SDK and Modular Avatar " +
                    "to enable one-click \"Build Mappings\". The live preview and the runtime Neuro " +
                    "Reactor work without them.", HelpBoxMessageType.Info));
            }

            _bodyHost.Add(footer);
        }

        // ── Rows ───────────────────────────────────────────────────────────

        void RebuildRows()
        {
            _rowsContainer.Clear();
            _liveRowBars.Clear();
            if (_so == null) return;
            _so.Update();

            var list = _so.FindProperty("bindings");
            if (list.arraySize == 0)
            {
                _rowsContainer.Add(new HelpBox("No mappings yet. Press “Add Mapping”.", HelpBoxMessageType.None));
                return;
            }

            for (int i = 0; i < list.arraySize; i++)
                _rowsContainer.Add(BuildRow(list.GetArrayElementAtIndex(i), i));
        }

        VisualElement BuildRow(SerializedProperty prop, int index)
        {
            var binding = _binder.bindings[index];

            var card = new VisualElement
            {
                style =
                {
                    marginBottom = 6, paddingTop = 4, paddingBottom = 6, paddingLeft = 6, paddingRight = 6,
                    backgroundColor = new Color(1f, 1f, 1f, 0.04f),
                    borderTopLeftRadius = 4, borderTopRightRadius = 4,
                    borderBottomLeftRadius = 4, borderBottomRightRadius = 4,
                    borderLeftWidth = 3, borderLeftColor = BandColor(BandFromParam(binding.ParameterName)),
                }
            };

            // Header line
            var header = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            var enabledToggle = new Toggle();
            enabledToggle.BindProperty(prop.FindPropertyRelative("enabled"));
            header.Add(enabledToggle);

            var titleLabel = new Label(binding.DisplayName)
            {
                style = { flexGrow = 1, unityFontStyleAndWeight = FontStyle.Bold, marginLeft = 2 }
            };
            header.Add(titleLabel);

            // Live value bar (per row)
            var barTrack = new VisualElement
            {
                style =
                {
                    width = 60, height = 8, marginRight = 6,
                    backgroundColor = new Color(0, 0, 0, 0.3f),
                    borderTopLeftRadius = 4, borderBottomLeftRadius = 4,
                    borderTopRightRadius = 4, borderBottomRightRadius = 4,
                    display = _preview.IsRunning ? DisplayStyle.Flex : DisplayStyle.None,
                }
            };
            var barFill = new VisualElement
            {
                style =
                {
                    width = 0, height = 8, backgroundColor = BandColor(BandFromParam(binding.ParameterName)),
                    borderTopLeftRadius = 4, borderBottomLeftRadius = 4,
                }
            };
            barTrack.Add(barFill);
            header.Add(barTrack);
            var valueLabel = new Label("") { style = { width = 26, fontSize = 10, opacity = 0.7f,
                display = _preview.IsRunning ? DisplayStyle.Flex : DisplayStyle.None } };
            header.Add(valueLabel);
            _liveRowBars.Add((binding, barFill, valueLabel));

            int captured = index;
            var removeBtn = new Button(() => RemoveBinding(captured)) { text = "✕" };
            removeBtn.style.width = 22;
            header.Add(removeBtn);
            card.Add(header);

            // Source
            var sourceChoices = new List<string>();
            foreach (var band in EegBands.All) sourceChoices.Add(band.DefaultParameter);
            sourceChoices.Add("Custom…");
            string current = binding.sourceId;
            bool known = sourceChoices.Contains(current);
            var sourceDrop = new DropdownField("Source", sourceChoices, known ? current : "Custom…");
            sourceDrop.tooltip = "EEG parameter the VRChat OSC bridge emits. Must match for OSC to deliver data.";
            var customField = new TextField("Parameter name") { value = binding.sourceId,
                style = { display = known ? DisplayStyle.None : DisplayStyle.Flex } };
            sourceDrop.RegisterValueChangedCallback(e =>
            {
                if (e.newValue == "Custom…")
                {
                    customField.style.display = DisplayStyle.Flex;
                }
                else
                {
                    customField.style.display = DisplayStyle.None;
                    ApplyString(prop, "sourceId", e.newValue);
                    ApplyString(prop, "parameterNameOverride", "");
                    titleLabel.text = binding.DisplayName;
                    card.style.borderLeftColor = BandColor(BandFromParam(binding.ParameterName));
                }
            });
            customField.RegisterValueChangedCallback(e =>
            {
                ApplyString(prop, "sourceId", e.newValue);
                titleLabel.text = binding.DisplayName;
            });
            card.Add(sourceDrop);
            card.Add(customField);

            // Response curve
            var curve = new CurveField("Response") { tooltip = "x = signal (0..1), y = output strength (0..1)." };
            curve.BindProperty(prop.FindPropertyRelative("responseCurve"));
            card.Add(curve);

            // Target type
            var targetTypeProp = prop.FindPropertyRelative("targetType");
            var typeField = new EnumField("Target", binding.targetType);
            typeField.BindProperty(targetTypeProp);
            card.Add(typeField);

            // Target detail host (rebuilt on type change)
            var detail = new VisualElement();
            card.Add(detail);
            BuildTargetDetail(detail, prop, binding, titleLabel);
            typeField.RegisterValueChangedCallback(_ =>
            {
                _so.ApplyModifiedProperties();
                BuildTargetDetail(detail, prop, binding, titleLabel);
            });

            // Synced toggle
            var synced = new Toggle("Network sync (remote players see it)");
            synced.BindProperty(prop.FindPropertyRelative("synced"));
            synced.style.marginTop = 2;
            card.Add(synced);

            return card;
        }

        void BuildTargetDetail(VisualElement host, SerializedProperty prop, NeuroBinding binding, Label titleLabel)
        {
            host.Clear();
            if (binding.targetType == NeuroTargetType.Blendshape)
            {
                var smr = new ObjectField("Skinned Mesh") { objectType = typeof(SkinnedMeshRenderer), allowSceneObjects = true };
                smr.BindProperty(prop.FindPropertyRelative("skinnedRenderer"));
                host.Add(smr);

                var blendHost = new VisualElement();
                host.Add(blendHost);
                RebuildBlendshapeDropdown(blendHost, prop, binding, titleLabel);
                smr.RegisterValueChangedCallback(_ =>
                {
                    _so.ApplyModifiedProperties();
                    RebuildBlendshapeDropdown(blendHost, prop, binding, titleLabel);
                });
            }
            else
            {
                var r = new ObjectField("Renderer") { objectType = typeof(Renderer), allowSceneObjects = true };
                r.BindProperty(prop.FindPropertyRelative("materialRenderer"));
                host.Add(r);

                var matIndex = new IntegerField("Material Index");
                matIndex.BindProperty(prop.FindPropertyRelative("materialIndex"));
                host.Add(matIndex);

                var propHost = new VisualElement();
                host.Add(propHost);
                RebuildMaterialPropDropdown(propHost, prop, binding, titleLabel);
                r.RegisterValueChangedCallback(_ =>
                {
                    _so.ApplyModifiedProperties();
                    RebuildMaterialPropDropdown(propHost, prop, binding, titleLabel);
                });
                matIndex.RegisterValueChangedCallback(_ =>
                {
                    _so.ApplyModifiedProperties();
                    RebuildMaterialPropDropdown(propHost, prop, binding, titleLabel);
                });

                var min = new FloatField("Output Min");
                min.BindProperty(prop.FindPropertyRelative("materialMin"));
                host.Add(min);
                var max = new FloatField("Output Max");
                max.BindProperty(prop.FindPropertyRelative("materialMax"));
                host.Add(max);
            }
        }

        void RebuildBlendshapeDropdown(VisualElement host, SerializedProperty prop, NeuroBinding binding, Label titleLabel)
        {
            host.Clear();
            var smr = binding.skinnedRenderer;
            if (smr == null || smr.sharedMesh == null)
            {
                host.Add(new Label("Assign a Skinned Mesh Renderer to pick a blendshape.")
                { style = { fontSize = 10, opacity = 0.6f } });
                return;
            }
            var names = new List<string>();
            for (int i = 0; i < smr.sharedMesh.blendShapeCount; i++)
                names.Add(smr.sharedMesh.GetBlendShapeName(i));
            if (names.Count == 0)
            {
                host.Add(new HelpBox("This mesh has no blendshapes.", HelpBoxMessageType.Warning));
                return;
            }
            string cur = binding.blendShapeName;
            var drop = new DropdownField("Blendshape", names, names.Contains(cur) ? cur : names[0]);
            if (!names.Contains(cur)) ApplyString(prop, "blendShapeName", names[0]);
            drop.RegisterValueChangedCallback(e =>
            {
                ApplyString(prop, "blendShapeName", e.newValue);
                titleLabel.text = binding.DisplayName;
            });
            host.Add(drop);
        }

        void RebuildMaterialPropDropdown(VisualElement host, SerializedProperty prop, NeuroBinding binding, Label titleLabel)
        {
            host.Clear();
            var props = TryGetFloatShaderProperties(binding);
            if (props == null || props.Count == 0)
            {
                var tf = new TextField("Material Property") { value = binding.materialProperty };
                tf.tooltip = "Shader float property, e.g. _EmissionStrength.";
                tf.RegisterValueChangedCallback(e =>
                {
                    ApplyString(prop, "materialProperty", e.newValue);
                    titleLabel.text = binding.DisplayName;
                });
                host.Add(tf);
                return;
            }
            props.Add("Custom…");
            string cur = binding.materialProperty;
            bool known = props.Contains(cur);
            var drop = new DropdownField("Material Property", props, known ? cur : "Custom…");
            var custom = new TextField("Custom property") { value = cur,
                style = { display = known ? DisplayStyle.None : DisplayStyle.Flex } };
            drop.RegisterValueChangedCallback(e =>
            {
                if (e.newValue == "Custom…") { custom.style.display = DisplayStyle.Flex; }
                else
                {
                    custom.style.display = DisplayStyle.None;
                    ApplyString(prop, "materialProperty", e.newValue);
                    titleLabel.text = binding.DisplayName;
                }
            });
            custom.RegisterValueChangedCallback(e =>
            {
                ApplyString(prop, "materialProperty", e.newValue);
                titleLabel.text = binding.DisplayName;
            });
            host.Add(drop);
            host.Add(custom);
        }

        static List<string> TryGetFloatShaderProperties(NeuroBinding binding)
        {
            var r = binding.materialRenderer;
            if (r == null) return null;
            var mats = r.sharedMaterials;
            if (mats == null || binding.materialIndex < 0 || binding.materialIndex >= mats.Length) return null;
            var mat = mats[binding.materialIndex];
            if (mat == null || mat.shader == null) return null;

            var result = new List<string>();
            int count = ShaderUtil.GetPropertyCount(mat.shader);
            for (int i = 0; i < count; i++)
            {
                var t = ShaderUtil.GetPropertyType(mat.shader, i);
                if (t == ShaderUtil.ShaderPropertyType.Float || t == ShaderUtil.ShaderPropertyType.Range)
                    result.Add(ShaderUtil.GetPropertyName(mat.shader, i));
            }
            return result;
        }

        // ── Actions ────────────────────────────────────────────────────────

        void AddBinding()
        {
            Undo.RecordObject(_binder, "Add Mapping");
            _binder.AddBinding();
            EditorUtility.SetDirty(_binder);
            _so.Update();
            RebuildRows();
        }

        void RemoveBinding(int index)
        {
            Undo.RecordObject(_binder, "Remove Mapping");
            if (index >= 0 && index < _binder.bindings.Count)
                _binder.bindings.RemoveAt(index);
            EditorUtility.SetDirty(_binder);
            _so.Update();
            RebuildRows();
        }

        void AddBinderToSelection()
        {
            var go = Selection.activeGameObject;
            if (go == null)
            {
                EditorUtility.DisplayDialog("Neuro Binder", "Select a GameObject (your avatar root) first.", "OK");
                return;
            }
            var b = go.GetComponent<NeuroBinder>();
            if (b == null)
            {
                b = Undo.AddComponent<NeuroBinder>(go);
            }
            SetTarget(b);
        }

        void BuildMappings()
        {
            var builder = NeuroMappingBuilderRegistry.Active;
            if (builder == null) return;
            _so.ApplyModifiedProperties();

            if (!builder.CanBuild(_binder, out string reason))
            {
                EditorUtility.DisplayDialog("Build Mappings", "Cannot build:\n\n" + reason, "OK");
                return;
            }

            var result = builder.Build(_binder);
            EditorUtility.DisplayDialog("Build Mappings",
                (result.Success ? "✅ " : "❌ ") + result.Message, "OK");
            if (result.Success && result.CreatedAssetPaths != null && result.CreatedAssetPaths.Count > 0)
            {
                var first = AssetDatabase.LoadMainAssetAtPath(result.CreatedAssetPaths[0]);
                if (first != null) EditorGUIUtility.PingObject(first);
            }
        }

        // ── Preview wiring ──────────────────────────────────────────────────

        void TogglePreview()
        {
            if (_preview.IsRunning) _preview.Stop();
            else _preview.Start(_binder);
            UpdatePreviewVisibility();
        }

        void OnPreviewStateChanged()
        {
            UpdateStatus();
        }

        void UpdatePreviewVisibility()
        {
            bool on = _preview.IsRunning;
            if (_previewButton != null) _previewButton.text = on ? "■ Stop Preview" : "▶ Live Preview";
            if (_metersRow != null) _metersRow.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            foreach (var (_, fill, value) in _liveRowBars)
            {
                fill.parent.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
                value.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            }
            UpdateStatus();
        }

        void UpdateStatus()
        {
            if (_statusPill == null) return;
            Color c;
            string t;
            if (!_preview.IsRunning) { c = new Color(0.5f, 0.5f, 0.5f); t = "Idle"; }
            else if (!_preview.IsConnected) { c = new Color(0.9f, 0.5f, 0.1f); t = "Connecting…"; }
            else if (_preview.Warming) { c = new Color(0.9f, 0.8f, 0.1f); t = "Warming up FFT…"; }
            else { c = new Color(0.2f, 0.8f, 0.3f); t = "Live"; }

            if (!string.IsNullOrEmpty(_preview.LastError) && _preview.IsRunning && !_preview.IsConnected)
                t += $"  ({_preview.LastError})";

            _statusPill.style.backgroundColor = c;
            _statusLabel.text = t;
        }

        void RepaintMeters()
        {
            if (!_preview.IsRunning || _preview.Analyzer == null || !_preview.Analyzer.HasData) return;
            var values = _preview.Analyzer.Values;

            foreach (var kv in _bandMeterFills)
            {
                float v = values.TryGetValue(kv.Key, out var f) ? f : 0f;
                kv.Value.style.height = Mathf.Clamp01(v) * 40f;
            }

            foreach (var (binding, fill, value) in _liveRowBars)
            {
                if (binding == null) continue;
                float src = _preview.Analyzer.Get(binding.ParameterName);
                float outp = binding.Evaluate(src);
                fill.style.width = Mathf.Clamp01(outp) * 60f;
                value.text = outp.ToString("0.00");
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────

        void ApplyString(SerializedProperty rowProp, string field, string value)
        {
            var p = rowProp.FindPropertyRelative(field);
            p.stringValue = value;
            _so.ApplyModifiedProperties();
        }

        static string BandFromParam(string param)
        {
            foreach (var b in EegBands.All)
                if (param != null && param.EndsWith(b.Name, System.StringComparison.OrdinalIgnoreCase))
                    return b.Name;
            return "Alpha";
        }

        static Color BandColor(string bandName)
        {
            switch (bandName)
            {
                case "Delta": return new Color(0.545f, 0.361f, 0.965f);
                case "Theta": return new Color(0.024f, 0.714f, 0.831f);
                case "Alpha": return new Color(0.133f, 0.773f, 0.369f);
                case "Beta": return new Color(0.961f, 0.620f, 0.043f);
                case "Gamma": return new Color(0.937f, 0.267f, 0.267f);
                default: return new Color(0.6f, 0.6f, 0.6f);
            }
        }
    }
}
