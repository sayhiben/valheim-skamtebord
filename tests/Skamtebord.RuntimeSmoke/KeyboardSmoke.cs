using System;
using System.Collections;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Skamtebord.RuntimeSmoke;

// This tests real bindings and PlayerController, not OS input delivery. No calls to
// rider.Toggle/CaptureControls and no patches to ZInput or the mod's UI guard.
internal static class KeyboardSmoke
{
    private static Keyboard keyboard;
    private static KeyboardState held;
    private static void QueueKeyboard() { if (keyboard != null) InputSystem.QueueStateEvent(keyboard, held); }
    private static T Get<T>(object target, string name) => (T)AccessTools.Property(target.GetType(), name).GetValue(target);
    private static float Speed(Rigidbody body) => Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up).magnitude;

    private static IEnumerator Hold(float seconds, params Key[] keys)
    {
        held = new KeyboardState(keys);
        float end = Time.time + seconds;
        while (Time.time < end) yield return new WaitForEndOfFrame();
    }

    private static IEnumerator Shot(string root, string name)
    {
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(Path.Combine(root, name + ".png"));
        yield return new WaitForEndOfFrame();
    }

    internal static IEnumerator Run(Player player, Component rider, GameObject platform, string root,
        Action<bool, string> check, Action<string> log)
    {
        // Only the synthetic QA device may run without window focus. Leave the
        // physical keyboard and the game's normal input/UI guards unchanged.
        InputSystem.RegisterLayout("{\"name\":\"SkamtebordQAKeyboard\",\"extend\":\"Keyboard\",\"runInBackground\":\"enabled\"}");
        keyboard = (Keyboard)InputSystem.AddDevice("SkamtebordQAKeyboard");
        keyboard.MakeCurrent();
        held = new KeyboardState();
        InputSystem.onBeforeUpdate += QueueKeyboard;
        try
        {
            Application.runInBackground = true;
            log($"Keyboard focused={Application.isFocused}, enabled={keyboard.enabled}, background={keyboard.canRunInBackground}, policy={InputSystem.settings.backgroundBehavior}");
            check(keyboard.enabled && keyboard.canRunInBackground, "QA keyboard remains available without window focus");
            Time.timeScale = 1f;
            var body = player.GetComponent<Rigidbody>();
            long initialPoints = Get<long>(Get<object>(rider, "Progression"), "LifetimePoints");
            var animator = player.GetComponentInChildren<Animator>();
            var original = animator.runtimeAnimatorController;
            bool originalRootMotion = animator.applyRootMotion;
            var playerController = player.GetComponent<PlayerController>();
            check(playerController && playerController.enabled, "normal PlayerController remains enabled");
            log("Animator human=" + animator.isHuman + " avatar=" + animator.avatar.name);
            foreach (var name in new[] { HumanBodyBones.Hips, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
                log(name + "=" + player.transform.InverseTransformPoint(animator.GetBoneTransform(name).position));
            // Visible, fixed camera for repeatable image evidence; player input stays enabled.
            var originalCamera = Utils.GetMainCamera();
            var gameCamera = originalCamera ? originalCamera.GetComponent<GameCamera>() : null;
            if (!gameCamera) gameCamera = UnityEngine.Object.FindObjectOfType<GameCamera>();
            if (gameCamera) gameCamera.enabled = false;
            if (originalCamera) originalCamera.enabled = false;
            var camera = new GameObject("Keyboard test camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.fieldOfView = 47;
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 600;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.50f, .68f, .74f);
            camera.cullingMask = ~LayerMask.GetMask("UI", "Invisible");
            camera.allowHDR = false;
            camera.renderingPath = RenderingPath.Forward;
            var tracker = camera.gameObject.AddComponent<KeyboardCamera>();
            tracker.Target = player.transform;
            var testShader = Shader.Find("Standard") ?? Shader.Find("Custom/Creature") ?? Shader.Find("Sprites/Default");
            platform.GetComponent<Renderer>().sharedMaterial = new Material(testShader) { color = new Color(.19f, .27f, .25f) };
            RenderSettings.ambientLight = new Color(.65f, .65f, .65f);
            if (EnvMan.instance)
            {
                EnvMan.instance.m_debugTimeOfDay = true;
                EnvMan.instance.m_debugTime = .42f;
                EnvMan.instance.m_debugEnv = "Clear";
                EnvMan.instance.ForceInstantEnvironmentSwitch();
            }
            var light = new GameObject("Keyboard test daylight").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.transform.rotation = Quaternion.Euler(45, -30, 0);

            // A hidden D3D11 launch can leave the initial swapchain black. Recreate
            // it before any evidence capture, as in the resolution checks below.
            Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
            yield return Hold(.1f);
            Screen.SetResolution(1920, 1080, FullScreenMode.Windowed);
            yield return Hold(.2f);
            yield return Hold(.1f); // Bootstrap already waits for a movable, grounded player.
            yield return Hold(.15f, Key.B);
            yield return Hold(.45f);
            check(Get<bool>(rider, "Riding"), "B key mounts through the normal keyboard binding");
            check(animator.runtimeAnimatorController is AnimatorOverrideController, "rider uses an AnimatorOverrideController");
            check(animator.applyRootMotion == originalRootMotion, "mount preserves Valheim's root-motion setting and animator state");
            yield return new WaitForEndOfFrame();
            var renderedFrame = ScreenCapture.CaptureScreenshotAsTexture();
            var pixels = renderedFrame.GetPixels32();
            bool nonBlackFrame = pixels.Count(pixel => pixel.r > 16 || pixel.g > 16 || pixel.b > 16) > pixels.Length / 20;
            UnityEngine.Object.Destroy(renderedFrame);
            check(nonBlackFrame, "graphical QA renders a non-black frame before capture");
            yield return Shot(root, "01-coasting-stance");

            Transform front = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform back = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            Vector3 backMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), backMax = -Vector3.one * float.MaxValue;
            Vector3 frontMin = backMin, frontMax = backMax;
            held = new KeyboardState(Key.W);
            float start = Time.time;
            float initialSpeed = Speed(body);
            bool pushClipSeen = false;
            float maximumRootTravel = 0f;
            while (Time.time - start < 1.5f)
            {
                yield return new WaitForEndOfFrame();
                Vector3 foot = player.transform.InverseTransformPoint(back.position);
                backMin = Vector3.Min(backMin, foot); backMax = Vector3.Max(backMax, foot);
                foot = player.transform.InverseTransformPoint(front.position);
                frontMin = Vector3.Min(frontMin, foot); frontMax = Vector3.Max(frontMax, foot);
                pushClipSeen |= animator.GetCurrentAnimatorClipInfo(0).Any(c => c.clip.name == "SkatePush" && c.weight > .2f);
                maximumRootTravel = Mathf.Max(maximumRootTravel, animator.deltaPosition.magnitude);
                if (Time.time - start > .55f && Time.time - start < .65f) ScreenCapture.CaptureScreenshot(Path.Combine(root, "02-pushing.png"));
            }
            check(Speed(body) > initialSpeed + 2f && Get<bool>(rider, "Pushing"), $"W key pushes: {initialSpeed:F2}->{Speed(body):F2} m/s");
            check(pushClipSeen, "Humanoid SkatePush clip plays while pushing");
            check(maximumRootTravel < .01f, $"push animation adds no root travel ({maximumRootTravel:F5}m maximum per frame)");
            log($"Foot travel in player space: front={frontMax-frontMin}, back={backMax-backMin}");
            check((frontMax-frontMin).magnitude < .08f, "front support foot stays planted through the push cycle");
            check((backMax-backMin).magnitude > .15f, "pushing foot visibly moves through the cycle");
            yield return Hold(.5f);
            check(!Get<bool>(rider, "Pushing"), "releasing W returns to coasting");
            yield return Shot(root, "03-coast-and-hud");

            float heading = body.rotation.eulerAngles.y;
            yield return Hold(.35f, Key.D);
            yield return Hold(.15f);
            check(Mathf.Abs(Mathf.DeltaAngle(heading, body.rotation.eulerAngles.y)) > 7f, "D key steers through PlayerController");
            yield return Hold(.12f, Key.Space);
            check(!Get<bool>(rider, "Grounded"), "Space key launches an ollie");
            yield return Hold(3f);
            check(Get<bool>(rider, "Riding") && Get<bool>(rider, "Grounded"), "keyboard ollie lands and retains the ride");
            object progression = Get<object>(rider, "Progression");
            check(Get<long>(progression, "LifetimePoints") - initialPoints == 100, "keyboard ollie banks 100 newly earned Skamtebord XP");
            yield return Hold(.9f, Key.S);
            yield return Hold(.1f);
            check(Speed(body) < 1f, "S key brakes the board");

            yield return Hold(.15f, Key.Tab);
            yield return Hold(.2f);
            check(InventoryGui.IsVisible(), "Tab opens the ordinary inventory");
            yield return Hold(.2f, Key.B, Key.W);
            check(Get<bool>(rider, "Riding") && !Get<bool>(rider, "Pushing"), "inventory blocks mount and push keyboard input");
            yield return Hold(.15f);
            yield return Hold(.15f, Key.Tab);
            yield return Hold(.2f);
            check(!InventoryGui.IsVisible(), "Tab closes the ordinary inventory");

            yield return Hold(.15f, Key.F5);
            yield return Hold(.2f);
            check(global::Console.IsVisible(), "F5 opens the ordinary console");
            yield return Hold(.15f, Key.F5);
            yield return Hold(.2f);
            check(!global::Console.IsVisible(), "F5 closes the ordinary console");
            yield return Hold(.15f, Key.B);
            yield return Hold(.3f);
            check(!Get<bool>(rider, "Riding") && animator.runtimeAnimatorController == original, "dismount restores the original animator controller");
            Vector3 walkingStart = body.position;
            yield return Hold(.7f, Key.W);
            log($"Walking diagnostics: distance={Vector3.Distance(walkingStart, body.position):F3}, velocity={body.linearVelocity}, CanMove={player.CanMove()}, ground={player.IsOnGround()}, Forward={ZInput.GetButton("Forward")}, takeInput={AccessTools.Method(typeof(PlayerController), "TakeInput").Invoke(playerController, new object[]{false})}, clips=" + string.Join(", ", animator.GetCurrentAnimatorClipInfo(0).Select(c => c.clip.name+":"+c.weight)));
            foreach (var field in new[] { "m_moveDir", "m_currentVel", "m_lastPos", "m_lazyPos", "m_rootMotion", "m_slipping" })
                log(field + "=" + AccessTools.Field(typeof(Character), field)?.GetValue(player));
            yield return Shot(root, "04-normal-walking-restored");
            yield return Hold(.2f);
            check(Vector3.Distance(walkingStart, body.position) > .3f, "ordinary walking works after dismount");
            yield return Hold(.2f, Key.B);
            yield return Hold(.4f);
            check(Get<bool>(rider, "Riding"), "B remounts after normal walking");
            if (Environment.GetCommandLineArgs().Contains("-skamtebord-keyboard-capture"))
            {
                string frames = Path.Combine(root, "push-video");
                Directory.CreateDirectory(frames);
                Time.captureFramerate = 30;
                for (int frame = 0; frame < 180; frame++)
                {
                    held = frame < 30 ? new KeyboardState(Key.S) : frame < 135 ? new KeyboardState(Key.W) : new KeyboardState();
                    yield return new WaitForEndOfFrame();
                    ScreenCapture.CaptureScreenshot(Path.Combine(frames, $"frame_{frame:D5}.png"));
                }
                Time.captureFramerate = 0;
            }
            foreach (var size in new[] { new Vector2Int(1280,720), new Vector2Int(2560,1440) })
            {
                Screen.SetResolution(size.x, size.y, FullScreenMode.Windowed);
                yield return Hold(.6f);
                var hud = UnityEngine.Object.FindFirstObjectByType(AccessTools.TypeByName("Skamtebord.SkateHud"));
                Rect rect = Get<Rect>(hud, "LastPanelRect");
                check(rect.xMin > Screen.width*.5f && rect.yMin > Screen.height*.24f && rect.xMax <= Screen.width && rect.yMax < Screen.height*.9f, $"HUD stays in clear screen bounds at {Screen.width}x{Screen.height}");
                yield return Shot(root, $"05-hud-{Screen.width}x{Screen.height}");
            }
            yield return Hold(.2f, Key.B);
            yield return Hold(.3f);
            check(!Get<bool>(rider, "Riding") && player.CanMove(), "second dismount leaves the character immediately movable");
            log("Input System keyboard test completed; no direct rider control injection or focus-guard patch.");
            UnityEngine.Object.Destroy(camera.gameObject);
            if (originalCamera) originalCamera.enabled = true;
            if (gameCamera) gameCamera.enabled = true;
        }
        finally
        {
            InputSystem.onBeforeUpdate -= QueueKeyboard;
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            keyboard = null;
            InputSystem.RemoveLayout("SkamtebordQAKeyboard");
        }
    }
}

internal sealed class KeyboardCamera : MonoBehaviour
{
    internal Transform Target;
    private void LateUpdate()
    {
        if (!Target) return;
        RenderSettings.fog = false;
        RenderSettings.ambientLight = new Color(.6f, .65f, .68f);
        transform.position = Target.position + new Vector3(3.1f, 1.65f, 3.1f);
        transform.LookAt(Target.position + Vector3.up * .9f);
    }
}
