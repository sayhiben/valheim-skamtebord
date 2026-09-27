using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Skamtebord.RuntimeSmoke;

internal static class RampRenderDiagnostics
{
    internal static IEnumerator Run(string root, Action<string> log)
    {
        var camera = Utils.GetMainCamera();
        var effects = camera.GetComponents<Behaviour>().Where(b => b.enabled && !(b is Camera) && !(b is GameCamera)).ToArray();
        var path = camera.renderingPath;
        bool fog = RenderSettings.fog;
        Camera.CameraCallback noFog = c => { if (c == camera) RenderSettings.fog = false; };
        try
        {
            for (int stage = -1; stage < effects.Length + 3; stage++)
            {
                foreach (var effect in effects) effect.enabled = true;
                string name = "baseline";
                if (stage >= 0 && stage < effects.Length) { effects[stage].enabled = false; name = "without-" + effects[stage].GetType().Name; }
                if (stage >= effects.Length) { foreach (var effect in effects) effect.enabled = false; name = "without-all-effects"; }
                if (stage >= effects.Length + 1) { Camera.onPreCull -= noFog; Camera.onPreCull += noFog; name += "-or-fog"; }
                if (stage >= effects.Length + 2) { camera.renderingPath = RenderingPath.Forward; name += "-forward"; }
                yield return new WaitForEndOfFrame();
                yield return new WaitForEndOfFrame();
                log(name + ": path=" + camera.actualRenderingPath + ", fog=" + RenderSettings.fog + ", density=" + RenderSettings.fogDensity);
                ScreenCapture.CaptureScreenshot(Path.Combine(root, "render-" + name + ".png"));
                yield return new WaitForEndOfFrame();
            }
        }
        finally
        {
            Camera.onPreCull -= noFog;
            RenderSettings.fog = fog;
            camera.renderingPath = path;
            foreach (var effect in effects) effect.enabled = true;
        }
    }
}
