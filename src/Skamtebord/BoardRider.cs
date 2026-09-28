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
    private const string SprintingKey = "skamtebord.sprinting";
    private const string TrickKey = "skamtebord.trick";
    private const string SequenceKey = "skamtebord.trickSequence";
    private const string TrickTimeKey = "skamtebord.trickTime";
    private const string GrabKey = "skamtebord.grabHeld";
    private const string LeanKey = "skamtebord.lean";
    internal const string SurfaceRotationKey = "skamtebord.surfaceRotationUsed";
    private const string ExperienceKey = "com.skamtebord.valheim.xp.v1";
    private static readonly System.Reflection.MethodInfo GetSkill = AccessTools.Method(typeof(Skills), "GetSkill");
    private static readonly System.Reflection.MethodInfo NextSkillRequirement = AccessTools.Method(typeof(Skills.Skill), "GetNextLevelRequirement");
    private static readonly System.Reflection.MethodInfo SetCrouch = AccessTools.Method(typeof(Player), "SetCrouch");
    private static readonly AccessTools.FieldRef<Player, bool> AutoRun = AccessTools.FieldRefAccess<Player, bool>("m_autoRun");
    private static readonly AccessTools.FieldRef<Character, Vector3> GroundNormal = AccessTools.FieldRefAccess<Character, Vector3>("m_lastGroundNormal");
    private static readonly AccessTools.FieldRef<Character, Vector3> LastGroundPoint = AccessTools.FieldRefAccess<Character, Vector3>("m_lastGroundPoint");
    private static readonly AccessTools.FieldRef<Character, bool> GroundContact = AccessTools.FieldRefAccess<Character, bool>("m_groundContact");
    private static readonly AccessTools.FieldRef<Character, Vector3> PendingGroundNormal = AccessTools.FieldRefAccess<Character, Vector3>("m_groundContactNormal");
    private static readonly AccessTools.FieldRef<Character, Vector3> PendingGroundPoint = AccessTools.FieldRefAccess<Character, Vector3>("m_groundContactPoint");
    private static readonly AccessTools.FieldRef<Character, Collider> PendingGroundCollider = AccessTools.FieldRefAccess<Character, Collider>("m_lowestContactCollider");
    private const float JumpGraceSeconds = .12f;
    private Player player;
    private Rigidbody body;
    private CapsuleCollider capsule;
    private ZNetView view;
    private ZSyncAnimation animationSync;
    private SkateAnimator skateAnimator;
    private bool publishedPushing;
    private bool sprintRequested, publishedSprinting;
    private GameObject board;
    private Transform visual;
    private Quaternion originalVisualRotation;
    private bool posing, loaded, wasGrounded;
    private Vector3 controls;
    private bool jumpQueued;
    private bool jumpHeld, grabHeld, grabUsedThisHold;
    private float jumpHoldTime, randomTrickUntil, nextPoseSync;
    private Vector3 contactNormal = Vector3.up, surfaceNormal = Vector3.up, surfaceForward = Vector3.forward;
    private float contactTime = float.NegativeInfinity, lean;
    private Collider contactCollider;
    private Vector3 contactPoint, contactNormalSum;
    private float contactWeight, bestContactAlignment;
    private bool probeContact;
    private static readonly int SupportMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "blocker", "vehicle");
    private Vector3 lastStepPosition;
    private Vector3 lastVelocity;
    private bool hasSurfaceFrame;
    private float previousHeading;
    private readonly System.Random trickRandom = new System.Random();
    private bool ollieAvailable;
    private float lastSupportedTime = float.NegativeInfinity;
    private float heading, takeoffTime, takeoffSpeed, ignoreGroundUntil;
    private float lastAthleticsReward = -30f, lastMaintenance, trickStart, trickDuration;
    private int trickSequence;
    private TrickId visualTrick;
    private bool receivedRemoteRide;
    private float cachedFriction, cachedStaticFriction;
    private PhysicsMaterialCombine cachedCombine;
    private CollisionDetectionMode previousCollisionMode;
    internal bool Riding { get; private set; }
    internal bool Grounded { get; private set; }
    internal bool Sprinting { get; private set; }
    internal bool Pushing => Riding && Grounded && surfaceNormal.y > .3f && controls.z > .1f && !Sprinting && player.HaveStamina(.1f);
    internal bool Grabbing => grabHeld;
    internal Vector3 SurfaceNormal => surfaceNormal;
    internal ComboSession Combo { get; } = new ComboSession();
    internal SkamtebordProgression Progression { get; private set; } = new SkamtebordProgression();
    internal string Status { get; private set; } = "";
    internal float StatusUntil { get; private set; }
    internal float Speed => body ? body.linearVelocity.magnitude : 0f;
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
        if (!IsLocal) { if (Riding) Dismount(true); return; }
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
        if (!InputAllowed()) { ClearInput(); return; }
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
        ollieAvailable = true;
        lastSupportedTime = Time.time;
        ignoreGroundUntil = 0f;
        heading = player.transform.eulerAngles.y;
        previousHeading = heading;
        surfaceNormal = Vector3.up;
        // Seed the first skating tick from the ground on which mounting was allowed.
        // Later ticks use our actual board-relative contacts, not walking grace.
        contactCollider = player.GetLastGroundCollider();
        contactNormal = GroundNormal(player).normalized;
        contactPoint = LastGroundPoint(player);
        contactTime = Time.fixedTime;
        contactWeight = 0f;
        probeContact = false;
        surfaceForward = Quaternion.Euler(0, heading, 0) * Vector3.forward;
        lastStepPosition = body.position;
        hasSurfaceFrame = false;
        ClearInput();
        controls = Vector3.zero;
        sprintRequested = Sprinting = false;
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
        view.GetZDO().Set(SurfaceRotationKey, true);
        SkamtebordPlugin.Instance.Radio?.SetSkating(true);
        Say("Push forward. Find a hill. Land something ridiculous.");
    }

    internal void CaptureControls(Vector3 move, bool jump)
    {
        controls = InputAllowed() ? move : Vector3.zero;
        jumpQueued |= InputAllowed() && jump;
    }

    internal void CaptureSprint(bool sprint) => sprintRequested = InputAllowed() && sprint;

    internal void CaptureJumpHeld(bool held) => jumpHeld = InputAllowed() && held;

    private void ClearInput()
    {
        controls = Vector3.zero;
        jumpQueued = jumpHeld = grabHeld = grabUsedThisHold = sprintRequested = false;
        jumpHoldTime = randomTrickUntil = 0f;
    }

    // Called from Character.UpdateWalking, inside the owner's normal physics update.
    // Character's surrounding motion/ground code still handles falling, damage and network sync.
    internal bool Step(float dt)
    {
        if (!Riding || !IsLocal) return false;
        if (!CanRide()) { Dismount(true); return false; }
        if (!InputAllowed()) ClearInput();
        body.useGravity = true;
        // Only current, accepted support contacts apply grip or pushing. There is
        // no downward adhesion: Unity collision/gravity decide when a crest releases.
        Grounded = HasSupport(dt);
        if (Grounded)
        {
            lastSupportedTime = Time.time;
            ollieAvailable = true;
        }
        var normal = Grounded ? contactNormal : surfaceNormal;
        if (normal.sqrMagnitude < .1f) normal = Vector3.up;
        normal.Normalize();
        if (!hasSurfaceFrame || Vector3.Distance(body.position, lastStepPosition) > 5f || Mathf.Abs(Mathf.DeltaAngle(previousHeading, heading)) > .01f)
        {
            surfaceForward = Quaternion.Euler(0, heading, 0) * Vector3.forward;
            surfaceNormal = Vector3.up;
            hasSurfaceFrame = true;
        }
        lastStepPosition = body.position;
        Sprinting = Grounded && sprintRequested && controls.z > .1f && player.HaveStamina(6f * dt);

        Combo.Tick(dt);
        if (!Grounded && wasGrounded) BeginAir();
        if (Grounded && !wasGrounded)
        {
            // Impact perpendicular to a transition matters, not its vertical descent speed.
            bool safe = Vector3.Dot(lastVelocity, normal) > -12f
                && Time.time >= trickStart + trickDuration;
            bool landed = Combo.Land(Time.time - takeoffTime, Mathf.Min(takeoffSpeed, Speed), safe);
            if (!safe) { Dismount(true); Say("BAIL — land with the board under you."); return false; }
            if (!landed && Combo.TrickCount == 0) Say("Roll faster and finish the trick before landing.");
        }
        wasGrounded = Grounded;
        var bank = Combo.Bank();
        if (bank != null) Award(bank);

        Vector3 velocity = body.linearVelocity;
        float speed = Vector3.ProjectOnPlane(velocity, normal).magnitude;
        float steering = Settings.TurnSpeed.Value * 1.3f / (1f + speed / 18f);
        if (Grounded)
        {
            surfaceForward = Quaternion.FromToRotation(surfaceNormal, normal) * surfaceForward;
            surfaceNormal = normal;
            surfaceForward = Vector3.ProjectOnPlane(surfaceForward, normal).normalized;
            if (surfaceForward.sqrMagnitude < .1f) surfaceForward = Vector3.Cross(transform.right, normal).normalized;
        }
        surfaceForward = Quaternion.AngleAxis(controls.x * steering * dt * (Grounded ? 1f : .5f), surfaceNormal) * surfaceForward;
        // In flight, retain launch direction through the apex and gently follow the trajectory.
        // No velocity is injected: this only poses the rider/collider for the return transition.
        if (!Grounded && velocity.sqrMagnitude > 1f)
        {
            Vector3 trajectory = velocity.normalized;
            if (Vector3.Dot(trajectory, surfaceForward) > -.1f)
                surfaceForward = Vector3.Slerp(surfaceForward, trajectory, 1f - Mathf.Exp(-4f * dt)).normalized;
            surfaceNormal = Vector3.ProjectOnPlane(surfaceNormal, surfaceForward).normalized;
            if (surfaceNormal.sqrMagnitude < .1f) surfaceNormal = Vector3.ProjectOnPlane(Vector3.up, surfaceForward).normalized;
        }
        Quaternion rotation = Quaternion.LookRotation(surfaceForward, surfaceNormal);
        if (Vector3.ProjectOnPlane(surfaceForward, Vector3.up).sqrMagnitude > .01f)
            heading = Mathf.Atan2(surfaceForward.x, surfaceForward.z) * Mathf.Rad2Deg;
        previousHeading = heading;
        body.MoveRotation(rotation);
        body.angularVelocity = Vector3.zero;
        Vector3 forward = surfaceForward;
        lean = Mathf.MoveTowards(lean, -controls.x * Mathf.Clamp(speed / 12f, 0, 1f) * 14f, dt * 100f);

        if (Grounded)
        {
            // Unity gravity projects itself into the slope through the low-friction contact.
            // Trucks redirect the rolling velocity in the actual support plane.
            // Removing its sideways component every tick loses terrain-earned
            // speed in corners; rotation preserves energy without adding any.
            Vector3 tangent = Vector3.ProjectOnPlane(velocity, normal);
            Vector3 rolling = forward * (Vector3.Dot(tangent, forward) >= 0f ? tangent.magnitude : -tangent.magnitude);
            float gripAngle = Vector3.Angle(tangent, rolling) * Mathf.Deg2Rad * (1f - Mathf.Exp(-12f * dt));
            Vector3 carved = Vector3.RotateTowards(tangent, rolling, gripAngle, 0f);
            float brake = controls.z < -.1f ? Settings.BrakeStrength.Value * -controls.z : .32f;
            carved = Vector3.MoveTowards(carved, Vector3.zero, brake * dt);
            float staminaRate = Sprinting ? 6f : 2f;
            float topSpeed = Sprinting ? Settings.SprintTopSpeed.Value : Settings.PushTopSpeed.Value;
            // A wall ride is carried by momentum. A planted foot cannot push up
            // a vertical face; gravity is always free to slow and reverse the roll.
            float pushSupport = Mathf.Clamp01(normal.y);
            if (pushSupport > .05f && controls.z > .1f && tangent.magnitude < topSpeed && player.HaveStamina(staminaRate * dt))
            {
                // Budget only the foot's contribution, using total surface speed.
                // Steering/sliding must not reopen acceleration above the cap,
                // and the last push step must not overshoot it. Gravity is separate.
                float push = (Sprinting ? Settings.SprintAcceleration.Value : Settings.PushAcceleration.Value)
                    * Mathf.Clamp01(controls.z) * pushSupport * dt;
                carved += forward * Mathf.Min(push, Mathf.Max(0f, topSpeed - carved.magnitude));
                if (!Sprinting) player.UseStamina(staminaRate * dt);
            }
            if (Sprinting) player.UseStamina(6f * dt);
            // Optional downhill governor acts only along supported terrain.
            // Never cap a launch or damp world-horizontal velocity in flight.
            if (Settings.MaximumSpeed.Value > 0f)
            {
                float limit = Mathf.Max(Settings.MaximumSpeed.Value, Mathf.Max(Settings.PushTopSpeed.Value, Settings.SprintTopSpeed.Value));
                if (carved.magnitude > limit)
                    carved = Vector3.MoveTowards(carved, carved.normalized * limit, (carved.magnitude - limit) * Mathf.Min(1f, dt * 3f));
            }
            body.AddForce(carved - tangent, ForceMode.VelocityChange);
        }
        bool tookOff = false;
        if (jumpQueued && ollieAvailable && Time.time - lastSupportedTime <= JumpGraceSeconds && player.HaveStamina(5f))
        {
            player.UseStamina(5f);
            float approachSpeed = Speed;
            // The collider already redirects velocity up the ramp. Keep that full
            // momentum and add the ollie impulse; never replace its vertical part.
            float uphill = Mathf.Max(0, Vector3.Dot(surfaceForward, Vector3.up));
            Vector3 jumpDirection = Vector3.Slerp(surfaceNormal, surfaceForward, uphill * uphill).normalized;
            Vector3 launch = body.linearVelocity + jumpDirection * Settings.JumpSpeed.Value;
            player.ForceJump(launch, effects: false); // No vanilla jump XP/stamina charge.
            ollieAvailable = false;
            ignoreGroundUntil = Time.time + .18f;
            if (Grounded)
            {
                BeginAir();
                takeoffSpeed = approachSpeed; // The jump impulse itself is not approach momentum.
            } // A grace-window ollie continues the same flight.
            Grounded = wasGrounded = false;
            Sprinting = false;
            Trick(TrickId.Ollie);
            tookOff = true;
        }
        UpdateEasyTricks(dt, tookOff);
        jumpQueued = false;
        lastVelocity = body.linearVelocity;
        animationSync?.SetFloat("forward_speed", Pushing ? 2.5f : 0f);
        animationSync?.SetFloat("sideway_speed", 0f);
        animationSync?.SetBool("onGround", Grounded);
        animationSync?.SetBool("falling", false); // Keep the skating stance while airborne.
        if (publishedPushing != Pushing)
        {
            publishedPushing = Pushing;
            view.GetZDO().Set(PushingKey, publishedPushing);
        }
        if (publishedSprinting != Sprinting)
        {
            publishedSprinting = Sprinting;
            view.GetZDO().Set(SprintingKey, publishedSprinting);
        }
        view.GetZDO().Set(GrabKey, grabHeld);
        if (Time.time >= nextPoseSync)
        {
            view.GetZDO().Set(LeanKey, lean);
            nextPoseSync = Time.time + .1f;
        }
        return true;
    }

    private void UpdateEasyTricks(float dt, bool tookOff)
    {
        if (Grounded) { grabHeld = false; randomTrickUntil = 0; }
        if (!jumpHeld) { jumpHoldTime = 0; grabUsedThisHold = false; grabHeld = false; }
        else if (!Grounded) jumpHoldTime += dt;
        // An airborne tap queues one unlocked trick, even if the ollie is still finishing.
        // Holding that tap instead becomes a grab; it never produces a second jump impulse.
        if (jumpQueued && !Grounded && !tookOff) randomTrickUntil = Time.time + .35f;
        if (!Grounded && jumpHeld && jumpHoldTime >= .18f && !grabUsedThisHold)
        {
            randomTrickUntil = 0;
            if (Trick(TrickId.Grab)) { grabUsedThisHold = true; grabHeld = true; }
        }
        if (!Grounded && randomTrickUntil > Time.time && !jumpHeld && Combo.TrickCooldownSeconds <= .0001f)
        {
            var choices = TrickCatalog.All.Where(t => t.Id != TrickId.Ollie && t.Id != TrickId.Grab && t.MinimumSkillLevel <= Progression.Level).ToArray();
            if (choices.Length > 0) Trick(choices[trickRandom.Next(choices.Length)].Id);
            randomTrickUntil = 0;
        }
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

    private bool Trick(TrickId id)
    {
        var definition = TrickCatalog.Get(id);
        if (Progression.Level < definition.MinimumSkillLevel)
        {
            Say($"{definition.Name} unlocks at Skamtebord {definition.MinimumSkillLevel}.");
            return false;
        }
        if (!Combo.TryAddTrick(id, Progression.Level)) return false;
        AnimateTrick(id);
        view.GetZDO().Set(TrickKey, (int)id);
        view.GetZDO().Set(SequenceKey, ++trickSequence);
        view.GetZDO().Set(TrickTimeKey, ZNet.instance.GetTime().Ticks);
        return true;
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
        ClearInput();
        ollieAvailable = false;
        lastSupportedTime = float.NegativeInfinity;
        publishedPushing = false;
        sprintRequested = Sprinting = publishedSprinting = false;
        Combo.Bail(); // Exiting never banks an unfinished combo.
        if (view && view.IsValid() && view.IsOwner())
        {
            view.GetZDO().Set(RidingKey, false);
            view.GetZDO().Set(PushingKey, false);
            view.GetZDO().Set(SprintingKey, false);
            view.GetZDO().Set(GrabKey, false);
            view.GetZDO().Set(LeanKey, 0f);
        }
        skateAnimator?.Dispose();
        if (capsule)
        {
            capsule.material.dynamicFriction = cachedFriction;
            capsule.material.staticFriction = cachedStaticFriction;
            capsule.material.frictionCombine = cachedCombine;
        }
        if (body)
        {
            body.collisionDetectionMode = previousCollisionMode;
            body.rotation = Quaternion.Euler(0, heading, 0);
            // Replace any MoveRotation queued by the final skating physics tick.
            body.MoveRotation(body.rotation);
            transform.rotation = body.rotation;
            body.angularVelocity = Vector3.zero;
            if (view && view.IsValid() && view.IsOwner()) view.GetZDO().SetRotation(body.rotation);
        }
        hasSurfaceFrame = false;
        contactTime = float.NegativeInfinity;
        contactCollider = null;
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
        RecordContact(collision);
        if (!Riding || !IsLocal) return;
        for (int i = 0; i < collision.contactCount; i++)
        {
            var contact = collision.GetContact(i);
            // Cache the approach from before the solver removes impact velocity.
            // A stopped rigidbody still hit the wall at its incoming speed.
            Vector3 incoming = lastVelocity;
            if (collision.collider.attachedRigidbody) incoming -= collision.collider.attachedRigidbody.GetPointVelocity(contact.point);
            if (!SupportingContact(contact, collision.collider, out _)
                && Mathf.Abs(contact.normal.y) < .4f && Vector3.Dot(incoming, contact.normal) < -6f)
            { Dismount(true); break; }
        }
    }

    private void OnCollisionStay(Collision collision) => RecordContact(collision);

    private void RecordContact(Collision collision)
    {
        if (!Riding || !IsLocal || Time.time < ignoreGroundUntil) return;
        for (int i = 0; i < collision.contactCount; i++)
        {
            var point = collision.GetContact(i);
            if (!SupportingContact(point, collision.collider, out var normal)) continue;
            float alignment = Vector3.Dot(normal, surfaceNormal);
            if (contactTime != Time.fixedTime || contactWeight == 0f)
            {
                contactNormalSum = Vector3.zero;
                contactWeight = 0f;
                bestContactAlignment = -1f;
            }
            // Blend the supporting patch across collider seams in this physics
            // tick. Callback order must not choose a different riding plane.
            float weight = Mathf.Max(.1f, alignment);
            contactNormalSum += normal * weight;
            contactWeight += weight;
            contactNormal = contactNormalSum.normalized;
            if (alignment > bestContactAlignment)
            {
                bestContactAlignment = alignment;
                contactPoint = point.point;
                contactCollider = collision.collider;
            }
            contactTime = Time.fixedTime;
            probeContact = false;
        }
    }

    private bool SupportingContact(ContactPoint point, Collider collider, out Vector3 normal)
    {
        var pipe = collider.GetComponent<HalfpipeSurface>();
        // The built-in piece supplies a smooth mesh normal, never an exemption
        // from support/impact rules. Untagged terrain and build pieces use theirs.
        normal = pipe ? pipe.NormalAt(point.point) : point.normal;
        if (Vector3.Dot(normal, point.normal) < .65f) return false;
        float alignment = Vector3.Dot(normal, surfaceNormal);
        if (alignment < .35f || Vector3.Dot(point.point - body.position, transform.up) > .55f) return false;
        if (normal.y < .15f)
        {
            Vector3 relative = lastVelocity;
            if (collider.attachedRigidbody) relative -= collider.attachedRigidbody.GetPointVelocity(point.point);
            bool continuous = Time.time - lastSupportedTime <= JumpGraceSeconds;
            // Reach a steep face through a continuous transition, or land with
            // the board aligned and tangential momentum. Head-on walls fail.
            if (alignment < .65f || (!continuous && Vector3.ProjectOnPlane(relative, normal).sqrMagnitude < 4f)) return false;
        }
        return true;
    }

    private bool HasSupport(float dt) => Time.time >= ignoreGroundUntil && contactCollider
        && contactCollider.enabled && contactCollider.gameObject.activeInHierarchy
        && (probeContact ? contactTime == Time.fixedTime : Time.fixedTime - contactTime <= dt * 1.6f || body.IsSleeping());

    private bool BridgeSurfaceSeam()
    {
        if (Time.time < ignoreGroundUntil || Time.time-lastSupportedTime > .08f) return false;
        // A character capsule can briefly lose contact at a small mesh seam.
        // Both virtual wheels must still be within 12 cm of a continuous surface.
        // This only classifies support: no position snap or downward force.
        const float halfWheelbase = .35f, castHeight = .35f, skin = .12f;
        Vector3 direction = Vector3.ProjectOnPlane(body.linearVelocity,surfaceNormal).normalized;
        if (direction.sqrMagnitude < .1f) direction = surfaceForward;
        Vector3 origin = body.position + surfaceNormal * castHeight;
        if (!Physics.Raycast(origin+direction*halfWheelbase,-surfaceNormal,out var front,castHeight+skin,SupportMask,QueryTriggerInteraction.Ignore)
            || !Physics.Raycast(origin-direction*halfWheelbase,-surfaceNormal,out var rear,castHeight+skin,SupportMask,QueryTriggerInteraction.Ignore)) return false;
        if (front.collider == capsule || rear.collider == capsule || Vector3.Dot(front.normal,rear.normal)<.94f) return false;
        Vector3 normal = (front.normal+rear.normal).normalized;
        if (Vector3.Dot(normal,surfaceNormal)<.9f) return false;
        Vector3 relative = body.linearVelocity;
        if (front.rigidbody) relative -= front.rigidbody.GetPointVelocity(front.point);
        float separatingSpeed = Mathf.Max(0,Vector3.Dot(relative,normal));
        float gravityIntoSurface = Mathf.Max(0,-Vector3.Dot(Physics.gravity,normal));
        // Permit only a skin-sized bounce that gravity can settle. An actual
        // launch must not be relabelled as ground because a ray still reaches it.
        if (separatingSpeed*separatingSpeed > 2*gravityIntoSurface*skin+.04f) return false;
        float span = Vector3.Distance(front.point,rear.point);
        float curvature = Vector3.Dot(front.normal-rear.normal,direction)/Mathf.Max(.1f,span);
        float speedSquared = Vector3.ProjectOnPlane(relative,normal).sqrMagnitude;
        // Following a convex crest would require inward acceleration v²/r. If
        // gravity cannot provide it, release: wheels cannot pull onto terrain.
        if (speedSquared*curvature > -Vector3.Dot(Physics.gravity,normal)+.5f) return false;
        // Follow the receding plane downhill. On a concave transition retain
        // the physical plane: tilting ahead of collision can kick the capsule out.
        contactNormal = curvature >= 0 ? normal : surfaceNormal;
        contactPoint = (front.point+rear.point)*.5f;
        contactCollider = front.collider;
        contactWeight = 0;
        contactTime = Time.fixedTime;
        probeContact = true;
        return true;
    }

    // Feed the same accepted contacts to Valheim's bookkeeping before it handles
    // landings and fall distance. Long supported descents are not airborne falls.
    // This never changes on-foot ground rules. The short seam probe has to
    // revalidate nearby geometry and the normal reaction on every physics tick.
    internal void PrepareGroundContact(float dt)
    {
        if (!Riding || !IsLocal) return;
        bool supported = HasSupport(dt) || BridgeSurfaceSeam();
        GroundContact(player) = supported;
        if (!supported) return;
        PendingGroundNormal(player) = contactNormal;
        PendingGroundPoint(player) = contactPoint;
        PendingGroundCollider(player) = contactCollider;
    }

    private void LateUpdate()
    {
        if (!view || !view.IsValid()) return;
        bool active = IsLocal ? Riding : view.GetZDO().HasOwner() && view.GetZDO().GetBool(RidingKey);
        if (!active || player.IsDead()) { if (board) board.SetActive(false); skateAnimator?.Dispose(); RestorePose(); return; }
        skateAnimator?.Mount();
        skateAnimator?.SetMotion(IsLocal ? Pushing : view.GetZDO().GetBool(PushingKey), IsLocal ? Sprinting : view.GetZDO().GetBool(SprintingKey));
        if (!board) board = BoardModel.Create(transform);
        board.SetActive(true);
        if (!IsLocal)
        {
            int sequence = view.GetZDO().GetInt(SequenceKey);
            int id = view.GetZDO().GetInt(TrickKey);
            if (sequence != trickSequence && id >= 0 && id <= (int)TrickId.ThreeSixty)
            {
                double elapsed = (ZNet.instance.GetTime().Ticks - view.GetZDO().GetLong(TrickTimeKey)) / (double)System.TimeSpan.TicksPerSecond;
                float duration = TrickCatalog.Get((TrickId)id).DurationSeconds;
                // World-clock corrections can move either side of a short clip.
                // Reserve a visible tail for newly received events, while the
                // first mounted snapshot expires old tricks for late joiners.
                if (elapsed >= -1 && elapsed < duration + (receivedRemoteRide ? 1f : 0f))
                {
                    AnimateTrick((TrickId)id);
                    trickStart -= Mathf.Clamp((float)elapsed,0,Mathf.Max(0,duration-.12f));
                }
            }
            trickSequence = sequence;
            receivedRemoteRide = true;
            grabHeld = view.GetZDO().GetBool(GrabKey);
            lean = Mathf.Lerp(lean, view.GetZDO().GetFloat(LeanKey), 1f - Mathf.Exp(-12f * Time.deltaTime));
        }
        float phase = trickDuration > 0 ? Mathf.Clamp01((Time.time - trickStart) / trickDuration) : 1f;
        float roll = 0, yaw = 0, lift = 0;
        if (grabHeld) { lift = .22f; roll = 25f; }
        else if (phase < 1)
        {
            if (visualTrick == TrickId.Kickflip) roll = 360 * phase;
            if (visualTrick == TrickId.Heelflip) roll = -360 * phase;
            if (visualTrick == TrickId.Shuvit) yaw = 180 * phase;
            if (visualTrick == TrickId.ThreeSixty) yaw = 360 * phase;
            if (visualTrick == TrickId.Grab) { lift = Mathf.Sin(phase * Mathf.PI) * .3f; roll = Mathf.Sin(phase * Mathf.PI) * 35f; }
        }
        board.transform.localPosition = new Vector3(0, .14f + lift, 0);
        board.transform.localRotation = Quaternion.Euler(0, yaw, roll);
        if (visual)
        {
            if (!posing) { originalVisualRotation = visual.localRotation; posing = true; }
            visual.localRotation = originalVisualRotation * Quaternion.Euler(grabHeld ? 12f : 0f, (skateAnimator?.Active == true ? 0 : 70) + (visualTrick == TrickId.ThreeSixty && phase < 1 ? yaw : 0), lean + (grabHeld ? 18f : 0f));
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
