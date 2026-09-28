using HarmonyLib;
using UnityEngine;

namespace Skamtebord;

[HarmonyPatch]
internal static class SkatePatches
{
    private static readonly AccessTools.FieldRef<ZDO,Vector3> NetworkRotation = AccessTools.FieldRefAccess<ZDO,Vector3>("m_rotation");

    // Valheim 1.0.16 omits identity rotations from ZDO packets, but its decoder
    // leaves the previous rotation in place when that flag is absent. That is
    // visible after a north-facing wall ride. Correct only tagged rider ZDOs,
    // without changing their data revision or the incoming packet position.
    [HarmonyPatch(typeof(ZDO), nameof(ZDO.Deserialize)), HarmonyPrefix]
    private static void ReadRotationFlag(ZPackage pkg, out bool __state)
    {
        int position = pkg.GetPos();
        __state = (pkg.ReadUShort() & 0x1000) == 0;
        pkg.SetPos(position);
    }

    [HarmonyPatch(typeof(ZDO), nameof(ZDO.Deserialize)), HarmonyPostfix]
    private static void RestoreOmittedIdentity(ZDO __instance, bool __state)
    {
        if (__state && __instance.GetBool(BoardRider.SurfaceRotationKey)) NetworkRotation(__instance) = Vector3.zero;
    }

    [HarmonyPatch(typeof(Character), "UpdateWalking"), HarmonyPrefix]
    private static bool Walking(Character __instance, float dt) => !(__instance == Player.m_localPlayer
        && __instance.TryGetComponent<BoardRider>(out var rider) && rider.Step(dt));

    [HarmonyPatch(typeof(Character), "UpdateGroundContact"), HarmonyPrefix]
    private static void SkatingGround(Character __instance, float dt)
    {
        if (__instance == Player.m_localPlayer && __instance.TryGetComponent<BoardRider>(out var rider)) rider.PrepareGroundContact(dt);
    }

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
        rider.CaptureJumpHeld(ZInput.GetButton("Jump") || ZInput.GetButton("JoyJump"));
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
