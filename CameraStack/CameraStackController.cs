using System;
using System.Linq;
using System.Reflection;
using FM.Graphics.CharacterBuilder.AppearanceController;
using FM.Match;
using FM.Match.Camera;
using SI.Anim;
using SI.Core;
using SI.MatchTypes;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using FM26Mods.CommonUI;

namespace CameraStack;

public class CameraStackController : MonoBehaviour
{
    public CameraStackController(IntPtr ptr) : base(ptr) { }

    // Settings (Referee PiP)
    public bool enableRefereePiP = false; // allow disabling referee PiP (requested)
    public float anchorHeight = 1.7f; // where to place the anchor on the referee body
    public Vector3 cameraLocalOffset = new Vector3(0f, 0.2f, -2.5f);
    public Vector3 cameraLocalEuler = new Vector3(10f, 0f, 0f);
    public bool copyMainCameraSettings = true;

    // Bottom-right overlay viewport (Referee PiP)
    public float viewportWidth = 0.28f;
    public float viewportHeight = 0.28f;
    public float viewportMarginX = 0.02f;
    public float viewportMarginY = 0.1f;

    // Player PiP settings (second mini camera)
    public bool enablePlayerPiP = true;
    public float playerAnchorHeight = 1.7f;
    public Vector3 playerCameraLocalOffset = new Vector3(0f, 0.25f, -2.4f);
    public Vector3 playerCameraLocalEuler = new Vector3(10f, 0f, 0f);

    // Top-left viewport for Player PiP
    public float playerViewportWidth = 0.28f;
    public float playerViewportHeight = 0.28f;
    public float playerViewportMarginX = 0.02f; // default from left
    public float playerViewportMarginY = 0.15f; // default from top

    // Runtime position (normalized) of the Player PiP rect (Camera.rect x/y)
    private bool _playerViewportPosInitialized;
    private float _playerViewportX; // normalized [0..1 - w]
    private float _playerViewportY; // normalized [0..1 - h], origin at bottom (Camera.rect.y)

    // Dragging state for moving the Player PiP window via UI
    private bool _isDragging;
    private Vector2 _dragStartMouse;
    private float _dragStartX;
    private float _dragStartY;

    // Screen-space dragging directly over the Player PiP camera viewport
    private bool _isScreenDragging;
    private Vector2 _screenDragStartMouse;
    private float _screenDragStartX;
    private float _screenDragStartY;

    // UI Toolkit button settings
    public bool showUiButton = true;

    private Transform _referee;
    private Transform _anchor;
    private Camera _overlayCam;
    private Camera _main;

    // Player PiP state
    private Transform[] _players;
    private int _playerIndex = -1;
    private Transform _playerAnchor;
    private Camera _playerOverlayCam;


    // UI Toolkit runtime elements
    private FM26Mods.CommonUI.RuntimeUiScaffold _uiScaffold;
    private Button _nextBtn;
    private VisualElement _container;

    private float _lastRefereeSearchTime;
    private const float RefereeSearchInterval = 1.0f;
    private float _lastPlayerSearchTime;
    private const float PlayerSearchInterval = 1.0f;

    private void Start()
    {
        TrySetup();
    }

