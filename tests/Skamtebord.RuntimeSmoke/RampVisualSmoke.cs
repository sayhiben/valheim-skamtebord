using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Skamtebord.RuntimeSmoke;

internal static class RampVisualSmoke
{
    internal static IEnumerator Run(Vector3 origin, string root, RenderingPath gameplayRenderingPath, Action<bool, string> check, Action<string> log)
    {
        var camera = new GameObject("Ramp visibility camera").AddComponent<Camera>();
        camera.enabled = false;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.04f, .06f, .09f);
        camera.cullingMask = 1 << 30;
        camera.fieldOfView = 45;
        camera.nearClipPlane = .5f;
        camera.farClipPlane = 150;
        camera.allowHDR = false;
        camera.renderingPath = gameplayRenderingPath;
        log("Ramp images use the normal game camera rendering path: " + gameplayRenderingPath);
        var target = new RenderTexture(640, 360, 24);
        camera.targetTexture = target;
        var image = new Texture2D(640, 360, TextureFormat.RGB24, false);
        // Keep a large floor in the render test: transparent, depthless fallback
        // materials can hide ramps behind the floor despite isolated-mesh tests passing.
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.layer = 30;
        floor.transform.position = origin - Vector3.up;
        floor.transform.localScale = new Vector3(200, 2, 200);
        UnityEngine.Object.DestroyImmediate(floor.GetComponent<Collider>());
        floor.GetComponent<Renderer>().sharedMaterial = QaBootstrap.FixtureMaterial(new Color(.03f, .1f, .2f));
        foreach (bool crest in new[] { false, true })
        {
            var ramp = RampSmoke.CreateRamp(origin, 1.25f / Mathf.Tan(20 * Mathf.Deg2Rad), crest);
            ramp.layer = 30;
            // Same material/shader as the play course; use a distinctive color for pixel detection.
            ramp.GetComponent<Renderer>().sharedMaterial.color = new Color(.8f, .32f, .08f);
            Vector3 center = ramp.GetComponent<Renderer>().bounds.center;
            var views = new[] { new Vector3(0, 2, -16), new Vector3(0, 2, 16), new Vector3(-20, 2, 0),
                new Vector3(20, 2, 0), new Vector3(0, 3, -60), new Vector3(0, 3, 60) };
            for (int i = 0; i < views.Length; i++)
            {
                camera.transform.position = center + views[i];
                camera.transform.LookAt(center);
                yield return new WaitForEndOfFrame();
                camera.Render();
                var previous = RenderTexture.active;
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 640, 360), 0, 0); image.Apply();
                RenderTexture.active = previous;
                int visible = image.GetPixels32().Count(p => p.r > 25 && p.r > p.g * 1.3f && p.r > p.b * 1.3f);
                string name = (crest ? "crest" : "kicker") + "-view-" + i;
                check(visible > 50, $"{name} visible at {views[i].magnitude:F0}m ({visible} pixels)");
                File.WriteAllBytes(Path.Combine(root, name + ".png"), image.EncodeToPNG());
            }
            UnityEngine.Object.Destroy(ramp);
        }
        camera.targetTexture = null;
        target.Release();
        UnityEngine.Object.Destroy(target);
        UnityEngine.Object.Destroy(image);
        UnityEngine.Object.Destroy(camera.gameObject);
        UnityEngine.Object.Destroy(floor);
        log("Closed ramp meshes rendered from front, back, left, right and both ends at 60m.");
    }
}
