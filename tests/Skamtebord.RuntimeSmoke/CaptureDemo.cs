using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Skamtebord.RuntimeSmoke;

/// <summary>
/// Developer-only footage of the actual owner-physics adapter. The calling harness must
/// already have isolated all saves and supplied its temporary character/platform.
/// No part of this class is distributed with the playable mod.
/// </summary>
internal static class CaptureDemo
{
    private const int Width = 960, Height = 540, FramesPerSecond = 30;
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    internal static IEnumerator Run(Player player, Component rider, GameObject platform,
        ManualLogSource logger, string outputRoot)
    {
        Directory.CreateDirectory(outputRoot);
        File.WriteAllText(Path.Combine(outputRoot, "capture-info.txt"),
            "Actual Valheim GPU rendering and Skamtebord owner physics, captured in an isolated temporary test world.\n" +
            "960x540; 30 frames/second. Frames contain no synthesized movement. Camera and marked platform are developer fixtures.\n" +
            "Clip 02 uses a temporary unlocked test character; earned XP is separately recorded as earned_xp.\n" +
            "Sequences: 01-push-carve-brake (10s), 02-tricks-xp (12s), 03-downhill (10s).\n");

        Rigidbody body = player.GetComponent<Rigidbody>();
        Vector3 deckCenter = platform.transform.position;
        platform.transform.rotation = Quaternion.identity;
        platform.transform.localScale = new Vector3(160, 2, 300);
        platform.GetComponent<Renderer>().sharedMaterial = Material("deck", new Color(.19f, .26f, .29f));
        GameObject decoration = BuildDeck(platform);

        // Let EnvMan choose its normal clear daytime palette, then provide stable fill light.
        // Field types verified against the installed 1.0.15 EnvMan: the oddly named
        // m_debugTimeOfDay is the switch and m_debugTime is the fractional day.
        if (EnvMan.instance)
        {
            EnvMan.instance.m_debugTimeOfDay = true;
            EnvMan.instance.m_debugTime = .42f;
            EnvMan.instance.m_debugEnv = "Clear";
            EnvMan.instance.ForceInstantEnvironmentSwitch();
        }
        var lightObject = new GameObject("Capture-only soft daylight");
        Light fill = lightObject.AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.color = new Color(1f, .91f, .77f);
        fill.intensity = 1.15f;
        fill.shadows = LightShadows.Soft;
        lightObject.transform.rotation = Quaternion.Euler(40, -35, 0);

        GameCamera gameCamera = Object.FindObjectOfType<GameCamera>();
        if (gameCamera) gameCamera.enabled = false;
        Camera originalCamera = Camera.main;
        if (originalCamera) originalCamera.enabled = false;
        var cameraObject = new GameObject("Capture-only tracking camera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        camera.fieldOfView = 47;
        camera.nearClipPlane = .05f;
        camera.farClipPlane = 600;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.50f, .68f, .74f);
        camera.cullingMask = ~LayerMask.GetMask("UI", "Invisible");
        camera.allowHDR = false;
        camera.allowMSAA = true;
        camera.renderingPath = RenderingPath.Forward;
        var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32)
        {
            name = "Skamtebord footage target", antiAliasing = 2
        };
        target.Create();
        camera.targetTexture = target;
        var pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        Time.captureFramerate = FramesPerSecond;
        QualitySettings.vSyncCount = 0;

        logger.LogInfo("SKAMTEBORD_CAPTURE READY GPU=" + SystemInfo.graphicsDeviceName + " output=" + outputRoot);

