using System;
using System.Linq;
using FM.Match;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FasterMatchStartup;

public class FasterMatchStartup : MonoBehaviour
{
    public FasterMatchStartup(IntPtr ptr) : base(ptr) { }
    
    private bool _pendingAutoSetup;
    private bool _didAutoSetupForThisScene;
    private float _autoSetupStartTime;
    
    void Start()
    {
        _didAutoSetupForThisScene = false;
        if (FasterMatchStartupBootstrap.AutoSetupOnSceneLoad != null && FasterMatchStartupBootstrap.AutoSetupOnSceneLoad.Value)
        {
            _pendingAutoSetup = true;
            _autoSetupStartTime = Time.realtimeSinceStartup;
            FasterMatchStartupBootstrap.LOG.LogInfo("FasterMatchStartup active in MatchPlayback: waiting until ready to auto-setup...");
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
            return; // keep waiting
        }

        string targetStateName = FasterMatchStartupBootstrap.AutoSetupTargetState?.Value ?? "Kick Off";
        var state = controller.m_stateMachine.States?.FirstOrDefault(p => p != null && p.m_name == targetStateName);
        if (state == null)
        {
            return; // keep waiting
        }

        // Ready
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
}