using System;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using UnityEngine.SceneManagement;
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

        ClassInjector.RegisterTypeInIl2Cpp<CameraStackController>();

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
                _sceneBoundObject = new GameObject("CameraStackController (scene)");
                var component = _sceneBoundObject.AddComponent<CameraStackController>();
                LOG.LogInfo("Spawned CameraStackController for MatchPlayback scene.");
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