    private void Update()
    {
        // Retry setup for referee PiP only if enabled
        if (enableRefereePiP)
        {
            if (_overlayCam == null || _referee == null)
            {
                if (Time.unscaledTime - _lastRefereeSearchTime > RefereeSearchInterval)
                {
                    _lastRefereeSearchTime = Time.unscaledTime;
                    TrySetup();
                }
            }
            else
            {
                // Keep parenting in case the referee object was re-instantiated
                if (_anchor != null && _anchor.parent != _referee)
                {
                    _anchor.SetParent(_referee, worldPositionStays: false);
                    _anchor.localPosition = new Vector3(0f, anchorHeight, 0f);
                    _anchor.localRotation = Quaternion.identity;
                }

                // Maintain desired local transform for referee PiP
                if (_overlayCam != null)
                {
                    var t = _overlayCam.transform;
                    t.localPosition = cameraLocalOffset;
                    t.localRotation = Quaternion.Euler(cameraLocalEuler);
                }
            }
        }
        else
        {
            // Ensure referee PiP is destroyed when disabled
            if (_overlayCam != null)
            {
                try { Destroy(_overlayCam.gameObject); } catch { }
                _overlayCam = null;
            }
        }

        // Player PiP maintenance and periodic discovery
        if (enablePlayerPiP)
        {
            if (Time.unscaledTime - _lastPlayerSearchTime > PlayerSearchInterval)
            {
                _lastPlayerSearchTime = Time.unscaledTime;
                EnsurePlayersResolved();
            }

            // Maintain single Player PiP and allow screen-space dragging
            MaintainPlayerPiP();
            TryHandleScreenSpaceDrag();
        }

        // Optional keyboard shortcut backup
        if (Keyboard.current != null)
        {
            var kc = Keyboard.current[Key.F6];
            if (kc != null && kc.wasPressedThisFrame)
            {
                CycleToNextPlayer();
            }

        }
    }

    private void TrySetup()
    {
        // Referee PiP setup only when enabled
        if (enableRefereePiP)
        {
            if (_referee == null)
            {
                _referee = ResolveRefereeTransform();
                if (_referee == null)
                {
                    CameraStackBootstrap.LOG?.LogDebug("CameraStack: Referee not found yet...");
                }
                else
                {
                    CameraStackBootstrap.LOG?.LogInfo("CameraStack: Referee found.");
                }
            }

            if (_referee != null && _anchor == null)
            {
                var anchorGo = new GameObject("CameraStack_RefereeAnchor");
                _anchor = anchorGo.transform;
                _anchor.SetParent(_referee, worldPositionStays: false);
                _anchor.localPosition = new Vector3(0f, anchorHeight, 0f);
                _anchor.localRotation = Quaternion.identity;
            }

            if (_anchor != null && _overlayCam == null)
            {
                var camGo = new GameObject("CameraStack_OverlayCam");
                _overlayCam = camGo.AddComponent<Camera>();
                camGo.hideFlags = HideFlags.None; // scene-bound

                _overlayCam.transform.SetParent(_anchor, worldPositionStays: false);
                _overlayCam.transform.localPosition = cameraLocalOffset;
                _overlayCam.transform.localRotation = Quaternion.Euler(cameraLocalEuler);

                _overlayCam.rect = ComputeViewportRect();
                _overlayCam.depth = 100f; // draw on top of the main

                _main = Camera.main;
                CopyMainCameraSettingsSafe(_overlayCam);

                _overlayCam.enabled = true;
                CameraStackBootstrap.LOG?.LogInfo("CameraStack: Overlay camera created (bottom-right).");
            }
        }

        // Ensure UI exists (button to cycle players)
        if (showUiButton)
        {
            EnsureUiCreated();
        }
    }

    private Rect ComputeViewportRect()
    {
        var w = Mathf.Clamp01(viewportWidth);
        var h = Mathf.Clamp01(viewportHeight);
        var x = Mathf.Clamp01(1f - w - viewportMarginX);
        var y = Mathf.Clamp01(viewportMarginY);
        return new Rect(x, y, w, h);
    }

    private const string PlayerRectXKey = "CameraStack_PlayerPiP_X";
    private const string PlayerRectYKey = "CameraStack_PlayerPiP_Y";

