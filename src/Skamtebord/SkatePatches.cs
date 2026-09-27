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

    // These vanilla actions run in Player.Update, outside SetControls. Reserve them while riding
    // so the same Q/E/R/F/C press cannot both perform a trick and use a door/weapon/guardian power.
    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButtonDown)), HarmonyPostfix, HarmonyPriority(Priority.Last)]
    private static void ReserveButtonDown(string name, ref bool __result) => ReserveButton(name, ref __result);

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButton)), HarmonyPostfix, HarmonyPriority(Priority.Last)]
    private static void ReserveButton(string name, ref bool __result)
    {
        if (!__result || !BoardRider.Local || !BoardRider.Local.Riding) return;
        if (name == "Use" || name == "Hide" || name == "GP" || name == "ToggleWalk" || name == "AutoRun"
            || name == "JoyUse" || name == "JoyHide" || name == "JoyGP") __result = false;
    }
}
