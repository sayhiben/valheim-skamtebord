using System;
using System.Collections;
using System.IO;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Skamtebord.RuntimeSmoke;

internal static class HandlingSmoke
{
    private static object Call(object target,string name,params object[] args) => AccessTools.Method(target.GetType(),name).Invoke(target,args);
    private static T Get<T>(object target,string name) => (T)AccessTools.Property(target.GetType(),name).GetValue(target);

    internal static IEnumerator Run(Player player,Component rider,GameObject platform,string root,Action<bool,string> check,Action<string> log)
    {
        var body=player.GetComponent<Rigidbody>();
        Vector3 floor=platform.transform.position+Vector3.up;
        var ctor=AccessTools.Constructor(AccessTools.TypeByName("Skamtebord.SkateSettings"),new[] {typeof(ConfigFile)});
        foreach(float original in new[] {105f,140f})
        {
            var config=new ConfigFile(Path.Combine(root,"handling-config-"+original+".cfg"),false);
            var turn=config.Bind("Physics","TurnSpeed",original);ctor.Invoke(new object[] {config});
            check(turn.Value==(original==105 ? 165 : original),"handling update migrates the old default and preserves custom steering " + original);
            turn.Value=105;ctor.Invoke(new object[] {config});
            check(turn.Value==105,"handling migration does not repeat after later customization " + original);
        }
        foreach(float seed in new[] {0f,8f,-8f})
        {
            var reset=SurfaceSmoke.Reset(player,rider,floor+new Vector3(0,.5f,-25));while(reset.MoveNext())yield return reset.Current;
            body.linearVelocity=Vector3.forward*seed;
            float start=Time.time,early=0,peakReverse=0;
            while(Time.time-start<2.5f)
            {
                player.AddStamina(100);Call(rider,"CaptureControls",Vector3.back,false);
                yield return new WaitForFixedUpdate();
                if(Time.time-start<.4f)early=body.linearVelocity.z;
                if(Get<bool>(rider,"Backing"))peakReverse=Mathf.Max(peakReverse,body.linearVelocity.magnitude);
            }
            log($"BACKUP seed={seed:F2} early={early:F3} final={body.linearVelocity} peak={peakReverse:F3}");
            check(Get<bool>(rider,"Riding") && body.linearVelocity.z < -2.7f && peakReverse<=3.02f,"holding backward stops then backs up at a bounded speed from " + seed);
            if(seed!=0)check(Mathf.Sign(early)==Mathf.Sign(seed) && Mathf.Abs(early)<Mathf.Abs(seed)-2,"backward input first brakes an existing roll, including fakie " + seed);
            Call(rider,"CaptureControls",Vector3.zero,false);yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();
            check(!Get<bool>(rider,"Backing"),"releasing backward leaves reverse mode");
        }
        foreach(bool sprint in new[] {false,true})
        {
            var reset=SurfaceSmoke.Reset(player,rider,floor+new Vector3(0,.5f,-25));while(reset.MoveNext())yield return reset.Current;
            body.linearVelocity=Vector3.back*6;
            float start=Time.time,peak=0;
            while(Time.time-start<2)
            {
                player.AddStamina(100);Call(rider,"CaptureSprint",sprint);Call(rider,"CaptureControls",Vector3.forward,false);
                yield return new WaitForFixedUpdate();peak=Mathf.Max(peak,body.linearVelocity.magnitude);
            }
            float cap=sprint ? 14 : 9;
            log($"FAKIE sprint={sprint} final={body.linearVelocity} peak={peak:F4}");
            check(body.linearVelocity.z < -cap+.15f && peak<=cap+.02f,"forward input accelerates backward rolling up to its existing " + cap + " m/s push cap");
        }
        foreach(float seed in new[] {0f,14f})
        {
            var reset=SurfaceSmoke.Reset(player,rider,floor+new Vector3(0,.5f,-25));while(reset.MoveNext())yield return reset.Current;
            body.linearVelocity=Vector3.forward*seed;float start=Time.time;
            while(Time.time-start<.4f) {Call(rider,"CaptureControls",Vector3.right,false);yield return new WaitForFixedUpdate();}
            float angle=Vector3.Angle(Vector3.forward,body.rotation*Vector3.forward);
            log($"TURN seed={seed:F1} angle={angle:F2} speed={body.linearVelocity.magnitude:F3}");
            check(angle>(seed==0 ? 75 : 48) && angle<100,"responsive ground steering remains slower at sprint speed " + seed);
        }
        foreach(int sign in new[] {1,-1})
        {
            var reset=SurfaceSmoke.Reset(player,rider,floor+new Vector3(0,.5f,-25));while(reset.MoveNext())yield return reset.Current;
            body.linearVelocity=Vector3.forward*6;Call(rider,"CaptureControls",Vector3.zero,true);
            yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();
            Vector3 initial=body.rotation*Vector3.forward,normal=Get<Vector3>(rider,"SurfaceNormal"),velocity=body.linearVelocity;
            float start=Time.time;
            while(Time.time-start<.3f) {Call(rider,"CaptureControls",Vector3.right*sign,false);yield return new WaitForFixedUpdate();}
            Vector3 turned=body.rotation*Vector3.forward;
            float angle=Vector3.SignedAngle(initial,turned,normal)*sign;
            check(!Get<bool>(rider,"Grounded") && angle>145 && angle<180,"air turn reaches almost 180 degrees in 0.3 seconds, direction " + sign);
            check(Mathf.Abs(body.linearVelocity.z-velocity.z)<.03f && Mathf.Abs(body.linearVelocity.x)<.03f,"air turn does not alter horizontal flight momentum, direction " + sign);
            Call(rider,"CaptureControls",Vector3.zero,false);start=Time.time;
            while(Time.time-start<.06f)yield return new WaitForFixedUpdate();
            check(Vector3.Angle(turned,body.rotation*Vector3.forward)<1,"releasing air steering retains the chosen orientation, direction " + sign);
            log($"AIR sign={sign} angle={angle:F2} retained={Vector3.Angle(turned,body.rotation*Vector3.forward):F2} velocity={body.linearVelocity}");
        }
        Vector3 center=floor+new Vector3(0,.28f,20);
        var pipe=FlowSmoke.CreatePipe(center);
        try
        {
            // A fakie ollie must follow uphill travel, not the board's downhill
            // nose. Compare both stances at the same actual curved lip.
            foreach(float yaw in new[] {0f,180f})
            {
                var reset=SurfaceSmoke.Reset(player,rider,center+Vector3.up*.5f,yaw);while(reset.MoveNext())yield return reset.Current;
                body.linearVelocity=Vector3.forward*16;float deadline=Time.time+3;
                while(body.position.y-center.y<3.7f && Time.time<deadline)yield return new WaitForFixedUpdate();
                check(Time.time<deadline && Get<bool>(rider,"Grounded"),"lip ollie reaches the supported transition, stance=" + yaw);
                Vector3 before=body.linearVelocity;Call(rider,"CaptureControls",Vector3.zero,true);
                yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();
                Vector3 delta=body.linearVelocity-before;
                log($"LIP_OLLIE stance={yaw} before={before} after={body.linearVelocity} delta={delta}");
                check(!Get<bool>(rider,"Grounded") && delta.y>3.5f && Mathf.Abs(delta.z)<1.2f,
                    "lip ollie adds a mostly vertical impulse to both forward and fakie momentum, stance=" + yaw);
            }
            foreach(bool turnInAir in new[] {false,true})
            {
                var reset=SurfaceSmoke.Reset(player,rider,center+Vector3.up*.5f);while(reset.MoveNext())yield return reset.Current;
                body.linearVelocity=Vector3.forward*16;
                float start=Time.time,airStart=-1,turned=0,oppositeHeight=0;
                bool launched=false,crossed=false,backwardReturn=false;
                using(var csv=new StreamWriter(Path.Combine(root,turnInAir ? "halfpipe-air-turn.csv" : "halfpipe-fakie.csv")))
                {
                    csv.WriteLine("time,x,y,z,vx,vy,vz,supported,forward_velocity,turn_degrees");
                    while(Time.time-start<8 && Get<bool>(rider,"Riding"))
                    {
                        bool grounded=Get<bool>(rider,"Grounded");Vector3 p=body.position-center;
                        if(!launched && !grounded && p.y>3.8f && body.linearVelocity.y>1) {launched=true;airStart=Time.time;}
                        float steering=turnInAir && launched && !grounded && turned<179 && Time.time-airStart<.5f ? Mathf.Min(1,(180-turned)/(540*Time.fixedDeltaTime)) : 0;
                        Call(rider,"CaptureSprint",true);Call(rider,"CaptureControls",new Vector3(steering,0,1),false);
                        yield return new WaitForFixedUpdate();turned+=steering*540*Time.fixedDeltaTime;
                        p=body.position-center;Vector3 v=body.linearVelocity;
                        float forwardVelocity=Vector3.Dot(v,body.rotation*Vector3.forward);
                        if(launched && p.z<0)crossed=true;
                        if(crossed && p.z<-3)oppositeHeight=Mathf.Max(oppositeHeight,p.y);
                        if(launched && grounded && p.y<1 && forwardVelocity<-2)backwardReturn=true;
                        csv.WriteLine(FormattableString.Invariant($"{Time.time-start:F3},{p.x:F3},{p.y:F3},{p.z:F3},{v.x:F3},{v.y:F3},{v.z:F3},{Get<bool>(rider,"Grounded")},{forwardVelocity:F3},{turned:F1}"));
                        if(oppositeHeight>3.5f)break;
                    }
                }
                log($"PIPE airTurn={turnInAir} launched={launched} crossed={crossed} fakie={backwardReturn} turn={turned:F1} oppositeHeight={oppositeHeight:F3} riding={Get<bool>(rider,"Riding")}");
                check(launched && crossed && Get<bool>(rider,"Riding") && oppositeHeight>3.5f,"holding sprint carries a lip landing across and up the opposite transition, air turn=" + turnInAir);
                check(turnInAir ? turned>=179 : backwardReturn,"halfpipe transfer supports both a deliberate half turn and backward riding, air turn=" + turnInAir);
            }
        }
        finally {if(Get<bool>(rider,"Riding"))Call(rider,"Dismount",false);UnityEngine.Object.Destroy(pipe);}
        check(SaveSystem.HasSessionFlag(SaveSystemSessionFlags.DontSaveAnything),"handling tests retain save isolation");
    }
}