        for (int take = 0; take < 3; take++)
        {
            string clip = take == 0 ? "01-push-carve-brake" : take == 1 ? "02-tricks-xp" : "03-downhill";
            string directory = Path.Combine(outputRoot, clip);
            Directory.CreateDirectory(directory);
            if (Get<bool>(rider, "Riding")) Invoke(rider, "Dismount", false);
            platform.transform.position = deckCenter;
            platform.transform.rotation = Quaternion.Euler(take == 2 ? 12f : 0f, 0, 0);
            // Decoration is parented in unit scale; it follows the same actual collider incline.
            decoration.transform.position = platform.transform.position;
            decoration.transform.rotation = platform.transform.rotation;
            Vector3 start = platform.transform.position + platform.transform.rotation * new Vector3(0, 1, -85);
            body.position = start + Vector3.up;
            player.transform.position = body.position;
            body.rotation = Quaternion.identity;
            player.transform.rotation = Quaternion.identity;
            player.ForceJump(Vector3.zero, false);
            body.linearVelocity = Vector3.zero;
            Physics.SyncTransforms();
            float settleUntil = Time.realtimeSinceStartup + 12;
            while ((!player.IsOnGround() || player.GetLastGroundCollider() != platform.GetComponent<Collider>() || !player.CanMove())
                   && Time.realtimeSinceStartup < settleUntil)
                yield return new WaitForFixedUpdate();
            if (!player.IsOnGround()) throw new InvalidOperationException("Capture character did not settle for " + clip);
            yield return new WaitForSeconds(.4f);
            body.linearVelocity = Vector3.zero;
            player.AddStamina(100f);
            if (take == 1)
            {
                // Unlocks a kickflip for demonstration, never persisted by the isolated harness.
                object progression = Get<object>(rider, "Progression");
                Invoke(progression, "AddPoints", 10000);
                Invoke(rider, "MirrorSkill");
            }
            long initialXP = Get<long>(Get<object>(rider, "Progression"), "LifetimePoints");
            Invoke(rider, "Toggle");
            if (!Get<bool>(rider, "Riding")) throw new InvalidOperationException("Capture mount failed for " + clip);
            Invoke(rider, "CaptureControls", Vector3.zero, false);
            logger.LogInfo("SKAMTEBORD_CAPTURE START " + clip + " start=" + body.position);

            int frameCount = (take == 1 ? 12 : 10) * FramesPerSecond;
            float clipStart = Time.time;
            bool firstJump = false, secondJump = false, kickflip = false, shuvit = false;
            using (var csv = new StreamWriter(Path.Combine(directory, "telemetry.csv")))
            {
                csv.WriteLine("frame,time,phase,speed_mps,riding,grounded,earned_xp,lifetime_xp,level,combo_points,combo_tricks,status,x,y,z");
                for (int frame = 0; frame < frameCount; frame++)
                {
                    float t = frame / (float)FramesPerSecond;
                    Vector3 controls = Vector3.zero;
                    bool jump = false;
                    string phase;
                    if (take == 0)
                    {
                        if (t < .8f) phase = "Ready";
                        else if (t < 3.8f) { phase = "Push"; controls = Vector3.forward; }
                        else if (t < 4.6f) { phase = "Carve right"; controls = new Vector3(.55f, 0, .3f); }
                        else if (t < 5.6f) { phase = "Carve left"; controls = new Vector3(-.55f, 0, .3f); }
                        else if (t < 7.6f) phase = "Coast";
                        else { phase = "Brake"; controls = Vector3.back; }
                    }
                    else if (take == 1)
                    {
                        controls = t < 2.8f || (t >= 5f && t < 7f) ? Vector3.forward : Vector3.zero;
                        phase = t < 2.8f ? "Push" : t < 4f ? "Ollie + Kickflip" : t < 5.3f ? "Landing / combo" : t < 7f ? "Push" : t < 8.2f ? "Ollie + Shuvit" : "Banked XP";
                        if (!firstJump && t >= 2.8f) { firstJump = true; jump = true; }
                        if (!secondJump && t >= 7f) { secondJump = true; jump = true; }
                        if (firstJump && !kickflip && t >= 2.94f && !Get<bool>(rider, "Grounded")) { Trick(rider, "Kickflip"); kickflip = true; }
                        if (secondJump && !shuvit && t >= 7.14f && !Get<bool>(rider, "Grounded")) { Trick(rider, "Shuvit"); shuvit = true; }
                    }
                    else phase = "12 degree slope / no push";
                    Invoke(rider, "CaptureControls", controls, jump);
                    yield return null;

                    // Run the real presentation adapter after movement, before this manual render.
                    Invoke(rider, "LateUpdate");
                    Vector3 focus = body.position + Vector3.up * .88f;
                    Vector3 cameraOffset = take == 1 ? new Vector3(-3.5f, 1.9f, -3.8f) : new Vector3(-4.3f, 2.3f, -4.4f);
                    camera.transform.position = focus + cameraOffset;
                    camera.transform.LookAt(focus + player.transform.forward * .55f);
                    RenderSettings.fog = false;
                    RenderSettings.ambientMode = AmbientMode.Flat;
                    RenderSettings.ambientLight = new Color(.56f, .62f, .65f);
                    camera.Render();
                    RenderTexture previous = RenderTexture.active;
                    RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                    pixels.Apply(false, false);
                    RenderTexture.active = previous;
                    File.WriteAllBytes(Path.Combine(directory, "frame_" + frame.ToString("D5") + ".png"), pixels.EncodeToPNG());
                    object progression = Get<object>(rider, "Progression");
                    object combo = Get<object>(rider, "Combo");
                    long xp = Get<long>(progression, "LifetimePoints");
                    csv.WriteLine(string.Join(",", frame, t.ToString("F3", Invariant), phase,
                        Get<float>(rider, "Speed").ToString("F3", Invariant), Get<bool>(rider, "Riding"), Get<bool>(rider, "Grounded"),
                        xp - initialXP, xp, Get<int>(progression, "Level"), Get<int>(combo, "PendingScore"), Get<int>(combo, "TrickCount"),
                        Quote(Get<string>(rider, "Status")), body.position.x.ToString("F3", Invariant), body.position.y.ToString("F3", Invariant), body.position.z.ToString("F3", Invariant)));
                    if (frame % FramesPerSecond == 0)
                    {
                        csv.Flush();
                        logger.LogInfo("SKAMTEBORD_CAPTURE FRAME " + clip + " " + frame + " speed=" + Get<float>(rider, "Speed").ToString("F2", Invariant) + " riding=" + Get<bool>(rider, "Riding") + " xp=" + (xp - initialXP));
                    }
                }
            }
            logger.LogInfo("SKAMTEBORD_CAPTURE DONE " + clip + " frames=" + frameCount + " simulationSeconds=" + (Time.time - clipStart));
        }

