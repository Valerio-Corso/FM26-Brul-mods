using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using FM.Match;
using FM.Match.Camera;
using FM.Match.Simulation;
using FM.UI;
using SI.Bindable;
using UnityEngine;
using UnityEngine.UIElements;
using FM.UI.MatchSimulation;
using Il2CppInterop.Runtime;
using UnityEngine.InputSystem;
using Panel = SI.Bindable.Panel;

namespace MatchUiOverlays
{
    // Finds a UI Toolkit widget by its concrete type and forces it (and its parents) visible,
    // then wires current Match2DSimulation data into it.
    public class ForceOverviewScreenControllerByType : MonoBehaviour
    {
        private PanelManager _manager;
        private Match2DSimulation _simulation;
        private FMMatchSimulationWidget _cachedWidget;
        private UIDocument _sourceDoc;
        private bool _disabledPathsOnce;
        private float _lastTryTime;
        private const float TryInterval = 0.25f;

        // Paths to disable (hide) when we enable our target widget
        private const string Path_OverviewLeftSide = "DefaultLayerSettings/PanelManager-container/Menu/Body/OverviewPrototype//BindingSetter/BindingRemapper/BindableSwitchElement_IsInOverview/MatchExperienceGrid////GridLayoutElementContent//OverviewLeftSide";
        private const string Path_MatchStats       = "DefaultLayerSettings/PanelManager-container/Menu/Body/OverviewPrototype//BindingSetter/BindingRemapper/BindableSwitchElement_IsInOverview/MatchExperienceGrid////GridLayoutElementContent//MatchStats";
        private const string Path_MatchEvents      = "DefaultLayerSettings/PanelManager-container/Menu/Body/OverviewPrototype//BindingSetter/BindingRemapper/BindableSwitchElement_IsInOverview/MatchExperienceGrid////GridLayoutElementContent//MatchEvents";
        private const string Path_OverviewRightSide= "DefaultLayerSettings/PanelManager-container/Menu/Body/OverviewPrototype//BindingSetter/BindingRemapper/BindableSwitchElement_IsInOverview/MatchExperienceGrid////GridLayoutElementContent//OverviewRightSide";

        public ForceOverviewScreenControllerByType(IntPtr ptr) : base(ptr) { }

        private void Start()
        {
            TryInitRefs();
            SetupPanelEventListeners();
        }

        private void SetupPanelEventListeners()
        {
            if (_manager == null) return;

            try
            {
                var openedHandler = OnPanelOpened;
                Action<Panel> closedHandler = OnPanelClosed;
            
                _manager.m_onPanelOpened += DelegateSupport.ConvertDelegate<PanelManager.PanelOpenedDelegate>(openedHandler);
                _manager.m_onPanelClosed += DelegateSupport.ConvertDelegate<PanelManager.PanelClosedDelegate>(closedHandler);
            }
            catch (Exception ex)
            {
                MatchUiOverlayPlugin.LOG?.LogError($"Failed to setup panel event listeners: {ex}");
            }

            // if (_simulation != null)
            // {
            //     try
            //     {
            //         void HighlightHandler(MatchShowHighlightEvent evt)
            //         {
            //             MatchUiOverlayPlugin.LOG?.LogInfo($"Highlight event received!");
            //         }
            //
            //         void HighlightHandler(MatchShowHighlightEvent evt)
            //         {
            //             MatchUiOverlayPlugin.LOG?.LogInfo($"Highlight event received!");
            //         }
            //
            //         _simulation.m_eventSubsystem.Register<MatchShowHighlightEvent>((Action<MatchShowHighlightEvent>)HighlightHandler);
            //         _simulation.m_eventSubsystem.Register<MatchShowOverviewEvent>((Action<MatchShowOverviewEvent>)HighlightHandler);
            //     }
            //     catch (Exception ex)
            //     {
            //         MatchUiOverlayPlugin.LOG?.LogError($"Failed to register highlight event: {ex}");
            //     }
            // }
        }

        private void OnPanelOpened(Panel panel)
        {
            try
            {
                MatchUiOverlayPlugin.LOG?.LogInfo($"Panel Opened: {panel?.name ?? "null"}");
            }
            catch (Exception ex)
            {
                MatchUiOverlayPlugin.LOG?.LogError($"OnPanelOpened error: {ex}");
            }
        }

        private void OnPanelClosed(Panel panel)
        {
            try
            {
                MatchUiOverlayPlugin.LOG?.LogInfo($"Panel Closed: {panel?.name ?? "null"}");
            }
            catch (Exception ex)
            {
                MatchUiOverlayPlugin.LOG?.LogError($"OnPanelClosed error: {ex}");
            }
        }