    private Rect ComputePlayerViewportRect()
    {
        var w = Mathf.Clamp01(playerViewportWidth);
        var h = Mathf.Clamp01(playerViewportHeight);

        if (!_playerViewportPosInitialized)
        {
            // Try load persisted placement first
            if (PlayerPrefs.HasKey(PlayerRectXKey) && PlayerPrefs.HasKey(PlayerRectYKey))
            {
                _playerViewportX = Mathf.Clamp01(PlayerPrefs.GetFloat(PlayerRectXKey));
                _playerViewportY = Mathf.Clamp01(PlayerPrefs.GetFloat(PlayerRectYKey));
            }
            else
            {
                _playerViewportX = Mathf.Clamp01(playerViewportMarginX);
                _playerViewportY = Mathf.Clamp01(1f - h - playerViewportMarginY);
            }
            _playerViewportPosInitialized = true;
        }

        // Clamp runtime position against current size
        var maxX = Mathf.Clamp01(1f - w);
        var maxY = Mathf.Clamp01(1f - h);
        var x = Mathf.Clamp(_playerViewportX, 0f, maxX);
        var y = Mathf.Clamp(_playerViewportY, 0f, maxY);
        _playerViewportX = x;
        _playerViewportY = y;
        return new Rect(x, y, w, h);
    }

    private Transform ResolveRefereeTransform()
    {
        try
        {
            var builder = FindObjectOfType<Match3DBuilder>();
            if (builder == null || builder.m_matchScene == null || builder.m_matchScene.Referee == null)
                return null;

            var refereeObj = builder.m_matchScene.Referee; // likely a Transform or wrapper
            var refereeTransform = refereeObj.transform;

            // Try resolve the visual wrapper if available
            var allPlayers = FindObjectsByType<PlayerVisuals3D>(FindObjectsSortMode.InstanceID);
            var refereeVisuals = allPlayers.FirstOrDefault(p => p.transform == refereeTransform);
            return refereeVisuals != null ? refereeVisuals.transform : refereeTransform;
        }
        catch (Exception ex)
        {
            CameraStackBootstrap.LOG?.LogWarning($"CameraStack: ResolveRefereeTransform failed: {ex.Message}");
            return null;
        }
    }
    
    private Transform[] ResolvePlayersTransform()
    {
        // var allP = FindObjectsByType<Player>(FindObjectsSortMode.InstanceID);
        
        var allManagers = FindObjectsByType<PlayerVisuals3D>(FindObjectsSortMode.InstanceID);
        var players = allManagers.ToArray();
        return players.Select(p => p.transform).ToArray();
    }

    private void EnsurePlayersResolved()
    {
        try
        {
            var list = ResolvePlayersTransform();
            if (list == null || list.Length == 0)
            {
                _players = Array.Empty<Transform>();
                return;
            }

            // Filter nulls and ensure uniqueness
            _players = list.Where(t => t != null).Distinct().ToArray();

            if (_players.Length == 0)
            {
                _playerIndex = -1;
                return;
            }

            // If current selection invalid, reset to first
            if (_playerIndex < 0 || _playerIndex >= _players.Length || _players[_playerIndex] == null)
            {
                SetPlayerIndex(0);
            }
            else
            {
                // Update anchor parenting if target instance changed
                SetupOrUpdatePlayerAnchorAndCamera();
            }
        }
        catch (Exception ex)
        {
            CameraStackBootstrap.LOG?.LogWarning($"CameraStack: EnsurePlayersResolved failed: {ex.Message}");
        }
    }

    private void SetPlayerIndex(int idx)
    {
        if (_players == null || _players.Length == 0)
        {
            _playerIndex = -1;
            return;
        }
        if (idx < 0) idx = 0;
        idx = idx % _players.Length;
        _playerIndex = idx;
        SetupOrUpdatePlayerAnchorAndCamera();
        UpdateUiButtonLabel();
    }

    private void CycleToNextPlayer()
    {
        if (_players == null || _players.Length == 0)
        {
            EnsurePlayersResolved();
            if (_players == null || _players.Length == 0) return;
        }
        var next = (_playerIndex + 1 + _players.Length) % _players.Length;
        SetPlayerIndex(next);
        CameraStackBootstrap.LOG?.LogInfo($"CameraStack: Switched player PiP to index {next} of {_players.Length}.");
    }

