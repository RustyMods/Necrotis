using System.Collections.Generic;
using HarmonyLib;

namespace Necrotis.Behaviors;

public static class PatchDoors
{
    private static readonly List<string> m_necroGates = new();
    public static void RegisterDoorToPatch(string name) => m_necroGates.Add(name);
    
    [HarmonyPatch(typeof(Door), nameof(Door.CanInteract))]
    private static class Door_CanInteract_Patch
    {
        private static void Postfix(Door __instance, ref bool __result)
        {
            if (!m_necroGates.Contains(__instance.name.Replace("(Clone)", string.Empty))) return;
            __result = __instance.m_animator.GetCurrentAnimatorStateInfo(0).IsTag("open") || __instance.m_animator.GetCurrentAnimatorStateInfo(0).IsTag("closed");
        }
    }
}