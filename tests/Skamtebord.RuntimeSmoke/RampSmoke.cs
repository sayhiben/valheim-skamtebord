using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Skamtebord.RuntimeSmoke;

// Real collider tests. Velocity is seeded only on the flat approach, then the
// ordinary owner physics supplies ramp contact, takeoff, gravity and landing.
internal static class RampSmoke
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private const float Rise = 1.25f;
    private static readonly float RunLength = Rise / Mathf.Tan(20f * Mathf.Deg2Rad);
    private static object Call(object target, string method, params object[] args) => AccessTools.Method(target.GetType(), method).Invoke(target, args);
    private static T Get<T>(object target, string property) => (T)AccessTools.Property(target.GetType(), property).GetValue(target);

    internal static void CreatePlayCourse(Vector3 origin)
    {
        if (EnvMan.instance)
        {
            EnvMan.instance.m_debugTimeOfDay = true;
            EnvMan.instance.m_debugTime = .42f;
            EnvMan.instance.m_debugEnv = "Clear";
            EnvMan.instance.ForceInstantEnvironmentSwitch();
        }
        CreateRamp(origin + new Vector3(0, 0, 12), RunLength, false);
        CreateRamp(origin + new Vector3(-14, 0, 12), Rise / Mathf.Tan(35f * Mathf.Deg2Rad), false);
        CreateRamp(origin + new Vector3(14, 0, 12), RunLength, true);
    }

    internal static GameObject CreateRamp(Vector3 origin, float length, bool crest)
    {
        var points = crest ? new[] { new Vector2(0, 0), new Vector2(length, Rise), new Vector2(length + 1.5f, .3f), new Vector2(length + 4, 0) }
            : new[] { new Vector2(0, 0), new Vector2(length, Rise) };
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        // Closed geometry fixes the old top sheet's backface disappearance.
        const float bottom = -.12f;
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int start = vertices.Count;
            vertices.AddRange(new[] { a, b, c, d });
            triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
        }
        for (int i = 1; i < points.Length; i++)
        {
            var a = points[i-1]; var b = points[i];
            Vector3 la = new Vector3(-5,a.y,a.x), ra = new Vector3(5,a.y,a.x);
            Vector3 lb = new Vector3(-5,b.y,b.x), rb = new Vector3(5,b.y,b.x);
            Vector3 ba = new Vector3(-5,bottom,a.x), bra = new Vector3(5,bottom,a.x);
            Vector3 bb = new Vector3(-5,bottom,b.x), brb = new Vector3(5,bottom,b.x);
            Quad(la,lb,rb,ra); Quad(ba,bra,brb,bb);
            Quad(ba,bb,lb,la); Quad(bra,ra,rb,brb);
        }
        var first = points[0]; var last = points[points.Length-1];
        Quad(new Vector3(-5,bottom,first.x),new Vector3(-5,first.y,first.x),new Vector3(5,first.y,first.x),new Vector3(5,bottom,first.x));
        Quad(new Vector3(-5,bottom,last.x),new Vector3(5,bottom,last.x),new Vector3(5,last.y,last.x),new Vector3(-5,last.y,last.x));
        var mesh = new Mesh { name = crest ? "Terrain crest fixture" : "Kicker fixture" };
        mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        var ramp = new GameObject(mesh.name);
        ramp.layer = LayerMask.NameToLayer("Default");
        ramp.transform.position = origin;
        ramp.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = ramp.AddComponent<MeshRenderer>();
        renderer.allowOcclusionWhenDynamic = false;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;
        renderer.sharedMaterial = QaBootstrap.FixtureMaterial(crest ? new Color(.35f, .48f, .32f) : new Color(.68f, .39f, .16f));
        ramp.AddComponent<MeshCollider>().sharedMesh = mesh;
        Physics.SyncTransforms();
        return ramp;
    }

    private sealed class Take
    {
        internal string Name;
        internal float Speed, JumpZ = float.PositiveInfinity, Delay = -1;
        internal bool Crest, Repeat, AirPush;
        internal float Angle = 20;
        internal bool Airborne, Landed, Riding, JumpSent;
        internal float Apex, AirTime, LipSpeed, LipUp, ExitSpeed, ExitUp, JumpUpBefore, JumpUpAfter;
    }

    internal static IEnumerator Run(Player player, Component rider, GameObject platform, string root, Action<bool, string> check, Action<string> log)
    {
        var body = player.GetComponent<Rigidbody>();
        Vector3 origin = platform.transform.position + Vector3.up;
        var cases = new[] {
            new Take { Name = "roll-slow", Speed = 10 },
            new Take { Name = "roll-fast", Speed = 16 },
            new Take { Name = "jump-early", Speed = 12, JumpZ = -3f },
            new Take { Name = "jump-lip", Speed = 12, JumpZ = RunLength - .65f },
            new Take { Name = "jump-grace", Speed = 12, Delay = .06f },
            new Take { Name = "jump-too-late", Speed = 12, Delay = .24f },
            new Take { Name = "jump-repeat", Speed = 12, JumpZ = RunLength - .65f, Repeat = true },
            new Take { Name = "terrain-crest", Speed = 16, Crest = true },
            new Take { Name = "roll-normal", Speed = 12 },
            new Take { Name = "steep-ramp", Speed = 12, Angle = 35 },
            new Take { Name = "air-push", Speed = 12, AirPush = true }
        };
        bool record = Environment.GetCommandLineArgs().Contains("-skamtebord-ramp-capture");
        using var recorder = record ? new RampRecorder(origin, root) : null;
        using (var summary = new StreamWriter(Path.Combine(root, "ramps.csv")))
        {
            summary.WriteLine("case,approach_mps,lip_horizontal,lip_up,air_horizontal,air_up,apex_above_floor,airtime,landed,riding,jump_up_before,jump_up_after");
            foreach (Take take in cases)
            {
                if (Get<bool>(rider, "Riding")) Call(rider, "Dismount", false);
                var ramp = CreateRamp(origin, Rise / Mathf.Tan(take.Angle * Mathf.Deg2Rad), take.Crest);
                body.position = origin + new Vector3(0, 1, -8);
                player.transform.position = body.position;
                body.rotation = player.transform.rotation = Quaternion.identity;
                player.ForceJump(Vector3.zero, false);
                Physics.SyncTransforms();
                float deadline = Time.realtimeSinceStartup + 12f;
                while ((!player.IsOnGround() || !player.CanMove() || Mathf.Abs(body.linearVelocity.y) > .5f) && Time.realtimeSinceStartup < deadline)
                    yield return new WaitForFixedUpdate();
                check(player.IsOnGround() && player.CanMove(), take.Name + " starts grounded and movable");
                player.AddStamina(100f);
                Call(rider, "Toggle");
                check(Get<bool>(rider, "Riding"), take.Name + " mounts on approach");
                body.linearVelocity = Vector3.forward * take.Speed;
                recorder?.StartTake(take.Name);
                float begin = Time.time, airStart = 0, lastContactTime = Time.time, jumpSentTime = 0;
                bool jumpPending = false;
                using (var csv = new StreamWriter(Path.Combine(root, "ramp-" + take.Name + ".csv")))
                {
                    csv.WriteLine("time,z,height,vz,vy,grounded,contact_age,jump,riding");
                    while (Time.time - begin < 4.5f)
                    {
                        float z = body.position.z - origin.z;
                        float age = (float)AccessTools.Field(typeof(Character), "m_lastGroundTouch").GetValue(player);
                        // UpdateMotion increments contact age after Step, so one fixed delta
                        // here means an actual collision was consumed this tick.
                        bool touching = age <= Time.fixedDeltaTime + .001f;
                        if (touching) lastContactTime = Time.time;
                        if (z > 1 && touching && !take.Airborne)
                        {
                            take.LipSpeed = new Vector2(body.linearVelocity.x, body.linearVelocity.z).magnitude;
                            take.LipUp = body.linearVelocity.y;
                        }
                        if (!touching && !take.Airborne)
                        {
                            take.Airborne = true; airStart = Time.time;
                            take.ExitSpeed = new Vector2(body.linearVelocity.x, body.linearVelocity.z).magnitude;
                            take.ExitUp = body.linearVelocity.y;
                        }
                        if (jumpPending) { take.JumpUpAfter = body.linearVelocity.y; jumpPending = false; }
                        bool jump = !take.JumpSent && (z >= take.JumpZ || (take.Delay >= 0 && z > RunLength && !touching && Time.time - lastContactTime >= take.Delay));
                        if (jump) { take.JumpSent = true; jumpSentTime = Time.time; take.JumpUpBefore = body.linearVelocity.y; jumpPending = true; }
                        if (take.Repeat && take.JumpSent && !touching && Time.time - jumpSentTime < .3f) jump = true;
                        Call(rider, "CaptureControls", take.AirPush && take.Airborne ? Vector3.forward : Vector3.zero, jump);
                        take.Apex = Mathf.Max(take.Apex, body.position.y - origin.y);
                        csv.WriteLine(string.Join(",", (Time.time-begin).ToString("F3", Invariant), z.ToString("F3", Invariant),
                            (body.position.y-origin.y).ToString("F3", Invariant), body.linearVelocity.z.ToString("F3", Invariant),
                            body.linearVelocity.y.ToString("F3", Invariant), Get<bool>(rider,"Grounded"), age.ToString("F3", Invariant), jump, Get<bool>(rider,"Riding")));
                        if (take.Airborne && touching && Time.time - airStart > .12f)
                        {
                            take.Landed = true; take.AirTime = Time.time - airStart;
                            yield return new WaitForFixedUpdate();
                            break;
                        }
                        yield return new WaitForFixedUpdate();
                    }
                }
                take.Riding = Get<bool>(rider, "Riding");
                recorder?.StopTake();
                summary.WriteLine(string.Join(",", take.Name, take.Speed.ToString(Invariant), take.LipSpeed.ToString("F3",Invariant),
                    take.LipUp.ToString("F3",Invariant), take.ExitSpeed.ToString("F3",Invariant), take.ExitUp.ToString("F3",Invariant),
                    take.Apex.ToString("F3",Invariant), take.AirTime.ToString("F3",Invariant), take.Landed, take.Riding,
                    take.JumpUpBefore.ToString("F3",Invariant), take.JumpUpAfter.ToString("F3",Invariant)));
                summary.Flush();
                log($"{take.Name}: lip=({take.LipSpeed:F2}h,{take.LipUp:F2}v), air=({take.ExitSpeed:F2}h,{take.ExitUp:F2}v), apex={take.Apex:F2}m, time={take.AirTime:F2}s, landed={take.Landed}, riding={take.Riding}, jump={take.JumpUpBefore:F2}->{take.JumpUpAfter:F2}");
                Call(rider, "Dismount", false);
                UnityEngine.Object.Destroy(ramp);
                yield return new WaitForFixedUpdate();
            }
        }
        // Compare whole flights, not a fabricated expected velocity assigned at the lip.
        check(cases[0].Airborne && cases[1].Airborne && cases[1].Apex > cases[0].Apex + .3f, "more approach momentum gives more ramp air without jumping");
        check(cases[1].ExitSpeed > cases[1].LipSpeed * .94f && cases[1].ExitUp > 1f, "roll-off preserves horizontal speed and ramp-generated lift");
        check(cases[3].Apex > cases[2].Apex + .5f, "jump near the lip gains more height than jumping early");
        check(cases[3].JumpUpAfter > cases[3].JumpUpBefore + 4.5f, "ollie adds upward impulse to existing ramp momentum");
        check(cases[4].JumpUpAfter > cases[4].JumpUpBefore + 4.5f, "jump just beyond the lip uses the short grace window");
        check(cases[5].JumpUpAfter < cases[5].JumpUpBefore + .1f, "late airborne jump cannot add an impulse");
        check(Mathf.Abs(cases[6].Apex - cases[3].Apex) < .2f, "repeated airborne jump requests do not stack boosts");
        check(cases[7].Airborne && cases[7].ExitUp > 1f, "terrain-shaped crest launches from its collision surface");
        check(cases[3].Landed && cases[3].Riding, "timed ramp ollie lands and keeps skating");
        check(cases[8].Landed && cases[3].Apex > cases[8].Apex + 1f, "timed ollie gets more air than rolling off at the same approach speed");
        check(cases[9].Airborne && cases[9].ExitUp > cases[8].ExitUp, "steeper collision surface redirects more momentum upward");
        check(Mathf.Abs(cases[10].Apex - cases[8].Apex) < .08f && Mathf.Abs(cases[10].AirTime - cases[8].AirTime) < .05f,
            "holding push in the air does not extend the flight");
        check(cases[4].JumpSent && cases[5].JumpSent, "grace and expired-grace trials actually submit their jump requests");
    }
}

