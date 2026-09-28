using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Skamtebord.RuntimeSmoke;

internal static class TerrainSmoke
{
    private static object Call(object target,string name,params object[] args) => AccessTools.Method(target.GetType(),name).Invoke(target,args);
    private static T Get<T>(object target,string name) => (T)AccessTools.Property(target.GetType(),name).GetValue(target);
    private static T Field<T>(object target,string name) => (T)AccessTools.Field(target.GetType(),name).GetValue(target);

    private static GameObject RoughHill(Vector3 origin,float angle)
    {
        var root=new GameObject("Rough triangulated hill");root.transform.position=origin;
        var vertices=new List<Vector3>();var triangles=new List<int>();
        const int across=20,length=110;const float spacing=.4f;
        for(int z=0;z<=length;z++)for(int x=0;x<=across;x++)
        {
            float xx=(x-across/2)*spacing,zz=z*spacing-6;
            float blend=Mathf.Clamp01(zz/2);
            float height=Mathf.Max(0,zz)*Mathf.Tan(angle*Mathf.Deg2Rad)
                +blend*(.055f*Mathf.Sin(zz*5.3f)+.035f*Mathf.Sin(xx*3.1f+zz*2.7f)+xx*.025f*Mathf.Sin(zz*1.9f));
            vertices.Add(new Vector3(xx,height,zz));
        }
        for(int z=0;z<length;z++)for(int x=0;x<across;x++)
        {
            int a=z*(across+1)+x,b=a+1,c=b+across+1,d=a+across+1;
            triangles.AddRange(new[] {a,d,c,a,c,b});
        }
        var mesh=new Mesh();mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
        root.AddComponent<MeshCollider>().sharedMesh=mesh;
        root.AddComponent<MeshFilter>().sharedMesh=mesh;
        root.AddComponent<MeshRenderer>().sharedMaterial=QaBootstrap.FixtureMaterial(new Color(.4f,.48f,.29f));
        Physics.SyncTransforms();return root;
    }

