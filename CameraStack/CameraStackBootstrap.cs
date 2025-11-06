using System;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using FM.Match.Camera;
using Il2CppInterop.Runtime.Injection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;


namespace CameraStack;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public partial class CameraStackBootstrap : BasePlugin
{
    public new static ManualLogSource LOG;
    private GameObject _sceneBoundObject;
    private Action<Scene, LoadSceneMode> _sceneLoadedDelegate;

    public override void Load()
    {
        LOG = Log;
        LOG.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");

        // // Apply Harmony patches (once)
        // try
        // {
        //     var harmonyId = MyPluginInfo.PLUGIN_GUID + ".patches";
        //     var harmony = new Harmony(harmonyId);
        //
        //     // Dump signatures of ActivateCamera to know exact params/overloads
        //     ActivateCamera_Introspection.DumpActivateCameraSignatures(LOG);
        //
        //     // Apply Harmony patches for all ActivateCamera overloads
        //     harmony.PatchAll(typeof(MatchVisualizationMode_ActivateCamera_Patch));
        //     
        //     CameraStackBootstrap.LOG?.LogInfo($"CameraStack: Harmony patches applied (id='{harmonyId}').");
        // }
        // catch (Exception ex)
        // {
        //     CameraStackBootstrap.LOG?.LogError($"CameraStack: Failed to apply Harmony patches: {ex}");
        // }

        ClassInjector.RegisterTypeInIl2Cpp<CameraStackController>();
        // ClassInjector.RegisterTypeInIl2Cpp<SimulationWidgetOverlay>();
        
        _sceneLoadedDelegate = (Action<Scene, LoadSceneMode>)OnSceneLoaded;
        SceneManager.sceneLoaded += _sceneLoadedDelegate;
        var active = SceneManager.GetActiveScene();
        OnSceneLoaded(active, LoadSceneMode.Single);
    }
    
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        try
        {
            if (scene.name == "MatchPlayback")
            {
                if (_sceneBoundObject != null) return;
                _sceneBoundObject = new GameObject("CameraStackControllers (scene)");
                var camStack = _sceneBoundObject.AddComponent<CameraStackController>();
                // var overlay = _sceneBoundObject.AddComponent<SimulationWidgetOverlay>();
                LOG.LogInfo("Spawned CameraStackController + ForceOverviewScreenController for MatchPlayback scene.");
            }
            else
            {
                if (_sceneBoundObject == null) return;
                Object.Destroy(_sceneBoundObject);
                _sceneBoundObject = null;
            }
        }
        catch (Exception ex)
        {
            LOG.LogError($"Error handling scene load for CameraStack: {ex}");
        }
    }
}