using BepInEx;
using System;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MatchUiOverlays;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class MatchUiOverlayPlugin : BasePlugin
{
    public new static ManualLogSource LOG;
    private GameObject _sceneBoundObject;
    private Action<Scene, LoadSceneMode> _sceneLoadedDelegate;

    public override void Load()
    {
        // Plugin startup logic
        LOG = base.Log;
        LOG.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
        
        // Register IL2CPP behaviours
        ClassInjector.RegisterTypeInIl2Cpp<ForceOverviewScreenControllerByType>();
        ClassInjector.RegisterTypeInIl2Cpp<PanelWarmupTester>();
        
        _sceneLoadedDelegate = (Action<Scene, LoadSceneMode>)OnSceneLoaded;
        SceneManager.sceneLoaded += _sceneLoadedDelegate;
        var active = SceneManager.GetActiveScene();
        OnSceneLoaded(active, LoadSceneMode.Single);
    }
    
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        try
        {
            // main scene GameWorld
            if (scene.name == "MatchPlayback")
            {
                if (_sceneBoundObject != null) return;
                _sceneBoundObject = new GameObject("MatchUiOverlayPlugin (scene)");
                
                // Attach controllers
                _sceneBoundObject.AddComponent<ForceOverviewScreenControllerByType>();
                var warm = _sceneBoundObject.AddComponent<PanelWarmupTester>();
                
                LOG.LogInfo("Spawned Match UI overlay helpers (ForceOverview + WarmupTester) for MatchPlayback scene.");
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
            LOG.LogError($"Error handling scene load for MatchUiOverlayPlugin: {ex}");
        }
    }
}
