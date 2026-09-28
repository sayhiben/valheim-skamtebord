using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace Skamtebord.RuntimeSmoke;

// Untagged MeshColliders exercise terrain/build-piece behavior independently of
// the halfpipe component. Every flight starts with velocity only on flat ground.
internal static class SurfaceSmoke
{
    private static object Call(object target,string name,params object[] args) => AccessTools.Method(target.GetType(),name).Invoke(target,args);
    private static T Get<T>(object target,string name) => (T)AccessTools.Property(target.GetType(),name).GetValue(target);

    internal static GameObject Profile(Vector3 origin,List<Vector2> points,bool split)
    {
        var root = new GameObject("Untagged skating surface fixture");
        root.transform.position = origin;
        int group = split ? 8 : points.Count-1;
        for (int start=0; start<points.Count-1; start+=group)
        {
            var patch = new GameObject("Surface patch"); patch.transform.SetParent(root.transform,false);
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            for(int i=start;i<Mathf.Min(start+group,points.Count-1);i++)
            {
                int n=vertices.Count; Vector2 a=points[i],b=points[i+1];
                vertices.AddRange(new[] {new Vector3(-4,a.y,a.x),new Vector3(-4,b.y,b.x),new Vector3(4,b.y,b.x),new Vector3(4,a.y,a.x)});
                triangles.AddRange(new[] {n,n+1,n+2,n,n+2,n+3});
            }
            var mesh=new Mesh(); mesh.SetVertices(vertices); mesh.SetTriangles(triangles,0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            patch.AddComponent<MeshCollider>().sharedMesh=mesh;
        }
        Physics.SyncTransforms(); return root;
    }

    private static List<Vector2> Transition(int segments=64)
    {
        var points=new List<Vector2> {new Vector2(-10,0),Vector2.zero};
        for(int i=1;i<=segments;i++)
        {
            float a=i*Mathf.PI*.5f/segments;
            points.Add(new Vector2(4*Mathf.Sin(a),4*(1-Mathf.Cos(a))));
        }
        points.Add(new Vector2(4,5)); // A truly vertical, ordinary collision face.
        return points;
    }

    internal static IEnumerator Reset(Player player,Component rider,Vector3 position,float yaw=0)
    {
        if(Get<bool>(rider,"Riding")) Call(rider,"Dismount",false);
        var body=player.GetComponent<Rigidbody>();
        body.position=position; body.rotation=Quaternion.Euler(0,yaw,0);
        player.transform.SetPositionAndRotation(position,body.rotation); player.ForceJump(Vector3.zero,false);
        player.SetLookDir(Quaternion.Euler(0,yaw,0)*Vector3.forward);
        Physics.SyncTransforms(); yield return new WaitForSeconds(.7f);
        player.AddStamina(100); Call(rider,"Toggle");
        if(!Get<bool>(rider,"Riding")) throw new InvalidOperationException("Surface trial could not mount on its flat approach.");
    }

    internal static IEnumerator Run(Player player,Component rider,GameObject platform,string root,Action<bool,string> check,Action<string> log)
    {
        var body=player.GetComponent<Rigidbody>();
        Vector3 origin=platform.transform.position+new Vector3(0,1.28f,0);
        foreach(bool split in new[] {false,true})
        {
            var fixture=Profile(origin,Transition(),split);
            var reset=Reset(player,rider,origin+new Vector3(0,.5f,-5)); while(reset.MoveNext()) yield return reset.Current;
            body.linearVelocity=Vector3.forward*18;
            float start=Time.time,gap=0,maxGap=0,peak=0; int vertical=0,gameSupported=0;
            bool launched=false; Vector3 exit=Vector3.zero;
            using(var csv=new StreamWriter(Path.Combine(root,split ? "surface-seams.csv" : "surface-vertical.csv")))
            {
                csv.WriteLine("time,z,y,vz,vy,supported,normal_y,walking_ground");
                while(Time.time-start<3.5f)
                {
                    Call(rider,"CaptureControls",Vector3.zero,false);
                    yield return new WaitForFixedUpdate();
                    Vector3 p=body.position-origin,v=body.linearVelocity,n=Get<Vector3>(rider,"SurfaceNormal");
                    bool supported=Get<bool>(rider,"Grounded"); peak=Mathf.Max(peak,p.y);
                    if(p.y>.3f && p.y<4.5f && v.y>1)
                    {
                        gap=supported ? 0 : gap+Time.fixedDeltaTime; maxGap=Mathf.Max(maxGap,gap);
                    }
                    if(supported && n.y<.1f && p.y>3.5f) {vertical++; if(player.IsOnGround()) gameSupported++;}
                    if(!launched && !supported && p.y>4.8f && v.y>1) {launched=true;exit=v;}
                    csv.WriteLine(FormattableString.Invariant($"{Time.time-start:F3},{p.z:F3},{p.y:F3},{v.z:F3},{v.y:F3},{supported},{n.y:F3},{player.IsOnGround()}"));
                    if(launched && v.y<=0) break;
                    if(!Get<bool>(rider,"Riding")) break;
                }
            }
            log($"VERTICAL split={split} samples={vertical} vanilla={gameSupported} gap={maxGap:F3} exit={exit} peak={peak:F3} riding={Get<bool>(rider,"Riding")}");
            check(vertical>=3,$"untagged transition (split={split}) supports its vertical face");
            check(gameSupported==vertical,$"untagged transition (split={split}) feeds supported verticals into game bookkeeping");
            check(maxGap<=.06f,$"untagged transition (split={split}) stays supported through the curve and seams");
            check(launched && exit.y>2 && Mathf.Abs(exit.z)<exit.y*.25f,$"untagged transition (split={split}) leaves its lip using upward momentum");
            check(peak>5.25f && Get<bool>(rider,"Riding"),$"untagged transition (split={split}) gains air without a false wall bail");
            Call(rider,"Dismount",false); UnityEngine.Object.Destroy(fixture); yield return new WaitForFixedUpdate();
        }

        // A slow coast must stall and roll back instead of sticking or gaining
        // height from normal changes. Gravity stays active throughout the curve.
        var slow=Profile(origin,Transition(),true);
        var slowReset=Reset(player,rider,origin+new Vector3(0,.5f,-2)); while(slowReset.MoveNext()) yield return slowReset.Current;
        body.linearVelocity=Vector3.forward*6;
        float slowStart=Time.time,slowPeak=0; bool rolledBack=false;
        while(Time.time-slowStart<3)
        {
            yield return new WaitForFixedUpdate(); slowPeak=Mathf.Max(slowPeak,body.position.y-origin.y);
            if(body.linearVelocity.z < -1) rolledBack=true;
        }
        log($"SLOW peak={slowPeak:F3} rollback={rolledBack}");
        check(slowPeak<1.5f && rolledBack && Get<bool>(rider,"Riding"),"insufficient momentum stalls and rolls back naturally on an untagged transition");
        Call(rider,"Dismount",false); UnityEngine.Object.Destroy(slow); yield return new WaitForFixedUpdate();

        // Convex terrain requires less downward curvature at speed: a fast board
        // leaves early, while a slow board follows farther. No magnetic ground snap.
        var crestPoints=new List<Vector2> {new Vector2(-10,6),new Vector2(0,6)};
        for(int i=1;i<=48;i++) {float a=i*75f/48*Mathf.Deg2Rad;crestPoints.Add(new Vector2(3*Mathf.Sin(a),6-3*(1-Mathf.Cos(a))));}
        var crest=Profile(origin,crestPoints,true);
        float[] departure=new float[2];
        for(int trial=0;trial<2;trial++)
        {
            var reset=Reset(player,rider,origin+new Vector3(0,6.5f,-2)); while(reset.MoveNext()) yield return reset.Current;
            body.linearVelocity=Vector3.forward*(trial==0 ? 3 : 14);
            float start=Time.time,air=0; bool left=false; Vector3 launch=Vector3.zero;
            while(Time.time-start<3)
            {
                yield return new WaitForFixedUpdate();
                // Tiny contact gaps between facets are not the crest's flight.
                // Measure the final departure that stays airborne for .2 seconds.
                if(Get<bool>(rider,"Grounded")) left=false;
                if(!left && !Get<bool>(rider,"Grounded"))
                {
                    left=true; departure[trial]=body.position.z-origin.z; launch=body.linearVelocity; air=Time.time;
                }
                if(left && Time.time-air>=.2f) break;
            }
            float elapsed=Time.time-air;
            log($"CREST speed={(trial==0 ? 3 : 14)} departure={departure[trial]:F3} initial={launch} after={body.linearVelocity} elapsed={elapsed:F3}");
            check(left && !Get<bool>(rider,"Grounded"),$"convex crest speed trial {trial} releases contact naturally");
            check(Mathf.Abs(body.linearVelocity.y-(launch.y+Physics.gravity.y*elapsed))<.6f,$"convex crest speed trial {trial} is ballistic after leaving support");
        }
        check(departure[0]>departure[1]+.5f,"faster momentum leaves the convex crest earlier instead of being pulled onto the downslope");
        Call(rider,"Dismount",false); UnityEngine.Object.Destroy(crest); yield return new WaitForFixedUpdate();

        // A long smooth descent includes faceted seams and an ordinary slope.
        var downhillPoints=new List<Vector2> {new Vector2(-10,9),new Vector2(0,9)};
        for(int i=1;i<=48;i++) {float a=i*40f/48*Mathf.Deg2Rad;downhillPoints.Add(new Vector2(16*Mathf.Sin(a),9-16*(1-Mathf.Cos(a))));}
        var last=downhillPoints[downhillPoints.Count-1];
        downhillPoints.Add(last+new Vector2(7,-7*Mathf.Tan(40*Mathf.Deg2Rad)));
        var downhill=Profile(origin,downhillPoints,true);
        var downhillReset=Reset(player,rider,origin+new Vector3(0,9.5f,-2)); while(downhillReset.MoveNext()) yield return downhillReset.Current;
        body.linearVelocity=Vector3.forward*4;
        float downhillStart=Time.time,downhillGap=0,maximumGap=0,downhillSpeed=4;
        using(var csv=new StreamWriter(Path.Combine(root,"surface-downhill.csv")))
        {
            csv.WriteLine("time,z,y,vz,vy,supported,normal_y");
            while(Time.time-downhillStart<4 && body.position.z-origin.z<last.x+3)
            {
                yield return new WaitForFixedUpdate();
                downhillGap=Get<bool>(rider,"Grounded") ? 0 : downhillGap+Time.fixedDeltaTime;maximumGap=Mathf.Max(maximumGap,downhillGap);
                downhillSpeed=Mathf.Max(downhillSpeed,body.linearVelocity.magnitude);
                var p=body.position-origin;var v=body.linearVelocity;
                csv.WriteLine(FormattableString.Invariant($"{Time.time-downhillStart:F3},{p.z:F3},{p.y:F3},{v.z:F3},{v.y:F3},{Get<bool>(rider,"Grounded")},{Get<Vector3>(rider,"SurfaceNormal").y:F3}"));
            }
        }
        log($"DOWNHILL maxSpeed={downhillSpeed:F3} unsupportedGap={maximumGap:F3} distance={body.position.z-origin.z:F3}");
        check(downhillSpeed>10 && Get<bool>(rider,"Riding"),"gravity gains substantial speed on an unpowered downhill run");
        check(maximumGap<=.06f,"a gentle downhill transition and its collider seams do not cause repeated false jumps");
        Call(rider,"Dismount",false); UnityEngine.Object.Destroy(downhill); yield return new WaitForFixedUpdate();

        var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="Head-on wall fixture";
        wall.transform.position=origin+new Vector3(0,4,3);wall.transform.localScale=new Vector3(8,8,.3f);
        var wallReset=Reset(player,rider,origin+new Vector3(0,.5f,-5)); while(wallReset.MoveNext()) yield return wallReset.Current;
        body.linearVelocity=Vector3.forward*14;
        float wallStart=Time.time;
        while(Get<bool>(rider,"Riding") && Time.time-wallStart<1.5f) yield return new WaitForFixedUpdate();
        log($"WALL riding={Get<bool>(rider,"Riding")} position={body.position-origin} velocity={body.linearVelocity}");
        check(!Get<bool>(rider,"Riding") && body.position.y-origin.y<1,"a head-on vertical wall still bails instead of becoming climbable ground");
        UnityEngine.Object.Destroy(wall);
    }
}
