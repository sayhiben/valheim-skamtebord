using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class BuildSkateAnimations
{
    public static void Build()
    {
        if (Application.unityVersion != "6000.0.75f1")
            throw new Exception("Use Unity 6000.0.75f1 to match the supported Valheim runtime.");
        const string source = "Assets/Source/Skater.fbx";
        AssetDatabase.ImportAsset(source, ImportAssetOptions.ForceSynchronousImport);
        var importer = (ModelImporter)AssetImporter.GetAtPath(source);
        // Read the FBX's T-pose, not transforms modified by a previous Humanoid import.
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
        importer.importAnimation = false;
        importer.SaveAndReimport();
        var model = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(source));
        model.name = "Skater";
        // FBX's default transform pose can be its first animation frame. Reconstruct
        // the actual T-pose from the original proxy skin's bind matrices instead.
        var bindMatrices = new Dictionary<Transform, Matrix4x4>();
        foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            for (int i = 0; i < skin.bones.Length; i++)
                bindMatrices[skin.bones[i]] = skin.localToWorldMatrix * skin.sharedMesh.bindposes[i].inverse;
        foreach (var pair in bindMatrices.OrderBy(p => Depth(p.Key)))
            pair.Key.SetPositionAndRotation(pair.Value.GetColumn(3), pair.Value.rotation);
        var transforms = model.GetComponentsInChildren<Transform>(true);
        var names = transforms.Select(t => t.name).ToHashSet();
        var human = HumanTrait.BoneName.Where(n => names.Contains(n.Replace(" ", "")))
            .Select(n => new HumanBone { humanName = n, boneName = n.Replace(" ", ""), limit = new HumanLimit { useDefaultValues = true } }).ToArray();
        var skeleton = transforms.Select(t => new SkeletonBone
        {
            name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale
        }).ToArray();
        var leftHand = transforms.Single(t => t.name == "LeftHand").position;
        var upperArm = transforms.Single(t => t.name == "LeftUpperArm").position;
        if (bindMatrices.Count < 15 || Mathf.Abs(leftHand.y - upperArm.y) > .03f || Mathf.Abs(leftHand.z - upperArm.z) > .03f)
            throw new Exception("The Humanoid reference must use the proxy skin's T-pose bind matrices.");
        UnityEngine.Object.DestroyImmediate(model);
        importer.importAnimation = true;
        importer.SaveAndReimport();
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.humanDescription = new HumanDescription
        {
            human = human, skeleton = skeleton, upperArmTwist = .5f, lowerArmTwist = .5f,
            upperLegTwist = .5f, lowerLegTwist = .5f, armStretch = .05f, legStretch = .05f,
            feetSpacing = 0, hasTranslationDoF = false
        };
        importer.importAnimation = true;
        importer.animationCompression = ModelImporterAnimationCompression.Off;
        importer.optimizeGameObjects = false;
        var clips = importer.defaultClipAnimations;
        foreach (var clip in clips)
        {
            clip.name = clip.takeName.Contains("SkatePush") ? "SkatePush" : "SkateCoast";
            clip.loopTime = true;
            clip.loopPose = true;
            clip.lockRootRotation = true;
            clip.lockRootHeightY = true;
            clip.lockRootPositionXZ = true;
            clip.keepOriginalOrientation = true;
            clip.keepOriginalPositionY = true;
            clip.keepOriginalPositionXZ = true;
            clip.heightFromFeet = false;
        }
        importer.clipAnimations = clips;
        importer.SaveAndReimport();
        var avatar = AssetDatabase.LoadAllAssetsAtPath(source).OfType<Avatar>().Single();
        if (!avatar.isValid || !avatar.isHuman) throw new Exception("Authoring skeleton is not a valid Humanoid avatar.");
        Directory.CreateDirectory("Assets/Clips");
        foreach (var name in new[] { "SkateCoast", "SkatePush" })
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(source).OfType<AnimationClip>().Single(c => c.name == name);
            if (!clip.humanMotion || clip.legacy || clip.length < .9f) throw new Exception("Invalid animation: " + name);
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (name == "SkateCoast" && curve.keys.Max(k => k.value) - curve.keys.Min(k => k.value) > .001f)
                    throw new Exception("Coasting must be still: " + binding.propertyName);
            }
            var path = "Assets/Clips/" + name + ".anim";
            var copy = UnityEngine.Object.Instantiate(clip);
            copy.name = name;
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing) { EditorUtility.CopySerialized(copy, existing); UnityEngine.Object.DestroyImmediate(copy); }
            else AssetDatabase.CreateAsset(copy, path);
            Debug.Log($"SKATE_ASSET {name}: humanoid={clip.humanMotion} loop={clip.isLooping} length={clip.length:F3}s");
        }
        AssetDatabase.SaveAssets();
        var output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../bundles"));
        Directory.CreateDirectory(output);
        var build = new AssetBundleBuild
        {
            assetBundleName = "skamtebord-animations",
            assetNames = new[] { "Assets/Clips/SkateCoast.anim", "Assets/Clips/SkatePush.anim" }
        };
        var manifest = BuildPipeline.BuildAssetBundles(output, new[] { build },
            BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.ForceRebuildAssetBundle,
            BuildTarget.StandaloneWindows64);
        if (!manifest) throw new Exception("Animation bundle build failed.");
        File.WriteAllText(Path.Combine(output, "build-info.txt"), "Unity " + Application.unityVersion + "\nOriginal Blender-authored Humanoid clips: SkateCoast, SkatePush.\n");
        Debug.Log("SKATE_ASSET SUCCESS " + output);
    }

    private static int Depth(Transform t) => t.parent ? 1 + Depth(t.parent) : 0;
}
