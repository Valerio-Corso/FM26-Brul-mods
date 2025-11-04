using System;
using System.Linq;
using System.Reflection;
using FM.Match;
using FM.Match.Camera;
using SI.Anim;
using SI.Core;
using SI.MatchTypes;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CameraStack;

public class CameraStackController : MonoBehaviour
{
    public CameraStackController(IntPtr ptr) : base(ptr) { }

    // Settings
    public float anchorHeight = 1.7f; // where to place the anchor on the referee body
    public Vector3 cameraLocalOffset = new Vector3(0f, 0.2f, -2.5f);
    public Vector3 cameraLocalEuler = new Vector3(10f, 0f, 0f);
    public bool copyMainCameraSettings = true;

    // Bottom-right overlay viewport
    public float viewportWidth = 0.28f;
    public float viewportHeight = 0.28f;
    public float viewportMarginX = 0.02f;
    public float viewportMarginY = 0.02f;

    private Transform _referee;
    private Transform _anchor;
    private Camera _overlayCam;
    private Camera _main;

    private float _lastRefereeSearchTime;
    private const float RefereeSearchInterval = 1.0f;

    private void Start()
    {
        TrySetup();
    }

    private void Update()
    {
        // Retry setup until all pieces are available
        if (_overlayCam == null || _referee == null)
        {
            if (Time.unscaledTime - _lastRefereeSearchTime > RefereeSearchInterval)
            {
                _lastRefereeSearchTime = Time.unscaledTime;
                TrySetup();
            }
            return;
        }

        // Keep parenting in case the referee object was re-instantiated
        if (_anchor != null && _anchor.parent != _referee)
        {
            _anchor.SetParent(_referee, worldPositionStays: false);
            _anchor.localPosition = new Vector3(0f, anchorHeight, 0f);
            _anchor.localRotation = Quaternion.identity;
        }

        // Maintain desired local transform
        if (_overlayCam != null)
        {
            var t = _overlayCam.transform;
            t.localPosition = cameraLocalOffset;
            t.localRotation = Quaternion.Euler(cameraLocalEuler);
        }
        
        if (Keyboard.current != null)
        {
            var kc = Keyboard.current[Key.F4];
            if (kc == null || !kc.wasPressedThisFrame) return;
            
            var cameraDirector = FindObjectOfType<CameraDirectorComponent>();
            var visualizationMode = cameraDirector != null ? cameraDirector.m_currentMatchVisualizationMode : null;

            // if (visualizationMode.m_cameraHandles != null)
            // {
            //     foreach (var (key, handle) in visualizationMode.m_cameraHandles)
            //     {
            //         string meta = "";
            //         try { meta += handle?.ToString(); } catch { }
            //         try { var kField = handle?.GetType().GetField("m_key", BindingFlags.NonPublic|BindingFlags.Instance); meta += $"; key={kField?.GetValue(handle)}"; } catch { }
            //         try { var pField = handle?.GetType().GetField("m_path", BindingFlags.NonPublic|BindingFlags.Instance); meta += $"; path={pField?.GetValue(handle)}"; } catch { }
            //         CameraStackBootstrap.LOG?.LogInfo($"handle[{key}] meta: {meta}");
            //     }
            // }
            
        }
        // on camera
        // FM.Match.Camera.CameraMode t;
        // FM.Match.Camera.CameraDirectorComponent p;
        // p.m_cameraMode.

        // FM.Match.Camera.MatchVisualizationMode s;
        // s.m_cameraHandles
    }

    private void TrySetup()
    {
        if (_referee == null)
        {
            _referee = ResolveRefereeTransform();
            if (_referee == null)
            {
                CameraStackBootstrap.LOG?.LogDebug("CameraStack: Referee not found yet...");
                return;
            }
            CameraStackBootstrap.LOG?.LogInfo("CameraStack: Referee found.");
        }

        if (_anchor == null)
        {
            var anchorGo = new GameObject("CameraStack_RefereeAnchor");
            _anchor = anchorGo.transform;
            _anchor.SetParent(_referee, worldPositionStays: false);
            _anchor.localPosition = new Vector3(0f, anchorHeight, 0f);
            _anchor.localRotation = Quaternion.identity;
        }

        if (_overlayCam == null)
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
            if (copyMainCameraSettings && _main != null)
            {
                try
                {
                    _overlayCam.fieldOfView = _main.fieldOfView;
                    _overlayCam.nearClipPlane = _main.nearClipPlane;
                    _overlayCam.farClipPlane = _main.farClipPlane;
                    _overlayCam.allowHDR = _main.allowHDR;
                    _overlayCam.allowMSAA = _main.allowMSAA;
                    _overlayCam.clearFlags = _main.clearFlags;
                    _overlayCam.backgroundColor = _main.backgroundColor;
                    _overlayCam.cullingMask = _main.cullingMask;
                }
                catch
                {
                    // In IL2CPP some properties may not be accessible; ignore per-field errors
                }
            }

            _overlayCam.enabled = true;
            CameraStackBootstrap.LOG?.LogInfo("CameraStack: Overlay camera created (bottom-right).");
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
            // return refereeVisuals.gameObject.fin;
        }
        catch (Exception ex)
        {
            CameraStackBootstrap.LOG?.LogWarning($"CameraStack: ResolveRefereeTransform failed: {ex.Message}");
            return null;
        }
    }

    private void OnDestroy()
    {
        try { if (_overlayCam != null) Destroy(_overlayCam.gameObject); } catch { }
        try { if (_anchor != null) Destroy(_anchor.gameObject); } catch { }
        _overlayCam = null;
        _anchor = null;
        _referee = null;
    }
}