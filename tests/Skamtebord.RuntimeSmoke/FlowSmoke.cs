using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Skamtebord.RuntimeSmoke;

internal static class FlowSmoke
{
    private static object Call(object target, string method, params object[] args) => AccessTools.Method(target.GetType(), method).Invoke(target, args);
    private static T Get<T>(object target, string property) => (T)AccessTools.Property(target.GetType(), property).GetValue(target);

    internal static GameObject CreatePipe(Vector3 origin)
    {
        var prefab = ZNetScene.instance.GetPrefab("Skamtebord_Halfpipe");
        var pipe = UnityEngine.Object.Instantiate(prefab, origin, Quaternion.identity);
        if (Player.m_localPlayer)
            pipe.GetComponent<Piece>().SetCreator(Player.m_localPlayer.GetPlayerID(),UserInfo.GetLocalUser().UserId);
        pipe.GetComponent<WearNTear>().OnPlaced();
        return pipe;
    }

    internal static IEnumerator Run(Player player, Component rider, GameObject platform, string root, Action<bool,string> check, Action<string> log)
    {
        var prefab = ZNetScene.instance.GetPrefab("Skamtebord_Halfpipe");
        check(prefab, "halfpipe prefab registered in live network scene");
        var piece = prefab.GetComponent<Piece>();
        check(piece.m_resources.Length == 1 && piece.m_resources[0].m_resItem.name == "Wood" && piece.m_resources[0].m_amount == 80,
            "halfpipe costs 80 wood with recoverable building resources");
        check(piece.m_resources[0].m_recover && piece.m_craftingStation.name == "piece_workbench", "halfpipe uses workbench and returns wood on removal");
        check(ObjectDB.instance.GetItemPrefab("Hammer").GetComponent<ItemDrop>().m_itemData.m_shared.m_buildPieces.m_pieces.Contains(prefab),
            "halfpipe is available in the ordinary Hammer build table");
        var body = player.GetComponent<Rigidbody>();
        var center = platform.transform.position + new Vector3(0, 1.28f, 18f);
        var pipe = CreatePipe(center);
        check(pipe.GetComponent<ZNetView>().IsValid() && pipe.GetComponent<ZNetView>().m_persistent, "placed halfpipe has a persistent network object");
        check(pipe.GetComponentInChildren<MeshCollider>().sharedMesh.triangles.Length > 1000, "halfpipe has real curved collision geometry");
        var oldCam = Utils.GetMainCamera();
        oldCam.enabled = false;
        var gameCam = oldCam.GetComponent<GameCamera>();
        if (gameCam) gameCam.enabled = false;
        var camera = new GameObject("Flow QA camera").AddComponent<Camera>();
        camera.tag = "MainCamera"; camera.nearClipPlane = .05f; camera.farClipPlane = 500;
        camera.transform.position = center + new Vector3(17, 9, -14);
        camera.transform.LookAt(center + Vector3.up * 2);
        camera.renderingPath = RenderingPath.Forward;
        Screen.SetResolution(1280,720,FullScreenMode.Windowed);
        yield return new WaitForSeconds(.15f);
        Screen.SetResolution(1920,1080,FullScreenMode.Windowed);
        yield return new WaitForSeconds(.3f);
        if (EnvMan.instance)
        {
            EnvMan.instance.m_debugTimeOfDay = true; EnvMan.instance.m_debugTime = .42f;
            EnvMan.instance.m_debugEnv = "Clear"; EnvMan.instance.ForceInstantEnvironmentSwitch();
        }
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(Path.Combine(root, "halfpipe-overview.png"));
        yield return new WaitForEndOfFrame();

        float rollApex = 0;
        foreach (int trial in new[] { 0, 1, 2 })
        {
            float sign = trial == 1 ? -1f : 1f;
            bool timedJump = trial == 2, jumpSent = false;
            string label = timedJump ? "timed-lip-jump" : sign.ToString();
            if (Get<bool>(rider,"Riding")) Call(rider,"Dismount",false);
            body.position = center + new Vector3(0,.4f,0);
            body.rotation = Quaternion.Euler(0, sign > 0 ? 0 : 180,0);
            player.transform.SetPositionAndRotation(body.position,body.rotation);
            player.ForceJump(Vector3.zero,false);
            Physics.SyncTransforms();
            yield return new WaitForSeconds(.6f);
            Call(rider,"Toggle");
            player.AddStamina(100f);
            check(Get<bool>(rider,"Riding"), $"halfpipe direction {sign}: mount on flat bottom");
            body.linearVelocity = new Vector3(0,0,sign * 16f);
            var telemetry = new List<string> { "time,x,y,z,vx,vy,vz,grounded,up_y,forward_y" };
            float start = Time.time, maxY = 0, maxTilt = 0;
            bool launched = false;
            Vector3 launch = Vector3.zero;
            while (Time.time - start < 4f)
            {
                bool jump = timedJump && !jumpSent && body.position.y-center.y > 3.7f && Get<bool>(rider,"Grounded");
                Call(rider,"CaptureControls", Vector3.zero,jump);
                jumpSent |= jump;
                yield return new WaitForFixedUpdate();
                Vector3 p = body.position - center, v = body.linearVelocity;
                bool supported = Get<bool>(rider,"Grounded");
                telemetry.Add(FormattableString.Invariant($"{Time.time-start:F3},{p.x:F4},{p.y:F4},{p.z:F4},{v.x:F4},{v.y:F4},{v.z:F4},{supported},{player.transform.up.y:F4},{player.transform.forward.y:F4}"));
                maxY = Mathf.Max(maxY,p.y); maxTilt = Mathf.Max(maxTilt,Vector3.Angle(player.transform.up,Vector3.up));
                if (!launched && !supported && p.y > 3.4f && v.y > 1)
                {
                    launched = true; launch = v;
                    log($"LIP sign={sign} p={p} v={v} tilt={maxTilt}");
                    yield return new WaitForEndOfFrame();
                    ScreenCapture.CaptureScreenshot(Path.Combine(root, $"halfpipe-lip-{label}.png"));
                }
                if (!Get<bool>(rider,"Riding")) break;
            }
            File.WriteAllLines(Path.Combine(root,$"halfpipe-{label}.csv"),telemetry);
            log($"PIPE sign={sign}: launched={launched} v={launch} maxY={maxY} tilt={maxTilt} riding={Get<bool>(rider,"Riding")}");
            check(launched, $"halfpipe direction {sign}: current momentum carries rider off the curved lip");
            check(launch.y > 4 && new Vector2(launch.x,launch.z).magnitude < launch.y * .2f, $"halfpipe direction {sign}: launch is within 12 degrees of vertical");
            check(maxTilt > 75, $"halfpipe direction {sign}: rider and board follow the wall orientation");
            check(maxY > 4.5f, $"halfpipe direction {sign}: momentum supplies air above the lip");
            // The timed ollie clears the lip and drifts outside the pipe. Its
            // subsequent >12 m/s impact on the flat floor is a real hard landing,
            // not a failed curved transition. Ground bookkeeping now catches it.
            Vector3 finish = body.position-center;
            bool hardFlatLanding = timedJump && !Get<bool>(rider,"Riding") && finish.z>7.5f && finish.y<.4f
                && Get<string>(rider,"Status").Contains("land with the board");
            check(Get<bool>(rider,"Riding") || hardFlatLanding, $"halfpipe direction {sign}: the curve does not trigger a wall bail");
            if(timedJump) check(hardFlatLanding,"high timed ollie outside the pipe correctly bails on its hard flat landing");
            if (trial == 0) rollApex = maxY;
            if (timedJump) check(jumpSent && maxY > rollApex+1.5f,"timed jump near the halfpipe lip adds height to the same approach momentum");
        }
        Call(rider,"Dismount",false);
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();
        check(Vector3.Angle(player.transform.up,Vector3.up) < 1f, "dismount restores upright player rotation");
        body.position = platform.transform.position + new Vector3(-25,1.4f,-25);
        body.rotation = Quaternion.identity;
        player.transform.SetPositionAndRotation(body.position,body.rotation);
        player.ForceJump(Vector3.zero,false);
        Physics.SyncTransforms();
        yield return new WaitForSeconds(.6f);
        var input = EasyInputSmoke.Run(player,rider,root,check,log);
        while (input.MoveNext()) yield return input.Current;
        var wear = pipe.GetComponent<WearNTear>();
        float before = wear.GetHealthPercentage();
        wear.ApplyDamage(100);
        yield return null;
        check(wear.GetHealthPercentage() < before && pipe.GetComponentInChildren<MeshRenderer>().enabled,"halfpipe takes ordinary building damage and keeps its visual");
        wear.Repair(); yield return null;
        check(wear.GetHealthPercentage() > .99f,"halfpipe supports ordinary repair");
        int WoodCount() => UnityEngine.Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None)
            .Where(i=>i.m_itemData.m_shared.m_name=="$item_wood").Sum(i=>i.m_itemData.m_stack);
        int woodBefore = WoodCount();
        wear.Remove(); yield return new WaitForSeconds(.2f);
        check(!pipe && WoodCount()-woodBefore == 80,"normal halfpipe removal destroys the network object and refunds 80 wood");
    }
}
