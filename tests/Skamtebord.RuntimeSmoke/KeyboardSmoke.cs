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
        bool originalToggleRun = ZInput.ToggleRun;
        float originalMaximumDelta = Time.maximumDeltaTime;
        // A stalled screenshot/render frame must not turn a synthetic 50 ms
        // tap into a 333 ms hold before the coroutine can submit key release.
        // This is QA timing only; normal gameplay time settings stay untouched.
        Time.maximumDeltaTime = .05f;
        ZInput.ToggleRun = false;
        try
        {
            Application.runInBackground = true;
            log($"Keyboard focused={Application.isFocused}, enabled={keyboard.enabled}, background={keyboard.canRunInBackground}, policy={InputSystem.settings.backgroundBehavior}");
            log("Player sprint preference ToggleRun=" + originalToggleRun + "; testing hold and toggle modes without saving settings.");
            check(keyboard.enabled && keyboard.canRunInBackground, "QA keyboard remains available without window focus");
            Time.timeScale = 1f;
            var body = player.GetComponent<Rigidbody>();
            ControlConfigSmoke.Run(root, check);
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
            var gameplayRenderingPath = originalCamera.actualRenderingPath;
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
            platform.GetComponent<Renderer>().sharedMaterial = QaBootstrap.FixtureMaterial(new Color(.19f, .27f, .25f));
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
            var visibility = RampVisualSmoke.Run(platform.transform.position + new Vector3(0, 1, -40), root, gameplayRenderingPath, check, log);
            while (visibility.MoveNext()) yield return visibility.Current;
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
            float tapStart = Time.time;
            yield return Hold(.05f, Key.Space);
            check(Time.time-tapStart<.12f, $"synthetic jump tap stays below the grab threshold ({Time.time-tapStart:F3}s)");
            check(!Get<bool>(rider, "Grounded"), "Space key launches an ollie");
            yield return Hold(3f);
            check(Get<bool>(rider, "Riding") && Get<bool>(rider, "Grounded"), "keyboard ollie lands and retains the ride");
            object progression = Get<object>(rider, "Progression");
            long earned = Get<long>(progression,"LifetimePoints")-initialPoints;
            check(earned == 100, $"keyboard ollie banks 100 newly earned Skamtebord XP (actual={earned}, status={Get<string>(rider,"Status")})");
            Vector3 brakingDirection=body.linearVelocity.normalized;
            yield return Hold(.9f, Key.S);
            check(Vector3.Dot(body.linearVelocity,brakingDirection)<1f, "S key brakes the original rolling direction");
            yield return Hold(1.2f, Key.S);
            check(Get<bool>(rider,"Backing") && Vector3.Dot(body.linearVelocity,player.transform.forward)<-2.7f && Speed(body)<3.05f,
                "holding S after stopping backs up through the normal PlayerController, capped at 3 m/s");
            check(Get<bool>(rider,"Pushing") && animator.GetCurrentAnimatorClipInfo(0).Any(c=>c.clip.name=="SkatePush" && c.weight>.2f),
                "backing up displays the synchronized push animation");
            yield return Hold(.1f);
            check(!Get<bool>(rider,"Backing"), "releasing S exits reverse control");

            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            float coastHipHeight = player.transform.InverseTransformPoint(hips.position).y;
            player.AddStamina(100f);
            yield return Hold(2f, Key.W);
            float ordinarySpeed = Speed(body);
            check(ordinarySpeed > 8f && ordinarySpeed < 9.4f, $"ordinary pushing reaches its 9 m/s cap ({ordinarySpeed:F2})");
            float sprintStamina = player.GetStamina();
            yield return Hold(1.5f, Key.W, Key.LeftShift);
            float sprintSpeed = Speed(body);
            check(Get<bool>(rider, "Sprinting") && sprintSpeed > 12f && sprintSpeed < 14.4f, $"Shift+W sprints through PlayerController ({sprintSpeed:F2} m/s)");
            check(player.GetStamina() < sprintStamina - 6f, "sprinting consumes stamina");
            check(animator.GetCurrentAnimatorClipInfo(0).Any(c => c.clip.name == "SkateTuck" && c.weight > .2f), "Humanoid SkateTuck clip plays during sprint");
            float tuckHipHeight = player.transform.InverseTransformPoint(hips.position).y;
            check(tuckHipHeight < coastHipHeight - .12f, $"sprint visibly crouches the rider (hips {coastHipHeight:F2}->{tuckHipHeight:F2}m)");
            check(Mathf.Abs(player.transform.InverseTransformPoint(front.position).y - frontMin.y) < .1f
                && Mathf.Abs(player.transform.InverseTransformPoint(back.position).y - frontMin.y) < .1f, "both feet remain on the board during tuck");
            check(animator.deltaPosition.magnitude < .01f, "tuck introduces no animation root travel");
            yield return Shot(root, "06-sprint-tuck");
            yield return Hold(.25f, Key.W);
            check(!Get<bool>(rider, "Sprinting") && Speed(body) > sprintSpeed - 1f, $"releasing sprint preserves accumulated speed ({sprintSpeed:F2}->{Speed(body):F2}, sprint={Get<bool>(rider, "Sprinting")}, run={ZInput.GetButton("Run")})");
            yield return Hold(.3f);
            check(animator.GetCurrentAnimatorClipInfo(0).Any(c => c.clip.name == "SkateCoast" && c.weight > .2f), "releasing sprint restores the normal skating stance");
            player.UseStamina(1000f);
            yield return Hold(.15f, Key.W, Key.LeftShift);
            check(!Get<bool>(rider, "Sprinting"), "empty stamina disables sprint");
            yield return Hold(1.6f, Key.S);
            player.AddStamina(100f);
            ZInput.ToggleRun = true;
            yield return Hold(.2f, Key.W, Key.LeftShift);
            yield return Hold(.2f, Key.W);
            check(Get<bool>(rider, "Sprinting"), "Valheim toggle-run preference keeps sprint active after releasing Shift");
            yield return Hold(.2f, Key.W, Key.LeftShift);
            yield return Hold(.2f, Key.W);
            check(!Get<bool>(rider, "Sprinting"), "second Shift press ends toggle-mode sprint");
            yield return Hold(.8f, Key.S);
            ZInput.ToggleRun = false;
            yield return Hold(.15f, Key.E);
            check(ZInput.GetButton("Use"), "E remains available to Valheim Use while mounted");
            yield return Hold(.1f);

            yield return Hold(.15f, Key.Tab);
            yield return Hold(.2f);
            check(InventoryGui.IsVisible(), "Tab opens the ordinary inventory");
            yield return Hold(.2f, Key.B, Key.W, Key.LeftShift, Key.J);
            check(Get<bool>(rider, "Riding") && !Get<bool>(rider, "Pushing") && !Get<bool>(rider, "Sprinting"), "inventory blocks mount, push, sprint and trick keyboard input");
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
            Key[] trickKeys = { Key.J, Key.K, Key.L, Key.U, Key.I };
            string[] trickNames = { "Shuvit", "Kickflip", "Heelflip", "Grab", "ThreeSixty" };
            for (int i = 0; i < trickKeys.Length; i++)
            {
                player.AddStamina(100f);
                yield return Hold(.6f, Key.W);
                yield return Hold(.06f, Key.Space);
                yield return Hold(.1f); // A tap: holding jump now deliberately starts a grab.
                yield return Hold(.08f, trickKeys[i]);
                string currentTrick = AccessTools.Field(rider.GetType(), "visualTrick").GetValue(rider).ToString();
                log($"DIRECT_TRICK key={trickKeys[i]} actual={currentTrick} riding={Get<bool>(rider,"Riding")} grounded={Get<bool>(rider,"Grounded")} position={body.position} velocity={body.linearVelocity} status={Get<string>(rider,"Status")}");
                check(currentTrick == trickNames[i], trickKeys[i] + " triggers " + trickNames[i] + " through the real trick binding");
                yield return Hold(1f);
                // These short flat ollies test input, not completing the longer advanced tricks.
                if (!Get<bool>(rider, "Riding")) { yield return Hold(.15f, Key.B); yield return Hold(.2f); }
                yield return Hold(.8f, Key.S);
            }
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
            Time.maximumDeltaTime = originalMaximumDelta;
            ZInput.ToggleRun = originalToggleRun;
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
