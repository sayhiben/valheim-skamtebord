using System.Collections.Generic;
using System.Linq;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace Skamtebord;

// A persistent vanilla build piece: normal placement, damage, removal and ZDO replication.
internal static class HalfpipePiece
{
    internal const string PrefabName = "Skamtebord_Halfpipe";
    internal const float Radius = 4f, FlatHalfLength = 3f, HalfWidth = 4f;
    private static bool registered;
    internal static GameObject Prefab;

    internal static void Register()
    {
        if (registered) return;
        var custom = new CustomPiece(PrefabName, "wood_floor", new PieceConfig
        {
            Name = "Wooden halfpipe",
            Description = "A wooden skate ramp with vertical lips. Carry speed through the curve and jump near the coping for extra air.",
            PieceTable = "Hammer", Category = "Skamtebord", CraftingStation = "piece_workbench",
            Requirements = new[] { new RequirementConfig("Wood", 80, recover: true) },
            Icon = CreateIcon()
        });
        Prefab = custom.PiecePrefab;
        var source = PrefabManager.Instance.GetPrefab("Wood").GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)
            .First(m => m && m.renderQueue < 2500);
        var timber = new Material(source) { name = "Skamtebord halfpipe timber", color = new Color(.57f,.34f,.17f) };
        foreach (Transform child in Prefab.transform.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
        foreach (var collider in Prefab.GetComponents<Collider>()) Object.DestroyImmediate(collider);
        var surface = new GameObject("Wooden riding surface");
        surface.layer = Prefab.layer;
        surface.transform.SetParent(Prefab.transform, false);
        var mesh = CreateMesh();
        surface.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = surface.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = timber;
        renderer.allowOcclusionWhenDynamic = false;
        surface.AddComponent<MeshCollider>().sharedMesh = mesh;
        surface.AddComponent<HalfpipeSurface>();

        // Supports sit outside the riding surface, leaving the entire transition clear.
        foreach (float x in new[] { -HalfWidth + .14f, HalfWidth - .14f })
            foreach (float side in new[] { -1f, 1f })
                for (int i = 1; i <= 4; i++)
                {
                    float theta = i * Mathf.PI / 8f;
                    float height = Radius * (1f - Mathf.Cos(theta));
                    Beam(Prefab.transform, new Vector3(x, height * .5f - .15f, side * (FlatHalfLength + Radius * Mathf.Sin(theta) + .15f)),
                        new Vector3(.22f, height + .3f, .22f), timber);
                }
        var wear = Prefab.GetComponent<WearNTear>();
        wear.m_new = wear.m_worn = wear.m_broken = surface;
        wear.m_wet = null;
        wear.m_snow = wear.m_snowWorn = wear.m_snowBroken = null;
        wear.m_health = 800f;
        wear.m_noRoofWear = false;
        wear.m_noSupportWear = true;
        wear.m_fragmentRoots = new[] { surface };
        wear.m_autoCreateFragments = false;
        custom.Piece.m_groundPiece = true;
        custom.Piece.m_groundOnly = false;
        custom.Piece.m_clipGround = true;
        custom.Piece.m_extraPlacementDistance = 10;
        custom.Piece.m_canBeRemoved = true;
        Prefab.GetComponent<ZNetView>().m_persistent = true;
        foreach (float x in new[] { -HalfWidth, HalfWidth })
        {
            var snap = new GameObject("_snappoint") { tag = "snappoint" };
            snap.transform.SetParent(Prefab.transform, false);
            snap.transform.localPosition = new Vector3(x, 0, 0);
        }
        if (!PieceManager.Instance.AddPiece(custom)) throw new System.InvalidOperationException("Could not register wooden halfpipe.");
        registered = true;
        SkamtebordPlugin.Instance.Log("Registered wooden halfpipe: Hammer / Skamtebord, Wood x80, workbench.");
    }

    private static void Beam(Transform parent, Vector3 position, Vector3 scale, Material material)
    {
        var beam = GameObject.CreatePrimitive(PrimitiveType.Cube);
        beam.name = "Timber support";
        beam.layer = parent.gameObject.layer;
        beam.transform.SetParent(parent, false);
        beam.transform.localPosition = position;
        beam.transform.localScale = scale;
        beam.GetComponent<Renderer>().sharedMaterial = material;
    }

