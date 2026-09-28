using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using HarmonyLib;
using UnityEngine;

namespace Skamtebord.RuntimeSmoke;

// Real two-process ZNet/ZDO replication over Valheim's TCP transport. The listener
// binds only 127.0.0.1; no public server, platform tickets, saves or production patch.
internal static class NetworkSmoke
{
    internal static string Role, Root;
    internal static bool Active => Role != null;
    private const string PhaseKey = "skamtebord.qa.phase", ActorKey = "skamtebord.qa.actor";
    private static object Call(object o,string m,params object[] args) => AccessTools.Method(o.GetType(),m).Invoke(o,args);
    private static T Get<T>(object o,string p) => (T)AccessTools.Property(o.GetType(),p).GetValue(o);

    internal static void Install(Harmony harmony)
    {
        var args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args,"-skamtebord-peer");
        if (index < 0) return;
        Role = args[index+1];
        if (Role != "host" && Role != "client") throw new ArgumentException("Unknown QA peer role");
        Root = Path.GetFullPath(args[Array.IndexOf(args,"-skamtebord-peer-root")+1]);
        Directory.CreateDirectory(Root);
        harmony.Patch(AccessTools.Method(typeof(ZNet),"Awake"),postfix:new HarmonyMethod(typeof(NetworkSmoke),nameof(NetAwake)));
        harmony.Patch(AccessTools.Method(typeof(ZNet),"SendPeerInfo"),prefix:new HarmonyMethod(typeof(NetworkSmoke),nameof(SendPeerInfo)));
        harmony.Patch(AccessTools.Method(typeof(ZNet),"Update"),prefix:new HarmonyMethod(typeof(NetworkSmoke),nameof(NetUpdate)));
    }

    // The legacy TCP connector is still shipped, but 1.0's Update no longer pumps
    // its completion. Enable that transport only inside this loopback QA process.
    private static void NetUpdate(ZNet __instance)
    {
        if (Role != "client") return;
        Call(__instance,"UpdateClientConnector",Time.unscaledDeltaTime);
        // Clients receive locations from the host and never run GenerateLocations.
        if (QaBootstrap.FastWorld && ZoneSystem.instance && !ZoneSystem.instance.LocationsGenerated
            && ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected)
            AccessTools.Property(typeof(ZoneSystem),"LocationsGenerated").SetValue(ZoneSystem.instance,true);
    }

    internal static void Configure(string name,World world)
    {
        ZNet.ResetServerHost();
        ZNet.m_onlineBackend = OnlineBackendType.CustomSocket;
        ZNet.SetServer(Role == "host",false,false,name,"",Role == "host" ? world : null);
        if (Role == "client") ZNet.SetServerHost("127.0.0.1",int.Parse(File.ReadAllText(Path.Combine(Root,"port.txt"))),OnlineBackendType.CustomSocket);
    }

    private static void NetAwake(ZNet __instance)
    {
        if (Role != "host") return;
        var listener = new TcpListener(IPAddress.Loopback,0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var socket = new ZSocket2();
        AccessTools.Field(typeof(ZSocket2),"m_listner").SetValue(socket,listener);
        AccessTools.Field(typeof(ZSocket2),"m_listenPort").SetValue(socket,port);
        AccessTools.Field(typeof(ZNet),"m_hostSocket").SetValue(__instance,socket);
        File.WriteAllText(Path.Combine(Root,"port.txt"),port.ToString());
    }

    private static bool SendPeerInfo(ZNet __instance,ZRpc rpc)
    {
        if (Role != "client" || ZNet.m_onlineBackend != OnlineBackendType.CustomSocket) return true;
        var pkg = new ZPackage();
        pkg.Write(ZNet.GetUID()); pkg.Write(Version.CurrentVersion.ToString()); pkg.Write(40u);
        pkg.Write(__instance.GetReferencePosition()); pkg.Write(Game.instance.GetPlayerProfile().GetName()); pkg.Write("");
        var distance = (SimulationDistance)Call(__instance,"GetDesiredSimulationDistance");
        distance.Serialize(ref pkg);
        pkg.Write(""); pkg.Write("");
        rpc.Invoke("PeerInfo",pkg);
        return false;
    }

    internal static IEnumerator Run(Player player,Component rider,GameObject platform,string saveRoot,Action<bool,string> check,Action<string> log)
    {
        // Bound two simultaneous render loops instead of letting both clients
        // contend for the GPU at the user's uncapped display refresh rate.
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;
        File.WriteAllText(Path.Combine(Root,Role+"-save-root.txt"),saveRoot);
        var test = Role == "host" ? Host(player,rider,platform,check,log) : Observer(player,check,log);
        while (test.MoveNext()) yield return test.Current;
    }

    private static IEnumerator Ack(int phase)
    {
        float until = Time.realtimeSinceStartup + 12;
        string file = Path.Combine(Root,"observed-"+phase+".txt");
        while (!File.Exists(file) && Time.realtimeSinceStartup < until) yield return null;
        if (!File.Exists(file)) throw new InvalidOperationException("Observer did not verify network phase " + phase);
    }

    private static IEnumerator Drive(Component rider,Vector3 controls,float seconds,bool sprint=false)
    {
        float end = Time.time + seconds;
        while (Time.time < end)
        {
            Call(rider,"CaptureControls",controls,false); Call(rider,"CaptureSprint",sprint);
            yield return new WaitForFixedUpdate();
        }
        Call(rider,"CaptureControls",Vector3.zero,false); Call(rider,"CaptureSprint",false);
    }

    private static IEnumerator Reset(Player player,Component rider,Vector3 position,float yaw)
    {
        if (Get<bool>(rider,"Riding")) Call(rider,"Dismount",false);
        var body = player.GetComponent<Rigidbody>();
        body.position = position; body.rotation = Quaternion.Euler(0,yaw,0);
        player.transform.SetPositionAndRotation(position,body.rotation); player.ForceJump(Vector3.zero,false);
        player.AddStamina(100f);
        Physics.SyncTransforms(); yield return new WaitForSeconds(.6f);
        Call(rider,"Toggle");
    }

    private static IEnumerator Host(Player player,Component rider,GameObject platform,Action<bool,string> check,Action<string> log)
    {
        var zdo = player.GetComponent<ZNetView>().GetZDO();
        zdo.Set(ActorKey,true); zdo.Set(PhaseKey,0);
        zdo.Set("skamtebord.trick",2);
        zdo.Set("skamtebord.trickSequence",42);
        zdo.Set("skamtebord.trickTime",ZNet.instance.GetTime().Ticks-TimeSpan.TicksPerSecond*10);
        var center = platform.transform.position + new Vector3(18,1.28f,18);
        var pipe = FlowSmoke.CreatePipe(center);
        Call(rider,"Toggle");
        float until = Time.realtimeSinceStartup + 90;
        while (!File.Exists(Path.Combine(Root,"client-ready.txt")) && Time.realtimeSinceStartup < until) yield return null;
        check(ZNet.instance.GetPeers().Any(p=>p.IsReady()),"second Valheim process completes the real ZNet peer handshake");
        var ack = Ack(0); while (ack.MoveNext()) yield return ack.Current;
        check(true,"late-joining observer sees an already-mounted rider and the persistent halfpipe");

        zdo.Set(PhaseKey,1);
        var drive = Drive(rider,Vector3.forward,2); while(drive.MoveNext()) yield return drive.Current;
        ack = Ack(1); while(ack.MoveNext()) yield return ack.Current;
        zdo.Set(PhaseKey,2);
        drive = Drive(rider,Vector3.forward,1.2f,true); while(drive.MoveNext()) yield return drive.Current;
        ack = Ack(2); while(ack.MoveNext()) yield return ack.Current;
        zdo.Set(PhaseKey,3);
        // Keep a sustained turn until the observer samples it; clearing the
        // lean after a fixed short interval can legitimately coalesce to zero
        // between ZDO snapshots on two busy render processes.
        float turnDeadline = Time.realtimeSinceStartup + 12;
        while (!File.Exists(Path.Combine(Root,"observed-3.txt")) && Time.realtimeSinceStartup < turnDeadline)
        {
            Call(rider,"CaptureControls",new Vector3(1,0,1).normalized,false);
            yield return new WaitForFixedUpdate();
        }
        Call(rider,"CaptureControls",Vector3.zero,false);
        ack = Ack(3); while(ack.MoveNext()) yield return ack.Current;
        check(true,"observer verifies replicated movement, pushing, sprint tuck and turning");

        var body = player.GetComponent<Rigidbody>();
        Vector3 origin = platform.transform.position + new Vector3(0,1.4f,-20);
        string[] tricks = { "Shuvit","Kickflip","Heelflip","Grab","ThreeSixty" };
        for(int i=0;i<tricks.Length;i++)
        {
            // The real halfpipe supplies enough airtime to finish every trick,
            // including the 360, without weakening ordinary landing checks.
            var reset = Reset(player,rider,center+Vector3.up*.4f,0); while(reset.MoveNext()) yield return reset.Current;
            body.linearVelocity = Vector3.forward * 16;
            float launchDeadline=Time.time+4;
            while ((Get<bool>(rider,"Grounded") || body.position.y<center.y+3.5f) && Time.time<launchDeadline)
                yield return new WaitForFixedUpdate();
            check(Time.time<launchDeadline,"host reaches the lip for " + tricks[i]);
            zdo.Set(PhaseKey,4+i);
            var trickType = AccessTools.TypeByName("Skamtebord.Core.TrickId");
            bool accepted = (bool)Call(rider,"Trick",Enum.Parse(trickType,tricks[i]));
            log($"TRICK {tricks[i]} accepted={accepted} riding={Get<bool>(rider,"Riding")} grounded={Get<bool>(rider,"Grounded")} velocity={body.linearVelocity} time={zdo.GetLong("skamtebord.trickTime")}");
            check(accepted,"host submits " + tricks[i] + " during the flight");
            ack = Ack(4+i); while(ack.MoveNext()) yield return ack.Current;
            check(true,"observer sees timestamped " + tricks[i] + " animation on the replicated board");
        }
        // A sustained grab needs sustained airtime. The separate flat-ground
        // input suite checks quick grabs; here a timed pipe ollie lets the
        // observer verify the held state across ordinary network snapshots.
        var grabReset = Reset(player,rider,center+Vector3.up*.4f,0); while(grabReset.MoveNext()) yield return grabReset.Current;
        body.linearVelocity = Vector3.forward * 16;
        float grabLaunchDeadline = Time.time + 4;
        while (body.position.y<center.y+3.7f && Time.time<grabLaunchDeadline) yield return new WaitForFixedUpdate();
        check(Time.time<grabLaunchDeadline,"host reaches the transition for a sustained airborne grab");
        zdo.Set(PhaseKey,9);
        Call(rider,"CaptureJumpHeld",true); Call(rider,"CaptureControls",Vector3.zero,true);
        yield return new WaitForSeconds(.26f);
        log($"HELD_GRAB riding={Get<bool>(rider,"Riding")} grounded={Get<bool>(rider,"Grounded")} grab={Get<bool>(rider,"Grabbing")} zdoGrab={zdo.GetBool("skamtebord.grabHeld")} speed={body.linearVelocity} jumpHeld={AccessTools.Field(rider.GetType(),"jumpHeld").GetValue(rider)} status={Get<string>(rider,"Status")}");
        check(Get<bool>(rider,"Grabbing"),"host starts the held grab through captured jump input");
        ack = Ack(9); while(ack.MoveNext()) yield return ack.Current;
        Call(rider,"CaptureJumpHeld",false);
        var pipeReset = Reset(player,rider,center+Vector3.up*.4f,0); while(pipeReset.MoveNext()) yield return pipeReset.Current;
        body.linearVelocity = Vector3.forward * 16;
        float poseLaunchDeadline = Time.time + 4;
        while ((Get<bool>(rider,"Grounded") || body.position.y<center.y+3.5f) && Time.time<poseLaunchDeadline)
            yield return new WaitForFixedUpdate();
        log($"SURFACE_LAUNCH position={body.position} velocity={body.linearVelocity} up={player.transform.up} riding={Get<bool>(rider,"Riding")}");
        check(Time.time<poseLaunchDeadline,"host reaches the vertical lip for the replicated surface pose");
        zdo.Set(PhaseKey,10);
        ack = Ack(10); while(ack.MoveNext()) yield return ack.Current;
        check(true,"observer sees the curved halfpipe ride and surface-aligned body/board");
        Call(rider,"Dismount",false); zdo.Set(PhaseKey,11);
        yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
        log($"DISMOUNT_HOST up={player.transform.up} bodyUp={body.rotation*Vector3.up} netUp={zdo.GetRotation()*Vector3.up}");
        check(Vector3.Angle(player.transform.up,Vector3.up)<1,"host stays upright after dismount and two physics ticks");
        ack = Ack(11); while(ack.MoveNext()) yield return ack.Current;
        var flatReset = Reset(player,rider,origin,0); while(flatReset.MoveNext()) yield return flatReset.Current;
        zdo.Set(PhaseKey,12); ack = Ack(12); while(ack.MoveNext()) yield return ack.Current;
        ZNetScene.instance.Destroy(pipe); zdo.Set(PhaseKey,13);
        ack = Ack(13); while(ack.MoveNext()) yield return ack.Current;
        check(true,"observer sees dismount, remount and halfpipe removal");
        until = Time.realtimeSinceStartup + 20;
        while(ZNet.instance.GetPeers().Count>0 && Time.realtimeSinceStartup<until) yield return null;
        check(ZNet.instance.GetPeers().Count == 0,"observer disconnect is handled by the normal network lifecycle");
    }

    private static IEnumerator Observer(Player player,Action<bool,string> check,Action<string> log)
    {
        var previousCamera = Utils.GetMainCamera();
        var gameCamera = UnityEngine.Object.FindFirstObjectByType<GameCamera>();
        if (gameCamera) gameCamera.enabled = false;
        if (previousCamera) previousCamera.enabled = false;
        var camera = new GameObject("Network observer camera").AddComponent<Camera>();
        camera.tag="MainCamera"; camera.nearClipPlane=.05f; camera.farClipPlane=500;
        camera.renderingPath=RenderingPath.Forward;
        if (EnvMan.instance)
        {
            EnvMan.instance.m_debugTimeOfDay=true; EnvMan.instance.m_debugTime=.42f;
            EnvMan.instance.m_debugEnv="Clear"; EnvMan.instance.ForceInstantEnvironmentSwitch();
        }
        var daylight = new GameObject("Observer daylight").AddComponent<Light>();
        daylight.type=LightType.Directional; daylight.intensity=1.2f;
        daylight.transform.rotation=Quaternion.Euler(45,-30,0);
        Screen.SetResolution(1280,720,FullScreenMode.Windowed);
        yield return new WaitForSeconds(.1f);
        Screen.SetResolution(960,540,FullScreenMode.Windowed);
        yield return new WaitForSeconds(.1f);
        File.WriteAllText(Path.Combine(Root,"client-ready.txt"),"ready");
        int phaseDone = -1, lastSequence = -1, reportedPhase = -1; Vector3 firstPosition = Vector3.zero;
        RuntimeAnimatorController expectedBase = null;
        float nextDiagnostic = 0;
        float until = Time.realtimeSinceStartup + 180;
        while (phaseDone < 13 && Time.realtimeSinceStartup < until)
        {
            yield return new WaitForEndOfFrame();
            var actor = Player.GetAllPlayers().FirstOrDefault(p=>p && p!=player && p.GetComponent<ZNetView>().GetZDO()?.GetBool(ActorKey)==true);
            if (!actor) continue;
            camera.transform.position=actor.transform.position+new Vector3(5,3,-6);
            camera.transform.LookAt(actor.transform.position+Vector3.up);
            var view = actor.GetComponent<ZNetView>(); var zdo = view.GetZDO();
            int phase = zdo.GetInt(PhaseKey);
            if (phase <= phaseDone) continue;
            var rider = actor.GetComponent(AccessTools.TypeByName("Skamtebord.BoardRider"));
            if (!rider) continue;
            var board = (GameObject)AccessTools.Field(rider.GetType(),"board").GetValue(rider);
            var animator = actor.GetComponentInChildren<Animator>();
            var clips = animator.GetCurrentAnimatorClipInfo(0);
            bool riding = board && board.activeInHierarchy;
            if (phase>=10 && Time.time>=nextDiagnostic)
            {
                log($"POSE phase={phase} riding={riding} position={actor.transform.position} up={actor.transform.up} grab={Get<bool>(rider,"Grabbing")} boardAngle={(board ? Quaternion.Angle(board.transform.localRotation,Quaternion.identity) : -1)}");
                nextDiagnostic=Time.time+1;
            }
            bool passed = false;
            if(phase==0) passed = riding && !view.IsOwner() && (float)AccessTools.Field(rider.GetType(),"trickDuration").GetValue(rider)==0
                && UnityEngine.Object.FindObjectsByType<Piece>(FindObjectsSortMode.None).Any(p=>p.name.StartsWith("Skamtebord_Halfpipe"));
            if(phase==1) passed = riding && Vector3.Distance(actor.transform.position,firstPosition)>1 && clips.Any(c=>c.clip.name=="SkatePush" && c.weight>.2f);
            if(phase==2) passed = riding && clips.Any(c=>c.clip.name=="SkateTuck" && c.weight>.2f);
            if(phase==3) passed = riding && Mathf.Abs(Mathf.DeltaAngle(actor.transform.eulerAngles.y,0))>15
                && Mathf.Abs((float)AccessTools.Field(rider.GetType(),"lean").GetValue(rider))>3;
            if(phase>=4 && phase<=8)
            {
                int expected = new[] { 1,2,3,4,5 }[phase-4];
                int shown = (int)AccessTools.Field(rider.GetType(),"visualTrick").GetValue(rider);
                float start = (float)AccessTools.Field(rider.GetType(),"trickStart").GetValue(rider);
                float duration = (float)AccessTools.Field(rider.GetType(),"trickDuration").GetValue(rider);
                double elapsed = (ZNet.instance.GetTime().Ticks-zdo.GetLong("skamtebord.trickTime"))/(double)TimeSpan.TicksPerSecond;
                int sequence = zdo.GetInt("skamtebord.trickSequence");
                if (sequence != lastSequence)
                {
                    log($"TRICK_RECEIVED phase={phase} seq={sequence} stored={zdo.GetInt("skamtebord.trick")} shown={shown} elapsed={elapsed:F3} localAge={Time.time-start:F3} riding={riding}");
                    lastSequence=sequence;
                }
                bool clockAligned = elapsed<0 || elapsed>duration-.12f || Math.Abs((Time.time-start)-elapsed)<.15;
                passed = riding && shown==expected && Time.time-start>=0 && Time.time-start<duration && clockAligned
                    && Quaternion.Angle(board.transform.localRotation,Quaternion.identity)>5;
            }
            if(phase==9)
            {
                passed = riding && Get<bool>(rider,"Grabbing") && Quaternion.Angle(board.transform.localRotation,Quaternion.identity)>15;
                int sequence=zdo.GetInt("skamtebord.trickSequence");
                if (sequence!=lastSequence)
                {
                    log($"GRAB_RECEIVED seq={sequence} riding={riding} zdoGrab={zdo.GetBool("skamtebord.grabHeld")} grab={Get<bool>(rider,"Grabbing")} angle={(board ? Quaternion.Angle(board.transform.localRotation,Quaternion.identity) : -1)}");
                    lastSequence=sequence;
                }
            }
            if(phase==10) passed = riding && actor.transform.up.y<.25f && actor.transform.position.y>253 && Vector3.Dot(board.transform.up,actor.transform.up)>.98f;
            if(phase==11)
            {
                var skateAnimator=AccessTools.Field(rider.GetType(),"skateAnimator").GetValue(rider);
                passed = !riding && Vector3.Angle(actor.transform.up,Vector3.up)<3 && !Get<bool>(skateAnimator,"Active")
                    && animator.runtimeAnimatorController==expectedBase && Vector3.Angle(zdo.GetRotation()*Vector3.up,Vector3.up)<1;
                if (reportedPhase!=phase)
                {
                    log($"DISMOUNT_OBSERVED board={riding} up={actor.transform.up} overlay={Get<bool>(skateAnimator,"Active")} controller={animator.runtimeAnimatorController.name}/{animator.runtimeAnimatorController.GetType().Name}");
                    reportedPhase=phase;
                }
            }
            if(phase==12) passed = riding;
            if(phase==13) passed = !UnityEngine.Object.FindObjectsByType<Piece>(FindObjectsSortMode.None).Any(p=>p.name.StartsWith("Skamtebord_Halfpipe"));
            if (!passed) continue;
            check(true,$"non-owner client verifies replicated phase {phase}, pos={actor.transform.position}, up={actor.transform.up}");
            ScreenCapture.CaptureScreenshot(Path.Combine(Root,$"observer-phase-{phase:D2}.png"));
            if(phase==0)
            {
                firstPosition=actor.transform.position;
                var skateAnimator=AccessTools.Field(rider.GetType(),"skateAnimator").GetValue(rider);
                expectedBase=(RuntimeAnimatorController)AccessTools.Field(skateAnimator.GetType(),"original").GetValue(skateAnimator);
            }
            File.WriteAllText(Path.Combine(Root,"observed-"+phase+".txt"),"Verified from this client's replicated objects.");
            phaseDone=phase;
        }
        check(phaseDone==13,"observer completes every replicated behavior and removal check");
        var radioType = AccessTools.TypeByName("Skamtebord.Radio.SkateRadio");
        var radio = UnityEngine.Object.FindFirstObjectByType(radioType);
        check(radio && !((AudioSource)AccessTools.Field(radioType,"_source").GetValue(radio)).isPlaying,"remote skating never starts this client's personal radio");
    }
}
