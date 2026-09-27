using HarmonyLib;
using UnityEngine;

namespace Skamtebord;

[HarmonyPatch]
internal static class SkatePatches
{
    [HarmonyPatch(typeof(Character), "UpdateWalking"), HarmonyPrefix]
    private static bool Walking(Character __instance, float dt) => !(__instance == Player.m_localPlayer
        && __instance.TryGetComponent<BoardRider>(out var rider) && rider.Step(dt));

    [HarmonyPatch(typeof(Character), "UpdateBodyFriction"), HarmonyPostfix]
    private static void Friction(Character __instance)
    {
        if (__instance == Player.m_localPlayer && __instance.TryGetComponent<BoardRider>(out var rider)) rider.ApplyFriction();
    }

    [HarmonyPatch(typeof(Player), nameof(Player.SetControls)), HarmonyPrefix]
    private static void Controls(Player __instance, ref Vector3 movedir, ref bool attack, ref bool attackHold,
        ref bool secondaryAttack, ref bool secondaryAttackHold, ref bool block, ref bool blockHold,
        ref bool jump, ref bool crouch, ref bool run, ref bool autoRun, ref bool dodge)
    {
        if (__instance != Player.m_localPlayer || !__instance.TryGetComponent<BoardRider>(out var rider) || !rider.Riding) return;
        rider.CaptureSprint(run);
        rider.CaptureControls(movedir, jump);
        movedir = Vector3.zero;
        attack = attackHold = secondaryAttack = secondaryAttackHold = block = blockHold = jump = crouch = run = autoRun = dodge = false;
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UseItem)), HarmonyPrefix]
    private static bool UseBoard(Humanoid __instance, ItemDrop.ItemData item)
    {
        if (__instance != Player.m_localPlayer) return true;
        if (!BoardItem.IsBoard(item))
        {
            __instance.GetComponent<BoardRider>()?.Dismount(false);
            return true;
        }
        var rider = __instance.GetComponent<BoardRider>() ?? __instance.gameObject.AddComponent<BoardRider>();
        rider.Toggle();
        return false;
    }

    [HarmonyPatch(typeof(Player), "UpdateKnownRecipesList"), HarmonyPostfix]
    private static void RecipeDiscovery(Player __instance) => BoardItem.OfferRecipe(__instance);

}
