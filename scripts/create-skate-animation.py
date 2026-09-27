"""Author original, editable Humanoid source animations in Blender 4.2.

Run: blender --background --python scripts/create-skate-animation.py
The proxy is an authoring aid only; no Valheim mesh or animation is exported.
"""
import bpy
import math
from pathlib import Path
from mathutils import Vector, Matrix, Quaternion

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "assets" / "animation-source"
UNITY = ROOT / "assets" / "SkamtebordAnimations" / "Assets" / "Source"
OUT.mkdir(parents=True, exist_ok=True)
UNITY.mkdir(parents=True, exist_ok=True)
bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
scene = bpy.context.scene
scene.render.fps = 30
scene.unit_settings.system = "METRIC"

rig_data = bpy.data.armatures.new("Skamtebord authoring skeleton")
rig = bpy.data.objects.new("Skater", rig_data)
scene.collection.objects.link(rig)
bpy.context.view_layer.objects.active = rig
rig.select_set(True)
bpy.ops.object.mode_set(mode="EDIT")
spec = {}
def bone(name, head, tail, parent=None):
    b = rig_data.edit_bones.new(name)
    b.head, b.tail = head, tail
    if parent: b.parent = rig_data.edit_bones[parent]
    spec[name] = (Vector(head), Vector(tail), parent)

bone("Hips", (0,0,1.01), (0,0,1.15))
bone("Spine", (0,0,1.15), (0,0,1.34), "Hips")
bone("Chest", (0,0,1.34), (0,0,1.51), "Spine")
bone("Neck", (0,0,1.51), (0,0,1.64), "Chest")
bone("Head", (0,0,1.64), (0,0,1.85), "Neck")
for side, s in (("Left",1),("Right",-1)):
    bone(side+"Shoulder", (s*.04,0,1.48), (s*.20,0,1.48), "Chest")
    bone(side+"UpperArm", (s*.20,0,1.48), (s*.49,0,1.48), side+"Shoulder")
    bone(side+"LowerArm", (s*.49,0,1.48), (s*.75,0,1.48), side+"UpperArm")
    bone(side+"Hand", (s*.75,0,1.48), (s*.88,0,1.48), side+"LowerArm")
    bone(side+"UpperLeg", (s*.10,0,1.01), (s*.10,0,.56), "Hips")
    bone(side+"LowerLeg", (s*.10,0,.56), (s*.10,0,.12), side+"UpperLeg")
    bone(side+"Foot", (s*.10,0,.12), (s*.10,-.17,.065), side+"LowerLeg")
    bone(side+"Toes", (s*.10,-.17,.065), (s*.10,-.27,.065), side+"Foot")
bpy.ops.object.mode_set(mode="OBJECT")
rest = {n: rig_data.bones[n].matrix_local.copy() for n in spec}

def mat(name, color):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color,1)
    return m
bodymat = mat("Authoring proxy / slate", (.13,.24,.29))
accent = mat("Pushing leg / orange", (.95,.34,.08))
deckmat = mat("Preview deck", (.34,.19,.08))

# Bone-parented proxy shapes make the editable source readable without shipping a character.
for name, (head, tail, _) in spec.items():
    mid = (head+tail)*.5
    radius = .065 if "Leg" in name else .045
    if name in ("Hips","Spine","Chest"): radius = .14
    if name == "Head": radius = .115
    bpy.ops.mesh.primitive_uv_sphere_add(segments=12, ring_count=8, location=mid)
    obj=bpy.context.object
    obj.name="Proxy_"+name
    obj.scale=(radius,radius,(tail-head).length*.53)
    obj.rotation_mode="QUATERNION"
    obj.rotation_quaternion=(tail-head).to_track_quat("Z","Y")
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.data.materials.append(accent if name.startswith("Right") and ("Leg" in name or "Foot" in name) else bodymat)
    # Armature modifier, rigid weighting: no parent-inverse ambiguity while posing.
    world=obj.matrix_world.copy()
    obj.parent=rig
    obj.matrix_world=world
    group=obj.vertex_groups.new(name=name)
    group.add(range(len(obj.data.vertices)),1,"REPLACE")
    mod=obj.modifiers.new("Preview skin", "ARMATURE")
    mod.object=rig

