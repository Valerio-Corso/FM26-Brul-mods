using System;
using System.Collections;
using BepInEx.IL2CPP;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.InputSystem;

namespace CameraStack
{
    // Always-on small PiP overlay that renders a UITK panel (off-screen) into a RenderTexture
    // and displays it via a simple quad in front of an orthographic overlay camera (no UnityEngine.UI dependency).
    public partial class SimulationWidgetOverlay : MonoBehaviour
    {
        public SimulationWidgetOverlay(IntPtr ptr) : base(ptr) {}

        // PiP placement (pixels)
        public float widthPixels = 480f;
        public float heightPixels = 270f;
        public float marginRight = 24f;
        public float marginBottom = 24f;
        public float opacity = 1.0f;
        public bool startEnabled = false;
        // Live-update by default: reparent the UI element into an off-screen UIDocument so it stays dynamic.
        // Set to true to use one-time snapshots instead.
        public bool mirrorBySnapshot = false; // default to live mirroring

        // RenderTexture
        public int rtWidth = 512;
        public int rtHeight = 512;
        public RenderTextureFormat rtFormat = RenderTextureFormat.ARGB32;
        public int rtDepth = 0;

        // Controls
        public Key toggleKey = Key.F10;
        public Key togglePanelKey = Key.F11;
        public Key dumpStateKey = Key.F12;
        public Key selectTargetKey = Key.F7;
        public Key testPatternKey = Key.F6;
        public Key captureSnapshotKey = Key.F5;

        private Camera _overlayCam;
        private GameObject _quad;
        private Material _mat;
        private RenderTexture _rt;
        private bool _testPattern;
        private Texture2D _testPatternTex;
        private Texture2D _snapshotTex;
        private UnityEngine.RectInt _snapshotRect;
        private bool _isCapturing;

        // Dedicated overlay layer (avoid clashing with cameras rendering TransparentFX)
        private const int OverlayLayerIndex = 30; // use a high, rarely-used layer
        private static int OverlayLayerMask => 1 << OverlayLayerIndex;
        private System.Collections.Generic.Dictionary<int, int> _cameraOriginalMasks = new System.Collections.Generic.Dictionary<int, int>();
        private int _lastCameraCount = -1;

        // UITK off-screen panel that renders into RT
        private PanelSettings _panelSettings;
        private UIDocument _offscreenDoc;
        private VisualElement _offscreenRoot;

        // Selection/reparenting state
        private VisualElement _selectedElement;
        private VisualElement _selectedParent;
        private int _selectedIndex = -1;
        private VisualElement _placeholder;
        private bool _isReparented;

        private bool _initialized;
        private float _nextProbeTime;
        private Vector2Int _lastScreenSize;

        private void Awake()
        {
            try { ClassInjector.RegisterTypeInIl2Cpp<SimulationWidgetOverlay>(); } catch { /* ignore if already registered */ }
        }

        private void Start()
        {
            try
            {
                CreateOverlayCamera();
                CreateRenderTexture();
                CreateQuad();
                CreateOffscreenPanel();

                // Ensure other cameras never render our overlay layer
                IsolateOverlayLayerFromOtherCameras();

                ApplyEnabled(startEnabled);
                LayoutPiP();
                _initialized = true;
                CameraStackBootstrap.LOG?.LogInfo("[PiP] SimulationWidgetOverlay initialized.");
            }
            catch (Exception ex)
            {
                CameraStackBootstrap.LOG?.LogError($"[PiP] Initialization failed: {ex}");
            }
        }

