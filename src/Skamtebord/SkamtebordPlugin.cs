using System.IO;
using BepInEx;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Managers;
using Jotunn.Utils;
using Skamtebord.Radio;
using UnityEngine;

namespace Skamtebord;

[BepInPlugin(Guid, "Skamtebord", ModVersion)]
[BepInDependency(Jotunn.Main.ModGuid, "2.30.1")]
[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
public sealed class SkamtebordPlugin : BaseUnityPlugin
{
    public const string Guid = "com.skamtebord.valheim";
    public const string ModVersion = "0.3.1";
    internal static SkamtebordPlugin Instance;
    internal SkateSettings Settings;
    internal SkateRadio Radio;
    internal Skills.SkillType SkateSkill;
    private Harmony harmony;
    private float scanTimer;

    private void Awake()
    {
        Instance = this;
        Settings = new SkateSettings(Config);
        SkateSkill = SkillManager.Instance.AddSkill(new SkillConfig
        {
            Identifier = Guid + ".skill",
            Name = "Skamtebord",
            Description = "Land skate tricks to bank Skamtebord XP and unlock more tricks. Progress is retained on death.",
            Icon = BoardModel.CreateIcon(),
            IncreaseStep = 1f
        });
        PrefabManager.OnVanillaPrefabsAvailable += BoardItem.Register;
        harmony = new Harmony(Guid);
        harmony.PatchAll();
        if (!Application.isBatchMode)
        {
            Radio = gameObject.AddComponent<SkateRadio>();
            Radio.Initialize(Path.GetDirectoryName(Info.Location), Config, Logger);
            gameObject.AddComponent<SkateHud>();
        }
        Logger.LogInfo($"Skamtebord {ModVersion} loaded for Valheim {(global::Version.CurrentVersion)}. Physics and progression ready.");
    }

    private void Update()
    {
        scanTimer -= Time.unscaledDeltaTime;
        if (scanTimer > 0f) return;
        scanTimer = 0.5f;
        foreach (var player in Player.GetAllPlayers())
            if (player && !player.GetComponent<BoardRider>()) player.gameObject.AddComponent<BoardRider>();
    }

    private void OnDestroy()
    {
        PrefabManager.OnVanillaPrefabsAvailable -= BoardItem.Register;
        if (Radio)
        {
            Radio.enabled = false;
            Destroy(Radio);
        }
        foreach (var player in Player.GetAllPlayers())
            if (player && player.TryGetComponent<BoardRider>(out var rider)) Destroy(rider);
        harmony?.UnpatchSelf();
        Instance = null;
    }

    internal void Log(string message) => Logger.LogInfo(message);
}
