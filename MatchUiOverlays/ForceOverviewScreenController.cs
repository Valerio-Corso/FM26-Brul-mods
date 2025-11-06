using System;
using System.Linq;
using System.Reflection;
using FM26Mods.CommonUI;
using UnityEngine;
using UnityEngine.UIElements;

namespace MatchUiOverlays
{
    // Ensures a specific deep UI "screen" (VisualElement subtree) stays visible.
    // Uses RuntimeUiScaffold to attach to a UIDocument and then
    // traverses the provided path to force visibility on the target and its ancestors.
    public class ForceOverviewScreenController : MonoBehaviour
    {
        public ForceOverviewScreenController(IntPtr ptr) : base(ptr) { }

        // The path provided by the user (names of VisualElements as they appear in the hierarchy)
        // Empty segments (multiple '/') are ignored.
        private const string TargetPath =
            "DefaultLayerSettings/PanelManager-container/Menu/Body/OverviewPrototype//BindingSetter/BindingRemapper/BindableSwitchElement_IsInOverview/" +
            "MatchExperienceGrid////GridLayoutElementContent//MatchViewer//TileBindingExpect/layout-tile-extra_large_8x4-default/////MatchSimulationVariables//tile states-match simulation-hero frame";

        // Optional helpers to also try toggling a switch if visibility is controlled by bindings
        private const string OverviewSwitchName = "BindableSwitchElement_IsInOverview";
        private const string PanelContainerName = "PanelManager-container";

        // Logging & runtime control
        public bool verboseLogging = true; // can be toggled via BepInEx config later
        private float _lastVerboseLogTime;
        private float _lastFoundLogTime;
        private bool _everFound;

        private RuntimeUiScaffold _ui;
        private float _lastTryTime;
        private const float TryInterval = 0.10f; // tighter enforcement (every 100 ms)
        private bool _reportedAttach;

        private void Update()
        {
            try
            {
                if (Time.unscaledTime - _lastTryTime < TryInterval) return;
                _lastTryTime = Time.unscaledTime;

                EnsureUi();

                // Enumerate all active UIDocuments and try to enforce in each, until first success
                var docs = SafeFindAllUiDocuments();
                if (docs == null || docs.Length == 0)
                {
                    MaybeLogEvery(2.0f, "ForceOverviewScreen: No UIDocuments found in active scenes yet.");
                    return;
                }

                UIDocument successDoc = null;
                VisualElement target = null;
                string failToken = null;

                foreach (var doc in docs)
                {
                    if (doc == null || doc.rootVisualElement == null) continue;

                    // Ensure panel container is visible in this doc
                    TryForceVisibleByName(doc.rootVisualElement, PanelContainerName);
                    // Try to enable overview switch in this doc
                    TryEnableOverviewSwitch(doc.rootVisualElement);

                    int failedAtIndex;
                    var found = FindByPathWithTrace(doc.rootVisualElement, TargetPath, out failedAtIndex);
                    if (found != null)
                    {
                        successDoc = doc;
                        target = found;
                        break;
                    }
                    else if (verboseLogging)
                    {
                        var tokens = SplitTokens(TargetPath);
                        if (failedAtIndex >= 0 && failedAtIndex < tokens.Length)
                        {
                            failToken = tokens[failedAtIndex];
                        }
                    }
                }

                if (target == null)
                {
                    // Fallback: search by the final token across docs
                    var last = GetLastToken(TargetPath);
                    if (!string.IsNullOrEmpty(last))
                    {
                        foreach (var doc in docs)
                        {
                            var ve = doc?.rootVisualElement?.Q<VisualElement>(name: last);
                            if (ve != null)
                            {
                                successDoc = doc;
                                target = ve;
                                break;
                            }
                        }
                    }
                }

                if (target != null)
                {
                    ForceElementAndParentsVisible(target);
                    _everFound = true;
                    if (verboseLogging && Time.unscaledTime - _lastFoundLogTime > 1.5f)
                    {
                        _lastFoundLogTime = Time.unscaledTime;
                        try
                        {
                            var docName = successDoc?.gameObject?.name;
                            var disp = target.resolvedStyle.display;
                            var vis = target.resolvedStyle.visibility;
                            MatchUiOverlayPlugin.LOG?.LogInfo($"ForceOverviewScreen: Target FOUND in UIDocument='{docName}'. Applied force-visible. Resolved display={disp}, visibility={vis}");
                        }
                        catch
                        {
                            MatchUiOverlayPlugin.LOG?.LogInfo("ForceOverviewScreen: Target FOUND and force-visible applied.");
                        }
                    }
                }
                else
                {
                    if (verboseLogging)
                    {
                        var hint = string.IsNullOrEmpty(failToken) ? "(no token hint)" : $"(first missing token='{failToken}')";
                        MaybeLogEvery(2.0f, $"ForceOverviewScreen: Target NOT found in any UIDocument {hint}.");
                    }
                }
            }
            catch (Exception ex)
            {
                MatchUiOverlayPlugin.LOG?.LogWarning($"ForceOverviewScreen: Update error: {ex}");
            }
        }

