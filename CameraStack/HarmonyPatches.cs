using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using FM.Match.Camera;

namespace CameraStack
{
    internal static class ActivateCamera_Introspection
    {
        public static void DumpActivateCameraSignatures(ManualLogSource log)
        {
            try
            {
                var t = typeof(MatchVisualizationMode);
                var methods = AccessTools.GetDeclaredMethods(t)
                    .Where(m => m.Name == "ActivateCamera")
                    .ToArray();

                if (methods.Length == 0)
                {
                    log?.LogWarning("No methods named ActivateCamera found on MatchVisualizationMode.");
                    return;
                }

                for (int i = 0; i < methods.Length; i++)
                {
                    var mi = methods[i];
                    var ps = mi.GetParameters();
                    string paramList = string.Join(
                        ", ",
                        ps.Select(p => $"{PrettyType(p.ParameterType)} {p.Name}{(p.HasDefaultValue ? $" = {p.DefaultValue}" : string.Empty)}"));
                    var returnType = (mi as MethodInfo)?.ReturnType ?? typeof(void);
                    log?.LogInfo($"[{i}] {mi.DeclaringType?.FullName}.{mi.Name}({paramList}) -> {PrettyType(returnType)}");
                }
            }
            catch (Exception ex)
            {
                log?.LogError($"Error dumping ActivateCamera signatures: {ex}");
            }
        }

        private static string PrettyType(Type t)
        {
            if (t == null) return "<null>";
            if (t.IsByRef) return PrettyType(t.GetElementType()) + "&";
            if (t.IsArray) return PrettyType(t.GetElementType()) + "[]";
            if (t.IsGenericType)
            {
                var def = t.GetGenericTypeDefinition();
                var defName = (def.FullName ?? def.Name);
                var tick = defName.IndexOf('`');
                if (tick >= 0) defName = defName.Substring(0, tick);
                var args = t.GetGenericArguments().Select(PrettyType);
                return $"{defName}<{string.Join(", ", args)}>";
            }
            return t.FullName ?? t.Name;
        }
    }

    [HarmonyPatch]
    internal static class MatchVisualizationMode_ActivateCamera_Patch
    {
        // Patch all overloads named ActivateCamera on MatchVisualizationMode
        static IEnumerable<MethodBase> TargetMethods()
        {
            var t = typeof(MatchVisualizationMode);
            return AccessTools.GetDeclaredMethods(t).Where(m => m.Name == "ActivateCamera");
        }

        // Minimal Prefix: log method signature only; avoid accessing runtime args to reduce IL2CPP marshalling
        [HarmonyPriority(Priority.Last)]
        static void Prefix(
            object __instance,
            MethodBase __originalMethod,
            ref bool __runOriginal)
        {
            var log = CameraStackBootstrap.LOG;
            try
            {
                var ps = __originalMethod.GetParameters();
                var signature = string.Join(
                    ", ",
                    ps.Select(p => $"{p.ParameterType.FullName} {p.Name}"));

                log?.LogInfo($"[ActivateCamera] {__originalMethod.DeclaringType?.FullName}.{__originalMethod.Name}({signature})");

                // If you need to block original later for diagnostics:
                // __runOriginal = false;
            }
            catch (Exception ex)
            {
                log?.LogError($"[ActivateCamera] Prefix error: {ex}");
            }
        }
    }
}