    internal static IEnumerator Run(Player player,Component rider,GameObject platform,string root,Action<bool,string> check,Action<string> report)
    {
        Action<string> log=message=>{File.AppendAllText(Path.Combine(root,"terrain-metrics.txt"),message+Environment.NewLine);report(message);};
        var body=player.GetComponent<Rigidbody>();Vector3 floor=platform.transform.position+Vector3.up;
        var original=Utils.GetMainCamera();original.enabled=false;
        var gameCamera=original.GetComponent<GameCamera>();if(gameCamera)gameCamera.enabled=false;
        var camera=new GameObject("Terrain QA camera").AddComponent<Camera>();camera.tag="MainCamera";
        camera.renderingPath=RenderingPath.Forward;camera.nearClipPlane=.05f;camera.farClipPlane=600;
        Screen.SetResolution(1280,720,FullScreenMode.Windowed);yield return new WaitForSeconds(.15f);
        Screen.SetResolution(1600,900,FullScreenMode.Windowed);yield return new WaitForSeconds(.3f);
        if(EnvMan.instance){EnvMan.instance.m_debugTimeOfDay=true;EnvMan.instance.m_debugTime=.4f;EnvMan.instance.m_debugEnv="Clear";EnvMan.instance.ForceInstantEnvironmentSwitch();}
        void CameraAtPlayer(){camera.transform.position=body.position+new Vector3(5,2.5f,-5);camera.transform.LookAt(body.position+Vector3.up);}
        foreach(float angle in new[] {0f,25f,35f})
        {
            Vector3 origin=floor+Vector3.up*.3f;
            var hill=RoughHill(origin,angle);
            var reset=SurfaceSmoke.Reset(player,rider,origin+new Vector3(0,.6f,-4));while(reset.MoveNext())yield return reset.Current;
            float start=Time.time,gap=0,maxGap=0,peakSpeed=0,rawChange=0,frameChange=0,maxSide=0;int samples=0;
            Vector3 lastRaw=Vector3.up,lastFrame=Vector3.up;
            using(var csv=new StreamWriter(Path.Combine(root,"rough-"+angle+".csv")))
            {
                csv.WriteLine("time,x,y,z,speed,supported,raw_angle,filtered_angle");
                while(Time.time-start<9 && body.position.z-origin.z<22 && Get<bool>(rider,"Riding"))
                {
                    player.AddStamina(100);Call(rider,"CaptureControls",Vector3.forward,false);
                    CameraAtPlayer();yield return new WaitForFixedUpdate();
                    Vector3 p=body.position-origin,n=Get<Vector3>(rider,"SurfaceNormal"),raw=Field<Vector3>(rider,"contactNormal");
                    bool supported=Get<bool>(rider,"Grounded");gap=supported?0:gap+Time.fixedDeltaTime;maxGap=Mathf.Max(maxGap,gap);
                    peakSpeed=Mathf.Max(peakSpeed,body.linearVelocity.magnitude);maxSide=Mathf.Max(maxSide,Mathf.Abs(p.x));
                    float rawDelta=Vector3.Angle(lastRaw,raw),frameDelta=Vector3.Angle(lastFrame,n);
                    if(supported && p.z>2){rawChange+=rawDelta*rawDelta;frameChange+=frameDelta*frameDelta;samples++;}
                    lastRaw=raw;lastFrame=n;
                    csv.WriteLine(FormattableString.Invariant($"{Time.time-start:F3},{p.x:F3},{p.y:F3},{p.z:F3},{body.linearVelocity.magnitude:F3},{supported},{rawDelta:F3},{frameDelta:F3}"));
                }
            }
            log($"ROUGH angle={angle} distance={body.position.z-origin.z:F2} y={body.position.y-origin.y:F2} lateral={maxSide:F2} gap={maxGap:F3} peakSpeed={peakSpeed:F3} normalRms={Mathf.Sqrt(rawChange/Mathf.Max(1,samples)):F3} filteredRms={Mathf.Sqrt(frameChange/Mathf.Max(1,samples)):F3} riding={Get<bool>(rider,"Riding")}");
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(root,"rough-hill-"+angle+".png"));
            check(Get<bool>(rider,"Riding") && body.position.z-origin.z>20,"pushes over rough triangulated ground/uphill without dismounting, grade="+angle);
            check(maxSide<2,"changing ground facets do not steer the board off its route, grade="+angle);
            check(samples>30 && frameChange<rawChange*.85f,"riding-frame angular jitter is reduced across rough facets, grade="+angle);
            check(maxGap<.3f,"rough ground avoids prolonged unsupported motion, grade="+angle);
            Call(rider,"Dismount",false);UnityEngine.Object.Destroy(hill);yield return new WaitForFixedUpdate();
        }

        // Assistance must require both an intentional push and stamina. Use
        // one identical incline and zero initial speed for each comparison.
        var assist=Field<ConfigEntry<float>>(Get<object>(rider,"Settings"),"UphillAssistance");
        float oldAssist=assist.Value;
        Vector3 gateOrigin=floor+Vector3.up*.3f;
        var gateSlope=SurfaceSmoke.Profile(gateOrigin,new List<Vector2>{new Vector2(-10,0),Vector2.zero,new Vector2(25,25*Mathf.Tan(35*Mathf.Deg2Rad))},false);
        try
        {
            foreach(int trial in new[] {0,1,2,3})
            {
                assist.Value=trial==2 ? 0 : oldAssist;
                var reset=SurfaceSmoke.Reset(player,rider,gateOrigin+new Vector3(0,8*Mathf.Tan(35*Mathf.Deg2Rad)+.4f,8));
                while(reset.MoveNext())yield return reset.Current;
                body.linearVelocity=Vector3.zero;float z=body.position.z,start=Time.time;
                while(Time.time-start<1.2f)
                {
                    if(trial==1)player.UseStamina(1000);else player.AddStamina(100);
                    Call(rider,"CaptureControls",trial==0 ? Vector3.zero : Vector3.forward,false);
                    yield return new WaitForFixedUpdate();
                }
                float distance=body.position.z-z;
                log($"ASSIST trial={trial} distance={distance:F3} riding={Get<bool>(rider,"Riding")}");
                check(Get<bool>(rider,"Riding") && (trial==3 ? distance>.5f : distance<-.3f),trial==3
                    ? "powered uphill start succeeds with stamina"
                    : "uphill assistance stays inactive without input, stamina, or enabled setting, trial="+trial);
                Call(rider,"Dismount",false);
            }
        }
        finally {assist.Value=oldAssist;UnityEngine.Object.Destroy(gateSlope);}
        yield return new WaitForFixedUpdate();

