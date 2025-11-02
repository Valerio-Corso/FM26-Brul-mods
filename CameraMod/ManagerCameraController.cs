using System;
using BepInEx.Logging;
using Il2CppInterop.Runtime.Startup;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using KeyCode = BepInEx.Unity.IL2CPP.UnityEngine.KeyCode;

namespace ManagerCameraMod;

public class ManagerCameraController : MonoBehaviour
{
    private const string ManagerPath = "MatchPlaybackController/MatchComponents/Match3DBuilder/Manager(Clone)/ManagerController";
    private const string BallPath = "MatchPlaybackController/MatchComponents/Match3DBuilder/BallPrefab(Clone)";
    private const string GameScene = "MatchPlayback";
    
    //AppearanceController/
    
    public KeyCode activationKey = KeyCode.F1;
    public Vector3 cameraLocalOffset = new Vector3(0f, 1.6f, .3f);

    // Mouse look settings
    public int panMouseButton = 1;
    public float mouseSensitivity = 0.5f;
    public bool lockCursorWhilePanning = true;

    // Zoom settings
    public bool enableZoom = true;
    public float zoomSpeed = 5f;
    public float minFov = 60f;
    public float maxFov = 110;

    // Manager movement settings
    public bool enableManagerMovement = true;
    public bool cameraRelativeMovement = true;
    public float moveSpeed = 4f;
    public bool disableManagerAnimator = true;

    // Internal mouse look state
    private float _yaw;
    private bool _isPanning;

    private ManualLogSource _logger;
    private readonly List<Behaviour> _temporarilyDisabledBehaviours = new();

    private Transform _manager;
    private Transform _ball;
    private Camera _lastMainCamera;
    private Camera _customCamera;
    private bool _customCameraActive;
    public bool copySettingsFromMain = true;
    private bool _originalMainEnabled = true;
    private string _lastSceneName = string.Empty;
    
    // Reflection caches for Unity Input System (if present)
    private System.Type _tKeyboard;
    private System.Type _tKeyEnum;
    private System.Type _tKeyControl;
    private System.Reflection.PropertyInfo _currentInput;
    private System.Reflection.PropertyInfo _currentInputItem;
    private System.Reflection.PropertyInfo _currentInputWasPressed;
    private System.Reflection.PropertyInfo _currentInputIsPressed;

    // Mouse reflection caches (Unity Input System)
    private System.Type _tMouse;
    private System.Type _tButtonControl;
    private System.Type _tVector2Control;
    private System.Reflection.PropertyInfo _mouseCurrent;
    private System.Reflection.PropertyInfo _mouseRightButton;
    private System.Reflection.PropertyInfo _mouseDelta;
    private System.Reflection.PropertyInfo _mouseScroll;
    private System.Reflection.PropertyInfo _buttonIsPressed;
    private System.Reflection.PropertyInfo _buttonWasPressedThisFrame;
    private System.Reflection.PropertyInfo _buttonWasReleasedThisFrame;
    private System.Reflection.MethodInfo _vector2ReadValue;
    

    public void Init(ManualLogSource logger)
    {
        _logger = logger;
    }
    
