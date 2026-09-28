using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Skamtebord.RuntimeSmoke;

// Real collision geometry and the ordinary owner physics update. No fixture
// tags, support overrides, or velocity injection after the initial approach.
internal static class CarvingSmoke
{
    private static object Call(object target,string name,params object[] args) => AccessTools.Method(target.GetType(),name).Invoke(target,args);
    private static T Get<T>(object target,string name) => (T)AccessTools.Property(target.GetType(),name).GetValue(target);

    private static GameObject Bowl(Vector3 origin)
    {
        var root=new GameObject("Untagged rounded halfpipe corner"); root.transform.position=origin;
        // A circular flat surrounded by a quarter-pipe. Curvature changes both
        // pitch and yaw, unlike the straight halfpipe/slope regression fixtures.
        const int rings=64, sectors=192;
        var vertices=new List<Vector3>(); var triangles=new List<int>();
        vertices.Add(Vector3.zero);
        for(int ring=0;ring<=rings;ring++)
        {
            float angle=ring*Mathf.PI*.5f/rings, radius=8+5*Mathf.Sin(angle), y=5*(1-Mathf.Cos(angle));
            for(int sector=0;sector<=sectors;sector++)
            {
                float theta=sector*2*Mathf.PI/sectors;
                vertices.Add(new Vector3(radius*Mathf.Cos(theta),y,radius*Mathf.Sin(theta)));
            }
        }
        for(int sector=0;sector<sectors;sector++) triangles.AddRange(new[] {0,2+sector,1+sector});
        for(int ring=0;ring<rings;ring++) for(int sector=0;sector<sectors;sector++)
        {
            int a=1+ring*(sectors+1)+sector, b=a+1, c=b+sectors+1, d=a+sectors+1;
            triangles.AddRange(new[] {a,b,c,a,c,d});
        }
        var mesh=new Mesh(); mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
        root.AddComponent<MeshCollider>().sharedMesh=mesh;
        Physics.SyncTransforms(); return root;
    }