    private void MaintainPlayerPiP()
    {
        if (!enablePlayerPiP) return;

        if (_players == null || _players.Length == 0)
        {
            // Attempt resolve; will early out if still none
            EnsurePlayersResolved();
            if (_players == null || _players.Length == 0) return;
        }

        if (_playerIndex < 0) SetPlayerIndex(0);

        // Keep anchor parented and set local transform
        if (_playerAnchor != null)
        {
            var target = _players != null && _playerIndex >= 0 && _playerIndex < _players.Length ? _players[_playerIndex] : null;
            if (target != null && _playerAnchor.parent != target)
            {
                _playerAnchor.SetParent(target, worldPositionStays: false);
            }
            _playerAnchor.localPosition = new Vector3(0f, playerAnchorHeight, 0f);
            _playerAnchor.localRotation = Quaternion.identity;
        }

        // Maintain camera local transform and viewport
        if (_playerOverlayCam != null)
        {
            var t = _playerOverlayCam.transform;
            t.localPosition = playerCameraLocalOffset;
            t.localRotation = Quaternion.Euler(playerCameraLocalEuler);
            _playerOverlayCam.rect = ComputePlayerViewportRect();
        }
    }

    private void SetupOrUpdatePlayerAnchorAndCamera()
    {
        if (!enablePlayerPiP) return;
        if (_players == null || _players.Length == 0) return;
        if (_playerIndex < 0 || _playerIndex >= _players.Length) return;

        var target = _players[_playerIndex];
        if (target == null) return;

        if (_playerAnchor == null)
        {
            var go = new GameObject("CameraStack_PlayerAnchor");
            _playerAnchor = go.transform;
        }
        _playerAnchor.SetParent(target, worldPositionStays: false);
        _playerAnchor.localPosition = new Vector3(0f, playerAnchorHeight, 0f);
        _playerAnchor.localRotation = Quaternion.identity;

        if (_playerOverlayCam == null)
        {
            var camGo = new GameObject("CameraStack_PlayerOverlayCam");
            _playerOverlayCam = camGo.AddComponent<Camera>();
            camGo.hideFlags = HideFlags.None;
            _playerOverlayCam.transform.SetParent(_playerAnchor, worldPositionStays: false);
            _playerOverlayCam.depth = 101f; // over main and ref PiP if overlapping order matters
            _playerOverlayCam.rect = ComputePlayerViewportRect();
            CopyMainCameraSettingsSafe(_playerOverlayCam);
            _playerOverlayCam.enabled = true;
        }

        // Ensure local transform
        _playerOverlayCam.transform.localPosition = playerCameraLocalOffset;
        _playerOverlayCam.transform.localRotation = Quaternion.Euler(playerCameraLocalEuler);
    }

    private void CopyMainCameraSettingsSafe(Camera cam)
    {
        if (!copyMainCameraSettings) return;
        if (_main == null) _main = Camera.main;
        if (_main == null || cam == null) return;
        try
        {
            cam.fieldOfView = _main.fieldOfView;
            cam.nearClipPlane = _main.nearClipPlane;
            cam.farClipPlane = _main.farClipPlane;
            cam.allowHDR = _main.allowHDR;
            cam.allowMSAA = _main.allowMSAA;
            cam.clearFlags = _main.clearFlags;
            cam.backgroundColor = _main.backgroundColor;
            cam.cullingMask = _main.cullingMask;
        }
        catch { }
    }

    private void EnsureUiCreated()
    {
        if (!showUiButton) return;
        if (_nextBtn != null && _container != null) return;

        try
        {
            if (_uiScaffold == null)
            {
                _uiScaffold = new RuntimeUiScaffold("CameraStack_UI", 100000);
            }
            _uiScaffold.EnsureDocument(preferExisting: true);
            var root = _uiScaffold.Root;
            if (root == null)
            {
                CameraStackBootstrap.LOG?.LogWarning("CameraStack: EnsureUiCreated has no rootVisualElement.");
                return;
            }

            _container = _uiScaffold.CreateContainer("CameraStack_PiPControls", leftPercent: playerViewportMarginX * 100f, topPixels: 6);
            _nextBtn = _uiScaffold.CreateButton("Next Player", CycleToNextPlayer, "CameraStack_NextPlayerBtn");
            _container.Add(_nextBtn);
            root.Add(_container);

            UpdateUiButtonLabel();

            var info = _uiScaffold.AttachedToExisting ? "existing" : "own";
            try
            {
                var doc = _uiScaffold.Document;
                CameraStackBootstrap.LOG?.LogInfo($"CameraStack: UI created and attached to {info} UIDocument (name='{doc?.name}', GO='{doc?.gameObject.name}').");
            }
            catch { }
        }
        catch (Exception ex)
        {
            CameraStackBootstrap.LOG?.LogWarning($"CameraStack: EnsureUiCreated failed: {ex.Message}");
        }
    }


