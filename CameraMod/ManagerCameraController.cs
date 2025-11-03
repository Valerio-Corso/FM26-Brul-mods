using System;
using BepInEx.Logging;
using Il2CppInterop.Runtime.Startup;
using System.Collections.Generic;
using FM.Graphics.CharacterBuilder.AppearanceController;
using FM.Match;
using FM.Match.Animation;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.Playables;
using KeyCode = BepInEx.Unity.IL2CPP.UnityEngine.KeyCode;

namespace ManagerCameraMod;

public class ManagerCameraController : MonoBehaviour
{
    private const string ManagerPath = "MatchPlaybackController/MatchComponents/Match3DBuilder/Manager(Clone)/ManagerController";
    private const string BallPath = "MatchPlaybackController/MatchComponents/Match3DBuilder/BallPrefab(Clone)";
    private const string GameScene = "MatchPlayback";


    private Key _activationKey = Key.F3;
    public Vector3 cameraLocalOffset = new Vector3(0f, 1.6f, .3f);

    // Mouse look settings
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

    // Clone-based control: keep original invisible, move the clone instead
    private Transform _managerOriginal;
    private GameObject _managerClone;

    private class RendererState
    {
        public Renderer Renderer;
        public bool WasEnabled;
    }

    private readonly List<RendererState> _originalRendererStates = new();

    public void Init(ManualLogSource logger)
    {
        _logger = logger;
        try
        {
            // Load settings from plugin config
            var keyStr = ManagerCameraBootstrap.ActivationKey?.Value;
            if (!string.IsNullOrWhiteSpace(keyStr))
            {
                if (!Enum.TryParse<Key>(keyStr.Trim(), true, out _activationKey))
                {
                    _activationKey = Key.F3;
                    _logger.LogWarning($"Invalid ActivationKey '{keyStr}'. Falling back to F3.");
                }
            }

            enableZoom = ManagerCameraBootstrap.EnableZoom?.Value ?? enableZoom;
            mouseSensitivity = ManagerCameraBootstrap.MouseSensitivity?.Value ?? mouseSensitivity;
            zoomSpeed = ManagerCameraBootstrap.ZoomSpeed?.Value ?? zoomSpeed;
            minFov = ManagerCameraBootstrap.MinFov?.Value ?? minFov;
            maxFov = ManagerCameraBootstrap.MaxFov?.Value ?? maxFov;
            enableManagerMovement = ManagerCameraBootstrap.EnableManagerMovement?.Value ?? enableManagerMovement;
            cameraRelativeMovement = ManagerCameraBootstrap.CameraRelativeMovement?.Value ?? cameraRelativeMovement;
            moveSpeed = ManagerCameraBootstrap.MoveSpeed?.Value ?? moveSpeed;
            disableManagerAnimator = ManagerCameraBootstrap.DisableManagerAnimator?.Value ?? disableManagerAnimator;
            lockCursorWhilePanning = ManagerCameraBootstrap.LockCursorWhilePanning?.Value ?? lockCursorWhilePanning;
            copySettingsFromMain = ManagerCameraBootstrap.CopySettingsFromMain?.Value ?? copySettingsFromMain;
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to read ManagerCameraMod config: {ex.Message}");
        }
    }

