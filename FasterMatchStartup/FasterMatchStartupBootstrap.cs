using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using BepInEx.Configuration;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using Object = UnityEngine.Object;


namespace FasterMatchStartup;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public partial class FasterMatchStartupBootstrap : BasePlugin
{
    public new static ManualLogSource LOG;
    public static ConfigEntry<bool> AutoSetupOnSceneLoad;
    public static ConfigEntry<float> AutoSetupExtraDelaySeconds; // grace period after scene load
    public static ConfigEntry<float> AutoSetupMaxDurationSeconds; // maximum wait time
    public static ConfigEntry<string> AutoSetupTargetState; // state name to switch to
    public static ConfigEntry<string> AutoSetupCameraReadyObjectName; // scene object gating readiness

    public override void Load()
    {
        LOG = Log;
        AutoSetupOnSceneLoad = Config.Bind("General", "AutoSetupOnSceneLoad", true, "If enabled, the mod will automatically set up when the MatchPlayback scene is loaded, once it is ready.");
        AutoSetupExtraDelaySeconds = Config.Bind("General", "AutoSetupExtraDelaySeconds", 1.0f, "Extra delay after MatchPlayback scene load before first auto-setup attempt (seconds).");
        AutoSetupMaxDurationSeconds = Config.Bind("General", "AutoSetupMaxDurationSeconds", 15.0f, "Maximum time to keep waiting for readiness before giving up (seconds).");
        AutoSetupTargetState = Config.Bind("General", "AutoSetupTargetState", "Kick Off", "Target state name to switch to when ready.");
        AutoSetupCameraReadyObjectName = Config.Bind("General", "AutoSetupCameraReadyObjectName", "CameraDynamicObject", "Name of a scene object whose presence indicates readiness (waits until it exists).");
        LOG.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded! AutoSetupOnSceneLoad={AutoSetupOnSceneLoad.Value}, Delay={AutoSetupExtraDelaySeconds.Value}s, Timeout={AutoSetupMaxDurationSeconds.Value}s, TargetState='{AutoSetupTargetState.Value}', Gate='{AutoSetupCameraReadyObjectName.Value}'");
        var go = new GameObject("FasterMatchStartupMod");
        Object.DontDestroyOnLoad(go);
        ClassInjector.RegisterTypeInIl2Cpp<FasterMatchStartup>();
        go.AddComponent<FasterMatchStartup>();
    }
}