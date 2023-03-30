#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System;
using System.IO;
using UnityEngine.Rendering;
using System.Collections.Generic;
using Unity.Collections;
using System.Text.RegularExpressions;

namespace Kamgam.MMDS
{
    public static partial class MakeMesh
    {
        public static void Invert(MeshFilter meshFilter)
        {
            InvertTriangles(meshFilter);
            InvertNormals(meshFilter);
        }

        public static void InvertNormals(MeshFilter meshFilter)
        {
            if (meshFilter != null)
            {
                Mesh mesh = meshFilter.sharedMesh;
                Vector3[] normals = mesh.normals;
                for (int i = 0; i < normals.Length; i++)
                {
                    normals[i] = -normals[i];
                }
                mesh.normals = normals;
            }
        }

        public static void InvertTriangles(MeshFilter meshFilter)
        {
            if (meshFilter != null)
            {
                Mesh mesh = meshFilter.sharedMesh;
                for (int m = 0; m < mesh.subMeshCount; m++)
                {
                    int[] triangles = mesh.GetTriangles(m);
                    for (int i = 0; i < triangles.Length; i += 3)
                    {
                        int temp = triangles[i + 0];
                        triangles[i + 0] = triangles[i + 1];
                        triangles[i + 1] = temp;
                    }
                    mesh.SetTriangles(triangles, m);
                }
            }
        }

        public static void CombineMesh(MeshFilter[] meshFilters, MeshFilter targetMeshFilter, bool recalculateNormals = true, bool optimize = true)
        {
            CombineInstance[] combine = new CombineInstance[meshFilters.Length];
            int i = 0;
            while (i < meshFilters.Length)
            {
                combine[i].mesh = meshFilters[i].sharedMesh;
                combine[i].transform = Matrix4x4.identity;
                meshFilters[i].gameObject.SetActive(false);
                i++;
            }
            targetMeshFilter.sharedMesh = new Mesh();
            targetMeshFilter.sharedMesh.CombineMeshes(combine);
            if (recalculateNormals)
            {
                targetMeshFilter.sharedMesh.RecalculateNormals();
            }
            if (optimize)
            {
                targetMeshFilter.sharedMesh.Optimize();
            }
        }

        public static void SetMeshReadable(Mesh sharedMesh, bool isReadable)
        {
            string asset_path = AssetDatabase.GetAssetPath(sharedMesh);
            ModelImporter importerForAsset = ModelImporter.GetAtPath(asset_path) as ModelImporter;
            importerForAsset.isReadable = isReadable;
            EditorUtility.SetDirty(sharedMesh);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        [MenuItem("Tools/Make Mesh/Invert Normals", priority = 200)]
        public static void InvertNormalsOnly()
        {
            if (Selection.activeGameObject != null)
            {
                InvertNormals(Selection.activeGameObject.GetComponent<MeshFilter>());
            }
        }

        [MenuItem("Tools/Make Mesh/Invert Triangles", priority = 200)]
        public static void InvertTrianglesOnly()
        {
            if (Selection.activeGameObject != null)
            {
                InvertTriangles(Selection.activeGameObject.GetComponent<MeshFilter>());
            }
        }

        [MenuItem("Tools/Make Mesh/Recalculate Normals", priority = 200)]
        public static void RecalculateNormals()
        {
            var meshFilter = Selection.activeGameObject.GetComponent<MeshFilter>();
            if (meshFilter != null)
            {
                meshFilter.sharedMesh.RecalculateNormals();
                meshFilter.sharedMesh.RecalculateTangents();
            }
        }

        public static Mesh extractSubMesh(Mesh mesh, int subMeshIndex)
        {
            var vertices = mesh.vertices;
            var normals = mesh.normals;

            var newVerts = new List<Vector3>();
            var newNormals = new List<Vector3>();
            var newTris = new List<int>();
            var triangles = mesh.GetTriangles(subMeshIndex);
            for (var i = 0; i < triangles.Length; i += 3)
            {
                var a = triangles[i + 0];
                var b = triangles[i + 1];
                var c = triangles[i + 2];
                newVerts.Add(vertices[a]);
                newVerts.Add(vertices[b]);
                newVerts.Add(vertices[c]);
                newNormals.Add(normals[a]);
                newNormals.Add(normals[b]);
                newNormals.Add(normals[c]);
                newTris.Add(newTris.Count);
                newTris.Add(newTris.Count);
                newTris.Add(newTris.Count);
            }
            var newMesh = new Mesh();
            newMesh.indexFormat = newVerts.Count > UInt16.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            newMesh.SetVertices(newVerts);
            newMesh.SetNormals(newNormals);
            newMesh.SetTriangles(newTris, 0, true);
            return newMesh;
        }

        [MenuItem("Tools/Make Mesh/Debug - Draw Normals for 5 Sec.", priority = 300)]
        public static void DrawNormals()
        {
            if (Selection.activeGameObject != null)
            {
                var meshFilter = Selection.activeGameObject.GetComponent<MeshFilter>();
                if (meshFilter != null)
                    DebugDrawNormals(meshFilter.sharedMesh, meshFilter.transform, Color.blue, 5);

                var skinnedMeshRenderer = Selection.activeGameObject.GetComponent<SkinnedMeshRenderer>();
                if (skinnedMeshRenderer != null)
                {
                    var mesh = new Mesh();
                    skinnedMeshRenderer.BakeMesh(mesh, true);
                    DebugDrawNormals(mesh, skinnedMeshRenderer.transform, Color.blue, 5);
                }
            }
        }

        public static void DebugDrawNormals(Mesh mesh, Transform t, Color color, float duration)
        {
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            for (int i = 0; i < vertices.Length; i++)
            {
                float normalLength = Mathf.Max(mesh.bounds.size.x, mesh.bounds.size.y, mesh.bounds.size.z) / 30f;
                Debug.DrawLine(t.TransformPoint(vertices[i]), t.TransformPoint(vertices[i] + normals[i] * normalLength), color, duration);
            }
        }
    }
}
#endif