        private void Update()
        {
            if (!_initialized) return;

            // Re-isolate if cameras change (new cameras created by the game)
            if (Camera.allCamerasCount != _lastCameraCount)
            {
                IsolateOverlayLayerFromOtherCameras();
            }

            // Toggle
            if (Keyboard.current != null)
            {
                var key = Keyboard.current[(UnityEngine.InputSystem.Key)toggleKey];
                if (key != null && key.wasPressedThisFrame)
                {
                    ApplyEnabled(!_overlayCam.enabled);
                }

                var keyPanel = Keyboard.current[(UnityEngine.InputSystem.Key)togglePanelKey];
                if (keyPanel != null && keyPanel.wasPressedThisFrame)
                {
                    if (_offscreenDoc != null)
                    {
                        bool newState = !_offscreenDoc.gameObject.activeSelf;
                        _offscreenDoc.gameObject.SetActive(newState);
                        CameraStackBootstrap.LOG?.LogInfo($"[PiP] Off-screen panel active = {newState}");
                    }
                }

                // Select target under mouse
                var keyPick = Keyboard.current[(UnityEngine.InputSystem.Key)selectTargetKey];
                if (keyPick != null && keyPick.wasPressedThisFrame)
                {
                    TryPickHoveredElement();
                }

                var keyDump = Keyboard.current[(UnityEngine.InputSystem.Key)dumpStateKey];
                if (keyDump != null && keyDump.wasPressedThisFrame)
                {
                    DumpState();
                }

                // Toggle test pattern
                var keyTest = Keyboard.current[(UnityEngine.InputSystem.Key)testPatternKey];
                if (keyTest != null && keyTest.wasPressedThisFrame)
                {
                    _testPattern = !_testPattern;
                    if (_testPattern)
                    {
                        EnsureTestPatternTexture();
                        if (_mat != null) _mat.mainTexture = _testPatternTex;
                    }
                    else
                    {
                        if (_mat != null) _mat.mainTexture = (_snapshotTex != null ? _snapshotTex : _rt);
                    }
                    CameraStackBootstrap.LOG?.LogInfo($"[PiP] TestPattern = {_testPattern}, matTex={_mat?.mainTexture?.name}");
                }

                // Manual snapshot capture
                var keySnap = Keyboard.current[(UnityEngine.InputSystem.Key)captureSnapshotKey];
                if (keySnap != null && keySnap.wasPressedThisFrame)
                {
                    RequestSnapshotForSelection();
                }
            }

            // Relayout on resolution change
            var size = new Vector2Int(Screen.width, Screen.height);
            if (size != _lastScreenSize)
            {
                LayoutPiP();
            }

            // Update quad visibility and RT hygiene
            UpdateQuadVisibility();
            if (_overlayCam != null && _overlayCam.enabled)
            {
                // Clear RT each frame to avoid undefined memory (can appear as solid yellow on some GPUs)
                SafeClearRT();
            }
        }

        private void UpdateQuadVisibility()
        {
            try
            {
                bool hasContent = _testPattern || _isReparented || (_snapshotTex != null);
                bool shouldShow = _overlayCam != null && _overlayCam.enabled && hasContent;
                if (_quad != null && _quad.activeSelf != shouldShow)
                {
                    _quad.SetActive(shouldShow);
                }
                // Only keep off-screen doc active if we actually mirrored content via reparent
                EnsureOffscreenDocActive(_isReparented);
            }
            catch (Exception ex)
            {
                CameraStackBootstrap.LOG?.LogError($"[PiP] UpdateQuadVisibility error: {ex}");
            }
        }

        private void CreateOverlayCamera()
        {
            var camGo = new GameObject("PiP_OverlayCamera");
            camGo.transform.SetParent(this.transform, false);
            _overlayCam = camGo.AddComponent<Camera>();
            _overlayCam.clearFlags = CameraClearFlags.Depth; // avoid undefined color buffer issues
            _overlayCam.backgroundColor = new Color(0, 0, 0, 0);
            _overlayCam.depth = 100f;
            _overlayCam.orthographic = true;
            _overlayCam.nearClipPlane = 0.01f;
            _overlayCam.farClipPlane = 50f;
            // Make 1 unit == 1 pixel in Y:
            _overlayCam.orthographicSize = Screen.height * 0.5f;
            // Render only our dedicated PiP layer
            _overlayCam.cullingMask = OverlayLayerMask;
            _overlayCam.allowMSAA = false;
            _overlayCam.allowHDR = false;
            _overlayCam.useOcclusionCulling = false;
        }

