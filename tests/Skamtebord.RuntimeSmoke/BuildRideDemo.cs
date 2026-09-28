using System;
using System.Collections;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using Unity.Collections;
using UnityEngine;

namespace Skamtebord.RuntimeSmoke;

// A scripted, disposable game capture. Uses the released mod, native crafting
// and placement, real Unity physics, and the game's mixed audio output.
internal static class BuildRideDemo
{
    private const int Fps=30, Seconds=40;
    private static object Call(object target,string name,params object[] args) => AccessTools.Method(target.GetType(),name).Invoke(target,args);
    private static T Get<T>(object target,string name) => (T)AccessTools.Property(target.GetType(),name).GetValue(target);
    private static T Field<T>(object target,string name) => (T)AccessTools.Field(target.GetType(),name).GetValue(target);

    internal static IEnumerator Run(Player player,Component rider,GameObject platform,string root,Action<bool,string> check,Action<string> log)
    {
        string frames=Path.Combine(root,"demo-frames");Directory.CreateDirectory(frames);
        var body=player.GetComponent<Rigidbody>();var inventory=player.GetInventory();
        inventory.RemoveAll();
        foreach(var item in new[] {("Wood",44),("Wood",44),("Resin",4),("LeatherScraps",2),("Hammer",1)})
            inventory.AddItem(ObjectDB.instance.GetItemPrefab(item.Item1),item.Item2);
        player.m_baseStamina=300;player.SetMaxStamina(300,false);player.AddStamina(300);
        player.GetSkills().CheatRaiseSkill("Jump",100);
        check(player.GetSkills().GetSkillLevel(Skills.SkillType.Jump)==100 && player.GetMaxStamina()>=300,"demo character has Jump 100 and 300 stamina");

        object settings=Get<object>(rider,"Settings");
        var jump=(ConfigEntry<float>)AccessTools.Field(settings.GetType(),"JumpSpeed").GetValue(settings);
        float previousJump=jump.Value;jump.Value=8;
        var radioType=AccessTools.TypeByName("Skamtebord.Radio.SkateRadio");
        var radio=UnityEngine.Object.FindFirstObjectByType(radioType);
        var radioEnabled=radio==null ? null : Field<ConfigEntry<bool>>(radio,"_enabled");
        bool previousRadio=radioEnabled?.Value ?? false;if(radioEnabled!=null) radioEnabled.Value=false;
        var hudPosition=(ConfigEntry<Vector2>)AccessTools.Field(settings.GetType(),"HudPosition").GetValue(settings);
        var hudScale=(ConfigEntry<float>)AccessTools.Field(settings.GetType(),"HudScale").GetValue(settings);
        Vector2 previousHud=hudPosition.Value;float previousScale=hudScale.Value;
        hudPosition.Value=new Vector2(1,1);hudScale.Value=.8f;

        // Prepare an unsaved, level patch of actual terrain. Ground build pieces
        // require a Heightmap; the elevated physics-test cube is not build land.
        Heightmap terrain=null;
        float terrainDeadline=Time.realtimeSinceStartup+20;
        while(!terrain && Time.realtimeSinceStartup<terrainDeadline) {terrain=Heightmap.FindHeightmap(Vector3.zero);yield return null;}
        check(terrain,"local terrain is available for native building");
        terrain.GetWorldHeight(Vector3.zero,out float groundHeight);
        groundHeight=Mathf.Max(groundHeight,34);
        Vector3 floor=new Vector3(0,groundHeight,0);
        for(int z=0;z<=terrain.m_width;z++) for(int x=0;x<=terrain.m_width;x++)
            terrain.SetHeight(x,z,groundHeight-terrain.transform.position.y);
        // A small raised build point keeps the thin riding mesh clear of the
        // surrounding ground, like building on a gently raised terrain vertex.
        terrain.WorldToVertex(floor,out int buildX,out int buildZ);
        terrain.SetHeight(buildX,buildZ,groundHeight-terrain.transform.position.y+.2f);
        Call(terrain,"PaintCleared",floor,22f,TerrainModifier.PaintType.Dirt,false,true,1f);
        Call(terrain,"RebuildCollisionMesh");Call(terrain,"RebuildRenderMesh");Call(terrain,"UpdateCornerDepths");
        if(ClutterSystem.instance) ClutterSystem.instance.ResetGrass(floor,24);
        platform.SetActive(false);
        log("Leveled disposable terrain at "+floor);
        Vector3 center=floor+new Vector3(0,.28f,0);
        var station=UnityEngine.Object.Instantiate(ZNetScene.instance.GetPrefab("piece_workbench"),floor+new Vector3(-8,0,4),Quaternion.Euler(0,90,0));
        station.GetComponent<Piece>().SetCreator(player.GetPlayerID(),UserInfo.GetLocalUser().UserId);
        station.GetComponent<WearNTear>().OnPlaced();
        player.AddKnownStation(station.GetComponent<CraftingStation>());
        body.position=floor+new Vector3(-7,.3f,-2);body.rotation=Quaternion.Euler(0,90,0);
        player.transform.SetPositionAndRotation(body.position,body.rotation);player.ForceJump(Vector3.zero,false);Physics.SyncTransforms();

        var original=Utils.GetMainCamera();var gameCamera=original.GetComponent<GameCamera>();
        if(gameCamera) gameCamera.enabled=false;original.enabled=false;
        foreach(var listener in UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None)) listener.enabled=false;
        var camera=new GameObject("Demo tracking camera").AddComponent<Camera>();camera.tag="MainCamera";
        camera.nearClipPlane=.05f;camera.farClipPlane=500;camera.fieldOfView=50;camera.renderingPath=RenderingPath.Forward;
        camera.gameObject.AddComponent<AudioListener>();AudioListener.pause=false;AudioListener.volume=1;
        camera.transform.position=body.position+new Vector3(-4,2.8f,-4);camera.transform.LookAt(body.position+Vector3.up);
        var light=new GameObject("Demo daylight").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.15f;
        light.transform.rotation=Quaternion.Euler(45,-35,0);
        RenderSettings.ambientLight=new Color(.65f,.65f,.65f);RenderSettings.fog=false;
        if(EnvMan.instance) {EnvMan.instance.m_debugTimeOfDay=true;EnvMan.instance.m_debugTime=.42f;EnvMan.instance.m_debugEnv="Clear";EnvMan.instance.ForceInstantEnvironmentSwitch();}
        Screen.SetResolution(960,540,FullScreenMode.Windowed);yield return new WaitForSeconds(.2f);
        Screen.SetResolution(1280,720,FullScreenMode.Windowed);yield return new WaitForSeconds(.8f);
        var audioConfig=AudioSettings.GetConfiguration();audioConfig.speakerMode=AudioSpeakerMode.Stereo;
        AudioSettings.Reset(audioConfig);yield return new WaitForSeconds(.5f);
        MessageHud.instance.ClearUnlockQueue();
        foreach(var message in Field<System.Collections.Generic.List<GameObject>>(MessageHud.instance,"m_unlockMessages")) if(message) UnityEngine.Object.Destroy(message);
        var overlay=camera.gameObject.AddComponent<DemoOverlay>();
        var pipePiece=ZNetScene.instance.GetPrefab("Skamtebord_Halfpipe").GetComponent<Piece>();
        var hammer=inventory.GetAllItems().First(i=>i.m_dropPrefab && i.m_dropPrefab.name=="Hammer");
        GameObject pipe=null;bool trickSent=false,airSeen=false,landSeen=false;float airTime=0;
        int rideTake=-1,acceptedTricks=0,landedTakes=0;
        var gui=InventoryGui.instance;
        var telemetry=new StreamWriter(Path.Combine(root,"demo-telemetry.csv"));
        telemetry.WriteLine("frame,seconds,stage,x,y,z,speed,grounded,riding,trick,combo,xp");
        int samples=0;double squares=0;float peak=0;
        var audio=new BinaryWriter(File.Create(Path.Combine(root,"demo-audio.wav")));
        audio.Write(new byte[44]);
        Time.captureFramerate=Fps;QualitySettings.vSyncCount=0;
        check(AudioRenderer.Start(),"Unity AudioRenderer starts synchronized capture");
        try
        {
            for(int frame=0;frame<Seconds*Fps;frame++)
            {
                foreach(var raven in UnityEngine.Object.FindObjectsByType<Raven>(FindObjectsSortMode.None)) raven.gameObject.SetActive(false);
                MessageHud.instance.ClearUnlockQueue();
                float t=frame/(float)Fps;
                overlay.Title=t<7 ? "01  CRAFT THE BOARD" : t<13 ? "02  BUILD A WOODEN HALFPIPE" : "03  RIDE / TRICK / REPEAT";
                overlay.Detail=t<7 ? "8 wood · 4 resin · 2 leather scraps" : t<13 ? "Hammer → Skamtebord · 80 wood · workbench" : "Demo cheats: Jump 100 · 300 stamina · boosted ollies";
                player.AddStamina(300);
                if(frame==15) gui.Show(null);
                if(frame==30)
                {
                    var recipes=(IList)AccessTools.Field(typeof(InventoryGui),"m_availableRecipes").GetValue(gui);
                    int index=-1;
                    for(int i=0;i<recipes.Count;i++) if(Get<Recipe>(recipes[i],"Recipe").m_item.name=="Skamtebord_Board") index=i;
                    check(index>=0,"skateboard appears in the real crafting menu");Call(gui,"SetRecipe",index,true);
                }
                if(frame==75) {gui.m_craftButton.onClick.Invoke();log("Craft button invoked; native craft timer and recipe cost apply.");}
                if(frame==175)
                {
                    check(inventory.GetAllItems().Any(i=>i.m_dropPrefab && i.m_dropPrefab.name=="Skamtebord_Board"),"native crafting creates the skateboard");
                    check(inventory.CountItems("$item_wood")==80 && inventory.CountItems("$item_resin")==0 && inventory.CountItems("$item_leatherscraps")==0,"crafting consumed the board recipe materials");
                    QaBootstrap.PutBoardInFirstSlot(player);
                }
                if(frame==195) gui.Hide();
                if(frame==210)
                {
                    player.EquipItem(hammer,false);player.SetLookDir(Vector3.right);
                    camera.transform.position=floor+new Vector3(-14,8,-12);camera.transform.LookAt(floor+Vector3.up*.2f);
                    original.transform.SetPositionAndRotation(camera.transform.position,camera.transform.rotation);
                    AccessTools.Field(typeof(Player),"m_placeRotation").SetValue(player,0);
                    check(player.SetSelectedPiece(pipePiece),"halfpipe selected from the normal Hammer table");
                    Hud.instance.TogglePieceSelection();
                }
                if(frame==275) Hud.HidePieceSelection();
                if(frame==320)
                {
                    check(player.HaveRequirements(pipePiece,Player.RequirementMode.CanBuild),"80 Wood and nearby workbench satisfy the halfpipe build requirements");
                    bool placed=player.TryPlacePiece(pipePiece);
                    log("Placement result="+placed+" status="+player.GetPlacementStatus());
                    check(placed,"native placement builds the halfpipe");
                    player.ConsumeResources(pipePiece.m_resources,0);
                    hammer.m_shared.m_buildEffect.Create(player.transform.position,Quaternion.identity);
                    pipe=UnityEngine.Object.FindObjectsByType<Piece>(FindObjectsSortMode.None).First(p=>p.name.StartsWith("Skamtebord_Halfpipe") && p.GetComponent<ZNetView>()?.IsValid()==true).gameObject;
                    center=pipe.transform.position;
                    check(inventory.CountItems("$item_wood")==0,"building consumes the halfpipe's 80 Wood");
                }
                if(frame==355) {player.UnequipItem(hammer,false);player.HideHandItems(false);}
                // Deliberate camera cuts between prepared run-ins. Only their
                // starting flat-ground momentum is seeded; flight/landings use
                // the unmodified release physics and collision response.
                if(frame==390 || frame==660 || frame==930)
                {
                    if(rideTake>=0 && landSeen) landedTakes++;
                    rideTake++;trickSent=airSeen=landSeen=false;
                    if(Get<bool>(rider,"Riding")) Call(rider,"Dismount",false);
                    float sign=rideTake==1 ? -1 : 1;
                    body.position=center+new Vector3(0,.06f,rideTake==2 ? -2.5f : 0);
                    body.rotation=Quaternion.Euler(0,sign>0 ? 0 : 180,0);player.transform.SetPositionAndRotation(body.position,body.rotation);
                    player.ForceJump(Vector3.zero,false);Physics.SyncTransforms();
                }
                int takeFrame=frame-(390+rideTake*270);
                if(rideTake>=0)
                {
                    if(takeFrame==15)
                    {
                        Call(rider,"Toggle");check(Get<bool>(rider,"Riding"),"demo run-in mounts on the halfpipe flat");
                        body.linearVelocity=Vector3.forward*(rideTake==1 ? -16f : rideTake==2 ? 3f : 16f);
                    }
                    if(Get<bool>(rider,"Riding"))
                    {
                        Call(rider,"CaptureControls",Vector3.zero,rideTake==2 && takeFrame==24);
                        bool grounded=Get<bool>(rider,"Grounded");
                        if(!grounded && !airSeen && takeFrame>16) {airSeen=true;airTime=Time.time;}
                        if(airSeen && !trickSent && !grounded && Time.time-airTime>.055f)
                        {
                            string trick=rideTake==0 ? "Kickflip" : rideTake==1 ? "Shuvit" : "Grab";
                            var id=Enum.Parse(AccessTools.TypeByName("Skamtebord.Core.TrickId"),trick);
                            bool accepted=(bool)Call(rider,"Trick",id);trickSent|=accepted;if(accepted) acceptedTricks++;
                            log("Demo trick="+trick+" accepted="+accepted+" velocity="+body.linearVelocity);
                        }
                        if(airSeen && trickSent && grounded) landSeen=true;
                    }
                    Vector3 target=Vector3.Lerp(center+Vector3.up*1.8f,body.position+Vector3.up*.8f,.38f);
                    camera.transform.position=center+new Vector3(rideTake==1 ? -13 : 13,8,-11);
                    camera.transform.LookAt(target);
                    overlay.Detail="Demo cheats: Jump 100 · 300 stamina · prepared run-ins";
                }
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(frames,$"frame_{frame:D5}.png"));
                int count=AudioRenderer.GetSampleCountForCaptureFrame();
                using(var data=new NativeArray<float>(count*2,Allocator.Temp))
                {
                    if(!AudioRenderer.Render(data)) throw new InvalidOperationException("Audio render failed at frame "+frame);
                    foreach(float sample in data)
                    {
                        float value=Mathf.Clamp(sample,-1,1);audio.Write((short)Mathf.RoundToInt(value*32767));samples++;squares+=value*value;peak=Mathf.Max(peak,Mathf.Abs(value));
                    }
                }
                var combo=Get<object>(rider,"Combo");Vector3 p=body.position-center;
                telemetry.WriteLine(FormattableString.Invariant($"{frame},{t:F3},{rideTake},{p.x:F3},{p.y:F3},{p.z:F3},{body.linearVelocity.magnitude:F3},{Get<bool>(rider,"Grounded")},{Get<bool>(rider,"Riding")},{Field<object>(rider,"visualTrick")},{Get<int>(combo,"PendingScore")},{Get<long>(Get<object>(rider,"Progression"),"LifetimePoints")}"));
                if(frame%150==0) log("Capture frame="+frame+" samples="+samples+" audioPeak="+peak);
            }
            if(landSeen) landedTakes++;
            check(acceptedTricks>=3 && landedTakes>=2,"demo contains three tricks and at least two landed runs");
            check(peak>.001f && samples>AudioSettings.outputSampleRate*Seconds,"captured game audio has nonzero signal and full duration");
            check(Get<long>(Get<object>(rider,"Progression"),"LifetimePoints")>67500,"demo banks newly earned trick XP");
            File.WriteAllText(Path.Combine(root,"demo-info.txt"),$"40 seconds, 1280x720, 30 fps. Actual game frames and synchronized stereo game audio.\nJump 100, stamina 300, ollie impulse 8; flat-ground run-ins seeded at 16/16/3 m/s. No airborne forces or landing bypass. Scripted native crafting/building and trick controls on prepared disposable terrain.\nAudio: {AudioSettings.outputSampleRate} Hz, {samples/2} stereo frames, peak {peak}, RMS {Math.Sqrt(squares/samples)}.\nTricks accepted {acceptedTricks}; runs with landing {landedTakes}.\n");
        }
        finally
        {
            AudioRenderer.Stop();Time.captureFramerate=0;
            audio.Seek(0,SeekOrigin.Begin);audio.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));audio.Write(36+samples*2);audio.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            audio.Write(16);audio.Write((short)1);audio.Write((short)2);audio.Write(AudioSettings.outputSampleRate);audio.Write(AudioSettings.outputSampleRate*4);audio.Write((short)4);audio.Write((short)16);
            audio.Write(System.Text.Encoding.ASCII.GetBytes("data"));audio.Write(samples*2);audio.Dispose();telemetry.Dispose();
            jump.Value=previousJump;hudPosition.Value=previousHud;hudScale.Value=previousScale;if(radioEnabled!=null) radioEnabled.Value=previousRadio;
        }
        yield return new WaitForSeconds(.4f);
    }
}

internal sealed class DemoOverlay : MonoBehaviour
{
    internal string Title="SKAMTEBORD",Detail="";
    private GUIStyle title,detail;
    private void OnGUI()
    {
        if(title==null) {title=new GUIStyle(GUI.skin.label){fontSize=22,fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleCenter,normal={textColor=new Color(1,.8f,.42f)}};detail=new GUIStyle(title){fontSize=14,fontStyle=FontStyle.Normal,normal={textColor=Color.white}};}
        GUI.color=new Color(.03f,.055f,.045f,.88f);GUI.DrawTexture(new Rect(Screen.width*.23f,9,Screen.width*.54f,62),Texture2D.whiteTexture);GUI.color=Color.white;
        GUI.Label(new Rect(Screen.width*.23f,12,Screen.width*.54f,28),Title,title);GUI.Label(new Rect(Screen.width*.23f,40,Screen.width*.54f,25),Detail,detail);
    }
}