    private bool test = false;
    private void Update()
    {
        // todo: works, to test more
        // if (Keyboard.current[Key.F1].wasPressedThisFrame)
        // {
        //     _logger.LogWarning($"[CAMMOD] Input recognized");
        // }
        
        var activeScene = SceneManager.GetActiveScene();

        // TODO: Temp fix: Detect scene changes without using SceneManager.activeSceneChanged (IL2CPP issues)
        if (activeScene.IsValid())
        {
            var sceneName = activeScene.name;
            if (_lastSceneName != sceneName)
            {
                if (_customCameraActive) DisableCustomCamera(true);

                _lastSceneName = sceneName;
                _manager = null;
                _ball = null;
                _lastMainCamera = null;
            }
        }

        // Only operate in the MatchPlayback scene.
        // TODO: Test removing this as the scene is destroyed the camera will be too (remove DontDestroyOnLoad)
        if (!activeScene.IsValid() || activeScene.name != GameScene)
        {
            if (_customCameraActive)
                DisableCustomCamera(true);
            return;
        }

        if (NewInputWasPressedThisFrame(activationKey))
        {
            if (_customCameraActive)
                DisableCustomCamera(false);
            else
                EnableCustomCamera();
        }

        // While active, allow right-mouse-button panning; keep camera anchored to Manager
        if (_customCameraActive)
        {
            if (_customCamera == null || _manager == null)
            {
                _logger.LogWarning("ManagerCameraBootstrap: Required reference lost; deactivating our camera.");
                DisableCustomCamera(false);
                return;
            }

            // Begin panning on RMB down (InputSystem-reflection first, legacy fallback)
            if (MouseRightDown())
            {
                _isPanning = true;
                // initialize from current local rotation in case it changed externally
                var e = _customCamera.transform.localEulerAngles;
                _yaw = e.y;
                if (lockCursorWhilePanning)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
            }

            // End panning on RMB up
            if (MouseRightUp())
            {
                _isPanning = false;
                if (lockCursorWhilePanning)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
            }

            // While holding RMB, update yaw from mouse delta (horizontal pan only)
            if (MouseRightHeld())
            {
                var delta = GetMouseDelta();
                float mouseX = delta.x * mouseSensitivity;
                _yaw += mouseX;
                _customCamera.transform.localRotation = Quaternion.Euler(0f, _yaw, 0f);
            }

            // Optional zoom via mouse wheel
            if (enableZoom && _customCamera != null)
            {
                float scrollY = GetScrollY();
                if (Mathf.Abs(scrollY) > 0.0001f)
                {
                    // Normalize: legacy returns ~0.1 per notch; InputSystem often returns 120 per notch
                    if (Mathf.Abs(scrollY) > 10f) scrollY /= 120f;
                    var fov = _customCamera.fieldOfView - scrollY * zoomSpeed;
                    _customCamera.fieldOfView = Mathf.Clamp(fov, minFov, maxFov);
                }
            }

            // WASD manager movement (camera-relative on XZ plane)
            if (enableManagerMovement && _manager != null)
            {
                int h = 0;
                int v = 0;
                if (IsKeyHeld(KeyCode.A)) h -= 1;
                if (IsKeyHeld(KeyCode.D)) h += 1;
                if (IsKeyHeld(KeyCode.S)) v -= 1;
                if (IsKeyHeld(KeyCode.W)) v += 1;

                if (h != 0 || v != 0)
                {
                    Vector3 moveDir;
                    if (cameraRelativeMovement && _customCamera != null)
                    {
                        var fwd = _customCamera.transform.forward; fwd.y = 0f; if (fwd.sqrMagnitude > 1e-6f) fwd.Normalize();
                        var right = _customCamera.transform.right; right.y = 0f; if (right.sqrMagnitude > 1e-6f) right.Normalize();
                        moveDir = right * h + fwd * v;
                    }
                    else
                    {
                        moveDir = new Vector3(h, 0f, v);
                    }
                    if (moveDir.sqrMagnitude > 1f) moveDir.Normalize();

                    var pos = _manager.position;
                    pos += moveDir * moveSpeed * Time.deltaTime;
                    // keep original Y height
                    pos.y = _manager.position.y;
                    _manager.position = pos;
                }
            }
        }
    }


    private static float NormalizeAngle(float angle)
    {
        angle %= 360f;
        if (angle > 180f) angle -= 360f;
        if (angle < -180f) angle += 360f;
        return angle;
    }