        private void CreateRenderTexture()
        {
            _rt = new RenderTexture(rtWidth, rtHeight, rtDepth, rtFormat)
            {
                name = "PiP_RT",
                useDynamicScale = true,
                autoGenerateMips = false,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            _rt.Create();
        }

        private void CreateQuad()
        {
            _quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _quad.name = "PiP_Quad";
            // Parent to overlay camera so coordinates are camera-local
            if (_overlayCam != null)
                _quad.transform.SetParent(_overlayCam.transform, false);
            else
                _quad.transform.SetParent(this.transform, false);

            // Put the quad on our dedicated overlay layer so only our overlay cam sees it
            _quad.layer = OverlayLayerIndex;

            // Remove collider (not needed) — Collider type may be absent in some build profiles; ignore if not present

            var mr = _quad.GetComponent<MeshRenderer>();
            // Prefer a transparent sprite/UI shader (commonly present) for correct alpha blending
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("UI/Default");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
            if (shader == null) shader = Shader.Find("Unlit/Texture");
            _mat = new Material(shader);
            _mat.color = new Color(1f, 1f, 1f, Mathf.Clamp01(opacity));
            _mat.mainTexture = _rt;
            // Ensure transparent rendering queue (after geometry, before overlay UI if any)
            _mat.renderQueue = 3000;
            mr.sharedMaterial = _mat;
        }

        private void CreateOffscreenPanel()
        {
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _panelSettings.name = "PiP_PanelSettings";
            _panelSettings.targetTexture = _rt;
            _panelSettings.clearColor = true;
            _panelSettings.colorClearValue = new Color(0,0,0,0);
            _panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
            _panelSettings.referenceDpi = 96;
            _panelSettings.match = 1f;

            var uiGo = new GameObject("PiP_UIDocument");
            uiGo.transform.SetParent(this.transform, false);
            _offscreenDoc = uiGo.AddComponent<UIDocument>();
            _offscreenDoc.panelSettings = _panelSettings;
            // Keep off-screen panel disabled until we actually mirror content to avoid interfering with live UI
            _offscreenDoc.gameObject.SetActive(false);

            _offscreenRoot = _offscreenDoc.rootVisualElement;
            if (_offscreenRoot != null)
            {
                _offscreenRoot.style.flexDirection = FlexDirection.Column;
                _offscreenRoot.style.width = new Length(100, LengthUnit.Percent);
                _offscreenRoot.style.height = new Length(100, LengthUnit.Percent);
                _offscreenRoot.style.backgroundColor = new StyleColor(new Color(0, 0, 0, 0));
                _offscreenRoot.pickingMode = PickingMode.Ignore; // ensure it never captures input
            }
        }

        private void LayoutPiP()
        {
            _lastScreenSize = new Vector2Int(Screen.width, Screen.height);
            if (_overlayCam != null)
            {
                _overlayCam.orthographicSize = _lastScreenSize.y * 0.5f; // 1 unit == 1 pixel in Y
                // Use full-screen viewport for the overlay camera so 1 unit == 1 pixel mapping is correct.
                // We will size and position the quad itself to the desired PiP rectangle.
                _overlayCam.rect = new Rect(0f, 0f, 1f, 1f);
            }
            if (_quad != null)
            {
                // Position in bottom-right in camera-local coordinates:
                // Camera center is (0,0); right edge is +Screen.width*0.5, bottom is -Screen.height*0.5
                float x = (_lastScreenSize.x * 0.5f) - marginRight - (widthPixels * 0.5f);
                float y = -(_lastScreenSize.y * 0.5f) + marginBottom + (heightPixels * 0.5f);
                _quad.transform.localPosition = new Vector3(x, y, 5f);
                _quad.transform.localRotation = Quaternion.identity;
                _quad.transform.localScale = new Vector3(widthPixels, heightPixels, 1f);
            }
        }

        private void ApplyEnabled(bool enabled)
        {
            if (_overlayCam != null) _overlayCam.enabled = enabled;
            if (_quad != null) _quad.SetActive(enabled);

            if (enabled)
            {
                var main = Camera.main;
                try
                {
                    CameraStackBootstrap.LOG?.LogInfo($"[PiP] Enabling overlay. Main cam: name={main?.name} depth={main?.depth} mask={(main!=null?main.cullingMask:0)} clear={main?.clearFlags} bg={main?.backgroundColor} targetTex={(main?.targetTexture!=null?main.targetTexture.name:"<null>")}");
                }
                catch { }

                // Do not auto-activate the off-screen panel; only activate if we actually mirror a selection.
                EnsureOffscreenDocActive(_isReparented);
                if (_selectedElement != null)
                {
                    if (mirrorBySnapshot)
                    {
                        RequestSnapshotForSelection();
                    }
                    else if (!_isReparented)
                    {
                        ReparentSelectedToOffscreen();
                    }
                }
                // Prime RT with transparent clear to avoid old memory
                SafeClearRT();
            }
            else
            {
                // Restore any reparented element and hide off-screen doc until needed
                RestoreSelectedToOriginal();
                EnsureOffscreenDocActive(false);
            }
        }

        private void DumpState()
        {
            try
            {
                var camMask = _overlayCam != null ? _overlayCam.cullingMask : 0;
                string MaskToLayers(int mask)
                {
                    if (mask == 0) return "<none>";
                    System.Text.StringBuilder sb = new System.Text.StringBuilder();
                    for (int i = 0; i < 32; i++)
                    {
                        if ((mask & (1 << i)) != 0)
                        {
                            if (sb.Length > 0) sb.Append(',');
                            sb.Append(i);
                        }
                    }
                    return sb.ToString();
                }

                CameraStackBootstrap.LOG?.LogInfo("[PiP] ---- DumpState ----");
                CameraStackBootstrap.LOG?.LogInfo($"[PiP] overlayCam enabled={_overlayCam?.enabled} depth={_overlayCam?.depth} ortho={_overlayCam?.orthographic} size={_overlayCam?.orthographicSize}");
                CameraStackBootstrap.LOG?.LogInfo($"[PiP] overlayCam cullingMask={camMask} layers={MaskToLayers(camMask)} clearFlags={_overlayCam?.clearFlags} bg={_overlayCam?.backgroundColor} near={_overlayCam?.nearClipPlane} far={_overlayCam?.farClipPlane}");
                if (_quad != null)
                {
                    var mr = _quad.GetComponent<MeshRenderer>();
                    var sh = (mr != null && mr.sharedMaterial != null) ? mr.sharedMaterial.shader?.name : "<no-mat>";
                    var tex = (mr != null && mr.sharedMaterial != null) ? mr.sharedMaterial.mainTexture : null;
                    CameraStackBootstrap.LOG?.LogInfo($"[PiP] quad active={_quad.activeSelf} layer={_quad.layer} pos={_quad.transform.localPosition} scale={_quad.transform.localScale} shader={sh} tex={(tex != null ? tex.name : "<null>")}");
                }
                if (_rt != null)
                {
                    CameraStackBootstrap.LOG?.LogInfo($"[PiP] RT size={_rt.width}x{_rt.height} created={_rt.IsCreated()} format={_rt.format}");
                }
                if (_offscreenDoc != null)
                {
                    CameraStackBootstrap.LOG?.LogInfo($"[PiP] offscreenDoc active={_offscreenDoc.gameObject.activeSelf} hasPanel={( _offscreenDoc.panelSettings != null)} rootChildren={_offscreenRoot?.childCount}");
                }

                // List all cameras and whether they render the overlay layer
                var cams = Camera.allCameras;
                CameraStackBootstrap.LOG?.LogInfo($"[PiP] Cameras in scene: {cams.Length}");
                for (int i = 0; i < cams.Length; i++)
                {
                    var c = cams[i];
                    if (c == null) continue;
                    bool rendersOverlay = (c.cullingMask & OverlayLayerMask) != 0;
                    bool isMain = (c == Camera.main);
                    string tgt = c.targetTexture != null ? c.targetTexture.name : "<null>";
                    CameraStackBootstrap.LOG?.LogInfo($"[PiP] Cam[{i}] name={c.name} main={isMain} depth={c.depth} clear={c.clearFlags} bg={c.backgroundColor} ortho={c.orthographic} mask={MaskToLayers(c.cullingMask)} rendersOverlay={rendersOverlay} targetTex={tgt}");
                    try
                    {
                        var comps = c.gameObject.GetComponents<Component>();
                        System.Text.StringBuilder compSb = new System.Text.StringBuilder();
                        for (int ci = 0; ci < comps.Length; ci++)
                        {
                            var comp = comps[ci];
                            if (comp == null) continue;
                            if (compSb.Length > 0) compSb.Append(',');
                            var tn = comp.GetType().Name;
                            compSb.Append(tn);
                            if (compSb.Length > 300) { compSb.Append("..."); break; }
                        }
                        CameraStackBootstrap.LOG?.LogInfo($"[PiP]  └ Components: {compSb}");
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                CameraStackBootstrap.LOG?.LogError($"[PiP] DumpState error: {ex}");
            }
        }

        private void OnDestroy()
        {
            try
            {
                // Restore any UI we reparented
                RestoreSelectedToOriginal();
                EnsureOffscreenDocActive(false);

                // Restore camera masks we modified
                RestoreOverlayLayerOnOtherCameras();

                if (_rt != null)
                {
                    _rt.Release();
                    Destroy(_rt);
                    _rt = null;
                }
                if (_mat != null)
                {
                    Destroy(_mat);
                    _mat = null;
                }
                if (_testPatternTex != null)
                {
                    Destroy(_testPatternTex);
                    _testPatternTex = null;
                }
            }
            catch (Exception ex)
            {
                CameraStackBootstrap.LOG?.LogError($"[PiP] Cleanup error: {ex}");
            }
        }

        private void SafeClearRT()
        {
            try
            {
                if (_rt == null) return;
                var prev = RenderTexture.active;
                RenderTexture.active = _rt;
                GL.PushMatrix();
                GL.LoadPixelMatrix(0, _rt.width, _rt.height, 0);
                GL.Clear(true, true, new Color(0,0,0,0));
                GL.PopMatrix();
                RenderTexture.active = prev;
            }
            catch (Exception ex)
            {
                CameraStackBootstrap.LOG?.LogError($"[PiP] SafeClearRT error: {ex}");
            }
        }

        private void EnsureTestPatternTexture()
        {
            if (_testPatternTex != null) return;
            const int w = 256;
            const int h = 256;
            _testPatternTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            _testPatternTex.name = "PiP_TestPattern";
            var colors = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float nx = (float)x / (w - 1);
                    // 4 vertical bands
                    Color c = Color.clear;
                    if (nx < 0.25f) c = new Color(1,0,0,0.8f);
                    else if (nx < 0.5f) c = new Color(0,1,0,0.8f);
                    else if (nx < 0.75f) c = new Color(0,0,1,0.8f);
                    else c = new Color(1,1,0,0.8f);
                    // add horizontal alpha gradient
                    float ny = (float)y / (h - 1);
                    c.a = Mathf.Lerp(0.2f, 0.9f, ny);
                    colors[y * w + x] = c;
                }
            }
            _testPatternTex.SetPixels(colors);
            _testPatternTex.Apply(false, false);
        }

        private bool ComputeSelectedScreenRect(out RectInt rect)
        {
            rect = new RectInt(0, 0, 0, 0);
            try
            {
                if (_selectedElement == null) return false;
                var panel = _selectedElement.panel;
                if (panel == null) return false;

                var wb = _selectedElement.worldBound; // in panel coords (top-left origin, y-down)
                int w = Mathf.Max(1, Mathf.RoundToInt(wb.width));
                int h = Mathf.Max(1, Mathf.RoundToInt(wb.height));
                int x = Mathf.RoundToInt(wb.x);
                int yTop = Mathf.RoundToInt(wb.y);
                int y = Screen.height - (yTop + h); // convert to bottom-left origin

                // clamp to screen
                x = Mathf.Clamp(x, 0, Screen.width - 1);
                y = Mathf.Clamp(y, 0, Screen.height - 1);
                if (x + w > Screen.width) w = Screen.width - x;
                if (y + h > Screen.height) h = Screen.height - y;
                if (w <= 0 || h <= 0) return false;

                rect = new RectInt(x, y, w, h);
                return true;
            }
            catch (Exception ex)
            {
                CameraStackBootstrap.LOG?.LogError($"[PiP] ComputeSelectedScreenRect error: {ex}");
                return false;
            }
        }

        private void RequestSnapshotForSelection()
        {
            try
            {
                if (_isCapturing) return;
                if (_selectedElement == null)
                {
                    CameraStackBootstrap.LOG?.LogInfo("[PiP] Cannot snapshot: no selection.");
                    return;
                }
                if (!ComputeSelectedScreenRect(out _snapshotRect))
                {
                    CameraStackBootstrap.LOG?.LogInfo("[PiP] Cannot snapshot: selected element rect is invalid or off-screen.");
                    return;
                }
                StartCoroutine(CaptureSnapshotCoroutine().WrapToIl2Cpp());
            }
            catch (Exception ex)
            {
                CameraStackBootstrap.LOG?.LogError($"[PiP] RequestSnapshotForSelection error: {ex}");
            }
        }

        private IEnumerator CaptureSnapshotCoroutine()
        {
            _isCapturing = true;
            // Wait for end of frame to ensure UI is fully rendered
            yield return new WaitForEndOfFrame();
            try
            {
                if (_snapshotRect.width <= 0 || _snapshotRect.height <= 0)
                {
                    yield break;
                }
                // Create/update snapshot texture
                if (_snapshotTex == null || _snapshotTex.width != _snapshotRect.width || _snapshotTex.height != _snapshotRect.height)
                {
                    if (_snapshotTex != null) Destroy(_snapshotTex);
                    _snapshotTex = new Texture2D(_snapshotRect.width, _snapshotRect.height, TextureFormat.RGBA32, false);
                    _snapshotTex.name = "PiP_Snapshot";
                }

                // Read pixels from screen
                _snapshotTex.ReadPixels(new Rect(_snapshotRect.x, _snapshotRect.y, _snapshotRect.width, _snapshotRect.height), 0, 0, false);
                _snapshotTex.Apply(false, false);

                // Display snapshot unless test pattern is active
                if (!_testPattern && _mat != null)
                {
                    _mat.mainTexture = _snapshotTex;
                }
                CameraStackBootstrap.LOG?.LogInfo($"[PiP] Snapshot captured: {_snapshotRect.width}x{_snapshotRect.height} at {_snapshotRect.x},{_snapshotRect.y}");
            }
            catch (Exception ex)
            {
                CameraStackBootstrap.LOG?.LogError($"[PiP] CaptureSnapshotCoroutine error: {ex}");
            }
            finally
            {
                _isCapturing = false;
            }
        }


        private void EnsureOffscreenDocActive(bool active)
        {
            try
            {
                if (_offscreenDoc == null) return;

                // Keep alive if currently showing reparented content
                if (_isReparented) active = true;

                if (_offscreenDoc.gameObject.activeSelf != active)
                    _offscreenDoc.gameObject.SetActive(active);

                if (!active) return;

                if (_offscreenRoot == null)
                {
                    _offscreenRoot = _offscreenDoc.rootVisualElement;
                    if (_offscreenRoot != null)
                    {
                        _offscreenRoot.style.flexDirection = FlexDirection.Column;
                        _offscreenRoot.style.width = new Length(100, LengthUnit.Percent);
                        _offscreenRoot.style.height = new Length(100, LengthUnit.Percent);
                        _offscreenRoot.style.backgroundColor = new StyleColor(new Color(0, 0, 0, 0));
                        _offscreenRoot.pickingMode = PickingMode.Ignore;
                    }
                }
            }
            catch (Exception ex)
            {
                CameraStackBootstrap.LOG?.LogError($"[PiP] EnsureOffscreenDocActive error: {ex}");
            }
        }

        private void EnsureHelpPlaceholder()
        {
            try
            {
                if (_offscreenRoot == null) return;
                if (_offscreenRoot.childCount > 0) return; // something already visible

                var help = new Label("PiP overlay: press F7 over a UI widget to mirror it here");
                help.style.color = new StyleColor(new Color(1f, 1f, 1f, 0.9f));
                help.style.backgroundColor = new StyleColor(new Color(0f, 0f, 0f, 0.35f));
                help.style.flexGrow = 1f;
                help.style.paddingLeft = 6;
                help.style.paddingRight = 6;
                help.style.paddingTop = 4;
                help.style.paddingBottom = 4;
                _offscreenRoot.Add(help);
            }
            catch (Exception ex)
            {
                CameraStackBootstrap.LOG?.LogError($"[PiP] EnsureHelpPlaceholder error: {ex}");
            }
        }

        private void TryPickHoveredElement()
        {
            try
            {
                if (Mouse.current == null) return;
                Vector2 mousePos = Mouse.current.position.ReadValue();
                var docs = GameObject.FindObjectsOfType<UIDocument>();
                VisualElement best = null;

                foreach (var doc in docs)
                {
                    if (doc == null) continue;
                    var root = doc.rootVisualElement;
                    var panel = root?.panel;
                    if (panel == null) continue;

                    var panelMouse = RuntimePanelUtils.ScreenToPanel(panel, mousePos);
                    var hovered = panel.Pick(panelMouse);
                    if (hovered == null) continue;

                    // Prefer elements with names hinting simulation
                    if (best == null) { best = hovered; continue; }
                    string hname = hovered.name != null ? hovered.name.ToLower() : string.Empty;
                    string bname = best.name != null ? best.name.ToLower() : string.Empty;
                    bool hovIsSim = (hname.Contains("sim") || hname.Contains("simulation"));
                    bool bestIsSim = (bname.Contains("sim") || bname.Contains("simulation"));
                    if (hovIsSim && !bestIsSim)
                    {
                        best = hovered;
                    }
                }

                if (best == null)
                {
                    CameraStackBootstrap.LOG?.LogInfo("[PiP] No UI element under mouse to select.");
                    return;
                }

                if (_isReparented)
                {
                    RestoreSelectedToOriginal();
                }

                _selectedElement = best;
                _selectedParent = best.parent;
                _selectedIndex = (_selectedParent != null) ? _selectedParent.IndexOf(best) : -1;

                CameraStackBootstrap.LOG?.LogInfo($"[PiP] Selected element: /{BuildElementPath(best)}");

                if (_overlayCam != null && _overlayCam.enabled)
                {
                    if (mirrorBySnapshot)
                    {
                        RequestSnapshotForSelection();
                    }
                    else
                    {
                        _placeholder = new VisualElement();
                        _placeholder.name = "PiP_Placeholder";
                        if (_selectedParent != null && _selectedIndex >= 0)
                        {
                            _selectedParent.Insert(_selectedIndex, _placeholder);
                        }
                        ReparentSelectedToOffscreen();
                    }
                }
            }
            catch (Exception ex)
            {
                CameraStackBootstrap.LOG?.LogError($"[PiP] TryPickHoveredElement error: {ex}");
            }
        }

        private void ReparentSelectedToOffscreen()
        {
            try
            {
                if (_selectedElement == null) return;
                EnsureOffscreenDocActive(true);
                if (_offscreenRoot == null) return;

                // Remove any existing children (like help label)
                _offscreenRoot.Clear();

                _selectedElement.RemoveFromHierarchy();
                _offscreenRoot.Add(_selectedElement);
                _isReparented = true;
                CameraStackBootstrap.LOG?.LogInfo("[PiP] Reparented selection into off-screen panel.");
            }
            catch (Exception ex)
            {
                CameraStackBootstrap.LOG?.LogError($"[PiP] ReparentSelectedToOffscreen error: {ex}");
            }
        }

        private void RestoreSelectedToOriginal()
        {
            try
            {
                if (!_isReparented) return;
                if (_selectedElement == null) return;

                if (_selectedParent != null)
                {
                    _selectedElement.RemoveFromHierarchy();

                    if (_selectedIndex >= 0 && _selectedIndex <= _selectedParent.childCount)
                        _selectedParent.Insert(_selectedIndex, _selectedElement);
                    else
                        _selectedParent.Add(_selectedElement);
                }

                if (_placeholder != null)
                {
                    _placeholder.RemoveFromHierarchy();
                    _placeholder = null;
                }

                _isReparented = false;
                CameraStackBootstrap.LOG?.LogInfo("[PiP] Restored selection to original panel.");
            }
            catch (Exception ex)
            {
                CameraStackBootstrap.LOG?.LogError($"[PiP] RestoreSelectedToOriginal error: {ex}");
            }
        }

        private string BuildElementPath(VisualElement element)
        {
            try
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                var cur = element;
                while (cur != null)
                {
                    string nm = string.IsNullOrEmpty(cur.name) ? cur.GetType().Name : cur.name;
                    sb.Insert(0, "/" + nm);
                    cur = cur.parent;
                }
                return sb.ToString();
            }
            catch { return "<path>"; }
        }