    private void OnNextButtonClick(ClickEvent evt)
    {
        CycleToNextPlayer();
    }

    private void OnNextPointerUp(PointerUpEvent evt)
    {
        CycleToNextPlayer();
        evt?.StopImmediatePropagation();
    }

    private void UpdateUiButtonLabel()
    {
        if (_nextBtn == null) return;
        if (_players == null || _players.Length == 0 || _playerIndex < 0)
        {
            _nextBtn.text = "Next Player (n/a)";
            return;
        }
        var name = _players[_playerIndex] != null ? _players[_playerIndex].name : "<null>";
        _nextBtn.text = $"Next Player (current: {name})";
    }

    // --- Drag handlers for moving the Player PiP viewport ---
    private void OnContainerPointerDown(PointerDownEvent evt)
    {
        try
        {
            if (evt == null) return;
            // Ensure initial values exist
            var rect = ComputePlayerViewportRect();
            _dragStartX = _playerViewportX;
            _dragStartY = _playerViewportY;
            _dragStartMouse = new Vector2(evt.position.x, evt.position.y);
            _isDragging = true;
            // Note: pointer capture not used for max compatibility across UI Toolkit versions
            evt.StopImmediatePropagation();
        }
        catch { }
    }

    private void OnContainerPointerMove(PointerMoveEvent evt)
    {
        try
        {
            if (!_isDragging || evt == null) return;
            ApplyDragDelta(evt.position.x, evt.position.y);
            evt.StopImmediatePropagation();
        }
        catch { }
    }

    private void OnContainerPointerUp(PointerUpEvent evt)
    {
        try
        {
            if (!_isDragging) return;
            _isDragging = false;
            // No pointer release (for compatibility)
            PersistPlayerViewportPosition();
            evt?.StopImmediatePropagation();
        }
        catch { }
    }

    // Mouse event fallbacks map to the same logic
    private void OnContainerMouseDown(MouseDownEvent evt)
    {
        try
        {
            if (evt == null) return;
            var rect = ComputePlayerViewportRect();
            _dragStartX = _playerViewportX;
            _dragStartY = _playerViewportY;
            _dragStartMouse = new Vector2(evt.mousePosition.x, evt.mousePosition.y);
            _isDragging = true;
            evt.StopImmediatePropagation();
        }
        catch { }
    }

    private void OnContainerMouseMove(MouseMoveEvent evt)
    {
        try
        {
            if (!_isDragging || evt == null) return;
            ApplyDragDelta(evt.mousePosition.x, evt.mousePosition.y);
            evt.StopImmediatePropagation();
        }
        catch { }
    }

    private void OnContainerMouseUp(MouseUpEvent evt)
    {
        try
        {
            if (!_isDragging) return;
            _isDragging = false;
            PersistPlayerViewportPosition();
            evt?.StopImmediatePropagation();
        }
        catch { }
    }

