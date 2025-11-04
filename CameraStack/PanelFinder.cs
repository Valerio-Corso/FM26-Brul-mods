using System;
using BepInEx;
using BepInEx.IL2CPP;
using UnityEngine;
using UnityEngine.UIElements;
using System.Text;
using CameraStack;
using FM.Match.Camera;
using SI.Bindable;
using SI.Bindable.Events;
using UnityEngine.InputSystem;

public class PanelFinder : MonoBehaviour
{
    private Camera _uiCamera;
    
    private System.Action<SI.Bindable.Panel> _panelOpenedDelegate;

    public PanelFinder(IntPtr ptr) : base(ptr) { }

    void Start()
    {
        _uiCamera = Camera.main;
        
        var panelManager = FindObjectOfType<PanelManager>();
        _panelOpenedDelegate = OnPanelOpened;
        panelManager.m_onPanelOpened += _panelOpenedDelegate;
    }

    void Update()
    {
        
        if (Keyboard.current != null)
        {
            var kc = Keyboard.current[Key.F8];
            if (kc != null && kc.wasPressedThisFrame)
            {
                var docs = GameObject.FindObjectsOfType<UIDocument>();
                
                // Make sure mouse is available
                if (Mouse.current == null)
                    return;

                Vector2 mousePos = Mouse.current.position.ReadValue();
                foreach (var doc in docs)
                {
                    if (doc == null || doc.rootVisualElement == null)
                        continue;

                    var panel = doc.rootVisualElement.panel;
                    if (panel == null)
                        continue;

                    // Convert screen coordinates to panel space
                    var panelMouse = RuntimePanelUtils.ScreenToPanel(panel, mousePos);

                    // Try to pick a VisualElement under the cursor
                    var hovered = panel.Pick(panelMouse);
                    if (hovered != null)
                    {
                        CameraStackBootstrap.LOG.LogInfo($"[UIHover] Hovering over: {GetHierarchyPath(hovered)} (Panel: {doc.name})");
                    }
                }
                
            }
            
            var kc9 = Keyboard.current[Key.F9];
            if (kc9 != null && kc9.wasPressedThisFrame)
            {
                var panelManager = FindObjectOfType<PanelManager>();
                
                foreach (var panelId in panelManager.Panels)
                {
                    CameraStackBootstrap.LOG.LogInfo($"[CAMMOD] Panel id: {panelId.name}");
                }

                var matchVisualization = FindObjectOfType<MatchVisualizationMode>();
                CameraStackBootstrap.LOG.LogInfo($"[CAMMOD] current camera instance: {matchVisualization.m_currentCameraPrefabInstanceId}");
            }
        }
    }
    
    private void OnPanelOpened(SI.Bindable.Panel panel)
    {
        CameraStackBootstrap.LOG.LogInfo($"[CAMMOD] Panel opened: {panel.name} id {panel.PanelID.name}");
    }

    private string GetHierarchyPath(VisualElement element)
    {
        StringBuilder sb = new StringBuilder();
        var current = element;
        while (current != null)
        {
            sb.Insert(0, "/" + current.name);
            current = current.parent;
        }
        return sb.ToString();
    }
}