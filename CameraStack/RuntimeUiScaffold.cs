using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace FM26Mods.CommonUI
{
    // Small reusable helper for runtime UI Toolkit scaffolding in IL2CPP/BepInEx mods.
    // Responsibilities:
    // - Find an existing top-most UIDocument in the active scenes, or create a private one as fallback.
    // - Expose the rootVisualElement and basic element creation helpers.
    // - Keep references so the created fallback GO can be destroyed cleanly.
    public class RuntimeUiScaffold : IDisposable
    {
        private readonly string _fallbackGoName;
        private readonly int _fallbackSortingOrder;
        private GameObject _go;                 // only when we create our own document
        private UIDocument _doc;                // the attached or created document
        private PanelSettings _panelSettings;   // only for fallback document
        private bool _attachedToExisting;

        public RuntimeUiScaffold(string fallbackGoName = "Runtime_UI", int fallbackSortingOrder = 100000)
        {
            _fallbackGoName = fallbackGoName;
            _fallbackSortingOrder = fallbackSortingOrder;
        }

        public UIDocument Document => _doc;
        public VisualElement Root => _doc != null ? _doc.rootVisualElement : null;
        public bool AttachedToExisting => _attachedToExisting;

        // Ensures there is a UIDocument: preferExisting tries to attach to any active scene UIDocument with highest sorting order.
        public void EnsureDocument(bool preferExisting = true)
        {
            if (_doc != null) return;

            if (preferExisting)
            {
                _doc = FindBestExistingUiDocument();
                _attachedToExisting = _doc != null;
            }

            if (_doc == null)
            {
                _go = new GameObject(_fallbackGoName);
                _doc = _go.AddComponent<UIDocument>();
                _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                _panelSettings.clearDepthStencil = true;
                _panelSettings.scale = 1.0f;
                _panelSettings.referenceDpi = 96;
                _panelSettings.fallbackDpi = 96;
                _panelSettings.targetDisplay = 0;
                _panelSettings.sortingOrder = _fallbackSortingOrder;
                _doc.panelSettings = _panelSettings;
                try { _doc.sortingOrder = _fallbackSortingOrder; } catch { }
            }

            if (_doc.rootVisualElement != null)
            {
                _doc.rootVisualElement.style.flexGrow = 1;
            }
        }

        public VisualElement CreateContainer(string name,
            float? leftPercent = null,
            float? topPixels = null,
            PickingMode picking = PickingMode.Position)
        {
            var ve = new VisualElement
            {
                name = name,
                pickingMode = picking
            };
            ve.style.position = Position.Absolute;
            if (leftPercent.HasValue)
                ve.style.left = new Length(leftPercent.Value, LengthUnit.Percent);
            if (topPixels.HasValue)
                ve.style.top = topPixels.Value;
            ve.style.paddingLeft = 6;
            ve.style.paddingRight = 6;
            ve.style.paddingTop = 6;
            ve.style.paddingBottom = 6;
            return ve;
        }

        public Button CreateButton(string text, Action onClick, string name = null)
        {
            var btn = new Button(onClick)
            {
                text = text
            };
            if (!string.IsNullOrEmpty(name)) btn.name = name;
            btn.style.position = Position.Relative;
            btn.style.paddingLeft = 8;
            btn.style.paddingRight = 8;
            btn.style.paddingTop = 6;
            btn.style.paddingBottom = 6;
            btn.pickingMode = PickingMode.Position;
            return btn;
        }

        public static UIDocument FindBestExistingUiDocument()
        {
            try
            {
                var docs = GameObject.FindObjectsOfType<UIDocument>();
                UIDocument best = null;
                int bestOrder = int.MinValue;
                foreach (var doc in docs)
                {
                    if (doc == null) continue;
                    var go = doc.gameObject;
                    if (go == null || !go.scene.IsValid()) continue;
                    if (!go.activeInHierarchy || !doc.isActiveAndEnabled) continue;

                    int order = 0;
                    try { order = (int)doc.sortingOrder; } catch { order = 0; }
                    if (best == null || order > bestOrder)
                    {
                        best = doc;
                        bestOrder = order;
                    }
                }
                return best;
            }
            catch
            {
                return null;
            }
        }

        public void Dispose()
        {
            try
            {
                // We do not destroy existing documents; only our fallback GO.
                if (_go != null)
                {
                    try { UnityEngine.Object.Destroy(_go); } catch { }
                    _go = null;
                }
                _doc = null;
                _panelSettings = null;
            }
            catch { }
        }
    }
}
