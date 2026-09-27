using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace Skamtebord.RuntimeSmoke;

/// <summary>Developer-only isolated-process harness. Never include in release packages.</summary>
[BepInPlugin("com.skamtebord.runtime-smoke", "Skamtebord runtime smoke", "0.1.0")]
[BepInDependency("com.skamtebord.valheim")]
public sealed class RuntimeSmoke : BaseUnityPlugin
{
    private static RuntimeSmoke instance;
    private static string saveRoot;
    private static string testName;
    private Harmony harmony;
    private float deadline;
    private bool finished;
    private int passes;
    private bool testingControls;
    private bool interactive, ready;
    private readonly List<string> checks = new List<string>();
    private readonly List<string> timings = new List<string> { "phase,seconds_since_engine_start" };

    private void Awake()
    {
        if (!Environment.GetCommandLineArgs().Contains("-skamtebord-smoke"))
        {
            Logger.LogWarning("Runtime smoke disabled; explicit -skamtebord-smoke argument required.");
            enabled = false;
            return;
        }
        instance = this;
        Application.runInBackground = true;
        interactive = Environment.GetCommandLineArgs().Contains("-skamtebord-qa");
        deadline = Time.realtimeSinceStartup + 420f;
        testName = "skamtebord_smoke_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
        saveRoot = Path.GetFullPath(Path.Combine(Paths.GameRootPath, "smoke-saves", testName));
        Directory.CreateDirectory(saveRoot);
        Utils.SetSaveDataPath(saveRoot);
        SaveSystem.SetSessionFlags(SaveSystemSessionFlags.DontSaveAnything);
        harmony = new Harmony("com.skamtebord.runtime-smoke");
        try
        {
            // The test world/profile exist only in memory. Keep menu enumeration and cloud
            // migration away from user data even if Steam cloud is enabled globally.
            Patch(typeof(Utils), "SetSaveDataPath", nameof(ForceSaveRoot));
            Patch(typeof(Utils), "ResetSaveDataPath", nameof(Skip));
            Patch(typeof(FileHelpers), "get_CloudStorageSupported", nameof(ReturnFalse));
            Patch(typeof(FileHelpers), "get_CloudStorageSupportedAndEnabled", nameof(ReturnFalse));
            Patch(typeof(SaveSystem), "GetWorldList", nameof(EmptyWorlds));
            Patch(typeof(SaveSystem), "GetAllPlayerProfiles", nameof(EmptyProfiles));
            Patch(typeof(Game), "Start", nameof(PrepareFreshProfile));
            Patch(typeof(Game), "SetActivityCampaignProgress", nameof(Skip));
            QaBootstrap.Install(harmony, message => Log("BOOTSTRAP", message));
            // Avoid platform achievement/stat side effects from an automated fresh spawn.
            foreach (MethodInfo method in typeof(Achievements).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                if (method.Name == "AchievementEvent" || method.Name == "AchievementStatIncrementEvent" || method.Name == "AchievementStatSetEvent")
                    harmony.Patch(method, prefix: new HarmonyMethod(typeof(RuntimeSmoke), nameof(Skip)));
            Log("ISOLATION", "All game saves disabled; local test root=" + saveRoot + "; cloud storage and achievement writes disabled for this process.");
            StartCoroutine(GuardedRun());
        }
        catch (Exception error)
        {
            Finish(false, "Isolation/bootstrap failed: " + error);
        }
    }

    private void Patch(Type type, string method, string prefix)
    {
        MethodInfo target = AccessTools.Method(type, method) ?? throw new MissingMethodException(type.FullName, method);
        harmony.Patch(target, prefix: new HarmonyMethod(typeof(RuntimeSmoke), prefix));
    }

    private static void ForceSaveRoot(ref string path) => path = saveRoot;
    private static bool Skip() => false;
    private static bool ReturnFalse(ref bool __result) { __result = false; return false; }
    private static bool EmptyWorlds(ref List<World> __result) { __result = new List<World>(); return false; }
    private static bool EmptyProfiles(ref List<PlayerProfile> __result) { __result = new List<PlayerProfile>(); return false; }
    private static void PrepareFreshProfile(Game __instance)
    {
        PlayerProfile profile = __instance.GetPlayerProfile();
        if (profile == null || profile.m_filename != testName || profile.m_fileSource != FileHelpers.FileSource.Local)
            throw new InvalidOperationException("Refusing to operate on a non-test character profile.");
        profile.SetName("Skamtebord Smoke Test");
        profile.m_firstSpawn = false;
    }

    private void Update()
    {
        if (!finished && instance == this && !(interactive && ready) && Time.realtimeSinceStartup >= deadline)
            Finish(false, "420-second harness deadline reached; completed checks=" + passes);
    }

    private IEnumerator GuardedRun()
    {
        IEnumerator run = Run();
        while (!finished)
        {
            object current;
            try
            {
                if (!run.MoveNext()) yield break;
                current = run.Current;
            }
            catch (Exception error)
            {
                Finish(false, "Unhandled harness/runtime failure: " + error);
                yield break;
            }
            yield return current;
        }
    }

    private IEnumerator Run()
    {
        Log("WAIT", "Waiting for the startup scene and mod registration.");
        while (!FejdStartup.instance || !QaBootstrap.MenuReady) yield return null;
        Mark("menu_ready");
        Check(SaveSystem.HasSessionFlag(SaveSystemSessionFlags.DontSaveAnything), "save suppression active");
        Check(Utils.GetSaveDataPath(FileHelpers.FileSource.Local) == saveRoot, "save path isolated");
        var world = new World(testName, "SkamtebordSmoke1") { m_fileSource = FileHelpers.FileSource.Local };
        Game.SetProfile(testName, FileHelpers.FileSource.Local);
        ZNet.m_onlineBackend = OnlineBackendType.Steamworks;
        ZNet.SetServer(true, false, false, testName, "", world);
        ZNet.ResetServerHost();
        Log("START", "Loading a new in-memory solo world, no listening/public server and no character file.");
        AccessTools.Method(typeof(FejdStartup), "LoadMainScene").Invoke(FejdStartup.instance, null);

        bool catalogChecked = false;
        while (!Player.m_localPlayer)
        {
            if (!catalogChecked && Game.instance && ObjectDB.instance && ObjectDB.instance.GetItemPrefab("Skamtebord_Board"))
            {
                CheckCatalog();
                catalogChecked = true;
            }
            yield return null;
        }
        Mark("player_spawned");
        Player player = Player.m_localPlayer;
        if (!catalogChecked) CheckCatalog();
        Check(Game.instance.GetPlayerProfile().m_filename == testName, "fresh test character selected");
        var view = player.GetComponent<ZNetView>();
        Check(view && view.IsValid() && view.IsOwner(), "local player owns networked physics body");
        // In -nographics, a renderer can remain culled forever. Let the real spawn/idle
        // animator progress, as it would for a visible local player in a normal client.
        foreach (Animator animator in player.GetComponentsInChildren<Animator>(true))
        {
            Log("SPAWN_ANIMATOR", $"Before fixture adjustment: culling={animator.cullingMode} enabled={animator.enabled} speed={animator.speed} "
                + $"state={animator.GetCurrentAnimatorStateInfo(0).fullPathHash} tag={animator.GetCurrentAnimatorStateInfo(0).tagHash} CanMove={player.CanMove()} intro={player.InIntro()} cutscene={player.InCutscene()}");
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }
        if (Game.instance.InIntro(true)) Game.instance.SkipIntro();
        while (player.InIntro() || player.InCutscene()) yield return null;
        Check(!Game.instance.InIntro(true) && !player.InIntro() && !CinematicsManager.IsPlaying() && !Valkyrie.m_instance,
            "QA starts without cinematic, text intro, or Valkyrie");
        player.SetGodMode(true);
        player.SetGhostMode(true);
        AccessTools.Method(typeof(Player), "SetCrouch").Invoke(player, new object[] { false });
        player.m_autoRun = false;
        bool keyboardTest = Environment.GetCommandLineArgs().Contains("-skamtebord-keyboard-test");
        var controller = player.GetComponent<PlayerController>();
        if (controller && !keyboardTest && !interactive) controller.enabled = false;

        Type riderType = AccessTools.TypeByName("Skamtebord.BoardRider") ?? throw new TypeLoadException("Skamtebord.BoardRider");
        Component rider = player.GetComponent(riderType);
        while (!rider) { yield return null; rider = player.GetComponent(riderType); }
        while (!(bool)AccessTools.Field(riderType, "loaded").GetValue(rider)) yield return null;
        // The input-focus guard is not a physics dependency. Bypass it only in this harness;
        // controls still pass through the real rider adapter and Character.UpdateWalking patch.
        if (!keyboardTest && !interactive)
        {
            harmony.Patch(AccessTools.Method(riderType, "InputAllowed"), prefix: new HarmonyMethod(typeof(RuntimeSmoke), nameof(AllowTestControls)));
            testingControls = true;
        }
        Check(player.IsRecipeKnown("Skamtebord"), "recipe offered to fresh character");
        Check(player.GetInventory().AddItem(ObjectDB.instance.GetItemPrefab("Skamtebord_Board"), 1), "test inventory receives registered board");
        QaBootstrap.PutBoardInFirstSlot(player);
        Check(player.GetInventory().GetItemAt(0,0)?.m_dropPrefab.name == "Skamtebord_Board", "board is ready in hotbar slot 1");
        long initialPoints = Get<long>(Get<object>(rider, "Progression"), "LifetimePoints");
        Check(Get<int>(Get<object>(rider, "Progression"), "Level") == (QaBootstrap.FreshProgression ? 0 : 25),
            QaBootstrap.FreshProgression ? "fresh progression preset starts at level 0" : "QA preset unlocks every trick at Skamtebord 25");
        if (!QaBootstrap.FreshProgression)
            Check(player.GetSkills().GetSkillLevel(Skills.SkillType.Run) == 25f && player.GetSkills().GetSkillLevel(Skills.SkillType.Jump) == 25f,
                "related Run and Jump skills are ready at level 25");

        Vector3 origin = player.transform.position;
        origin.y = Mathf.Max(250f, origin.y + 60f);
        var platform = QaBootstrap.EnsurePlatform(origin);
        origin = platform.transform.position + Vector3.up;
        Rigidbody body = player.GetComponent<Rigidbody>();
        body.position = origin + Vector3.up;
        player.transform.position = body.position;
        body.rotation = Quaternion.identity;
        player.transform.rotation = Quaternion.identity;
        player.ForceJump(Vector3.zero, false);
        Physics.SyncTransforms();
        float settleDeadline = Time.realtimeSinceStartup + 12f;
        while ((!OnPlatform(player, platform) || !player.CanMove()) && Time.realtimeSinceStartup < settleDeadline) yield return new WaitForFixedUpdate();
        Log("MOUNT_STATE", MountState(player, rider, platform));
        Check(OnPlatform(player, platform), "character settles on real static collider");
        Check(player.CanMove(), "QA character is movable before controls begin");
        if (interactive)
        {
            Invoke(rider, "Toggle");
            Check(Get<bool>(rider, "Riding"), "interactive QA starts mounted with normal controls");
        }
        Mark("skate_ready");
        ready = true;
        File.WriteAllText(Path.Combine(saveRoot, "qa-ready.json"), JsonUtility.ToJson(new ReadyRecord
        {
            processId = System.Diagnostics.Process.GetCurrentProcess().Id, readySeconds = Time.realtimeSinceStartup,
            mode = interactive ? "play" : keyboardTest ? "keyboard" : "physics", saveRoot = saveRoot,
            initialPoints = initialPoints, level = Get<int>(Get<object>(rider, "Progression"), "Level"), fastWorld = QaBootstrap.FastWorld
        }, true));
        File.WriteAllText(Path.Combine(Paths.GameRootPath, "latest-qa-session.txt"), saveRoot);
        if (interactive)
        {
            player.Message(MessageHud.MessageType.Center, "QA ready: slot 1 / B board • " + (QaBootstrap.FreshProgression ? "fresh progression" : "all tricks unlocked") + " • no saves");
            File.WriteAllLines(Path.Combine(saveRoot, "smoke-result.txt"), checks.Concat(new[] { "READY Interactive QA; close the game when finished." }));
            Log("READY", $"Interactive QA ready in {Time.realtimeSinceStartup:F2}s. Saves remain disabled.");
            yield break;
        }
        if (keyboardTest)
        {
            var keyboardRun = KeyboardSmoke.Run(player, rider, platform, saveRoot, Check, message => Log("KEYBOARD", message));
            while (keyboardRun.MoveNext()) yield return keyboardRun.Current;
            Check(SaveSystem.HasSessionFlag(SaveSystemSessionFlags.DontSaveAnything), "keyboard test retains save isolation");
            Finish(true, "Keyboard events exercised normal Valheim input, animation, HUD and physics; evidence in " + saveRoot);
            yield break;
        }
        Invoke(rider, "Toggle");
        Check(Get<bool>(rider, "Riding"), "mount succeeds with inventory board; " + MountState(player, rider, platform));
        if (Environment.GetCommandLineArgs().Contains("-skamtebord-capture"))
        {
            var captureRoot = Path.GetFullPath(Path.Combine(Paths.GameRootPath, "..", "captures", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")));
            // Flatten the nested iterator so exceptions are caught by GuardedRun.
            var demo = CaptureDemo.Run(player, rider, platform, Logger, captureRoot);
            while (demo.MoveNext()) yield return demo.Current;
            Finish(true, "Graphical captures written to " + captureRoot);
            yield break;
        }
        Vector3 pushStart = body.position;
        float initialSpeed = HorizontalSpeed(body);
        float pushEnd = Time.time + 2f;
        while (Time.time < pushEnd)
        {
            Invoke(rider, "CaptureControls", Vector3.forward, false);
            yield return new WaitForFixedUpdate();
        }
        Invoke(rider, "CaptureControls", Vector3.zero, false);
        float pushedSpeed = HorizontalSpeed(body);
        Check(pushedSpeed > initialSpeed + 2f && Vector3.Distance(pushStart, body.position) > 2f,
            $"owner physics accelerates under push: {initialSpeed:F2} -> {pushedSpeed:F2} m/s");

        float initialHeading = body.rotation.eulerAngles.y;
        float steerEnd = Time.time + .45f;
        while (Time.time < steerEnd)
        {
            Invoke(rider, "CaptureControls", Vector3.right, false);
            yield return new WaitForFixedUpdate();
        }
        Invoke(rider, "CaptureControls", Vector3.zero, false);
        Check(Mathf.Abs(Mathf.DeltaAngle(initialHeading, body.rotation.eulerAngles.y)) > 8f, "owner physics steering rotates board heading");
        player.AddStamina(100f);
        Invoke(rider, "CaptureControls", Vector3.zero, true);
        bool seenAir = false;
        float jumpDeadline = Time.time + 5f;
        while (Time.time < jumpDeadline)
        {
            yield return new WaitForFixedUpdate();
            if (!Get<bool>(rider, "Grounded")) seenAir = true;
            if (seenAir && Get<bool>(rider, "Grounded")) break;
        }
        Check(seenAir, "ollie leaves ground under Unity gravity");
        Check(Get<bool>(rider, "Riding") && Get<bool>(rider, "Grounded"), "ollie safely lands and keeps skating");
        float landingSpeed = HorizontalSpeed(body);
        Vector3 landingPosition = body.position;
        float coastStart = Time.time;
        float coastRealStart = Time.realtimeSinceStartup;
        float bankDeadline = coastStart + 3f;
        while (Time.time < bankDeadline) yield return new WaitForFixedUpdate();
        object progression = Get<object>(rider, "Progression");
        long points = Get<long>(progression, "LifetimePoints");
        Check(points - initialPoints == 100, "safe landed combo banks 100 new lifetime XP: " + (points - initialPoints));

        float coastSpeed = HorizontalSpeed(body);
        float coastDistance = Vector3.ProjectOnPlane(body.position - landingPosition, Vector3.up).magnitude;
        Log("COAST_STATE", $"speed={landingSpeed:F3}->{coastSpeed:F3} distance={coastDistance:F3} simulationSeconds={Time.time - coastStart:F3} "
            + $"realSeconds={Time.realtimeSinceStartup - coastRealStart:F3}; " + MountState(player, rider, platform));
        Check(Get<bool>(rider, "Riding") && (bool)Invoke(rider, "CanRide") && coastDistance > .1f,
            $"coasting advances while mounted after landing: speed={landingSpeed:F3}->{coastSpeed:F3} m/s, distance={coastDistance:F3}m");

        // Give braking its own moving baseline. A low residual coast speed cannot prove
        // braking response, and must not turn a correct stop into an impossible assertion.
        player.AddStamina(100f);
        float brakeSetupEnd = Time.time + 2f;
        while (HorizontalSpeed(body) < 6f && Time.time < brakeSetupEnd && Get<bool>(rider, "Riding"))
        {
            Invoke(rider, "CaptureControls", Vector3.forward, false);
            yield return new WaitForFixedUpdate();
        }
        Invoke(rider, "CaptureControls", Vector3.zero, false);
        float beforeBrake = HorizontalSpeed(body);
        Log("BRAKE_BASELINE", $"speed={beforeBrake:F3}; " + MountState(player, rider, platform));
        Check(Get<bool>(rider, "Riding") && beforeBrake >= 3f, $"brake test establishes moving baseline: {beforeBrake:F3} m/s");
        float brakeStart = Time.time;
        float brakeRealStart = Time.realtimeSinceStartup;
        float brakeEnd = brakeStart + 1f;
        while (Time.time < brakeEnd)
        {
            Invoke(rider, "CaptureControls", Vector3.back, false);
            yield return new WaitForFixedUpdate();
        }
        float afterBrake = HorizontalSpeed(body);
        string brakeDetails = $"speed={beforeBrake:F3}->{afterBrake:F3} simulationSeconds={Time.time - brakeStart:F3} "
            + $"realSeconds={Time.realtimeSinceStartup - brakeRealStart:F3}; " + MountState(player, rider, platform);
        Log("BRAKE_RESULT", brakeDetails);
        Check(Get<bool>(rider, "Riding") && afterBrake < beforeBrake - 1f, "braking reduces owner velocity; " + brakeDetails);
        Invoke(rider, "Dismount", false);
        Check(!Get<bool>(rider, "Riding"), "dismount restores walking mode");

        // A separate no-push run proves that movement comes from Unity's downhill gravity.
        platform.transform.rotation = Quaternion.Euler(12f, 0f, 0f);
        Vector3 slopeStart = platform.transform.position + platform.transform.rotation * new Vector3(0, 1f, -15f);
        body.position = slopeStart + Vector3.up;
        player.transform.position = body.position;
        body.rotation = Quaternion.identity;
        player.transform.rotation = Quaternion.identity;
        player.ForceJump(Vector3.zero, false);
        Physics.SyncTransforms();
        settleDeadline = Time.realtimeSinceStartup + 12f;
        while ((!OnPlatform(player, platform) || !player.CanMove()) && Time.realtimeSinceStartup < settleDeadline) yield return new WaitForFixedUpdate();
        Check(OnPlatform(player, platform), "character settles on inclined static collider");
        body.linearVelocity = Vector3.zero;
        Invoke(rider, "Toggle");
        Check(Get<bool>(rider, "Riding"), "board mounts on a 12-degree slope; " + MountState(player, rider, platform));
        Invoke(rider, "CaptureControls", Vector3.zero, false);
        Vector3 downhillStart = body.position;
        float downhillEnd = Time.time + 2.5f;
        while (Time.time < downhillEnd) yield return new WaitForFixedUpdate();
        Check(Get<bool>(rider, "Riding") && HorizontalSpeed(body) > 1f && body.position.z > downhillStart.z + 1f,
            $"gravity alone accelerates downhill: {HorizontalSpeed(body):F2} m/s");
        Invoke(rider, "Dismount", false);
        Check(SaveSystem.HasSessionFlag(SaveSystemSessionFlags.DontSaveAnything) && Utils.GetSaveDataPath(FileHelpers.FileSource.Local) == saveRoot,
            "save isolation remains active after simulation");
        Finish(true, "Real Unity recipe, owner push/steer/ollie/landing/XP/brake/dismount/downhill checks completed.");
    }

    private void CheckCatalog()
    {
        GameObject prefab = ObjectDB.instance.GetItemPrefab("Skamtebord_Board");
        Check(prefab && prefab.GetComponent<ItemDrop>(), "board prefab exists in live ObjectDB");
        Recipe recipe = ObjectDB.instance.m_recipes.FirstOrDefault(r => r && r.m_item && r.m_item.gameObject.name == "Skamtebord_Board");
        Check(recipe, "live crafting recipe registered");
        Check(recipe.m_craftingStation == null && recipe.m_resources.Length == 3, "recipe is hand craftable with three ingredients");
        var requirements = recipe.m_resources.ToDictionary(r => r.m_resItem.gameObject.name, r => r.m_amount);
        Check(requirements.TryGetValue("Wood", out int wood) && wood == 8 && requirements.TryGetValue("Resin", out int resin) && resin == 4
            && requirements.TryGetValue("LeatherScraps", out int leather) && leather == 2, "recipe costs match design");
    }

    private static bool AllowTestControls(ref bool __result)
    {
        if (instance == null || !instance.testingControls) return true;
        __result = true;
        return false;
    }

    private static float HorizontalSpeed(Rigidbody body) => Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up).magnitude;
    private static bool OnPlatform(Player player, GameObject platform) => player.IsOnGround()
        && player.GetLastGroundCollider() == platform.GetComponent<Collider>()
        && Mathf.Abs(player.GetComponent<Rigidbody>().linearVelocity.y) < .5f;

    private static string MountState(Player player, object rider, GameObject platform)
    {
        ZNetView view = player.GetComponent<ZNetView>();
        Rigidbody body = player.GetComponent<Rigidbody>();
        Animator animator = player.GetComponentInChildren<Animator>();
        string inventory = string.Join(",", player.GetInventory().GetAllItems().Select(i =>
            (i.m_dropPrefab ? i.m_dropPrefab.name : "NULL_PREFAB") + ":" + i.m_shared.m_name));
        return $"Status=[{Get<string>(rider, "Status")}] Riding={Get<bool>(rider, "Riding")} CanRide={Invoke(rider, "CanRide")} HasBoard={Invoke(rider, "HasBoard")} "
            + $"IsLocal={Get<bool>(rider, "IsLocal")} isLocalPlayer={player == Player.m_localPlayer} viewValid={(view && view.IsValid())} owner={(view && view.IsValid() && view.IsOwner())} "
            + $"onGround={player.IsOnGround()} onFixture={OnPlatform(player, platform)} collider={(player.GetLastGroundCollider() ? player.GetLastGroundCollider().name : "null")} "
            + $"dead={player.IsDead()} teleport={player.IsTeleporting()} swimming={player.IsSwimming()} attached={player.IsAttached()} intro={player.InIntro()} cutscene={player.InCutscene()} "
            + $"dodge={player.InDodge()} attack={player.InAttack()} stagger={player.IsStaggering()} knockback={player.IsKnockedBack()} encumbered={player.IsEncumbered()} "
            + $"placeMode={player.InPlaceMode()} debugFly={player.IsDebugFlying()} canMove={player.CanMove()} emote={player.InEmote()} "
            + $"position={body.position} velocity={body.linearVelocity} damping={body.linearDamping:F3} gravity={Physics.gravity} useGravity={body.useGravity} "
            + $"kinematic={body.isKinematic} sleeping={body.IsSleeping()} layer={LayerMask.LayerToName(player.gameObject.layer)} "
            + $"animator={(animator ? $"path={HierarchyPath(animator.transform)},sameAsPlayerRoot={animator.transform == player.transform},enabled={animator.enabled},cull={animator.cullingMode},speed={animator.speed},state={animator.GetCurrentAnimatorStateInfo(0).fullPathHash},tag={animator.GetCurrentAnimatorStateInfo(0).tagHash}" : "null")} "
            + $"inventory=[{inventory}]";
    }
    private static string HierarchyPath(Transform transform)
    {
        string path = transform.name;
        while (transform.parent) { transform = transform.parent; path = transform.name + "/" + path; }
        return path;
    }
    private static object Invoke(object target, string name, params object[] args) => AccessTools.Method(target.GetType(), name).Invoke(target, args);
    private static T Get<T>(object target, string name) => (T)AccessTools.Property(target.GetType(), name).GetValue(target, null);

    private void Check(bool success, string description)
    {
        if (!success) throw new InvalidOperationException("CHECK FAILED: " + description);
        passes++;
        checks.Add("PASS " + description);
        Log("PASS", description);
    }

    private void Log(string tag, string message) => Logger.LogInfo("SKAMTEBORD_SMOKE " + tag + " " + message);

    private void Mark(string phase)
    {
        timings.Add(phase + "," + Time.realtimeSinceStartup.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
        File.WriteAllLines(Path.Combine(saveRoot, "timings.csv"), timings);
        Log("TIMING", phase + "=" + Time.realtimeSinceStartup.ToString("F2") + "s");
    }

    [Serializable]
    private sealed class ReadyRecord
    {
        public int processId, level;
        public float readySeconds;
        public long initialPoints;
        public string mode, saveRoot;
        public bool fastWorld;
    }

    private void Finish(bool success, string message)
    {
        if (finished) return;
        finished = true;
        Mark(success ? "completed" : "failed");
        Log(success ? "SUCCESS" : "FAIL", message);
        try
        {
            if (saveRoot != null)
                File.WriteAllLines(Path.Combine(saveRoot, "smoke-result.txt"), checks.Concat(new[] { (success ? "SUCCESS " : "FAIL ") + message }));
        }
        catch (Exception error) { Logger.LogError("Could not write smoke report: " + error); }
        // Do not remove save/achievement suppression while the game executes shutdown hooks.
        Application.Quit(success ? 0 : 1);
    }
}
