using System;
using System.Linq;
using System.Reflection;
using FM.Match;
using UnityEngine;
using Il2CppInterop.Runtime.Injection;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using Object = System.Object;

namespace ManagerCameraMod;

public class MyBehaviour : MonoBehaviour
{
    public MyBehaviour(IntPtr ptr) : base(ptr) { }
    
    private Action<Scene, LoadSceneMode> _sceneLoadedDelegate;
    
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
            if (Keyboard.current != null && Keyboard.current[Key.F1].wasPressedThisFrame)
            {
                var controller = FindObjectOfType<MatchPlaybackController>();
                
                if (controller != null)
                {
                    if (controller.Match != null)
                    {
                        // exception
                        // controller.Match.JumpToKickOff();
                        
                        var state = controller.m_stateMachine.States.FirstOrDefault(p => p.m_name == "Kick Off");
                        controller.m_stateMachine.SetCurrentState(state);
                        
                        // ManagerCameraMod._log.LogInfo("All States:");
                        // ManagerCameraMod._log.LogInfo($"Start: {controller.m_stateMachine.StartState.m_name}");
                        // foreach (var state in controller.m_stateMachine.States)
                        // {
                        //     ManagerCameraMod._log.LogInfo($"full - {state.GetType().FullName}");
                        //     ManagerCameraMod._log.LogInfo($"name - {state.m_name}");
                        //     ManagerCameraMod._log.LogInfo($"guid - {state.m_guid}");
                        // }
                        //
                        // ManagerCameraMod._log.LogInfo($"Current State: {controller.m_stateMachine.CurrentState.GetType().Name}");
                        //
                        
                        
                        // ManagerCameraMod._log.LogInfo($"State: {controller.GetPlayState()}");
                        // var type = controller.GetType();
                        // var fieldInfo = type.GetField("m_matchState", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                        // if (fieldInfo != null)
                        // {
                        //     var value = fieldInfo.GetValue(controller);
                        //     ManagerCameraMod._log.LogInfo($"m_matchState = {value}");
                        // }
                    }
                    else
                    {
                        ManagerCameraMod._log.LogWarning("Match is null!");
                    }
                }
                else
                {
                    ManagerCameraMod._log.LogWarning("MatchPlaybackController not found in scene!");
                }
            }
        }
        catch (Exception ex)
        {
            ManagerCameraMod._log.LogError($"Error with Input in Update: {ex}");
        }
    }
    
    // Event handler method:
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "MatchPlayback")
        {
            
        }
    }
}