// Offscreen GPU captures avoid hidden-window swapchain limitations. These are
// game-rendered frames of the same tests, without HUD or synthetic movement.
internal sealed class RampRecorder : IDisposable
{
    private readonly Camera camera;
    private readonly RenderTexture target;
    private readonly Texture2D pixels;
    private readonly RampFrameWriter writer;
    private readonly GameObject lightObject;
    private readonly string root;
    private readonly int previousRate;
    private string directory;
    private int frame;

    internal RampRecorder(Vector3 origin, string output)
    {
        root = output;
        previousRate = Time.captureFramerate;
        Time.captureFramerate = 30;
        if (EnvMan.instance)
        {
            EnvMan.instance.m_debugTimeOfDay = true;
            EnvMan.instance.m_debugTime = .42f;
            EnvMan.instance.m_debugEnv = "Clear";
            EnvMan.instance.ForceInstantEnvironmentSwitch();
        }
        camera = new GameObject("Ramp QA camera").AddComponent<Camera>();
        camera.enabled = false;
        camera.transform.position = origin + new Vector3(12, 5, 4);
        camera.transform.LookAt(origin + new Vector3(0, 1, 4));
        camera.fieldOfView = 50; camera.nearClipPlane = .1f; camera.farClipPlane = 400;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.50f,.68f,.74f);
        camera.cullingMask = ~LayerMask.GetMask("UI", "Invisible");
        camera.allowHDR = false; camera.renderingPath = RenderingPath.Forward;
        target = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32);
        target.Create(); camera.targetTexture = target;
        pixels = new Texture2D(960, 540, TextureFormat.RGB24, false);
        lightObject = new GameObject("Ramp QA light");
        var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.2f;
        light.transform.rotation = Quaternion.Euler(45, -30, 0);
        writer = camera.gameObject.AddComponent<RampFrameWriter>();
        writer.Capture = Capture;
    }

    internal void StartTake(string name)
    {
        if (name != "roll-normal" && name != "jump-lip" && name != "jump-early") return;
        directory = Path.Combine(root, "ramp-video", name);
        Directory.CreateDirectory(directory); frame = 0;
    }
    internal void StopTake() { directory = null; }
    private void Capture()
    {
        if (directory == null) return;
        RenderSettings.fog = false;
        RenderSettings.ambientLight = new Color(.65f,.65f,.65f);
        var previous = RenderTexture.active;
        try
        {
            camera.Render(); RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0,0,960,540),0,0); pixels.Apply();
            File.WriteAllBytes(Path.Combine(directory,$"frame_{frame++:D5}.png"),pixels.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; }
    }
    public void Dispose()
    {
        writer.Capture = null;
        Time.captureFramerate = previousRate;
        camera.targetTexture = null;
        target.Release();
        UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(pixels);
        UnityEngine.Object.Destroy(camera.gameObject); UnityEngine.Object.Destroy(lightObject);
    }
}

internal sealed class RampFrameWriter : MonoBehaviour
{
    internal Action Capture;
    private void LateUpdate() => Capture?.Invoke();
}
