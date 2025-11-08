using System;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using Il2CppSystem.IO;
using Il2CppSystem.Linq;
using UnityEngine;
using SI.Bindable; // PanelManager
using SI.UI;
using UnityEngine.AddressableAssets;
using UnityEngine.InputSystem;
using UnityEngine.ResourceManagement.ResourceLocations; // PanelCloseOptions

namespace MatchUiOverlays
{
    public class PanelWarmupTester : MonoBehaviour
    {
        private PanelManager _manager;
        private Panel panel;

        private Dictionary<string, PanelID> _panelIds = new(784);
        private Dictionary<string, UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<PanelID>> _panelIdHandles = new();

        public PanelWarmupTester(IntPtr ptr) : base(ptr)
        {
        }

        private void Start()
        {
            _manager = UnityEngine.Object.FindObjectOfType<PanelManager>();
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current[Key.F1].wasPressedThisFrame)
            {
                foreach (var managerPanel in _manager.Panels)
                {
                    Log($"PanelID id: {managerPanel?.ID}");
                    Log($"PanelID name: {managerPanel?.name}");
                    Log($"PanelID dev state: {managerPanel?.m_panelDescription.developmentState}");
                }

                Vector2 mousePos = Mouse.current.position.ReadValue();
                panel = _manager.GetPanelAtMousePos(mousePos);
                Log($"Panel name: {panel?.name}");
                Log($"Panel instance name: {panel?.InstanceName}");
                Log($"Panel Identifier: {panel?.Identifier}");
                Log($"Panel visible: {panel?.visible}");
                Log($"Panel key: {panel?.Key.m_key}");
                Log($"PanelID id: {panel?.PanelID.ID}");
                Log($"PanelID name: {panel?.PanelID.name}");
                Log($"PanelID dev state: {panel?.PanelID.m_panelDescription.developmentState}");
            }

            if (panel != null && Keyboard.current != null && Keyboard.current[Key.F2].wasPressedThisFrame)
            {
                
            }

            if (panel != null && Keyboard.current != null && Keyboard.current[Key.F3].wasPressedThisFrame)
            {
            }

            if (Keyboard.current != null && Keyboard.current[Key.F4].wasPressedThisFrame)
            {
                
                
                // Log($"Found {panelLocations.Count} panel locations:");
                // foreach (var location in panelLocations)
                // {
                //     Log($"  - PrimaryKey: {location.PrimaryKey}");
                //     Log($"    InternalId: {location.InternalId}");
                // }
            }

            if (Keyboard.current != null && Keyboard.current[Key.F5].wasPressedThisFrame)
            {
                StartCoroutine(LoadAllPanelIDsCoroutine().WrapToIl2Cpp());
            }

            if (Keyboard.current != null && Keyboard.current[Key.F6].wasPressedThisFrame)
            {
                var key = "OnThePitchShotMapGraphCard";
                Log($"Panel count: {_panelIds.ContainsKey(key)}");
                if (!_panelIds.ContainsKey(key))
                {
                    var panelId = _panelIds[key];
                    Log($"Opening panel: {panelId.name}");
                    _manager.m_registry.TryFindPanelIDReference(panelId, out var panelIDRef);
                    _manager.m_registry.Prepare(panelIDRef);
                }

                // StartCoroutine(DumpPanelUxmlCoroutine().WrapToIl2Cpp());
            }
        }