        private void TryInitRefs()
        {
            if (_manager == null)
                _manager = UnityEngine.Object.FindObjectOfType<PanelManager>();
            if (_simulation == null)
                _simulation = UnityEngine.Object.FindObjectOfType<Match2DSimulation>();
        }

        private static string[] SplitTokens(string path)
        {
            if (string.IsNullOrEmpty(path)) return Array.Empty<string>();
            return path.Split(new[] {'/'}, StringSplitOptions.RemoveEmptyEntries);
        }

        private static VisualElement FindByPath(VisualElement root, string path, out int failedAtIndex)
        {
            failedAtIndex = -1;
            if (root == null || string.IsNullOrEmpty(path)) return null;
            var tokens = SplitTokens(path);
            VisualElement cur = root;
            for (int i = 0; i < tokens.Length; i++)
            {
                var token = tokens[i];
                var next = cur.Q<VisualElement>(name: token);
                if (next == null)
                {
                    failedAtIndex = i;
                    return null;
                }
                cur = next;
            }
            return cur;
        }

        private void DisableOtherOverviewPaths(VisualElement root)
        {
            // First try exact/partial paths (if they happen to match this document's structure)
            TryDisablePath(root, Path_OverviewLeftSide);
            TryDisablePath(root, Path_MatchStats);
            TryDisablePath(root, Path_MatchEvents);
            TryDisablePath(root, Path_OverviewRightSide);

            // Then, as a robust fallback (since provided paths can be partial),
            // sweep the tree and hide by final element name across the whole document.
            TryDisableByNameSweep(root, "OverviewLeftSide");
            TryDisableByNameSweep(root, "MatchStats");
            TryDisableByNameSweep(root, "MatchEvents");
            TryDisableByNameSweep(root, "OverviewRightSide");
        }