    private void ApplyDragDelta(float pointerX, float pointerY)
    {
        float screenW = Mathf.Max(1f, (float)Screen.width);
        float screenH = Mathf.Max(1f, (float)Screen.height);
        var currentPos2 = new Vector2(pointerX, pointerY);
        Vector2 delta = currentPos2 - _dragStartMouse; // UI Toolkit origin top-left, y increases down

        // Convert pixel delta to normalized camera rect delta
        float dx = delta.x / screenW;
        float dy = -delta.y / screenH; // invert Y: UI y-down -> Camera.rect y-up (origin bottom)

        float w = Mathf.Clamp01(playerViewportWidth);
        float h = Mathf.Clamp01(playerViewportHeight);

        float maxX = Mathf.Clamp01(1f - w);
        float maxY = Mathf.Clamp01(1f - h);

        _playerViewportX = Mathf.Clamp(_dragStartX + dx, 0f, maxX);
        _playerViewportY = Mathf.Clamp(_dragStartY + dy, 0f, maxY);

        if (_playerOverlayCam != null)
        {
            _playerOverlayCam.rect = new Rect(_playerViewportX, _playerViewportY, w, h);
        }
    }

    private void PersistPlayerViewportPosition()
    {
        try
        {
            PlayerPrefs.SetFloat(PlayerRectXKey, _playerViewportX);
            PlayerPrefs.SetFloat(PlayerRectYKey, _playerViewportY);
            PlayerPrefs.Save();
        }
        catch { }
    }

    // Allow dragging directly on the Player PiP camera rectangle (no UI needed)
    private void TryHandleScreenSpaceDrag()
    {
        if (_playerOverlayCam == null) return;
        if (Mouse.current == null) return;

        var mouse = Mouse.current;
        var left = mouse.leftButton;
        if (left == null) return;

        // Convert camera rect to screen pixels (origin bottom-left)
        var r = ComputePlayerViewportRect();
        float sw = Mathf.Max(1f, (float)Screen.width);
        float sh = Mathf.Max(1f, (float)Screen.height);
        Rect pixelRect = new Rect(r.x * sw, r.y * sh, r.width * sw, r.height * sh);

        Vector2 cur = mouse.position.ReadValue(); // bottom-left origin

        if (!_isScreenDragging)
        {
            // Start drag only when press begins inside the PiP rect
            if (left.wasPressedThisFrame && pixelRect.Contains(cur))
            {
                _isScreenDragging = true;
                _screenDragStartMouse = cur;
                _screenDragStartX = _playerViewportX;
                _screenDragStartY = _playerViewportY;
            }
        }
        else
        {
            if (left.isPressed)
            {
                // Apply delta (no Y inversion here; both mouse and Camera.rect use bottom-origin)
                Vector2 delta = cur - _screenDragStartMouse;
                float dx = delta.x / sw;
                float dy = delta.y / sh;

                float w = Mathf.Clamp01(playerViewportWidth);
                float h = Mathf.Clamp01(playerViewportHeight);
                float maxX = Mathf.Clamp01(1f - w);
                float maxY = Mathf.Clamp01(1f - h);

                _playerViewportX = Mathf.Clamp(_screenDragStartX + dx, 0f, maxX);
                _playerViewportY = Mathf.Clamp(_screenDragStartY + dy, 0f, maxY);

                _playerOverlayCam.rect = new Rect(_playerViewportX, _playerViewportY, w, h);
            }

            if (left.wasReleasedThisFrame)
            {
                _isScreenDragging = false;
                PersistPlayerViewportPosition();
            }
        }
    }


    private void OnDestroy()
    {
        try { if (_overlayCam != null) Destroy(_overlayCam.gameObject); } catch { }
        try { if (_playerOverlayCam != null) Destroy(_playerOverlayCam.gameObject); } catch { }
        try { if (_anchor != null) Destroy(_anchor.gameObject); } catch { }
        try { if (_playerAnchor != null) Destroy(_playerAnchor.gameObject); } catch { }
        try { _uiScaffold?.Dispose(); } catch { }
        try { PlayerPrefs.SetFloat(PlayerRectXKey, _playerViewportX); PlayerPrefs.SetFloat(PlayerRectYKey, _playerViewportY); PlayerPrefs.Save(); } catch { }
        _overlayCam = null;
        _playerOverlayCam = null;
        _anchor = null;
        _playerAnchor = null;
        _referee = null;
        _players = null;
        _uiScaffold = null;
        _nextBtn = null;
    }
}