        private void EnsureUi()
        {
            if (_ui == null)
            {
                _ui = new RuntimeUiScaffold("ForceOverview_UI", 100001);
            }
            // We still create/keep a fallback doc for optional future on-screen debug, but we attach to existing when possible.
            _ui.EnsureDocument(preferExisting: true);
            if (!_reportedAttach && _ui.Document != null)
            {
                _reportedAttach = true;
                var info = _ui.AttachedToExisting ? "existing" : "own";
                MatchUiOverlayPlugin.LOG?.LogInfo($"ForceOverviewScreen: Attached to {info} UIDocument (name='{_ui.Document?.gameObject?.name}', sortingOrder='{SafeGetSortingOrder(_ui.Document)}').");

                // Log a quick snapshot of all documents
                var docs = SafeFindAllUiDocuments();
                if (docs != null && docs.Length > 0)
                {
                    foreach (var d in docs)
                    {
                        if (d == null) continue;
                        MatchUiOverlayPlugin.LOG?.LogInfo($"ForceOverviewScreen: Seen UIDocument name='{d.gameObject.name}', sortingOrder='{SafeGetSortingOrder(d)}', active='{d.isActiveAndEnabled && d.gameObject.activeInHierarchy}'");
                    }
                }
            }
        }

        private static int SafeGetSortingOrder(UIDocument doc)
        {
            try { return (int)doc.sortingOrder; } catch { return 0; }
        }

        private static UIDocument[] SafeFindAllUiDocuments()
        {
            try
            {
                return GameObject.FindObjectsOfType<UIDocument>();
            }
            catch
            {
                return Array.Empty<UIDocument>();
            }
        }

        private static void ForceElementAndParentsVisible(VisualElement ve)
        {
            var cur = ve;
            while (cur != null)
            {
                try
                {
                    // Remove common hiding styles by overriding
                    cur.style.display = DisplayStyle.Flex;
                    cur.style.visibility = Visibility.Visible;
                    cur.style.opacity = 1f;
                    // Ensure it participates in picking/layout
                    cur.pickingMode = PickingMode.Position;
                }
                catch { }
                cur = cur.parent;
            }
        }

        private static string[] SplitTokens(string path)
        {
            return string.IsNullOrEmpty(path)
                ? Array.Empty<string>()
                : path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static string GetLastToken(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            return SplitTokens(path).LastOrDefault();
        }

        private static VisualElement FindByPathWithTrace(VisualElement root, string path, out int failedAtIndex)
        {
            failedAtIndex = -1;
            if (root == null || string.IsNullOrEmpty(path)) return null;
            var tokens = SplitTokens(path);
            VisualElement cur = root;
            for (int i = 0; i < tokens.Length; i++)
            {
                var token = tokens[i];
                var next = cur.Q<VisualElement>(name: token); // recursive search within subtree
                if (next == null)
                {
                    failedAtIndex = i;
                    return null;
                }
                cur = next;
            }
            return cur;
        }

        private void MaybeLogEvery(float seconds, string msg)
        {
            if (!verboseLogging) return;
            if (Time.unscaledTime - _lastVerboseLogTime < seconds) return;
            _lastVerboseLogTime = Time.unscaledTime;
            MatchUiOverlayPlugin.LOG?.LogInfo(msg);
        }

        private static void TryForceVisibleByName(VisualElement root, string name)
        {
            if (string.IsNullOrEmpty(name) || root == null) return;
            var ve = root.Q<VisualElement>(name: name);
            if (ve != null) ForceElementAndParentsVisible(ve);
        }

        private static void TryEnableOverviewSwitch(VisualElement root)
        {
            try
            {
                var sw = root.Q<VisualElement>(name: OverviewSwitchName);
                if (sw == null) return;

                // If it's a Toggle, make sure it's ON
                if (sw is Toggle t)
                {
                    if (!t.value) t.value = true;
                    return;
                }

                // Try reflection for INotifyValueChanged<bool> or SetValueWithoutNotify(true)
                try
                {
                    var type = sw.GetType();
                    var setWithoutNotify = type.GetMethod("SetValueWithoutNotify", BindingFlags.Public | BindingFlags.Instance);
                    if (setWithoutNotify != null && setWithoutNotify.GetParameters().Length == 1)
                    {
                        var p = setWithoutNotify.GetParameters()[0];
                        if (p.ParameterType == typeof(bool))
                        {
                            setWithoutNotify.Invoke(sw, new object[] { true });
                            return;
                        }
                    }

                    var valueProp = type.GetProperty("value", BindingFlags.Public | BindingFlags.Instance);
                    if (valueProp != null && valueProp.PropertyType == typeof(bool))
                    {
                        var cur = (bool)(valueProp.GetValue(sw) ?? false);
                        if (!cur) valueProp.SetValue(sw, true);
                        return;
                    }
                }
                catch { }
            }
            catch { }
        }
    }
}