        // Observe the real Humanoid pose on a real incline; freeze only position
        // after settling to compare the two animation settings at one location.
        Vector3 poseOrigin=floor+new Vector3(0,.3f,0);
        var slope=SurfaceSmoke.Profile(poseOrigin,new List<Vector2>{new Vector2(-10,0),Vector2.zero,new Vector2(25,25*Mathf.Tan(30*Mathf.Deg2Rad))},false);
        var poseReset=SurfaceSmoke.Reset(player,rider,poseOrigin+new Vector3(0,5*Mathf.Tan(30*Mathf.Deg2Rad)+.5f,5));while(poseReset.MoveNext())yield return poseReset.Current;
        var constraint=body.constraints;body.constraints=RigidbodyConstraints.FreezePosition;
        var strength=Field<ConfigEntry<float>>(Get<object>(rider,"Settings"),"BalanceStrength");float oldStrength=strength.Value;
        var animator=player.GetComponentInChildren<Animator>();
        var left=animator.GetBoneTransform(HumanBodyBones.LeftFoot);var right=animator.GetBoneTransform(HumanBodyBones.RightFoot);
        var spine=animator.GetBoneTransform(HumanBodyBones.Spine);var neck=animator.GetBoneTransform(HumanBodyBones.Neck);
        try
        {
            strength.Value=0;yield return new WaitForSeconds(.5f);CameraAtPlayer();yield return new WaitForEndOfFrame();
            Vector3 footBefore=player.transform.InverseTransformPoint(left.position),rearBefore=player.transform.InverseTransformPoint(right.position);
            float before=Vector3.Angle(neck.position-spine.position,Vector3.up);
            ScreenCapture.CaptureScreenshot(Path.Combine(root,"balance-off.png"));
            strength.Value=1;yield return new WaitForSeconds(.5f);yield return new WaitForEndOfFrame();
            float after=Vector3.Angle(neck.position-spine.position,Vector3.up);
            float footError=Mathf.Max(Vector3.Distance(footBefore,player.transform.InverseTransformPoint(left.position)),Vector3.Distance(rearBefore,player.transform.InverseTransformPoint(right.position)));
            log($"BALANCE tilt={Vector3.Angle(player.transform.up,Vector3.up):F2} torso={before:F2}->{after:F2} footError={footError:F4}");
            ScreenCapture.CaptureScreenshot(Path.Combine(root,"balance-on.png"));
            check(before>20 && after<before-12,"torso actively counterleans toward vertical on a 30-degree slope");
            check(footError<.045f,"leg compensation keeps both animated feet on the board");
        }
        finally {strength.Value=oldStrength;body.constraints=constraint;Call(rider,"Dismount",false);UnityEngine.Object.Destroy(slope);}
        yield return new WaitForFixedUpdate();