        private void TryDisablePath(VisualElement root, string path)
        {
            try
            {
                int failedAt;
                var ve = FindByPath(root, path, out failedAt);
                if (ve != null)
                {
                    // Log concrete type for future type-based lookups
                    try
                    {
                        var typeName = ve.GetType().FullName;
                        MatchUiOverlayPlugin.LOG?.LogInfo($"Disable path hit: '{path}' -> type '{typeName}'");
                    }
                    catch { }

                    // Hide safely
                    HideElement(ve);
                }
                else
                {
                    if (failedAt >= 0)
                    {
                        var tokens = SplitTokens(path);
                        string missing = failedAt < tokens.Length ? tokens[failedAt] : "(end)";
                        try { MatchUiOverlayPlugin.LOG?.LogInfo($"Disable path miss: '{path}' first missing token='{missing}'"); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                try { MatchUiOverlayPlugin.LOG?.LogWarning($"TryDisablePath error for '{path}': {ex.Message}"); } catch { }
            }
        }

        private static void HideElement(VisualElement ve)
        {
            try
            {
                ve.style.display = DisplayStyle.None;
                ve.style.visibility = Visibility.Hidden;
                ve.style.opacity = 0f;
                ve.pickingMode = PickingMode.Ignore;
                ve.SetEnabled(false);
            }
            catch { }
        }

        private void TryDisableByNameSweep(VisualElement root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name)) return;
            try
            {
                var matches = FindAllByName(root, name);
                if (matches.Count == 0)
                {
                    try { MatchUiOverlayPlugin.LOG?.LogInfo($"Disable sweep: No elements named '{name}' found in this UIDocument."); } catch { }
                    return;
                }

                foreach (var ve in matches)
                {
                    // Log name, type, and a shorthand path
                    try
                    {
                        var typeName = ve.GetType().FullName;
                        var shortPath = BuildShortPath(ve, maxDepth: 8);
                        MatchUiOverlayPlugin.LOG?.LogInfo($"Disable sweep hit name='{name}' type='{typeName}' path='{shortPath}'");
                    }
                    catch { }
                    HideElement(ve);
                }
            }
            catch (Exception ex)
            {
                try { MatchUiOverlayPlugin.LOG?.LogWarning($"Disable sweep error for name '{name}': {ex.Message}"); } catch { }
            }
        }

        private static List<VisualElement> FindAllByName(VisualElement root, string name)
        {
            var result = new List<VisualElement>(8);
            try
            {
                // Non-recursive DFS with a small stack for IL2CPP safety
                var stack = new Stack<VisualElement>();
                stack.Push(root);
                int guard = 0;
                while (stack.Count > 0 && guard++ < 50000)
                {
                    var cur = stack.Pop();
                    if (cur == null) continue;
                    try
                    {
                        if (cur.name == name)
                            result.Add(cur);
                    }
                    catch { }

                    // Push children
                    try
                    {
                        int cc = cur.childCount;
                        for (int i = 0; i < cc; i++)
                        {
                            var c = cur.ElementAt(i);
                            if (c != null) stack.Push(c);
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return result;
        }

        private static string BuildShortPath(VisualElement ve, int maxDepth = 6)
        {
            try
            {
                var parts = new List<string>(maxDepth);
                var cur = ve;
                int depth = 0;
                while (cur != null && depth++ < maxDepth)
                {
                    parts.Add(cur.name);
                    cur = cur.parent;
                }
                parts.Reverse();
                var path = string.Join('/', parts);
                if (ve.parent != null && ve.parent.parent != null) return ".../" + path;
                return path;
            }
            catch { return ve != null ? ve.name : string.Empty; }
        }

        FMMatchUIComponent uiComponent;
        public void Update()
        {
            try
            {
                if (uiComponent == null)
                {
                    uiComponent = FindObjectOfType<FMMatchUIComponent>();
                }
            }
            catch (Exception ex)
            {
                
            }

            if (uiComponent != null)
            {
                if (Keyboard.current != null && Keyboard.current[Key.D].wasPressedThisFrame) {
                    GameBackgroundModule t;
                    MatchBootstrap b;
                    MatchStateCache f;
                    MatchVisualizationMode m;
                    MatchPlaybackController s;
                    CameraDirectorComponent cd;
                    // FM.Match.
                    // _manager.open

                    // uiComponent.m_matchDebugControls.SetActive(true);
                }
            }

            try
            {
                if (Time.unscaledTime - _lastTryTime < TryInterval) return;
                _lastTryTime = Time.unscaledTime;

                TryInitRefs();
                if (_manager == null) return;

                // If we already cached the widget, keep it alive and visible
                if (_cachedWidget != null)
                {
                    if (!_disabledPathsOnce && _sourceDoc != null && _sourceDoc.rootVisualElement != null)
                    {
                        DisableOtherOverviewPaths(_sourceDoc.rootVisualElement);
                        _disabledPathsOnce = true;
                    }
                    WireAndShow(_cachedWidget);
                    return;
                }

                // Scan all active UIDocuments and look up by type directly
                var docs = UnityEngine.Object.FindObjectsOfType<UIDocument>();
                if (docs == null || docs.Length == 0) return;

                foreach (var doc in docs)
                {
                    var root = doc?.rootVisualElement;
                    if (root == null) continue;

                    var widget = root.Q<FMMatchSimulationWidget>();
                    if (widget != null)
                    {
                        _cachedWidget = widget;
                        _sourceDoc = doc;
                        DisableOtherOverviewPaths(root);
                        _disabledPathsOnce = true;
                        WireAndShow(widget);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                try { MatchUiOverlayPlugin.LOG?.LogError($"ForceOverviewScreenControllerByType Update error: {ex}"); } catch { Debug.LogError(ex.ToString()); }
            }
        }

        private void WireAndShow(FMMatchSimulationWidget widget)
        {
            if (widget == null) return;

            // Ensure visible and enabled in hierarchy
            ForceElementAndParentsVisible(widget);

            // If we have simulation data, wire it
            var data = _simulation != null ? _simulation.m_data : null;
            try
            {
                if (data != null)
                {
                    widget.SetSimulationData(data);
                }
            }
            catch (Exception ex)
            {
                try { MatchUiOverlayPlugin.LOG?.LogWarning($"SetSimulationData failed: {ex.Message}"); } catch { }
            }
        }

        private void ForceElementAndParentsVisible(VisualElement ve)
        {
            if (ve == null) return;
            try
            {
                // enable this element
                ve.style.display = DisplayStyle.Flex;
                ve.style.visibility = Visibility.Visible;
                ve.SetEnabled(true);

                // also ensure parents are visible to actually render the element
                var parent = ve.hierarchy?.parent;
                int safety = 0;
                while (parent != null && safety++ < 256)
                {
                    parent.style.display = DisplayStyle.Flex;
                    parent.style.visibility = Visibility.Visible;
                    parent.SetEnabled(true);
                    parent = parent.hierarchy?.parent;
                }
            }
            catch { }
        }
    }
}
