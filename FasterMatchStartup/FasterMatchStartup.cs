using System;
using System.Linq;
using FM.Match;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace FasterMatchStartup;

public class FasterMatchStartup : MonoBehaviour
{
    public FasterMatchStartup(IntPtr ptr) : base(ptr) { }
    
    private Action<Scene, LoadSceneMode> _sceneLoadedDelegate;

    // Auto-setup flags
    private bool _pendingAutoSetup;
    private bool _didAutoSetupForThisScene;
    private float _autoSetupStartTime;
    
    void Start()
    {
        _sceneLoadedDelegate = (Action<Scene, LoadSceneMode>)OnSceneLoaded;
        SceneManager.sceneLoaded += _sceneLoadedDelegate;
    }

    private void OnDestroy()
    {
        if (_sceneLoadedDelegate != null)
        {
            SceneManager.sceneLoaded -= _sceneLoadedDelegate;
            _sceneLoadedDelegate = null;
        }
    }

    private void Update()
    {
        try
        {
            // Auto-setup flow: wait until scene objects are ready, then perform once
            if (_pendingAutoSetup && ! _didAutoSetupForThisScene)
            {
                TryAutoSetup();
            }
            
            // Manual trigger for debugging/forcing the behavior
            if (Keyboard.current != null && Keyboard.current[Key.F1].wasPressedThisFrame)
            {
                TrySetKickoffState(manual:true);
            }
        }
        catch (Exception ex)
        {
            FasterMatchStartupBootstrap.LOG.LogError($"Error with Input in Update: {ex}");
        }
    }
    
    private void TryAutoSetup()
    {
        float elapsed = Time.realtimeSinceStartup - _autoSetupStartTime;
        // Respect extra delay after scene load
        if (elapsed < FasterMatchStartupBootstrap.AutoSetupExtraDelaySeconds.Value)
        {
            return; // wait a bit more before first attempt
        }
        // Timeout protection so we don't poll forever
        if (elapsed > FasterMatchStartupBootstrap.AutoSetupMaxDurationSeconds.Value)
        {
            FasterMatchStartupBootstrap.LOG.LogWarning("Auto-setup timed out waiting for MatchPlayback to be ready.");
            _pendingAutoSetup = false;
            return;
        }

        // Only attempt when controller and match are fully ready
        var controller = FindObjectOfType<MatchPlaybackController>();
        if (controller == null)
        {
            return; // keep waiting
        }

        if (controller.m_stateMachine == null)
        {
            return; // keep waiting
        }

        if (controller.Match == null)
        {
            return; // keep waiting
        }

        // Gate on camera-ready object presence if configured
        var gateName = FasterMatchStartupBootstrap.AutoSetupCameraReadyObjectName?.Value;
        if (!string.IsNullOrEmpty(gateName) && GameObject.Find(gateName) == null)
        {
            return; // wait until the camera dynamic object is present
        }

        // Target state name from config (default "Kick Off")
        string targetStateName = FasterMatchStartupBootstrap.AutoSetupTargetState?.Value ?? "Kick Off";
        // Find the desired state safely
        var state = controller.m_stateMachine.States?.FirstOrDefault(p => p != null && p.m_name == targetStateName);
        if (state == null)
        {
            return; // state list not ready yet; keep waiting
        }

        // Ready — perform the action once
        try
        {
            controller.m_stateMachine.SetCurrentState(state);
            _didAutoSetupForThisScene = true;
            _pendingAutoSetup = false;
            FasterMatchStartupBootstrap.LOG.LogInfo($"Auto-setup complete: switched to '{targetStateName}' state.");
        }
        catch (Exception ex)
        {
            // Something inside the state transition is still not ready; keep waiting until timeout
            FasterMatchStartupBootstrap.LOG.LogWarning($"Auto-setup: transition to '{targetStateName}' failed this frame, will retry. Reason: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void TrySetKickoffState(bool manual)
    {
        string targetStateName = FasterMatchStartupBootstrap.AutoSetupTargetState?.Value ?? "Kick Off";
        var controller = FindObjectOfType<MatchPlaybackController>();

        if (controller != null)
        {
            if (controller.Match != null && controller.m_stateMachine != null)
            {
                // Honor camera gate for manual too, to avoid crashing
                var gateName = FasterMatchStartupBootstrap.AutoSetupCameraReadyObjectName?.Value;
                if (!string.IsNullOrEmpty(gateName) && GameObject.Find(gateName) == null)
                {
                    if (manual)
                        FasterMatchStartupBootstrap.LOG.LogWarning($"Manual trigger: gate object '{gateName}' not present yet.");
                    return;
                }

                var state = controller.m_stateMachine.States?.FirstOrDefault(p => p != null && p.m_name == targetStateName);
                if (state != null)
                {
                    try
                    {
                        controller.m_stateMachine.SetCurrentState(state);
                        if (manual)
                            FasterMatchStartupBootstrap.LOG.LogInfo($"Manual trigger: switched to '{targetStateName}' state.");
                    }
                    catch (Exception ex)
                    {
                        if (manual)
                            FasterMatchStartupBootstrap.LOG.LogWarning($"Manual trigger: transition to '{targetStateName}' failed: {ex.GetType().Name}: {ex.Message}");
                    }
                }
                else if (manual)
                {
                    FasterMatchStartupBootstrap.LOG.LogWarning($"Manual trigger: '{targetStateName}' state not found yet.");
                }
            }
            else if (manual)
            {
                FasterMatchStartupBootstrap.LOG.LogWarning("Manual trigger: Controller or Match not ready yet!");
            }
        }
        else if (manual)
        {
            FasterMatchStartupBootstrap.LOG.LogWarning("Manual trigger: MatchPlaybackController not found in scene!");
        }
    }
    
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "MatchPlayback")
        {
            _didAutoSetupForThisScene = false; // reset for this scene load
            if (FasterMatchStartupBootstrap.AutoSetupOnSceneLoad != null && FasterMatchStartupBootstrap.AutoSetupOnSceneLoad.Value)
            {
                _pendingAutoSetup = true;
                _autoSetupStartTime = Time.realtimeSinceStartup;
                FasterMatchStartupBootstrap.LOG.LogInfo("MatchPlayback scene loaded: waiting until ready to auto-setup...");
            }
        }
        else
        {
            // Leaving MatchPlayback or entering other scenes — clear pending state
            _pendingAutoSetup = false;
            _didAutoSetupForThisScene = false;
        }
    }
}