        foreach(int trial in new[] {0,1,2,3})
        {
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);
            bool low=trial==3;float speed=trial==0?8:14,yaw=trial==1?50:0;
            wall.transform.position=floor+new Vector3(0,low?.15f:3,3);
            wall.transform.localScale=new Vector3(40,low?.3f:6,.4f);Physics.SyncTransforms();
            var reset=SurfaceSmoke.Reset(player,rider,floor+new Vector3(0,.5f,-4),yaw);while(reset.MoveNext())yield return reset.Current;
            body.linearVelocity=Quaternion.Euler(0,yaw,0)*Vector3.forward*speed;
            float start=Time.time;
            while(Time.time-start<1.6f && Get<bool>(rider,"Riding"))yield return new WaitForFixedUpdate();
            bool riding=Get<bool>(rider,"Riding");log($"OBSTACLE trial={trial} riding={riding} position={body.position-floor} speed={body.linearVelocity.magnitude:F2}");
            check(trial==2 ? !riding : riding,trial==2?"severe head-on impacts still bail":"moderate/glancing/low obstacle contact retains the ride, trial="+trial);
            if(trial==0)check(body.position.z-floor.z<3.1f,"forgiving collisions still block the rider instead of passing through obstacles");
            if(riding)Call(rider,"Dismount",false);UnityEngine.Object.Destroy(wall);yield return new WaitForFixedUpdate();
        }

        // Search loaded, untouched map terrain for a clear uphill line. No
        // height edits, seeded velocity or synthetic colliders on this run.
        int terrainMask=LayerMask.GetMask("terrain"),obstacleMask=LayerMask.GetMask("Default","static_solid","Default_small","piece");
        Vector3 bestStart=Vector3.zero,bestDirection=Vector3.zero;float bestScore=-1;
        for(int x=-28;x<=28;x+=8)for(int z=-28;z<=28;z+=8)foreach(Vector3 direction in new[] {Vector3.forward,Vector3.back,Vector3.right,Vector3.left})
        {
            Vector3 start=new Vector3(x,0,z);float first=0,last=0,variation=0;bool valid=true;
            for(int i=0;i<=15;i++)
            {
                Vector3 point=start+direction*(i*2);
                if(!Physics.Raycast(point+Vector3.up*200,Vector3.down,out var hit,190,terrainMask) || hit.normal.y<.72f){valid=false;break;}
                point=hit.point;
                if(Physics.CheckCapsule(point+Vector3.up*.5f,point+Vector3.up*1.5f,.4f,obstacleMask,QueryTriggerInteraction.Ignore)){valid=false;break;}
                if(i==0)first=point.y;else variation+=Mathf.Abs(point.y-last);
                last=point.y;
            }
            float gain=last-first;
            if(valid && gain>2 && gain<13 && variation>bestScore){bestScore=variation;bestStart=start+Vector3.up*first;bestDirection=direction;}
        }
        check(bestScore>0,"found a clear uneven uphill route on the generated map's actual Heightmap");
        log($"NATIVE_ROUTE start={bestStart} direction={bestDirection} heightVariation={bestScore:F2} seed=SkamtebordSmoke1 (unmodified terrain)");
        platform.SetActive(false);
        var nativeReset=SurfaceSmoke.Reset(player,rider,bestStart+Vector3.up*.6f,Quaternion.LookRotation(bestDirection).eulerAngles.y);while(nativeReset.MoveNext())yield return nativeReset.Current;
        log($"NATIVE_MOUNT position={body.position} velocity={body.linearVelocity} forward={player.transform.forward} normal={Field<Vector3>(rider,"contactNormal")}");
        float nativeStart=Time.time,progress=0,maxDeviation=0,nextFrame=0;int frame=0;
        Directory.CreateDirectory(Path.Combine(root,"native-frames"));
        using(var csv=new StreamWriter(Path.Combine(root,"native-terrain.csv")))
        {
            csv.WriteLine("time,x,y,z,speed,supported,progress,lateral,heading,rolling_direction");
            while(Time.time-nativeStart<12 && progress<26 && Get<bool>(rider,"Riding"))
            {
                player.AddStamina(100);Call(rider,"CaptureControls",Vector3.forward,false);CameraAtPlayer();
                yield return new WaitForFixedUpdate();Vector3 p=body.position-bestStart;
                progress=Vector3.Dot(p,bestDirection);float deviation=Mathf.Abs(Vector3.Dot(p,Vector3.Cross(bestDirection,Vector3.up)));
                maxDeviation=Mathf.Max(maxDeviation,deviation);
                if(Time.time-nativeStart>=nextFrame)
                {
                    ScreenCapture.CaptureScreenshot(Path.Combine(root,"native-frames",$"frame-{frame++:D4}.png"));
                    nextFrame=Time.time-nativeStart+.1f;
                }
                csv.WriteLine(FormattableString.Invariant($"{Time.time-nativeStart:F3},{body.position.x:F3},{body.position.y:F3},{body.position.z:F3},{body.linearVelocity.magnitude:F3},{Get<bool>(rider,"Grounded")},{progress:F3},{deviation:F3},{player.transform.eulerAngles.y:F1},{Field<float>(rider,"rollingDirection"):F0}"));
            }
        }
        yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(root,"native-terrain.png"));
        log($"NATIVE_RESULT progress={progress:F2} deviation={maxDeviation:F2} heightGain={body.position.y-bestStart.y:F2} riding={Get<bool>(rider,"Riding")}");
        check(Get<bool>(rider,"Riding") && progress>24 && body.position.y>bestStart.y+1,"pushes from rest uphill across unmodified generated terrain");
        check(maxDeviation<3,"native terrain facets do not cause uncontrolled steering drift");
        Call(rider,"Dismount",false);
        check(SaveSystem.HasSessionFlag(SaveSystemSessionFlags.DontSaveAnything),"terrain tests never save or modify a personal world");
    }
}
