using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using Object = UnityEngine.Object;


namespace ManagerCameraMod;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public partial class ManagerCameraMod : BasePlugin
{
    public new static ManualLogSource _log;
    

    public override void Load()
    {
        _log = Log;
        _log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");

        var go = new GameObject("ManagerCameraController");
        Object.DontDestroyOnLoad(go);
        
        ClassInjector.RegisterTypeInIl2Cpp<ManagerCameraController>();
        var component = go.AddComponent<ManagerCameraController>();
        component.Init(_log);
    }
}