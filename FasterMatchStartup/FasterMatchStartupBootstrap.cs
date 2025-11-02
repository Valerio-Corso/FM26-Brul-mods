using System;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using BepInEx.Configuration;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;


namespace FasterMatchStartup;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public partial class FasterMatchStartupBootstrap : BasePlugin
{
    public new static ManualLogSource LOG;
    public static ConfigEntry<bool> AutoSetupOnSceneLoad;
    public static ConfigEntry<float> AutoSetupExtraDelaySeconds;
    public static ConfigEntry<float> AutoSetupMaxDurationSeconds;
    public static ConfigEntry<string> AutoSetupTargetState;
    public static ConfigEntry<string> AutoSetupCameraReadyObjectName;

    private GameObject _sceneBoundObject;
    private Action<Scene, LoadSceneMode> _sceneLoadedDelegate;

    public override void Load()
    {
        LOG = Log;
        AutoSetupOnSceneLoad = Config.Bind("General", "AutoSetupOnSceneLoad", true, "If enabled, the mod will automatically set up when the MatchPlayback scene is loaded, once it is ready.");
        AutoSetupExtraDelaySeconds = Config.Bind("General", "AutoSetupExtraDelaySeconds", 1.0f, "Extra delay after MatchPlayback scene load before first auto-setup attempt (seconds), only tweak if you experience issues..");
        AutoSetupMaxDurationSeconds = Config.Bind("General", "AutoSetupMaxDurationSeconds", 15.0f, "Safety: Maximum time to keep waiting for readiness before giving up (seconds).");
        AutoSetupTargetState = Config.Bind("General", "AutoSetupTargetState", "Kick Off", "Experimental: Target state name to switch to when ready. Don't change this unless you know what you're doing!");
        AutoSetupCameraReadyObjectName = Config.Bind("General", "AutoSetupCameraReadyObjectName", "CameraDynamicObject", "Experimental: Name of a scene object whose presence indicates readiness (waits until it exists).");
        LOG.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded! AutoSetupOnSceneLoad={AutoSetupOnSceneLoad.Value}, Delay={AutoSetupExtraDelaySeconds.Value}s, Timeout={AutoSetupMaxDurationSeconds.Value}s, TargetState='{AutoSetupTargetState.Value}', Gate='{AutoSetupCameraReadyObjectName.Value}'");

        ClassInjector.RegisterTypeInIl2Cpp<FasterMatchStartup>();

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
                _sceneBoundObject = new GameObject("FasterMatchStartup (scene)");
                _sceneBoundObject.AddComponent<FasterMatchStartup>();
                LOG.LogInfo("Spawned FasterMatchStartup component for MatchPlayback scene.");
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
            LOG.LogError($"Error handling scene load for FasterMatchStartup: {ex}");
        }
    }
}