        private System.Collections.IEnumerator LoadAllPanelIDsCoroutine()
        {
            var panelLocations = new List<IResourceLocation>();

            // Discover every key that can locate a PanelID
            foreach (var locator in Addressables.ResourceLocators.ToList())
            {
                foreach (var key in locator.Keys.ToList())
                {
                    var panelIDType = Il2CppSystem.Type.GetType(typeof(PanelID).AssemblyQualifiedName);
                    if (locator.Locate(key, panelIDType, out Il2CppSystem.Collections.Generic.IList<IResourceLocation> found))
                    {
                        var collection = found.TryCast<Il2CppSystem.Collections.Generic.ICollection<IResourceLocation>>();
                        if (collection != null)
                        {
                            for (int i = 0; i < collection.Count; i++)
                            {
                                panelLocations.Add(found[i]);
                            }
                        }
                    }
                }
            }

            Log($"Loading {panelLocations.Count} PanelID assets...");

            // Load each PanelID asset
            foreach (var location in panelLocations)
            {
                UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<PanelID> handle = default;
                handle = Addressables.LoadAssetAsync<PanelID>(location);

                // Wait for completion
                while (!handle.IsDone)
                {
                    yield return null;
                }

                try
                {
                    if (handle.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded)
                    {
                        _panelIdHandles.Add(handle.Result.name, handle);
                        _panelIds.Add(handle.Result.name, handle.Result);

                        // var panelID = handle.Result;
                        // Log($"Loaded PanelID:");
                        // Log($"  - Name: {panelID.name}");
                        // Log($"  - ID: {panelID.ID}");
                        // Log($"  - Dev State: {panelID.m_panelDescription.developmentState}");
                    }
                    else
                    {
                        Log($"Failed to load PanelID from {location.PrimaryKey}: {handle.Status}");
                    }
                }
                catch (Exception ex)
                {
                    Log($"Error loading PanelID from {location.PrimaryKey}: {ex.Message}");
                }
            }

            // try
            // {
            //     foreach (var panelId in _panelIds.Values)
            //     {
            //         Log($"PanelID: {panelId.name}, state = {panelId.m_panelDescription.developmentState}");
            //         // if (panelId != null && panelId.Description.developmentState == PanelID.DevelopmentState.WorkInProgress)
            //         // {
            //         // }
            //     }
            // }
            // catch (Exception ex)
            // {
            //     
            // }

            Log("Finished loading all PanelIDs");
        }

        private void Log(string msg)
        {
            try
            {
                MatchUiOverlayPlugin.LOG?.LogInfo(msg);
            }
            catch
            {
                UnityEngine.Debug.Log(msg);
            }
        }

        private System.Collections.IEnumerator DumpPanelUxmlCoroutine()
        {
            if (_panelIds.Count == 0)
            {
                Log("No panels loaded yet. Press F5 first to load all panels.");
                yield break;
            }

            var outputDir = System.IO.Path.Combine(BepInEx.Paths.PluginPath, "UxmlDumps");
            if (!System.IO.Directory.Exists(outputDir))
            {
                System.IO.Directory.CreateDirectory(outputDir);
            }

            Log($"Dumping UXML for {_panelIds.Count} panels to: {outputDir}");

            int successCount = 0;
            int failCount = 0;

            foreach (var kvp in _panelIds)
            {
                var panelName = kvp.Key;
                var panelID = kvp.Value;

                bool shouldContinue = false;
                UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<UnityEngine.UIElements.VisualTreeAsset> uxmlHandle = default;

                try
                {
                    var uxmlRef = panelID?.Description?.uxml;
                    if (uxmlRef == null || !uxmlRef.RuntimeKeyIsValid())
                    {
                        failCount++;
                        shouldContinue = true;
                    }
                    else
                    {
                        uxmlHandle = uxmlRef.LoadAssetAsync<UnityEngine.UIElements.VisualTreeAsset>();
                    }
                }
                catch (Exception ex)
                {
                    Log($"  - {panelName}: Exception loading reference: {ex.Message}");
                    failCount++;
                    shouldContinue = true;
                }

                if (shouldContinue)
                    continue;

                // Wait for completion with timeout (outside try-catch)
                int waitFrames = 0;
                while (!uxmlHandle.IsDone && waitFrames < 300) // 5 second timeout at 60fps
                {
                    waitFrames++;
                    yield return null;
                }

                try
                {
                    if (!uxmlHandle.IsDone)
                    {
                        Log($"  - {panelName}: Timeout loading UXML");
                        Addressables.Release(uxmlHandle);
                        failCount++;
                        continue;
                    }

                    if (uxmlHandle.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded)
                    {
                        var visualTreeAsset = uxmlHandle.Result;
                        if (visualTreeAsset != null)
                        {
                            try
                            {
                                // Get the UXML text content
                                var uxmlText = GetUxmlText(visualTreeAsset, panelName);

                                if (!string.IsNullOrEmpty(uxmlText))
                                {
                                    var filePath = System.IO.Path.Combine(outputDir, $"{SanitizeFileName(panelName)}.uxml");
                                    System.IO.File.WriteAllText(filePath, uxmlText);
                                    successCount++;

                                    // Log every 50 successful dumps
                                    if (successCount % 50 == 0)
                                    {
                                        Log($"  Progress: {successCount} panels dumped...");
                                    }
                                }
                                else
                                {
                                    failCount++;
                                }
                            }
                            catch (Exception ex)
                            {
                                Log($"  - {panelName}: Error processing UXML: {ex.Message}");
                                failCount++;
                            }
                        }
                        else
                        {
                            failCount++;
                        }
                    }
                    else
                    {
                        failCount++;
                    }

                    // Release the handle
                    Addressables.Release(uxmlHandle);
                }
                catch (Exception ex)
                {
                    Log($"  - {panelName}: Exception: {ex.Message}");
                    failCount++;
                    try
                    {
                        Addressables.Release(uxmlHandle);
                    }
                    catch
                    {
                    }
                }

                // Yield periodically to prevent frame drops (outside try-catch)
                if ((successCount + failCount) % 10 == 0)
                {
                    yield return null;
                }
            }

            Log($"Finished dumping UXML files to: {outputDir}");
            Log($"  Success: {successCount}, Failed: {failCount}");
        }
        private string SanitizeFileName(string fileName)
        {
            try
            {
                var invalid = System.IO.Path.GetInvalidFileNameChars();
                foreach (var c in invalid)
                {
                    fileName = fileName.Replace(c, '_');
                }

                return fileName;
            }
            catch
            {
                return "invalid_name";
            }
        }

