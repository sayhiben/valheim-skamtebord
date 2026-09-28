using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Skamtebord.RuntimeSmoke;

internal static class EasyInputSmoke
{
    private static Keyboard keyboard;
    private static KeyboardState held;
    private static void Queue() => InputSystem.QueueStateEvent(keyboard,held);
    private static T Get<T>(object target,string name) => (T)AccessTools.Property(target.GetType(),name).GetValue(target);
    private static IEnumerator Hold(float seconds,params Key[] keys)
    {
        held = new KeyboardState(keys);
        float end = Time.time + seconds;
        while (Time.time < end) yield return new WaitForEndOfFrame();
    }
    internal static IEnumerator Run(Player player,Component rider,string root,Action<bool,string> check,Action<string> log)
    {
        InputSystem.RegisterLayout("{\"name\":\"SkamtebordFlowKeyboard\",\"extend\":\"Keyboard\",\"runInBackground\":\"enabled\"}");
        keyboard = (Keyboard)InputSystem.AddDevice("SkamtebordFlowKeyboard"); keyboard.MakeCurrent();
        held = new KeyboardState(); InputSystem.onBeforeUpdate += Queue;
        bool toggle = ZInput.ToggleRun; ZInput.ToggleRun = false;
        object progression = Get<object>(rider,"Progression");
        var progressProperty = AccessTools.Property(rider.GetType(),"Progression");
        progressProperty.SetValue(rider, Activator.CreateInstance(progression.GetType(),new object[] { 0L }));
        try
        {
            var body = player.GetComponent<Rigidbody>();
            var animator = player.GetComponentInChildren<Animator>();
            check(player.GetComponent<PlayerController>().enabled, "easy controls use the ordinary PlayerController");
            yield return Hold(.08f,Key.B); yield return Hold(.1f);
            check(Get<bool>(rider,"Riding"),"beginner mounts through the normal B binding");
            yield return Hold(.06f,Key.Space); yield return Hold(2.7f);
            check(Get<long>(Get<object>(rider,"Progression"),"LifetimePoints")==0,"stationary keyboard ollie cannot convert its jump impulse into approach speed or XP");
            held = new KeyboardState(Key.W);
            float start = Time.time, missing = 0, maxMissing = 0;
            int cycles = 0; float previousPhase = 0;
            while (Time.time-start < 6.5f)
            {
                yield return new WaitForEndOfFrame();
                if (Time.time-start < 1) continue;
                var clips = animator.GetCurrentAnimatorClipInfo(0);
                // Locomotion blends several overridden walk/run clips. Their total
                // contribution is the visible push pose, not any single node weight.
                bool push = clips.Where(c => c.clip.name == "SkatePush").Sum(c => c.weight) > .7f;
                missing = push ? 0 : missing + Time.deltaTime;
                maxMissing = Mathf.Max(maxMissing,missing);
                float phase = animator.GetCurrentAnimatorStateInfo(0).normalizedTime % 1f;
                if (push && previousPhase > .8f && phase < .2f) cycles++;
                previousPhase = phase;
            }
            log($"CONTINUOUS_PUSH speed={body.linearVelocity.magnitude:F2}, missing={maxMissing:F3}s, completedCycles={cycles}");
            log("PUSH_CLIPS " + string.Join(", ",animator.GetCurrentAnimatorClipInfo(0).Select(c=>$"{c.clip.name}={c.weight:F3}")) + "; phase=" + animator.GetCurrentAnimatorStateInfo(0).normalizedTime);
            check(Get<bool>(rider,"Pushing") && body.linearVelocity.magnitude > 8f,"holding forward keeps push intent at the speed cap");
            check(maxMissing < .25f && cycles >= 3,"continuous forward completes multiple push cycles without cutting to coast");

            yield return Hold(.045f,Key.W,Key.Space); yield return Hold(.06f,Key.W);
            yield return Hold(.045f,Key.W,Key.Space); yield return Hold(.18f,Key.W);
            object combo = Get<object>(rider,"Combo");
            check(Get<string>(combo,"ComboLabel").Contains("Shuvit"),"second jump tap chooses a beginner shuvit without a skill unlock");
            check(body.linearVelocity.y < 5.3f,"airborne trick tap does not inject a second jump impulse");
            yield return Hold(.6f,Key.W);
            check(Get<bool>(rider,"Riding") && !Get<bool>(combo,"IsAirborne") && Get<string>(combo,"ComboLabel").Contains("Shuvit"),"beginner tap trick lands with its score intact");
            yield return Hold(.24f,Key.W,Key.Space);
            check(Get<bool>(rider,"Grabbing") && Get<string>(combo,"ComboLabel").Contains("Grab"),"holding jump in the air begins a beginner grab");
            int count = Get<int>(combo,"TrickCount");
            yield return Hold(.1f,Key.W,Key.Space);
            check(Get<int>(combo,"TrickCount") == count,"holding a grab does not repeatedly award tricks");
            yield return Hold(.04f,Key.W);
            check(!Get<bool>(rider,"Grabbing"),"releasing jump releases the grab");
            yield return Hold(.5f,Key.W);
            check(Get<bool>(rider,"Riding"),"beginner held grab lands safely");
            yield return Hold(2.2f);
            object bank = Get<object>(combo,"LastBank");
            check(bank != null && Get<string>(bank,"Label").Contains("Shuvit") && Get<string>(bank,"Label").Contains("Grab")
                && Get<long>(Get<object>(rider,"Progression"),"LifetimePoints") > 0,"beginner shuvit and grab bank Skamtebord XP after landing");
            float[] turns = new float[2];
            for (int i=0; i<2; i++)
            {
                body.linearVelocity = player.transform.forward * (i == 0 ? 3f : 14f);
                float yaw = body.rotation.eulerAngles.y;
                yield return Hold(.35f,Key.D);
                turns[i] = Mathf.Abs(Mathf.DeltaAngle(yaw,body.rotation.eulerAngles.y));
                yield return Hold(.06f);
            }
            log($"STEERING low={turns[0]:F2}deg high={turns[1]:F2}deg over .35s");
            check(turns[0] > 30 && turns[0] > turns[1]*1.25f,"normal steering turns tightly at low speed and progressively widens at high speed");
            yield return Hold(.08f,Key.B); yield return Hold(.08f);
            check(!Get<bool>(rider,"Riding"),"normal dismount works after the easier trick controls");
        }
        finally
        {
            progressProperty.SetValue(rider,progression);
            ZInput.ToggleRun = toggle;
            InputSystem.onBeforeUpdate -= Queue;
            InputSystem.RemoveDevice(keyboard); keyboard = null;
        }
    }
}