        // Ensure only our overlay camera renders the overlay layer
        private void IsolateOverlayLayerFromOtherCameras()
        {
            try
            {
                _lastCameraCount = Camera.allCamerasCount;
                var cams = Camera.allCameras;
                for (int i = 0; i < cams.Length; i++)
                {
                    var cam = cams[i];
                    if (cam == null) continue;
                    if (cam == _overlayCam) continue;

                    int id = cam.GetInstanceID();
                    if (!_cameraOriginalMasks.ContainsKey(id))
                    {
                        _cameraOriginalMasks[id] = cam.cullingMask;
                    }

                    // Remove our overlay bit from this camera mask
                    cam.cullingMask = cam.cullingMask & ~OverlayLayerMask;
                }
            }
            catch (Exception ex)
            {
                CameraStackBootstrap.LOG?.LogError($"[PiP] IsolateOverlayLayerFromOtherCameras error: {ex}");
            }
        }

        // Restore culling masks where we previously modified them
        private void RestoreOverlayLayerOnOtherCameras()
        {
            try
            {
                if (_cameraOriginalMasks == null || _cameraOriginalMasks.Count == 0) return;
                var cams = Camera.allCameras;
                for (int i = 0; i < cams.Length; i++)
                {
                    var cam = cams[i];
                    if (cam == null) continue;
                    if (cam == _overlayCam) continue;
                    int id = cam.GetInstanceID();
                    if (_cameraOriginalMasks.TryGetValue(id, out int original))
                    {
                        cam.cullingMask = original;
                    }
                }
                _cameraOriginalMasks.Clear();
            }
            catch (Exception ex)
            {
                CameraStackBootstrap.LOG?.LogError($"[PiP] RestoreOverlayLayerOnOtherCameras error: {ex}");
            }
        }
    }
}