    internal static Mesh CreateMesh()
    {
        var profile = new List<Vector2>(); // z, y, left lip to right lip
        var normals = new List<Vector2>();
        const int segments = 64;
        for (int i = segments; i >= 0; i--)
        {
            float a = i * Mathf.PI * .5f / segments;
            profile.Add(new Vector2(-FlatHalfLength - Radius * Mathf.Sin(a), Radius * (1 - Mathf.Cos(a))));
            normals.Add(new Vector2(Mathf.Sin(a), Mathf.Cos(a)));
        }
        for (int i = 0; i <= segments; i++)
        {
            float a = i * Mathf.PI * .5f / segments;
            profile.Add(new Vector2(FlatHalfLength + Radius * Mathf.Sin(a), Radius * (1 - Mathf.Cos(a))));
            normals.Add(new Vector2(-Mathf.Sin(a), Mathf.Cos(a)));
        }
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        var uv = new List<Vector2>();
        for (int i = 0; i + 1 < profile.Count; i++)
        {
            Vector2 a = profile[i], b = profile[i + 1];
            Vector2 belowA = a - normals[i] * .24f, belowB = b - normals[i + 1] * .24f;
            Vector3 al = new Vector3(-HalfWidth, a.y, a.x), ar = new Vector3(HalfWidth, a.y, a.x);
            Vector3 bl = new Vector3(-HalfWidth, b.y, b.x), br = new Vector3(HalfWidth, b.y, b.x);
            Vector3 dal = new Vector3(-HalfWidth, belowA.y, belowA.x), dar = new Vector3(HalfWidth, belowA.y, belowA.x);
            Vector3 dbl = new Vector3(-HalfWidth, belowB.y, belowB.x), dbr = new Vector3(HalfWidth, belowB.y, belowB.x);
            Quad(al, bl, br, ar); // interior riding face
            Quad(dar, dbr, dbl, dal);
            Quad(al, dal, dbl, bl);
            Quad(ar, br, dbr, dar);
            if (i == 0) Quad(ar, dar, dal, al);
            if (i == profile.Count - 2) Quad(bl, dbl, dbr, br);
        }
        var mesh = new Mesh { name = "Skamtebord closed wooden halfpipe" };
        mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.SetUVs(0, uv);
        mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
        return mesh;
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int first = vertices.Count;
            vertices.AddRange(new[] { a, b, c, d });
            uv.AddRange(new[] { Vector2.zero, new Vector2(0, Vector3.Distance(a,b)), new Vector2(Vector3.Distance(b,c),Vector3.Distance(a,b)), new Vector2(Vector3.Distance(a,d),0) });
            triangles.AddRange(new[] { first, first+1, first+2, first, first+2, first+3 });
        }
    }

    private static Sprite CreateIcon()
    {
        const int size = 128;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            float u = Mathf.Abs(x - 64f) / 52f;
            float curve = u < .3f ? 28f : 28f + 57f * (1f - Mathf.Sqrt(Mathf.Max(0, 1f - Mathf.Pow((u-.3f)/.7f, 2))));
            pixels[y*size+x] = u <= 1 && y <= curve && y > curve-9 ? new Color(.72f,.43f,.2f) : Color.clear;
        }
        texture.SetPixels(pixels); texture.Apply();
        return Sprite.Create(texture, new Rect(0,0,size,size), new Vector2(.5f,.5f));
    }
}

// Analytic normal removes facet jitter; the collider still determines contact and takeoff.
internal sealed class HalfpipeSurface : MonoBehaviour
{
    internal Vector3 NormalAt(Vector3 worldPoint)
    {
        Vector3 p = transform.InverseTransformPoint(worldPoint);
        float distance = Mathf.Max(0, Mathf.Abs(p.z) - HalfpipePiece.FlatHalfLength);
        float a = Mathf.Atan2(distance, Mathf.Max(0, HalfpipePiece.Radius - p.y));
        return transform.TransformDirection(new Vector3(0, Mathf.Cos(a), -Mathf.Sign(p.z) * Mathf.Sin(a))).normalized;
    }
}
