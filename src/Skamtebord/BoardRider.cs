using System.Globalization;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using Skamtebord.Core;
using UnityEngine;

namespace Skamtebord;

internal sealed class BoardRider : MonoBehaviour
{
    private const string RidingKey = "skamtebord.riding";
    private const string PushingKey = "skamtebord.pushing";
    private const string TrickKey = "skamtebord.trick";
    private const string SequenceKey = "skamtebord.trickSequence";
    private const string ExperienceKey = "com.skamtebord.valheim.xp.v1";
    private static readonly System.Reflection.MethodInfo GetSkill = AccessTools.Method(typeof(Skills), "GetSkill");
    private static readonly System.Reflection.MethodInfo NextSkillRequirement = AccessTools.Method(typeof(Skills.Skill), "GetNextLevelRequirement");
    private static readonly System.Reflection.MethodInfo SetCrouch = AccessTools.Method(typeof(Player), "SetCrouch");
    private static readonly AccessTools.FieldRef<Player, bool> AutoRun = AccessTools.FieldRefAccess<Player, bool>("m_autoRun");
    private static readonly AccessTools.FieldRef<Character, Vector3> GroundNormal = AccessTools.FieldRefAccess<Character, Vector3>("m_lastGroundNormal");
    private Player player;
    private Rigidbody body;
    private CapsuleCollider capsule;
    private ZNetView view;
    private ZSyncAnimation animationSync;
    private SkateAnimator skateAnimator;
    private float lastPushTime = -1f;
    private bool publishedPushing;
    private GameObject board;
    private Transform visual;
    private Quaternion originalVisualRotation;
    private bool posing, loaded, wasGrounded;
    private Vector3 controls;
    private bool jumpQueued;
    private float heading, takeoffTime, takeoffSpeed, ignoreGroundUntil, lastVerticalSpeed;
    private float lastAthleticsReward = -30f, lastMaintenance, trickStart, trickDuration;
    private int trickSequence;
    private TrickId visualTrick;
    private bool remoteRiding;
    private float cachedFriction, cachedStaticFriction;
    private PhysicsMaterialCombine cachedCombine;
    private CollisionDetectionMode previousCollisionMode;
    internal bool Riding { get; private set; }
    internal bool Grounded { get; private set; }
    internal bool Pushing => Riding && Grounded && controls.z > .1f && Time.time - lastPushTime < .15f;
    internal ComboSession Combo { get; } = new ComboSession();
    internal SkamtebordProgression Progression { get; private set; } = new SkamtebordProgression();
    internal string Status { get; private set; } = "";
    internal float StatusUntil { get; private set; }
    internal float Speed => body ? Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up).magnitude : 0f;
    internal static BoardRider Local => Player.m_localPlayer ? Player.m_localPlayer.GetComponent<BoardRider>() : null;
    private SkateSettings Settings => SkamtebordPlugin.Instance.Settings;
    private bool IsLocal => player == Player.m_localPlayer && view && view.IsValid() && view.IsOwner();

    private void Awake()
    {
        player = GetComponent<Player>();
        body = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();
        view = GetComponent<ZNetView>();
        animationSync = GetComponent<ZSyncAnimation>();
        var animator = GetComponentInChildren<Animator>();
        visual = animator ? animator.transform : null;
        skateAnimator = new SkateAnimator(animator);
    }

    private void Update()
    {
        if (!IsLocal) return;
        if (!loaded)
        {
            player.m_customData.TryGetValue(ExperienceKey, out var raw);
            long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long points);
            Progression = new SkamtebordProgression(points);
            loaded = true;
            MirrorSkill();
            BoardItem.OfferRecipe(player);
        }
        if (Time.time - lastMaintenance > 1f) { lastMaintenance = Time.time; MirrorSkill(); }
        if (Riding && (!CanRide() || !HasBoard())) Dismount(true);
        if (!InputAllowed()) { controls = Vector3.zero; jumpQueued = false; return; }
        if (Down(Settings.Mount)) Toggle();
        if (Down(Settings.RadioToggle)) SkamtebordPlugin.Instance.Radio?.Toggle();
        if (Down(Settings.RadioNext)) SkamtebordPlugin.Instance.Radio?.NextTrack();
        if (!Riding) return;
        if (!Grounded)
        {
            if (Down(Settings.Shuvit)) Trick(TrickId.Shuvit);
            if (Down(Settings.Kickflip)) Trick(TrickId.Kickflip);
            if (Down(Settings.Heelflip)) Trick(TrickId.Heelflip);
            if (Down(Settings.Grab)) Trick(TrickId.Grab);
            if (Down(Settings.Spin)) Trick(TrickId.ThreeSixty);
        }
    }

    // Valheim 1.0 uses Unity's new input system. Route keys through its compatibility API.
    private static bool Down(ConfigEntry<KeyboardShortcut> entry) => ZInput.GetKeyDown(entry.Value.MainKey, false)
        && entry.Value.Modifiers.All(key => ZInput.GetKey(key, false));

    internal static bool InputAllowed() => !Console.IsVisible() && !Menu.IsVisible() && !InventoryGui.IsVisible()
        && !TextInput.IsVisible() && !Minimap.IsOpen() && !StoreGui.IsVisible()
        && (!Chat.instance || !Chat.instance.HasFocus()) && !Hud.InRadial();

    private bool HasBoard() => player.GetInventory().GetAllItems().Any(BoardItem.IsBoard);

    private bool CanRide() => !player.IsDead() && !player.IsTeleporting() && !player.IsSwimming()
        && !player.IsAttached() && !player.InIntro() && !player.InDodge() && !player.InAttack()
        && !player.IsStaggering() && !player.IsKnockedBack() && !player.IsEncumbered()
        && !player.InPlaceMode() && !player.IsDebugFlying() && player.CanMove() && !player.InEmote();

    internal void Toggle()
    {
        if (!IsLocal) return;
        if (Riding) { Dismount(false); return; }
        if (!HasBoard()) { Say("Craft a Skamtebord first: 8 wood, 4 resin, 2 leather scraps."); return; }
        if (!CanRide() || !player.IsOnGround()) { Say("Find solid ground and free your hands before skating."); return; }
        Riding = true;
        Grounded = wasGrounded = true;
        heading = player.transform.eulerAngles.y;
        controls = Vector3.zero;
        lastPushTime = -1f;
        Combo.Bail();
        AutoRun(player) = false;
        SetCrouch.Invoke(player, new object[] { false });
        cachedFriction = capsule.material.dynamicFriction;
        cachedStaticFriction = capsule.material.staticFriction;
        cachedCombine = capsule.material.frictionCombine;
        previousCollisionMode = body.collisionDetectionMode;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.WakeUp();
        player.HideHandItems(animation: false);
        view.GetZDO().Set(RidingKey, true);
        SkamtebordPlugin.Instance.Radio?.SetSkating(true);
        Say("Push forward. Find a hill. Land something ridiculous.");
    }

    internal void CaptureControls(Vector3 move, bool jump)
    {
        controls = InputAllowed() ? move : Vector3.zero;
        jumpQueued |= InputAllowed() && jump;
    }

    // Called from Character.UpdateWalking, inside the owner's normal physics update.
    // Character's surrounding motion/ground code still handles falling, damage and network sync.
    internal bool Step(float dt)
    {
        if (!Riding || !IsLocal) return false;
        if (!CanRide()) { Dismount(true); return false; }
        if (!InputAllowed()) { controls = Vector3.zero; jumpQueued = false; }
        body.useGravity = true;
        Grounded = Time.time >= ignoreGroundUntil && player.IsOnGround();
        // 1.0.15's GetLastGroundNormal returns the transient contact accumulator, already reset
        // by UpdateGroundContact before this callback. Use the persisted contact normal instead.
        var normal = Grounded ? GroundNormal(player) : Vector3.up;
        if (normal.sqrMagnitude < .1f) normal = Vector3.up;
        normal.Normalize();

        Combo.Tick(dt);
        if (!Grounded && wasGrounded) BeginAir();
        if (Grounded && !wasGrounded)
        {
            bool safe = lastVerticalSpeed > -12f && normal.y > .55f && Time.time >= trickStart + trickDuration;
            bool landed = Combo.Land(Time.time - takeoffTime, Mathf.Min(takeoffSpeed, Speed), safe);
            if (!safe) { Dismount(true); Say("BAIL — land with the board under you."); return false; }
            if (!landed && Combo.TrickCount == 0) Say("Roll faster and finish the trick before landing.");
        }
        wasGrounded = Grounded;
        var bank = Combo.Bank();
        if (bank != null) Award(bank);

        Vector3 velocity = body.linearVelocity;
        float speed = Vector3.ProjectOnPlane(velocity, normal).magnitude;
        float steering = Settings.TurnSpeed.Value * Mathf.Lerp(1f, .42f, speed / Settings.MaximumSpeed.Value);
        heading += controls.x * steering * dt * (Grounded ? 1f : .45f);
        Quaternion rotation = Quaternion.Euler(0f, heading, 0f);
        body.MoveRotation(rotation);
        body.angularVelocity = Vector3.zero;
        Vector3 forward = Vector3.ProjectOnPlane(rotation * Vector3.forward, normal).normalized;
        Vector3 right = Vector3.Cross(normal, forward).normalized;

        if (Grounded)
        {
            // Unity gravity projects itself into the slope through the low-friction contact.
            // Directional grip removes lateral slip without overwriting downhill momentum.
            float sideways = Vector3.Dot(velocity, right);
            body.AddForce(-right * sideways * Mathf.Min(1f, 7f * dt), ForceMode.VelocityChange);
            Vector3 tangent = Vector3.ProjectOnPlane(velocity, normal);
            float brake = controls.z < -.1f ? Settings.BrakeStrength.Value * -controls.z : .32f;
            if (tangent.sqrMagnitude > .001f)
                body.AddForce(-tangent.normalized * Mathf.Min(tangent.magnitude, brake * dt), ForceMode.VelocityChange);
            if (controls.z > .1f && Vector3.Dot(velocity, forward) < Settings.PushTopSpeed.Value && player.HaveStamina(2f * dt))
            {
                body.AddForce(forward * (Settings.PushAcceleration.Value * controls.z), ForceMode.Acceleration);
                player.UseStamina(2f * dt);
                lastPushTime = Time.time;
            }
            if (jumpQueued && player.HaveStamina(5f))
            {
                player.UseStamina(5f);
                Vector3 launch = body.linearVelocity;
                launch.y = Mathf.Max(launch.y, Settings.JumpSpeed.Value);
                // effects:false avoids vanilla OnJump stamina/athletics progression.
                player.ForceJump(launch, effects: false);
                animationSync?.SetTrigger("jump");
                ignoreGroundUntil = Time.time + .18f;
                Grounded = wasGrounded = false;
                BeginAir();
                Trick(TrickId.Ollie);
            }
        }
        jumpQueued = false;
        // A soft speed ceiling retains vertical velocity and collision response.
        var horizontal = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
        if (horizontal.magnitude > Settings.MaximumSpeed.Value)
            body.AddForce(-horizontal.normalized * (horizontal.magnitude - Settings.MaximumSpeed.Value) * Mathf.Min(1, dt * 3), ForceMode.VelocityChange);
        lastVerticalSpeed = body.linearVelocity.y;
        animationSync?.SetFloat("forward_speed", Pushing ? 2.5f : 0f);
        animationSync?.SetFloat("sideway_speed", 0f);
        animationSync?.SetBool("onGround", Grounded);
        if (publishedPushing != Pushing)
        {
            publishedPushing = Pushing;
            view.GetZDO().Set(PushingKey, publishedPushing);
        }
        return true;
    }

    internal void ApplyFriction()
    {
        if (!Riding || !capsule) return;
        capsule.material.dynamicFriction = 0f;
        capsule.material.staticFriction = 0f;
        capsule.material.frictionCombine = PhysicsMaterialCombine.Minimum;
    }

    private void BeginAir()
    {
        takeoffTime = Time.time;
        takeoffSpeed = Speed;
        Combo.BeginAirborne();
    }

    private void Trick(TrickId id)
    {
        var definition = TrickCatalog.Get(id);
        if (Progression.Level < definition.MinimumSkillLevel)
        {
            Say($"{definition.Name} unlocks at Skamtebord {definition.MinimumSkillLevel}.");
            return;
        }
        if (!Combo.TryAddTrick(id, Progression.Level)) return;
        AnimateTrick(id);
        view.GetZDO().Set(TrickKey, (int)id);
        view.GetZDO().Set(SequenceKey, ++trickSequence);
    }

    private void AnimateTrick(TrickId id)
    {
        visualTrick = id;
        trickStart = Time.time;
        trickDuration = TrickCatalog.Get(id).DurationSeconds;
    }

    private void Award(BankResult result)
    {
        int oldLevel = Progression.Level;
        Progression.AddPoints(result.Experience);
        player.m_customData[ExperienceKey] = Progression.LifetimePoints.ToString(CultureInfo.InvariantCulture);
        MirrorSkill();
        Say($"+{result.Points:N0} Skamtebord XP" + (Progression.Level > oldLevel ? $"  •  Level {Progression.Level}" : ""));
        if (Settings.AthleticsExperience.Value && Time.time - lastAthleticsReward >= 30f)
        {
            player.GetSkills().RaiseSkill(Skills.SkillType.Jump, .05f);
            lastAthleticsReward = Time.time;
        }
    }

    private void MirrorSkill()
    {
        var skill = (Skills.Skill)GetSkill.Invoke(player.GetSkills(), new object[] { SkamtebordPlugin.Instance.SkateSkill });
        skill.m_level = Progression.Level + (Progression.Level < 100 ? Progression.LevelProgress : 0);
        // Keep the vanilla skill-panel progress bar consistent with our own XP curve.
        skill.m_accumulator = Progression.Level < 100
            ? (float)NextSkillRequirement.Invoke(skill, null) * Progression.LevelProgress : 0f;
    }

    internal void Dismount(bool bail)
    {
        if (!Riding) return;
        Riding = false;
        controls = Vector3.zero;
        jumpQueued = false;
        lastPushTime = -1f;
        publishedPushing = false;
        Combo.Bail(); // Exiting never banks an unfinished combo.
        if (view && view.IsValid() && view.IsOwner())
        {
            view.GetZDO().Set(RidingKey, false);
            view.GetZDO().Set(PushingKey, false);
        }
        skateAnimator?.Dispose();
        if (capsule)
        {
            capsule.material.dynamicFriction = cachedFriction;
            capsule.material.staticFriction = cachedStaticFriction;
            capsule.material.frictionCombine = cachedCombine;
        }
        if (body) body.collisionDetectionMode = previousCollisionMode;
        if (body && Grounded) // Prevent a fast dismount from becoming an on-foot launch.
        {
            Vector3 v = body.linearVelocity;
            Vector3 h = Vector3.ClampMagnitude(new Vector3(v.x, 0, v.z), 5f);
            body.linearVelocity = new Vector3(h.x, v.y, h.z);
        }
        SkamtebordPlugin.Instance?.Radio?.SetSkating(false);
        RestorePose();
        if (bail) Say("BAIL — combo lost.");
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!Riding || !IsLocal || collision.relativeVelocity.magnitude < 7f) return;
        for (int i = 0; i < collision.contactCount; i++)
        {
            var contact = collision.GetContact(i);
            if (Mathf.Abs(contact.normal.y) < .4f && Vector3.Dot(collision.relativeVelocity, contact.normal) < -6f)
            { Dismount(true); break; }
        }
    }

    private void LateUpdate()
    {
        if (!view || !view.IsValid()) return;
        bool active = IsLocal ? Riding : view.GetZDO().GetBool(RidingKey);
        if (!active || player.IsDead()) { if (board) board.SetActive(false); skateAnimator?.Dispose(); RestorePose(); remoteRiding = false; return; }
        skateAnimator?.Mount();
        skateAnimator?.SetPushing(IsLocal ? Pushing : view.GetZDO().GetBool(PushingKey));
        if (!board) board = BoardModel.Create(transform);
        board.SetActive(true);
        if (!IsLocal)
        {
            int sequence = view.GetZDO().GetInt(SequenceKey);
            int id = view.GetZDO().GetInt(TrickKey);
            if (remoteRiding && sequence != trickSequence && id >= 0 && id <= (int)TrickId.ThreeSixty) AnimateTrick((TrickId)id);
            trickSequence = sequence;
            remoteRiding = true;
        }
        float phase = trickDuration > 0 ? Mathf.Clamp01((Time.time - trickStart) / trickDuration) : 1f;
        float roll = 0, yaw = 0, lift = 0;
        if (phase < 1)
        {
            if (visualTrick == TrickId.Kickflip) roll = 360 * phase;
            if (visualTrick == TrickId.Heelflip) roll = -360 * phase;
            if (visualTrick == TrickId.Shuvit) yaw = 180 * phase;
            if (visualTrick == TrickId.ThreeSixty) yaw = 360 * phase;
            if (visualTrick == TrickId.Grab) { lift = Mathf.Sin(phase * Mathf.PI) * .3f; roll = Mathf.Sin(phase * Mathf.PI) * 35f; }
        }
        var groundNormal = player.IsOnGround() ? GroundNormal(player) : Vector3.up;
        if (groundNormal.sqrMagnitude < .1f) groundNormal = Vector3.up;
        Quaternion tilt = Quaternion.FromToRotation(Vector3.up, transform.InverseTransformDirection(groundNormal));
        board.transform.localPosition = new Vector3(0, .14f + lift, 0);
        board.transform.localRotation = tilt * Quaternion.Euler(0, yaw, roll);
        if (visual)
        {
            if (!posing) { originalVisualRotation = visual.localRotation; posing = true; }
            visual.localRotation = originalVisualRotation * Quaternion.Euler(0, (skateAnimator?.Active == true ? 0 : 70) + (visualTrick == TrickId.ThreeSixty && phase < 1 ? yaw : 0), 0);
        }
    }

    private void RestorePose()
    {
        if (posing && visual) visual.localRotation = originalVisualRotation;
        posing = false;
    }

    private void Say(string text) { Status = text; StatusUntil = Time.time + 4f; player.Message(MessageHud.MessageType.TopLeft, text); }

    private void OnDestroy()
    {
        Dismount(true);
        skateAnimator?.Dispose();
        RestorePose();
        if (board) Destroy(board);
    }
}