        Invoke(rider, "Dismount", false);
        Time.captureFramerate = 0;
        camera.targetTexture = null;
        target.Release();
        Object.Destroy(target);
        Object.Destroy(pixels);
        Object.Destroy(cameraObject);
        Object.Destroy(lightObject);
        Object.Destroy(decoration);
        File.WriteAllText(Path.Combine(outputRoot, "capture-complete.txt"), "SUCCESS: all three real-game frame sequences and telemetry completed.");
        logger.LogInfo("SKAMTEBORD_CAPTURE SUCCESS " + outputRoot);
    }

    private static GameObject BuildDeck(GameObject platform)
    {
        var root = new GameObject("Capture-only marked demonstration deck");
        root.transform.position = platform.transform.position;
        Material joint = Material("joints", new Color(.24f, .33f, .36f));
        Material edge = Material("ochre trim", new Color(.88f, .63f, .24f));
        Material marker = Material("pale lane marks", new Color(.81f, .83f, .69f));
        for (int z = -148; z <= 148; z += 4)
            Part(root, "Deck seam", new Vector3(0, 1.012f, z), new Vector3(160, .014f, .06f), joint);
        foreach (float x in new[] { -14f, 14f })
        {
            Part(root, "Lane edge", new Vector3(x, 1.023f, 0), new Vector3(.24f, .018f, 300), edge);
            for (int z = -140; z <= 140; z += 10)
            {
                Part(root, "Distance marker", new Vector3(x + (x < 0 ? 1 : -1), 1.031f, z), new Vector3(1.2f, .015f, .18f), marker);
                Part(root, "Guide post", new Vector3(x * 1.6f, 1.6f, z), new Vector3(.22f, 1.2f, .22f), edge);
            }
        }
        for (int z = -140; z <= 140; z += 6)
            Part(root, "Centerline", new Vector3(0, 1.025f, z), new Vector3(.12f, .018f, 1.3f), marker);
        return root;
    }

    private static Material Material(string name, Color color)
    {
        Shader shader = Shader.Find("Standard") ?? Shader.Find("Custom/Creature") ?? Shader.Find("Sprites/Default");
        var result = new Material(shader) { name = "Capture " + name, color = color };
        if (result.HasProperty("_Glossiness")) result.SetFloat("_Glossiness", .12f);
        return result;
    }

    private static void Part(GameObject root, string name, Vector3 position, Vector3 scale, Material material)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = name;
        Object.DestroyImmediate(part.GetComponent<Collider>());
        part.transform.SetParent(root.transform, false);
        part.transform.localPosition = position;
        part.transform.localScale = scale;
        part.GetComponent<Renderer>().sharedMaterial = material;
    }

    private static void Trick(Component rider, string name)
    {
        MethodInfo method = AccessTools.Method(rider.GetType(), "Trick");
        method.Invoke(rider, new[] { Enum.Parse(method.GetParameters()[0].ParameterType, name) });
    }
    private static object Invoke(object target, string name, params object[] args) => AccessTools.Method(target.GetType(), name).Invoke(target, args);
    private static T Get<T>(object target, string name) => (T)AccessTools.Property(target.GetType(), name).GetValue(target, null);
    private static string Quote(string text) => "\"" + (text ?? "").Replace("\"", "\"\"").Replace("\n", " ") + "\"";
}
