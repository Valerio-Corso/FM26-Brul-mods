using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;


namespace ManagerCameraMod;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public partial class ManagerCameraBootstrap : BasePlugin
{
    public new static ManualLogSource LOG;

    // Config
    public static ConfigEntry<string> ActivationKey;
    public static ConfigEntry<bool> EnableZoom;
    public static ConfigEntry<float> MouseSensitivity;
    public static ConfigEntry<float> ZoomSpeed;
    public static ConfigEntry<float> MinFov;
    public static ConfigEntry<float> MaxFov;
    public static ConfigEntry<bool> EnableManagerMovement;
    public static ConfigEntry<bool> CameraRelativeMovement;
    public static ConfigEntry<float> MoveSpeed;
    public static ConfigEntry<bool> DisableManagerAnimator;
    public static ConfigEntry<bool> LockCursorWhilePanning;
    public static ConfigEntry<bool> CopySettingsFromMain;

    private GameObject _sceneBoundObject;
    private Action<Scene, LoadSceneMode> _sceneLoadedDelegate;

    public override void Load()
    {
        LOG = Log;
        ActivationKey = Config.Bind("General", "ActivationKey", "F3", "Keyboard key to toggle the custom Manager camera. Uses Unity InputSystem Key enum names.");
        EnableZoom = Config.Bind("Camera", "EnableZoom", true, "Enable zoom control with mouse wheel.");
        MouseSensitivity = Config.Bind("Camera", "MouseSensitivity", 0.5f, "Yaw sensitivity while panning with RMB.");
        ZoomSpeed = Config.Bind("Camera", "ZoomSpeed", 5f, "Field of view change speed when scrolling.");
        MinFov = Config.Bind("Camera", "MinFov", 60f, "Minimum camera field of view.");
        MaxFov = Config.Bind("Camera", "MaxFov", 110f, "Maximum camera field of view.");
        EnableManagerMovement = Config.Bind("Movement", "EnableManagerMovement", true, "Allow moving the Manager with WASD while camera is active.");
        CameraRelativeMovement = Config.Bind("Movement", "CameraRelativeMovement", true, "Move relative to camera forward/right when enabled.");
        MoveSpeed = Config.Bind("Movement", "MoveSpeed", 4f, "Manager movement speed in units/second.");
        DisableManagerAnimator = Config.Bind("Advanced", "DisableManagerAnimator", true, "Temporarily disable interfering manager animator while camera active.");
        LockCursorWhilePanning = Config.Bind("Advanced", "LockCursorWhilePanning", true, "Lock and hide cursor when holding RMB to pan.");
        CopySettingsFromMain = Config.Bind("Advanced", "CopySettingsFromMain", true, "Copy FOV and other settings from the scene's main camera when enabling.");

        LOG.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded! ActivationKey={ActivationKey.Value}");

        ClassInjector.RegisterTypeInIl2Cpp<ManagerCameraController>();

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
                _sceneBoundObject = new GameObject("ManagerCameraController (scene)");
                var component = _sceneBoundObject.AddComponent<ManagerCameraController>();
                component.Init(LOG);
                LOG.LogInfo("Spawned ManagerCameraController for MatchPlayback scene.");
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
            LOG.LogError($"Error handling scene load for ManagerCameraMod: {ex}");
        }
    }
}