    internal static IEnumerator Run(Player player,Component rider,GameObject platform,string root,Action<bool,string> check,Action<string> log)
    {
        var body=player.GetComponent<Rigidbody>();
        Vector3 floor=platform.transform.position+Vector3.up;
        object settings=Get<object>(rider,"Settings");
        var governor=(ConfigEntry<float>)AccessTools.Field(settings.GetType(),"MaximumSpeed").GetValue(settings);
        float oldLimit=governor.Value;
        try
        {
            foreach(bool sprint in new[] {false,true}) foreach(bool turn in new[] {false,true})
            {
                var reset=SurfaceSmoke.Reset(player,rider,floor+new Vector3(0,.5f,-55)); while(reset.MoveNext()) yield return reset.Current;
                float cap=sprint ? 14 : 9, peak=0, start=Time.time;
                while(Time.time-start<7)
                {
                    player.AddStamina(100);
                    Call(rider,"CaptureSprint",sprint);
                    Call(rider,"CaptureControls",new Vector3(turn ? 1 : 0,0,1),false);
                    yield return new WaitForFixedUpdate();
                    peak=Mathf.Max(peak,Vector3.ProjectOnPlane(body.linearVelocity,Vector3.up).magnitude);
                }
                log($"PUSH sprint={sprint} turning={turn} peak={peak:F4} final={body.linearVelocity.magnitude:F4}");
                check(Get<bool>(rider,"Riding") && peak<=cap+.015f && peak>cap-.15f,$"{(sprint ? "sprint" : "push")} {(turn ? "turning" : "straight")} reaches its {cap} m/s limit without overshooting");
            }

            // Above both push caps, turning with W held must preserve coast
            // momentum without letting the foot replenish or increase it.
            foreach(float seed in new[] {20f,32f})
            {
                governor.Value=0;
                var reset=SurfaceSmoke.Reset(player,rider,floor+new Vector3(0,.5f,-30)); while(reset.MoveNext()) yield return reset.Current;
                body.linearVelocity=Vector3.forward*seed;
                float start=Time.time, peak=seed;
                while(Time.time-start<2)
                {
                    player.AddStamina(100); Call(rider,"CaptureSprint",true);
                    Call(rider,"CaptureControls",new Vector3(1,0,1),false);
                    yield return new WaitForFixedUpdate(); peak=Mathf.Max(peak,body.linearVelocity.magnitude);
                }
                float final=body.linearVelocity.magnitude;
                log($"FAST_CARVE initial={seed:F3} peak={peak:F3} final={final:F3}");
                check(Get<bool>(rider,"Riding") && peak<=seed+.05f && final<seed-.4f,"turning above both push caps adds no speed even with sprint held at " + seed + " m/s");
                check(final>seed-1.2f,"unpowered carve retains momentum apart from rolling resistance at " + seed + " m/s");
            }

            Vector3 origin=floor+Vector3.up*.28f;
            var points=new List<Vector2> {new Vector2(-10,60),new Vector2(0,60)};
            for(int i=1;i<=48;i++) {float a=i*40f/48*Mathf.Deg2Rad;points.Add(new Vector2(16*Mathf.Sin(a),60-16*(1-Mathf.Cos(a))));}
            Vector2 last=points[points.Count-1];points.Add(last+new Vector2(55,-55*Mathf.Tan(40*Mathf.Deg2Rad)));
            var hill=SurfaceSmoke.Profile(origin,points,true);
            float capped=0,uncapped=0;
            foreach(float limit in new[] {25f,0f})
            {
                governor.Value=limit;
                var reset=SurfaceSmoke.Reset(player,rider,origin+new Vector3(0,60.5f,-2)); while(reset.MoveNext()) yield return reset.Current;
                body.linearVelocity=Vector3.forward*4;
                float start=Time.time,peak=4;
                while(Time.time-start<14 && body.position.z-origin.z<last.x+50)
                {
                    yield return new WaitForFixedUpdate();peak=Mathf.Max(peak,body.linearVelocity.magnitude);
                }
                log($"HILL limit={limit:F1} peak={peak:F3} position={body.position-origin} riding={Get<bool>(rider,"Riding")}");
                check(Get<bool>(rider,"Riding") && body.position.z-origin.z>last.x+49 && peak>23,"unpowered descent exceeds both push limits with terrain governor " + limit);
                if(limit>0) capped=peak;else uncapped=peak;
                Call(rider,"Dismount",false);
            }
            // A soft governor balances continuing downslope gravity, so its
            // terminal speed is above the configured onset (Valheim gravity
            // is stronger than Unity's default Earth-gravity value).
            float terminal=25+(Physics.gravity.magnitude*Mathf.Sin(40*Mathf.Deg2Rad)-.32f)/3;
            check(capped<terminal+.5f && uncapped>29 && uncapped>capped+3,"optional terrain governor controls supported speed and 0 disables it");
            UnityEngine.Object.Destroy(hill);yield return new WaitForFixedUpdate();

            governor.Value=25;
            var airReset=SurfaceSmoke.Reset(player,rider,floor+new Vector3(0,.5f,-30)); while(airReset.MoveNext()) yield return airReset.Current;
            body.linearVelocity=Vector3.forward*32;
            Call(rider,"CaptureControls",Vector3.zero,true);yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();
            Vector3 launch=body.linearVelocity;float airStart=Time.time;
            while(Time.time-airStart<.3f) yield return new WaitForFixedUpdate();
            log($"FAST_AIR launch={launch} after={body.linearVelocity}");
            check(!Get<bool>(rider,"Grounded") && Mathf.Abs(body.linearVelocity.z-launch.z)<.03f && launch.z>30,"terrain governor does not drag an airborne board above 25 m/s");
            check(Mathf.Abs(body.linearVelocity.y-launch.y-Physics.gravity.y*(Time.time-airStart))<.3f,"high-speed flight still follows ordinary gravity");

            var bowl=Bowl(origin);
            foreach(int sign in new[] {1,-1})
            {
                var reset=SurfaceSmoke.Reset(player,rider,origin+new Vector3(sign*6,.5f,0)); while(reset.MoveNext()) yield return reset.Current;
                body.linearVelocity=Vector3.forward*17;
                float start=Time.time,peakBank=0,gap=0,maxGap=0,minPoseDot=1,peakEnergy=0,sweep=0;
                int bankFrames=0;float startEnergy=body.linearVelocity.sqrMagnitude*.5f-Physics.gravity.y*(body.position.y-origin.y);
                using(var csv=new StreamWriter(Path.Combine(root,"corner-"+sign+".csv")))
                {
                    csv.WriteLine("time,x,y,z,vx,vy,vz,supported,nx,ny,nz,sweep,steer");
                    while(Time.time-start<5 && sweep<110 && Get<bool>(rider,"Riding"))
                    {
                        Vector3 p=body.position-origin,radial=new Vector3(p.x,0,p.z).normalized;
                        Vector3 normal=Get<Vector3>(rider,"SurfaceNormal");
                        Vector3 direction=Vector3.Cross(radial,Vector3.up)*sign+radial*Mathf.Clamp((10.7f-new Vector2(p.x,p.z).magnitude)*.8f,-1,1);
                        direction=Vector3.ProjectOnPlane(direction,normal).normalized;
                        float steer=Mathf.Clamp(Vector3.SignedAngle(body.rotation*Vector3.forward,direction,normal)/25f,-1,1);
                        Call(rider,"CaptureControls",new Vector3(steer,0,0),false);
                        yield return new WaitForFixedUpdate();
                        p=body.position-origin;Vector3 v=body.linearVelocity;normal=Get<Vector3>(rider,"SurfaceNormal");
                        bool supported=Get<bool>(rider,"Grounded");
                        sweep=sign==1 ? Mathf.Atan2(p.z,p.x)*Mathf.Rad2Deg : Mathf.Atan2(p.z,-p.x)*Mathf.Rad2Deg;
                        float bank=Vector3.Angle(normal,Vector3.up);peakBank=Mathf.Max(peakBank,bank);
                        if(supported && bank>20) {bankFrames++;minPoseDot=Mathf.Min(minPoseDot,Vector3.Dot(body.rotation*Vector3.up,normal));}
                        gap=supported ? 0 : gap+Time.fixedDeltaTime;maxGap=Mathf.Max(maxGap,gap);
                        peakEnergy=Mathf.Max(peakEnergy,v.sqrMagnitude*.5f-Physics.gravity.y*p.y);
                        csv.WriteLine(FormattableString.Invariant($"{Time.time-start:F3},{p.x:F3},{p.y:F3},{p.z:F3},{v.x:F3},{v.y:F3},{v.z:F3},{supported},{normal.x:F3},{normal.y:F3},{normal.z:F3},{sweep:F3},{steer:F3}"));
                    }
                }
                log($"CORNER sign={sign} sweep={sweep:F2} bank={peakBank:F2} frames={bankFrames} gap={maxGap:F3} poseDot={minPoseDot:F4} energy={startEnergy:F3}->{peakEnergy:F3} speed={body.linearVelocity.magnitude:F3} position={body.position-origin}");
                check(Get<bool>(rider,"Riding") && sweep>100,"coasts around more than a 90-degree rounded corner, direction " + sign);
                check(peakBank>25 && bankFrames>=20 && minPoseDot>.95f,"rider banks with changing surface normals through the corner, direction " + sign);
                check(maxGap<=.1f,"rounded corner maintains continuous skating support, direction " + sign);
                check(peakEnergy<=startEnergy+5 && body.linearVelocity.magnitude>10,"banked steering preserves terrain momentum without creating energy, direction " + sign);
                Call(rider,"Dismount",false);
            }
            UnityEngine.Object.Destroy(bowl);
        }
        finally { governor.Value=oldLimit; if(Get<bool>(rider,"Riding")) Call(rider,"Dismount",false); }
        check(SaveSystem.HasSessionFlag(SaveSystemSessionFlags.DontSaveAnything),"carving tests retain save isolation");
    }
}
