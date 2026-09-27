using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Skamtebord.RuntimeSmoke;

// Disposable QA process only. Never shipped with Skamtebord or installed in a play profile.
internal static class QaBootstrap
{
    internal static bool MenuReady;
    internal static bool FastWorld;
    internal static bool FreshProgression;
    internal static GameObject Platform;
    internal static readonly Vector3 Spawn = new Vector3(0, 250, 0);
    private static Action<string> log;
    private const long UnlockedPoints = 67500; // Level 25: all six currently defined tricks.

    internal static void Install(Harmony harmony, Action<string> logger)
    {
        log = logger;
        FastWorld = !Environment.GetCommandLineArgs().Contains("-skamtebord-full-world");
        FreshProgression = Environment.GetCommandLineArgs().Contains("-skamtebord-fresh-progression");
        harmony.Patch(AccessTools.Method(typeof(FejdStartup), "Start"), postfix: Patch(nameof(MenuStarted)));
        harmony.Patch(AccessTools.Method(typeof(FejdStartup), "TryPlayIntroCinematic"), prefix: Patch(nameof(NoStartupCinematic)));
        harmony.Patch(AccessTools.Method(typeof(Player), "OnSpawned"), prefix: Patch(nameof(NoValkyrie)));
        harmony.Patch(AccessTools.Method(typeof(PlayerProfile), "LoadPlayerData"), postfix: Patch(nameof(SeedProgression)));
        if (FastWorld)
        {
            harmony.Patch(AccessTools.Method(typeof(ZoneSystem), "GenerateLocations"), prefix: Patch(nameof(FixtureWorld)));
            harmony.Patch(AccessTools.Method(typeof(Game), "FindSpawnPoint"), prefix: Patch(nameof(FindFixtureSpawn)));
        }
        log("Intro movies and Valkyrie disabled; " + (FastWorld ? "fast fixture world" : "full world generation") + "; "
            + (FreshProgression ? "fresh progression" : "Skamtebord 25 / all tricks, Run 25, Jump 25"));
    }

    private static HarmonyMethod Patch(string name) => new HarmonyMethod(typeof(QaBootstrap), name);
    private static void MenuStarted() { MenuReady = true; }
    private static IEnumerator Empty() { yield break; }
    private static bool NoStartupCinematic(ref IEnumerator __result) { __result = Empty(); return false; }
    private static void NoValkyrie(ref bool spawnValkyrie) { spawnValkyrie = false; }

    private static void SeedProgression(PlayerProfile __instance, Player player)
    {
        if (!__instance.m_filename.StartsWith("skamtebord_smoke_", StringComparison.Ordinal)
            || !SaveSystem.HasSessionFlag(SaveSystemSessionFlags.DontSaveAnything))
            throw new InvalidOperationException("QA setup requires the isolated unsaved test profile.");
        __instance.m_firstSpawn = false;
        player.m_customData["com.skamtebord.valheim.xp.v1"] = (FreshProgression ? 0 : UnlockedPoints).ToString(CultureInfo.InvariantCulture);
        if (!FreshProgression)
        {
            player.GetSkills().CheatRaiseSkill("Run", 25, false);
            player.GetSkills().CheatRaiseSkill("Jump", 25, false);
        }
        player.SetGodMode(true);
        player.SetGhostMode(true);
    }

    private static bool FixtureWorld(ZoneSystem __instance)
    {
        // No location discovery or natural-terrain assertions are made in fixture mode.
        // Set the property (not just its field) so ordinary loading subscribers complete.
        AccessTools.Field(typeof(ZoneSystem), "m_generateLocationsProgress").SetValue(__instance, 1f);
        AccessTools.Field(typeof(ZoneSystem), "m_estimatedGenerateLocationsCompletionTime").SetValue(__instance, DateTime.UtcNow);
        AccessTools.Property(typeof(ZoneSystem), "LocationsGenerated").SetValue(__instance, true);
        log("Skipped world-wide location placement for the isolated QA platform.");
        return false;
    }

    private static bool FindFixtureSpawn(ref Vector3 point, ref bool usedLogoutPoint, ref bool __result)
    {
        point = Spawn + Vector3.up * .1f;
        usedLogoutPoint = true;
        __result = Hud.instance && EnvMan.instance && ZNetScene.instance && ZoneSystem.instance
            && ZoneSystem.instance.LocationsGenerated && ObjectDB.instance && ObjectDB.instance.GetItemPrefab("Skamtebord_Board");
        if (__result)
        {
            EnsurePlatform(Spawn);
            ZNet.instance.SetReferencePosition(point);
        }
        return false;
    }

    internal static GameObject EnsurePlatform(Vector3 top)
    {
        if (Platform) return Platform;
        Platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Platform.name = "RuntimeSmoke temporary flat physics platform";
        Platform.layer = LayerMask.NameToLayer("Default");
        Platform.transform.position = top - Vector3.up;
        Platform.transform.localScale = new Vector3(200, 2, 200);
        Platform.GetComponent<Renderer>().sharedMaterial = FixtureMaterial(new Color(.19f, .27f, .25f));
        Physics.SyncTransforms();
        return Platform;
    }

    internal static Material FixtureMaterial(Color color)
    {
        // Shader.Find can miss Valheim's asset-bundle shaders. A sprite fallback
        // has no depth writes, making the large floor overpaint distant ramps.
        var source = ObjectDB.instance.GetItemPrefab("Wood").GetComponentsInChildren<Renderer>(true)
            .SelectMany(r => r.sharedMaterials).FirstOrDefault(m => m && m.renderQueue < 2500);
        if (!source) throw new InvalidOperationException("No opaque Wood material available for the QA fixture.");
        var previousShader = Shader.Find("Custom/Creature") ?? Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
        Debug.Log($"Skamtebord fixture material: {source.shader.name}, queue={source.renderQueue}; prior lookup={previousShader?.name}, queue={previousShader?.renderQueue}");
        return new Material(source) { color = color };
    }

    internal static void PutBoardInFirstSlot(Player player)
    {
        var inventory = player.GetInventory();
        var board = inventory.GetAllItems().Single(i => i.m_dropPrefab && i.m_dropPrefab.name == "Skamtebord_Board");
        var previous = inventory.GetItemAt(0, 0);
        if (previous != null && previous != board) previous.m_gridPos = board.m_gridPos;
        board.m_gridPos = new Vector2i(0, 0);
        AccessTools.Method(typeof(Inventory), "Changed").Invoke(inventory, new object[] { false, false });
    }
}