def rot(z): return Quaternion((0,0,1), math.radians(z))
def smooth(t): return t*t*(3-2*t)
def lerp(a,b,t): return Vector(a).lerp(Vector(b),smooth(t))
def path(keys,t):
    for (ta,a),(tb,b) in zip(keys,keys[1:]):
        if t <= tb: return lerp(a,b,max(0,(t-ta)/(tb-ta)))
    return Vector(keys[-1][1])
def solve(a,b,l1,l2,pole):
    d=b-a; length=min(d.length,l1+l2-.001)
    axis=d.normalized()
    along=(l1*l1-l2*l2+length*length)/(2*length)
    bend=(Vector(pole)-axis*Vector(pole).dot(axis)).normalized()
    return a+axis*along+bend*math.sqrt(max(0,l1*l1-along*along))
def put(name, head, tail, twist=None):
    p=rig.pose.bones[name]
    direction=tail-head
    original=spec[name][1]-spec[name][0]
    q=original.rotation_difference(direction) @ rest[name].to_quaternion()
    if twist is not None: q=twist @ rest[name].to_quaternion()
    p.matrix=Matrix.LocRotScale(head,q,Vector((1,1,1)))
    # Blender converts this world-space pose into parent-relative channels. Evaluate
    # each parent before assigning its children, otherwise the previous frame leaks
    # into the bake and even a constant coasting pose becomes an unstable animation.
    bpy.context.view_layer.update()

def pose(t,push,tuck=False):
    # Both feet are on the deck at cycle boundaries. The support foot stays planted.
    effort=math.sin(math.pi*t)**2 if push else 0
    yaw=rot(-70+40*effort)
    hips=Vector((-.025*effort,-.08*effort,1.025-.035*effort))
    lean=Vector((0,-.07-.10*effort,0))
    if tuck:
        hips.z -= .26
        hips.y -= .035
        lean.y -= .13
    spine=hips+Vector((0,0,.14))
    chest=spine+Vector((0,0,.19))+lean*.45
    neck=chest+Vector((0,0,.17))+lean*.40
    head=neck+Vector((0,0,.13))+lean*.15
    put("Hips", hips,spine,yaw)
    put("Spine",spine,chest, yaw @ Quaternion((1,0,0),math.radians(8+12*effort+(18 if tuck else 0))))
    put("Chest",chest,neck,yaw @ Quaternion((1,0,0),math.radians(5)))
    put("Neck",neck,head,yaw)
    put("Head",head,head+Vector((0,0,.21)),rot(-15))
    feet={"Left":Vector((.025,-.29,.285)), "Right":Vector((-.025,.29,.285))}
    if push:
        feet["Right"]=path([
            (0,(-.025,.29,.285)),(.18,(-.36,.03,.39)),
            (.32,(-.38,-.27,.12)),(.64,(-.38,.53,.12)),
            (.82,(-.27,.40,.42)),(1,(-.025,.29,.285))],t)
    for side,s in (("Left",1),("Right",-1)):
        hip=hips+yaw@Vector((s*.1,0,0))
        ankle=feet[side]
        knee=solve(hip,ankle,.45,.44,yaw@Vector((0,-1,0)))
        put(side+"UpperLeg",hip,knee)
        put(side+"LowerLeg",knee,ankle)
        footyaw=rot(-70+55*effort) if side=="Left" else rot(-70+70*effort)
        toe=ankle+footyaw@Vector((0,-.17,-.055))
        put(side+"Foot",ankle,toe)
        put(side+"Toes",toe,toe+footyaw@Vector((0,-.10,0)))
        clav=chest+Vector((0,0,.14))+yaw@Vector((s*.04,0,0))
        shoulder=clav+yaw@Vector((s*.16,0,0))
        swing=math.sin(2*math.pi*(t-.12))*.12 if push else 0
        wrist=shoulder+yaw@Vector((s*.26,-.13+s*swing,-.39))
        if tuck: wrist=shoulder+yaw@Vector((s*.16,-.20,-.32))
        elbow=solve(shoulder,wrist,.29,.26,yaw@Vector((s*.55,.8,0)))
        put(side+"Shoulder",clav,shoulder)
        put(side+"UpperArm",shoulder,elbow)
        put(side+"LowerArm",elbow,wrist)
        put(side+"Hand",wrist,wrist+(wrist-elbow).normalized()*.13)
    bpy.context.view_layer.update()