        private string GetUxmlText(UnityEngine.UIElements.VisualTreeAsset visualTreeAsset, string panelName)
        {
            try
            {
                // Try to serialize the asset structure
                var root = visualTreeAsset.CloneTree();

                // Generate UXML-like representation from the cloned tree
                return GenerateUxmlFromVisualElement(root, 0);
            }
            catch (Exception ex)
            {
                Log($"    Error extracting UXML text for {panelName}: {ex.Message}");
                return $"<!-- Error: {ex.Message} -->";
            }
        }

        private string GenerateUxmlFromVisualElement(UnityEngine.UIElements.VisualElement element, int indent = 0)
        {
            if (element == null) return string.Empty;

            try
            {
                var sb = new System.Text.StringBuilder();
                var indentStr = new string(' ', indent * 2);

                // Get element type name
                string typeName = "Unknown";
                try
                {
                    typeName = element.GetType().Name;
                }
                catch
                {
                }

                sb.Append($"{indentStr}<{typeName}");

                // Add common attributes
                try
                {
                    if (!string.IsNullOrEmpty(element.name))
                        sb.Append($" name=\"{EscapeXml(element.name)}\"");
                }
                catch
                {
                }

                try
                {
                    var classList = element.classList;
                    if (classList != null && classList.Count > 0)
                    {
                        var classArray = new List<string>();
                        // Manually iterate to avoid IL2CPP issues
                        for (int i = 0; i < classList.Count; i++)
                        {
                            try
                            {
                                var className = classList[i];
                                if (!string.IsNullOrEmpty(className))
                                    classArray.Add(className);
                            }
                            catch
                            {
                            }
                        }

                        if (classArray.Count > 0)
                        {
                            var classes = string.Join(" ", classArray);
                            sb.Append($" class=\"{EscapeXml(classes)}\"");
                        }
                    }
                }
                catch
                {
                }

                // Check if has children
                int childCount = 0;
                try
                {
                    childCount = element.childCount;
                }
                catch
                {
                    childCount = 0;
                }

                if (childCount > 0)
                {
                    sb.AppendLine(">");

                    // Recursively process children using hierarchy API
                    try
                    {
                        var children = element.Children();
                        if (children != null)
                        {
                            var childList = children.ToList();
                            foreach (var child in childList)
                            {
                                if (child != null)
                                {
                                    var childXml = GenerateUxmlFromVisualElement(child, indent + 1);
                                    if (!string.IsNullOrEmpty(childXml))
                                    {
                                        sb.Append(childXml);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine($"{indentStr}  <!-- Error iterating children: {ex.Message} -->");
                    }

                    sb.AppendLine($"{indentStr}</{typeName}>");
                }
                else
                {
                    sb.AppendLine(" />");
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"<!-- Error generating element: {ex.Message} -->\n";
            }
        }

        private string EscapeXml(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            try
            {
                return text
                    .Replace("&", "&amp;")
                    .Replace("<", "&lt;")
                    .Replace(">", "&gt;")
                    .Replace("\"", "&quot;")
                    .Replace("'", "&apos;");
            }
            catch
            {
                return text;
            }
        }
    }
}