    private bool test = false;

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            var keyCtrl = keyboard[_activationKey];
            if (keyCtrl != null && keyCtrl.wasPressedThisFrame)
            {
                if (_customCameraActive)
                    DisableCustomCamera();
                else
                    EnableCustomCamera();
            }
        }

        // While active, allow right-mouse-button panning; keep camera anchored to Manager
        if (_customCameraActive)
        {
            if (_customCamera == null || _manager == null)
            {
                _logger.LogWarning("ManagerCameraBootstrap: Required reference lost; deactivating our camera.");
                DisableCustomCamera();
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

                // Rotate the manager to face the same yaw as the camera (mouse look)
                if (_manager != null)
                {
                    var camFwd = _customCamera.transform.forward;
                    camFwd.y = 0f;
                    if (camFwd.sqrMagnitude > 1e-6f)
                    {
                        _manager.rotation = Quaternion.LookRotation(camFwd, Vector3.up);
                    }
                }
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
                var kb = Keyboard.current;
                if (kb != null)
                {
                    if (kb[Key.A].isPressed) h -= 1;
                    if (kb[Key.D].isPressed) h += 1;
                    if (kb[Key.S].isPressed) v -= 1;
                    if (kb[Key.W].isPressed) v += 1;
                }

                if (h != 0 || v != 0)
                {
                    Vector3 moveDir;
                    if (cameraRelativeMovement && _customCamera != null)
                    {
                        var fwd = _customCamera.transform.forward;
                        fwd.y = 0f;
                        if (fwd.sqrMagnitude > 1e-6f) fwd.Normalize();
                        var right = _customCamera.transform.right;
                        right.y = 0f;
                        if (right.sqrMagnitude > 1e-6f) right.Normalize();
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

    private bool MouseRightHeld()
    {
        var m = Mouse.current;
        return m != null && m.rightButton.isPressed;
    }

    private bool MouseRightDown()
    {
        var m = Mouse.current;
        return m != null && m.rightButton.wasPressedThisFrame;
    }

    private bool MouseRightUp()
    {
        var m = Mouse.current;
        return m != null && m.rightButton.wasReleasedThisFrame;
    }

    private Vector2 GetMouseDelta()
    {
        var m = Mouse.current;
        return m != null ? m.delta.ReadValue() : Vector2.zero;
    }

    private float GetScrollY()
    {
        var m = Mouse.current;
        return m != null ? m.scroll.ReadValue().y : 0f;
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

    private GameObject CreateManagerClone(Transform original)
    {
        try
        {
            if (original == null) return null;

            var clone = Instantiate(original.gameObject);
            clone.name = original.gameObject.name + " (CameraClone)";

            // Match world transform
            clone.transform.position = original.position;
            clone.transform.rotation = original.rotation;
            clone.transform.localScale = original.localScale;

            // Keep all components enabled on the clone per user preference.
            // Ensure the clone actually renders meshesparent
            var renderers = clone.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                try { r.enabled = true; } catch { }
                if (r is SkinnedMeshRenderer skinned)
                {
                    // Prevent culling issues when offscreen
                    try { skinned.updateWhenOffscreen = true; } catch { }
                }
            }

            return clone;
        }
        catch (Exception ex)
        {
            _logger?.LogError($"ManagerCamera: CreateManagerClone failed: {ex}");
            return null;
        }
    }

    private void HideOriginalRenderers(Transform original)
    {
        try
        {
            _originalRendererStates.Clear();
            if (original == null) return;
            var renderers = original.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                if (r == null) continue;
                var state = new RendererState { Renderer = r, WasEnabled = false };
                try { state.WasEnabled = r.enabled; } catch { }
                _originalRendererStates.Add(state);
                try { r.enabled = false; } catch { }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning($"ManagerCamera: HideOriginalRenderers error: {ex.Message}");
        }
    }

    private void RestoreOriginalRenderers()
    {
        try
        {
            if (_originalRendererStates.Count == 0) return;
            foreach (var st in _originalRendererStates)
            {
                if (st?.Renderer == null) continue;
                try { st.Renderer.enabled = st.WasEnabled; } catch { }
            }
        }
        finally
        {
            _originalRendererStates.Clear();
        }
    }

    private void DestroyManagerClone()
    {
        if (_managerClone != null)
        {
            try { Destroy(_managerClone); } catch { }
            _managerClone = null;
        }
    }

    private void EnableCustomCamera()
    {
        try
        {
            _lastMainCamera = Camera.main;

            // TODO: Why it doesn't work... usual error when resolving 
            var allManagers = FindObjectsByType<AppearanceController>(FindObjectsSortMode.InstanceID);
            foreach (var manager in allManagers)
            {
                if (manager.m_characterRole == CharacterRole.UserManager)
                {
                    _managerOriginal = manager.transform;

                    // If we already have a leftover clone for some reason, clean it up
                    if (_managerClone != null)
                    {
                        try { Destroy(_managerClone); } catch { }
                        _managerClone = null;
                    }

                    // Try to clone the original and control the clone instead
                    _managerClone = CreateManagerClone(_managerOriginal.parent);
                    if (_managerClone != null)
                    {
                        _manager = _managerClone.transform;
                        HideOriginalRenderers(_managerOriginal);
                        _logger?.LogInfo("ManagerCamera: Using manager clone for movement; original renderers hidden.");
                    }
                    else
                    {
                        // Fallback: control the original if cloning failed
                        _manager = _managerOriginal;
                        _logger?.LogWarning("ManagerCamera: Failed to clone manager; falling back to controlling the original.");
                    }

                    break;
                }
            }

            // If we couldn't locate the manager, abort safely
            if (_manager == null)
            {
                _logger?.LogWarning("ManagerCamera: UserManager not found in scene; cannot enable custom camera.");
                return;
            }

            // Create camera
            if (_customCamera == null)
            {
                var go = new GameObject("ManagerCam_Custom");
                _customCamera = go.AddComponent<Camera>();
                // Keep camera scene-bound; do not persist across scenes
                go.hideFlags = HideFlags.None;

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

    private void DisableCustomCamera()
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

            // Restore original manager visibility and destroy our clone (if any)
            RestoreOriginalRenderers();
            DestroyManagerClone();

            // Restore any temporarily disabled components on Manager (legacy path)
            if (disableManagerAnimator)
            {
                try
                {
                    RestoreDisabledManagerComponents();
                }
                catch
                {
                }
            }

            // Re-enable the game's camera
            if (_lastMainCamera != null)
            {
                try
                {
                    _lastMainCamera.enabled = _originalMainEnabled;
                }
                catch
                {
                }
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
            _managerOriginal = null;
            _ball = null;
            _lastMainCamera = null;
        }
    }

    public void PrintAnimatorInfo(Animator animator)
    {
        if (animator == null)
        {
            Debug.Log("Animator is null");
            return;
        }

        // Get the RuntimeAnimatorController
        var controller = animator.runtimeAnimatorController;
        if (controller == null)
        {
            Debug.Log("No AnimatorController assigned");
            return;
        }

        Debug.Log($"=== Animator Controller: {controller.name} ===");

        // Print all animation clips
        Debug.Log("\n--- Animation Clips ---");
        foreach (var clip in controller.animationClips)
        {
            Debug.Log($"Clip: {clip.name}, Length: {clip.length}s, FPS: {clip.frameRate}");
        }

        // Get layers and states (requires casting to AnimatorController for edit-time, 
        // or using reflection/layers for runtime)
        Debug.Log("\n--- Layers and States ---");
        for (int i = 0; i < animator.layerCount; i++)
        {
            Debug.Log($"\nLayer {i}: {animator.GetLayerName(i)}, Weight: {animator.GetLayerWeight(i)}");
        }

        // Print current state info for each layer
        Debug.Log("\n--- Current States ---");
        for (int i = 0; i < animator.layerCount; i++)
        {
            var stateInfo = animator.GetCurrentAnimatorStateInfo(i);
            Debug.Log($"Layer {i}: Current State Hash: {stateInfo.fullPathHash}, " +
                      $"Normalized Time: {stateInfo.normalizedTime:F2}");
        }

        // Print parameters
        Debug.Log("\n--- Parameters ---");
        foreach (var param in animator.parameters)
        {
            string value = param.type switch
            {
                AnimatorControllerParameterType.Float => animator.GetFloat(param.name).ToString("F2"),
                AnimatorControllerParameterType.Int => animator.GetInteger(param.name).ToString(),
                AnimatorControllerParameterType.Bool => animator.GetBool(param.name).ToString(),
                AnimatorControllerParameterType.Trigger => "(Trigger)",
                _ => "Unknown"
            };
            Debug.Log($"Parameter: {param.name}, Type: {param.type}, Value: {value}");
        }
    }
}