for name,push,frames in (("SkateCoast",False,30),("SkatePush",True,36),("SkateTuck",False,30)):
    rig.animation_data_create()
    action=bpy.data.actions.new(name)
    action.use_fake_user=True
    rig.animation_data.action=action
    for frame in range(frames+1):
        scene.frame_set(frame)
        pose(frame/frames,push,tuck=name=="SkateTuck")
        for pb in rig.pose.bones:
            pb.rotation_mode="QUATERNION"
            pb.keyframe_insert("location",frame=frame,group=pb.name)
            pb.keyframe_insert("rotation_quaternion",frame=frame,group=pb.name)
            pb.keyframe_insert("scale",frame=frame,group=pb.name)
    for curve in action.fcurves:
        for key in curve.keyframe_points: key.interpolation="LINEAR"
    if not push:
        assert all(max(k.co.y for k in c.keyframe_points)-min(k.co.y for k in c.keyframe_points) < .0001
                   for c in action.fcurves), "Coasting pose must remain constant throughout its loop"

rig.animation_data.action=None
for pb in rig.pose.bones: pb.matrix_basis=Matrix.Identity(4)
bpy.context.view_layer.update()
scene.frame_start=0;scene.frame_end=36
bpy.ops.object.select_all(action="DESELECT")
rig.select_set(True)
for obj in bpy.data.objects:
    if obj.name.startswith("Proxy_"): obj.select_set(True)
bpy.context.view_layer.objects.active=rig
bpy.ops.export_scene.fbx(filepath=str(UNITY/"Skater.fbx"),use_selection=True,
    object_types={"ARMATURE","MESH"},add_leaf_bones=False,bake_anim=True,bake_anim_use_all_actions=True,
    bake_anim_use_nla_strips=False,bake_anim_simplify_factor=0,axis_forward="-Z",axis_up="Y",
    apply_scale_options="FBX_SCALE_ALL")
rig.animation_data.action=bpy.data.actions["SkatePush"]
scene.frame_set(12)

# An editable, lit preview with an unexported deck and ground.
bpy.ops.mesh.primitive_cube_add(size=1,location=(0,0,.145))
deck=bpy.context.object;deck.name="Preview skateboard (not exported)";deck.scale=(.44,1.04,.06)
deck.data.materials.append(deckmat)
for x in (-.245,.245):
    for y in (-.33,.33):
        bpy.ops.mesh.primitive_cylinder_add(vertices=16,radius=.0675,depth=.08,location=(x,y,.0675),rotation=(0,math.pi/2,0))
        bpy.context.object.data.materials.append(accent)
bpy.ops.mesh.primitive_plane_add(size=200)
bpy.context.object.data.materials.append(mat("Ground",(.045,.065,.075)))
bpy.ops.object.light_add(type="AREA", location=(3,-4,6))
bpy.context.object.data.energy=1000;bpy.context.object.data.shape="DISK";bpy.context.object.data.size=5
bpy.ops.object.camera_add(location=(3,-4,2.0))
cam=bpy.context.object;cam.rotation_euler=(Vector((0,0,.95))-cam.location).to_track_quat("-Z","Y").to_euler()
cam.data.type="ORTHO";cam.data.ortho_scale=2.5;scene.camera=cam
scene.render.engine="BLENDER_EEVEE_NEXT"
scene.render.resolution_x=800;scene.render.resolution_y=800;scene.render.resolution_percentage=100
scene.world.color=(.25,.25,.25)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/"skate-push.blend"))
scene.render.filepath=str(OUT/"push-contact-preview.png")
bpy.ops.render.render(write_still=True)
print("SKAMTEBORD Animation source and FBX created", UNITY/"Skater.fbx")
