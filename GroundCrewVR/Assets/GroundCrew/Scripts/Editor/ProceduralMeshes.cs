using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GroundCrew.EditorTools
{
    /// <summary>
    /// Small convex meshes for training props (cone, wheel chock) that Unity has no primitive
    /// for. Saved as assets so scenes reference them. These are stand-ins: the art pipeline
    /// replaces them with modelled props later (see docs/ARCHITECTURE.md, "Assets").
    /// </summary>
    static class ProceduralMeshes
    {
        public static Mesh Cone(string folder, float radius, float height, int segments)
        {
            return LoadOrCreate($"{folder}/Cone_{segments}.asset", builder =>
            {
                var apex = new Vector3(0f, height, 0f);
                for (var i = 0; i < segments; i++)
                {
                    var a0 = i / (float)segments * Mathf.PI * 2f;
                    var a1 = (i + 1) / (float)segments * Mathf.PI * 2f;
                    var b0 = new Vector3(Mathf.Cos(a0) * radius, 0f, Mathf.Sin(a0) * radius);
                    var b1 = new Vector3(Mathf.Cos(a1) * radius, 0f, Mathf.Sin(a1) * radius);
                    builder.Triangle(b0, apex, b1);
                    builder.Triangle(Vector3.zero, b0, b1);
                }
            }, new Vector3(0f, height / 3f, 0f));
        }

        /// <summary>Trapezoid-profile wedge: length along X (toe at -X), height Y, width Z.</summary>
        public static Mesh Wedge(string folder, float length, float height, float width)
        {
            return LoadOrCreate($"{folder}/Wedge.asset", builder =>
            {
                var hl = length / 2f;
                var hw = width / 2f;
                // Profile in XY: toe (-hl,0), heel (hl,0), back top (hl,h), front top (-hl + 0.55L, h)
                var p = new[]
                {
                    new Vector2(-hl, 0f), new Vector2(hl, 0f), new Vector2(hl, height), new Vector2(-hl + length * 0.55f, height),
                };
                System.Func<Vector2, Vector3> F = v => new Vector3(v.x, v.y, -hw);
                System.Func<Vector2, Vector3> B = v => new Vector3(v.x, v.y, hw);

                // Side faces
                builder.Quad(F(p[0]), F(p[1]), F(p[2]), F(p[3]));
                builder.Quad(B(p[0]), B(p[1]), B(p[2]), B(p[3]));
                // Loop faces around the profile
                for (var i = 0; i < p.Length; i++)
                {
                    var j = (i + 1) % p.Length;
                    builder.Quad(F(p[i]), F(p[j]), B(p[j]), B(p[i]));
                }
            }, new Vector3(length * 0.1f, height * 0.4f, 0f));
        }

        static Mesh LoadOrCreate(string path, System.Action<Builder> build, Vector3 interiorPoint)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) return existing;

            var builder = new Builder(interiorPoint);
            build(builder);
            var mesh = builder.ToMesh(System.IO.Path.GetFileNameWithoutExtension(path));
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        /// <summary>Flat-shaded convex mesh builder that fixes triangle winding automatically.</summary>
        sealed class Builder
        {
            readonly Vector3 m_Interior;
            readonly List<Vector3> m_Vertices = new List<Vector3>();
            readonly List<int> m_Triangles = new List<int>();

            public Builder(Vector3 interiorPoint) => m_Interior = interiorPoint;

            public void Triangle(Vector3 a, Vector3 b, Vector3 c)
            {
                // Unity front faces are clockwise; the face normal is Cross(b - a, c - a).
                var normal = Vector3.Cross(b - a, c - a);
                var outward = (a + b + c) / 3f - m_Interior;
                if (Vector3.Dot(normal, outward) < 0f) (b, c) = (c, b);

                var start = m_Vertices.Count;
                m_Vertices.Add(a);
                m_Vertices.Add(b);
                m_Vertices.Add(c);
                m_Triangles.Add(start);
                m_Triangles.Add(start + 1);
                m_Triangles.Add(start + 2);
            }

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                Triangle(a, b, c);
                Triangle(a, c, d);
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(m_Vertices);
                mesh.SetTriangles(m_Triangles, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
