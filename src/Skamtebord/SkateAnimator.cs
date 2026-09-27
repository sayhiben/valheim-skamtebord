using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Skamtebord;

// Keep Valheim's state machine, animation events and parameter/network plumbing.
// Each rider owns an override controller; no shared player controller is modified.
internal sealed class SkateAnimator : IDisposable
{
    private static AnimationClip coast, push, tuck;
    private static bool triedLoading;
    private readonly Animator animator;
    private RuntimeAnimatorController original;
    private AnimatorOverrideController controller;
    private List<KeyValuePair<AnimationClip, AnimationClip>> normalClips, tuckClips;
    private bool tucking;
    internal bool Active => controller && animator && animator.runtimeAnimatorController == controller;

    internal SkateAnimator(Animator animator) { this.animator = animator; }

    private static bool Load()
    {
        if (triedLoading) return coast && push && tuck;
        triedLoading = true;
        try
        {
            using var stream = typeof(SkateAnimator).Assembly.GetManifestResourceStream("Skamtebord.Animations");
            if (stream == null) throw new InvalidOperationException("Embedded animation bundle is missing.");
            var bundle = AssetBundle.LoadFromStream(stream);
            if (!bundle) throw new InvalidOperationException("Unity could not load the animation bundle.");
            coast = bundle.LoadAsset<AnimationClip>("assets/clips/skatecoast.anim");
            push = bundle.LoadAsset<AnimationClip>("assets/clips/skatepush.anim");
            tuck = bundle.LoadAsset<AnimationClip>("assets/clips/skatetuck.anim");
            bundle.Unload(false);
            if (!coast || !push || !tuck || !coast.humanMotion || !push.humanMotion || !tuck.humanMotion) throw new InvalidOperationException("Humanoid skating clips are missing or invalid.");
            SkamtebordPlugin.Instance.Log($"Loaded Humanoid skate animations: coast={coast.length:F2}s, push={push.length:F2}s, tuck={tuck.length:F2}s.");
        }
        catch (Exception error) { SkamtebordPlugin.Instance.Log("Skating animation unavailable; using basic pose. " + error.Message); }
        return coast && push && tuck;
    }

    internal void Mount()
    {
        if (Active || !animator || !animator.isHuman || !Load()) return;
        Dispose();
        original = animator.runtimeAnimatorController;
        if (!original) return;
        controller = new AnimatorOverrideController(original) { name = "Skamtebord rider animations" };
        var clips = new List<KeyValuePair<AnimationClip, AnimationClip>>(controller.overridesCount);
        controller.GetOverrides(clips);
        int idleCount = 0, moveCount = 0;
        for (int i = 0; i < clips.Count; i++)
        {
            var pair = clips[i];
            string name = pair.Key.name.ToLowerInvariant();
            if (name.Contains("idle"))
            {
                clips[i] = new KeyValuePair<AnimationClip, AnimationClip>(pair.Key, coast);
                idleCount++;
            }
            else if (name.Contains("walk") || name.Contains("run") || name.Contains("jog") || name.Contains("sprint"))
            {
                clips[i] = new KeyValuePair<AnimationClip, AnimationClip>(pair.Key, push);
                moveCount++;
            }
        }
        if (idleCount == 0 || moveCount == 0)
        {
            SkamtebordPlugin.Instance.Log("Unsupported animator locomotion clips; keeping the original controller.");
            Dispose();
            return;
        }
        controller.ApplyOverrides(clips);
        normalClips = clips;
        tuckClips = clips.Select(pair => new KeyValuePair<AnimationClip, AnimationClip>(pair.Key,
            pair.Value == coast || pair.Value == push ? tuck : pair.Value)).ToList();
        tucking = false;
        SwapController(controller);
        // Root travel is baked out of all clips at import. Changing applyRootMotion
        // would reinitialize Valheim's state machine and replay its spawn animation.
        SkamtebordPlugin.Instance.Log($"Skate animator ready: {idleCount} idle and {moveCount} locomotion clips; base={original.name}.");
    }

    internal void SetMotion(bool pushing, bool sprinting)
    {
        if (!Active) return;
        if (tucking != sprinting)
        {
            controller.ApplyOverrides(sprinting ? tuckClips : normalClips);
            tucking = sprinting;
        }
        animator.SetFloat("forward_speed", pushing ? 2.5f : 0f, .14f, Time.deltaTime);
    }

    private void SwapController(RuntimeAnimatorController next)
    {
        // Switching back from an override to Valheim's base controller can reset
        // layer state to Standing Up. Carry live states/parameters across the swap.
        var states = Enumerable.Range(0, animator.layerCount).Select(animator.GetCurrentAnimatorStateInfo).ToArray();
        var weights = Enumerable.Range(0, animator.layerCount).Select(animator.GetLayerWeight).ToArray();
        var parameters = animator.parameters;
        var floats = parameters.Where(p => p.type == AnimatorControllerParameterType.Float).ToDictionary(p => p.nameHash, p => animator.GetFloat(p.nameHash));
        var ints = parameters.Where(p => p.type == AnimatorControllerParameterType.Int).ToDictionary(p => p.nameHash, p => animator.GetInteger(p.nameHash));
        var bools = parameters.Where(p => p.type == AnimatorControllerParameterType.Bool).ToDictionary(p => p.nameHash, p => animator.GetBool(p.nameHash));
        animator.runtimeAnimatorController = next;
        foreach (var pair in floats) animator.SetFloat(pair.Key, pair.Value);
        foreach (var pair in ints) animator.SetInteger(pair.Key, pair.Value);
        foreach (var pair in bools) animator.SetBool(pair.Key, pair.Value);
        for (int i = 0; i < states.Length; i++)
        {
            animator.SetLayerWeight(i, weights[i]);
            if (animator.HasState(i, states[i].fullPathHash))
                animator.Play(states[i].fullPathHash, i, states[i].normalizedTime);
        }
        animator.Update(0f);
    }

    public void Dispose()
    {
        if (Active)
        {
            SwapController(original);
        }
        if (controller) UnityEngine.Object.Destroy(controller);
        controller = null;
        original = null;
        normalClips = tuckClips = null;
        tucking = false;
    }
}
