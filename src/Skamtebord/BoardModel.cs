using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Skamtebord;

internal static class BoardModel
{
    private static Material wood, grip, iron, wheel;
    private static Sprite icon;
    private static Mesh deckMesh;

    private static Material Surface(string name, Color color)
    {
        var shader = Shader.Find("Custom/Creature") ?? Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
        var material = new Material(shader) { name = "Skamtebord_" + name, color = color };
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.15f);
        return material;
    }

    internal static GameObject Create(Transform parent)
    {
        if (!wood)
        {
            wood = Surface("Wood", new Color(0.52f, 0.28f, 0.12f));
            grip = Surface("Grip", new Color(0.075f, 0.12f, 0.12f));
            iron = Surface("Iron", new Color(0.36f, 0.40f, 0.41f));
            wheel = Surface("Wheels", new Color(0.86f, 0.63f, 0.25f));
        }
        var root = new GameObject("SkamtebordVisual");
        root.transform.SetParent(parent, false);
        var deck = new GameObject("Carved deck");
        deck.transform.SetParent(root.transform, false);
        var mesh = deckMesh ? deckMesh : (deckMesh = DeckMesh());
        deck.AddComponent<MeshFilter>().sharedMesh = mesh;
        deck.AddComponent<MeshRenderer>().sharedMaterial = wood;
        Part(root, "Grip", PrimitiveType.Cube, new Vector3(0, .025f, 0), new Vector3(.39f, .012f, .70f), grip);
        // Two thin wood-colored stripes give the grip a readable handmade motif.
        for (int i = -1; i <= 1; i += 2)
            Part(root, "Deck stripe", PrimitiveType.Cube, new Vector3(0, .034f, i * .20f), new Vector3(.395f, .004f, .02f), wood);
        foreach (float z in new[] { -.33f, .33f })
        {
            Part(root, "Truck", PrimitiveType.Cube, new Vector3(0, -.06f, z), new Vector3(.45f, .045f, .06f), iron);
            foreach (float x in new[] { -.245f, .245f })
            {
                var part = Part(root, "Wheel", PrimitiveType.Cylinder, new Vector3(x, -.075f, z), new Vector3(.135f, .04f, .135f), wheel);
                part.transform.localRotation = Quaternion.Euler(0, 0, 90);
            }
        }
        return root;
    }

    private static GameObject Part(GameObject parent, string name, PrimitiveType shape, Vector3 position, Vector3 scale, Material material)
    {
        var obj = GameObject.CreatePrimitive(shape);
        obj.name = name;
        Object.DestroyImmediate(obj.GetComponent<Collider>());
        obj.transform.SetParent(parent.transform, false);
        obj.transform.localPosition = position;
        obj.transform.localScale = scale;
        obj.GetComponent<Renderer>().sharedMaterial = material;
        return obj;
    }

    private static Mesh DeckMesh()
    {
        const int count = 32;
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        for (int side = 0; side < 2; side++)
            for (int i = 0; i < count; i++)
            {
                float a = i * 2f * Mathf.PI / count;
                float z = Mathf.Sin(a) * .53f;
                float x = Mathf.Cos(a) * .23f;
                vertices.Add(new Vector3(x, Mathf.Pow(Mathf.Abs(z) / .53f, 6) * .075f - side * .045f, z));
            }
        for (int i = 1; i < count - 1; i++)
        {
            triangles.AddRange(new[] { 0, i + 1, i, count, count + i, count + i + 1 });
        }
        for (int i = 0; i < count; i++)
        {
            int j = (i + 1) % count;
            triangles.AddRange(new[] { i, j, count + i, j, count + j, count + i });
        }
        var mesh = new Mesh { name = "Skamtebord carved deck" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        return mesh;
    }

    internal static Sprite CreateIcon()
    {
        if (icon) return icon;
        const int size = 128;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "SkamtebordIcon" };
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            float u = (x - 64) * .82f + (y - 64) * .57f;
            float v = -(x - 64) * .57f + (y - 64) * .82f;
            bool wheels = Mathf.Abs(u) > 11 && Mathf.Abs(u) < 23 && Mathf.Abs(Mathf.Abs(v) - 27) < 7;
            bool deck = u * u / 260f + v * v / 2400f < 1;
            Color c = Color.clear;
            if (wheels) c = new Color(.93f, .70f, .30f);
            if (deck) c = new Color(.57f, .34f, .16f);
            if (u * u / 160f + v * v / 1900f < 1) c = new Color(.11f, .22f, .23f);
            if (deck && Mathf.Abs(Mathf.Abs(v) - 20) < 2) c = new Color(.88f, .66f, .33f);
            pixels[y * size + x] = c;
        }
        texture.SetPixels(pixels);
        texture.Apply();
        icon = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100);
        return icon;
    }
}