    // TODO: Ugly reflection, ideally i would use Unity's InputSystem API directly', not sure why that's not working
    private bool TryInitInputSystem()
    {
        if (_tKeyboard != null && _tKeyEnum != null && _tKeyControl != null && _currentInput != null && _currentInputItem != null && _currentInputWasPressed != null && _currentInputIsPressed != null)
            return true;

        try
        {
            _tKeyboard = System.Type.GetType("UnityEngine.InputSystem.Keyboard, Unity.InputSystem");
            _tKeyEnum = System.Type.GetType("UnityEngine.InputSystem.Key, Unity.InputSystem");
            _tKeyControl = System.Type.GetType("UnityEngine.InputSystem.Controls.KeyControl, Unity.InputSystem");
            if (_tKeyboard == null || _tKeyEnum == null || _tKeyControl == null)
                return false;

            _currentInput = _tKeyboard.GetProperty("current", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            _currentInputItem = _tKeyboard.GetProperty("Item", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            _currentInputWasPressed = _tKeyControl.GetProperty("wasPressedThisFrame", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            _currentInputIsPressed = _tKeyControl.GetProperty("isPressed", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            return _currentInput != null && _currentInputItem != null && _currentInputWasPressed != null && _currentInputIsPressed != null;
        }
        catch
        {
            return false;
        }
    }

    private bool TryInitMouseInput()
    {
        // UnityEngine.Object.FindObjectsByType<>()
        // FM.Match.MatchPlaybackController.ContinueButtonMatchState.
            
        if (_tMouse != null && _tButtonControl != null && _tVector2Control != null &&
            _mouseCurrent != null && _mouseRightButton != null && _mouseDelta != null && _mouseScroll != null &&
            _buttonIsPressed != null && _buttonWasPressedThisFrame != null && _buttonWasReleasedThisFrame != null)
            return true;

        try
        {
            _tMouse = System.Type.GetType("UnityEngine.InputSystem.Mouse, Unity.InputSystem");
            _tButtonControl = System.Type.GetType("UnityEngine.InputSystem.Controls.ButtonControl, Unity.InputSystem");
            _tVector2Control = System.Type.GetType("UnityEngine.InputSystem.Controls.Vector2Control, Unity.InputSystem");
            if (_tMouse == null || _tButtonControl == null || _tVector2Control == null)
                return false;

            _mouseCurrent = _tMouse.GetProperty("current", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            _mouseRightButton = _tMouse.GetProperty("rightButton", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            _mouseDelta = _tMouse.GetProperty("delta", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            _mouseScroll = _tMouse.GetProperty("scroll", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            _buttonIsPressed = _tButtonControl.GetProperty("isPressed", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            _buttonWasPressedThisFrame = _tButtonControl.GetProperty("wasPressedThisFrame", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            _buttonWasReleasedThisFrame = _tButtonControl.GetProperty("wasReleasedThisFrame", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            _vector2ReadValue = _tVector2Control.GetMethod("ReadValue", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance, new System.Type[] { });

            return _mouseCurrent != null && _mouseRightButton != null && _mouseDelta != null && _mouseScroll != null &&
                   _buttonIsPressed != null && _buttonWasPressedThisFrame != null && _buttonWasReleasedThisFrame != null &&
                   _vector2ReadValue != null;
        }
        catch
        {
            return false;
        }
    }

    private bool NewInputWasPressedThisFrame(KeyCode keyCode)
    {
        if (!TryInitInputSystem()) return false;

        var keyboard = _currentInput.GetValue(null, null);
        if (keyboard == null) return false;

        // Map KeyCode to InputSystem.Key. Handle F1 explicitly
        object keyEnumValue = null;
        try
        {
            if (keyCode == KeyCode.F1)
            {
                keyEnumValue = System.Enum.Parse(_tKeyEnum, "F1");
            }
            else
            {
                keyEnumValue = System.Enum.Parse(_tKeyEnum, keyCode.ToString(), ignoreCase: true);
            }
        }
        catch
        {
            return false;
        }

        // Access indexer: Keyboard[key]
        var keyControl = _currentInputItem.GetValue(keyboard, new object[] { keyEnumValue });
        if (keyControl == null) return false;

        var value = _currentInputWasPressed.GetValue(keyControl, null);
        return value is bool b && b;
    }

    private bool IsKeyHeld(KeyCode keyCode)
    {
        // Try the new Input System via reflection
        if (TryInitInputSystem())
        {
            var keyboard = _currentInput.GetValue(null, null);
            if (keyboard != null)
            {
                object keyEnumValue = null;
                try { keyEnumValue = System.Enum.Parse(_tKeyEnum, keyCode.ToString(), ignoreCase: true); } catch { keyEnumValue = null; }
                if (keyEnumValue != null)
                {
                    var keyControl = _currentInputItem.GetValue(keyboard, new object[] { keyEnumValue });
                    if (keyControl != null)
                    {
                        var v = _currentInputIsPressed.GetValue(keyControl, null);
                        if (v is bool b) return b;
                    }
                }
            }
        }
        // Legacy fallback: map to UnityEngine.KeyCode
        try
        {
            var legacy = (UnityEngine.KeyCode)System.Enum.Parse(typeof(UnityEngine.KeyCode), keyCode.ToString(), true);
            return Input.GetKey(legacy);
        }
        catch { return false; }
    }

    private bool MouseRightHeld()
    {
        if (TryInitMouseInput())
        {
            var mouse = _mouseCurrent.GetValue(null, null);
            if (mouse != null)
            {
                var rb = _mouseRightButton.GetValue(mouse, null);
                if (rb != null)
                {
                    var v = _buttonIsPressed.GetValue(rb, null);
                    if (v is bool b) return b;
                }
            }
        }
        // Fallback to legacy
        return Input.GetMouseButton(panMouseButton);
    }

    private bool MouseRightDown()
    {
        if (TryInitMouseInput())
        {
            var mouse = _mouseCurrent.GetValue(null, null);
            if (mouse != null)
            {
                var rb = _mouseRightButton.GetValue(mouse, null);
                if (rb != null)
                {
                    var v = _buttonWasPressedThisFrame.GetValue(rb, null);
                    if (v is bool b) return b;
                }
            }
        }
        return Input.GetMouseButtonDown(panMouseButton);
    }

    private bool MouseRightUp()
    {
        if (TryInitMouseInput())
        {
            var mouse = _mouseCurrent.GetValue(null, null);
            if (mouse != null)
            {
                var rb = _mouseRightButton.GetValue(mouse, null);
                if (rb != null)
                {
                    var v = _buttonWasReleasedThisFrame.GetValue(rb, null);
                    if (v is bool b) return b;
                }
            }
        }
        return Input.GetMouseButtonUp(panMouseButton);
    }

    private Vector2 GetMouseDelta()
    {
        if (TryInitMouseInput())
        {
            var mouse = _mouseCurrent.GetValue(null, null);
            if (mouse != null)
            {
                var delta = _mouseDelta.GetValue(mouse, null);
                if (delta != null)
                {
                    var v = _vector2ReadValue.Invoke(delta, null);
                    if (v is Vector2 vec2) return vec2;
                }
            }
        }
        // Legacy fallback
        return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
    }

    private float GetScrollY()
    {
        if (TryInitMouseInput())
        {
            var mouse = _mouseCurrent.GetValue(null, null);
            if (mouse != null)
            {
                var scroll = _mouseScroll.GetValue(mouse, null);
                if (scroll != null)
                {
                    var v = _vector2ReadValue.Invoke(scroll, null);
                    if (v is Vector2 vec2) return vec2.y;
                }
            }
        }
        // Legacy fallback
        // Prefer mouseScrollDelta if available; otherwise axis
        try { return Input.mouseScrollDelta.y; } catch { return Input.GetAxis("Mouse ScrollWheel"); }
    }

    private void TryDisableManagerInterferingComponents(GameObject root)
    {
        _temporarilyDisabledBehaviours.Clear();
        if (root == null) return;

        // Disable Animator components by name to avoid hard reference to AnimationModule
        // DisableByTypeName(root, "UnityEngine.Animator");
        // DisableByTypeName(root, "Animator");
        //
        // // Disable specific known behaviours by name (present in FM.Match)
        // DisableByTypeName(root, "FM.Match.AnimatorCharacterVisuals3D");
        // DisableByTypeName(root, "AnimatorCharacterVisuals3D");
        //
        // // Optionally disable NavMeshAgent if present to prevent navigation overriding position
        // DisableByTypeName(root, "UnityEngine.AI.NavMeshAgent");
        // DisableByTypeName(root, "NavMeshAgent");
    }

    private void RestoreDisabledManagerComponents()
    {
        if (_temporarilyDisabledBehaviours.Count == 0) return;
        foreach (var b in _temporarilyDisabledBehaviours)
        {
            if (b != null)
            {
                b.enabled = true;
            }
        }
        _temporarilyDisabledBehaviours.Clear();
    }
    
    private void EnableCustomCamera()
    {
        try
        {
            _lastMainCamera = Camera.main;
            
            // TODO: Why it doesn't work... usual error when resolving 
            // var allManagers = FindObjectsByType<AppearanceController>(FindObjectsSortMode.InstanceID);
            // foreach (var manager in allManagers)
            // {
            //     if (manager.m_characterRole == CharacterRole.UserManager)
            //     {
            //         _manager = manager.transform;
            //         break;
            //     }
            // }
            
            var managerGo = GameObject.Find(ManagerPath);
            _manager = managerGo != null ? managerGo.transform : null;
            
            // managerGo.GetComponent<FM.Match.AnimatorCharacterVisuals3D>(

            var ballGo = GameObject.Find(BallPath);
            _ball = ballGo != null ? ballGo.transform : null;

            if (_manager == null)
            {
                _logger.LogWarning($"ManagerCameraBootstrap: Activate failed — Manager not found'.");
                return;
            }
            // Ball is optional for this mode; we no longer track it

            // Create camera
            if (_customCamera == null)
            {
                var go = new GameObject("ManagerCam_Custom");
                _customCamera = go.AddComponent<Camera>();
                DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                // TODO: Check if really needed
                go.tag = "MainCamera";

                // Copy visual settings from the game's main camera
                // TODO: more wonky stuff could be done here
                if (copySettingsFromMain && _lastMainCamera != null)
                {
                    try
                    {
                        _customCamera.fieldOfView = _lastMainCamera.fieldOfView;
                        _customCamera.nearClipPlane = _lastMainCamera.nearClipPlane;
                        _customCamera.farClipPlane = _lastMainCamera.farClipPlane;
                        _customCamera.allowHDR = _lastMainCamera.allowHDR;
                        _customCamera.allowMSAA = _lastMainCamera.allowMSAA;
                        _customCamera.clearFlags = _lastMainCamera.clearFlags;
                        _customCamera.backgroundColor = _lastMainCamera.backgroundColor;
                        _customCamera.cullingMask = _lastMainCamera.cullingMask;
                        _customCamera.depth = _lastMainCamera.depth + 1f; // render on top just in case
                    }
                    catch
                    {
                        /* ignore per-field copy issues on IL2CPP */ 
                    }
                }
            }

            // Parent and orient camera relative to the Manager; user will pan with RMB
            _customCamera.transform.SetParent(_manager, worldPositionStays: true);
            _customCamera.transform.localPosition = cameraLocalOffset;
            _customCamera.transform.localRotation = Quaternion.identity;
            // Initialize yaw from current local rotation
            var eul = _customCamera.transform.localEulerAngles;
            _yaw = eul.y;

            // Optionally disable manager animations/visual controllers that might override pose/position
            if (disableManagerAnimator)
            {
                // TryDisableManagerInterferingComponents(_manager.gameObject);
            }

            // Enable camera and disable the game's camera to avoid double rendering
            if (_lastMainCamera != null)
            {
                _originalMainEnabled = _lastMainCamera.enabled;
                _lastMainCamera.enabled = false;
            }
            _customCamera.gameObject.SetActive(true);
            _customCamera.enabled = true;
            _customCameraActive = true;
            _logger.LogInfo("ManagerCameraBootstrap: Custom camera activated (RMB to pan, WASD to move manager). Press key again to restore.");
        }
        catch (System.Exception ex)
        {
            _logger.LogError($"ManagerCameraBootstrap: ActivateOurCamera failed: {ex}");
            _customCameraActive = false;
        }
    }

    private void DisableCustomCamera(bool dueToSceneChange)
    {
        try
        {
            // Ensure cursor is unlocked when disabling
            if (lockCursorWhilePanning)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            _isPanning = false;

            // Disable camera but keep it around for reuse
            if (_customCamera != null)
            {
                _customCamera.enabled = false;
                _customCamera.gameObject.SetActive(false);
                // Detach from manager to avoid being destroyed if manager is destroyed
                _customCamera.transform.SetParent(null, worldPositionStays: true);
            }

            // Restore any temporarily disabled components on Manager
            if (disableManagerAnimator)
            {
                try
                {
                    RestoreDisabledManagerComponents();
                }
                catch { }
            }

            // Re-enable the game's camera
            if (_lastMainCamera != null)
            {
                try
                {
                    _lastMainCamera.enabled = _originalMainEnabled;
                }
                catch { }
            }

            _customCameraActive = false;
        }
        catch (System.Exception ex)
        {
            _logger.LogError($"ManagerCameraBootstrap: DeactivateOurCamera failed: {ex}");
        }
        finally
        {
            _manager = null;
            _ball = null;
            _lastMainCamera = null;
        }
    }
}