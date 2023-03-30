#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System;
using System.IO;
using UnityEngine.Rendering;
using Unity.Collections;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using UnityEditor.SceneManagement;

namespace Kamgam.MMDS
{
    public static partial class MakeMesh
    {
        public enum SubMeshBehaviour { Preserve, Duplicate }

        const string doubleSidedNamePart = "double-sided";

        // Timestamp used to avoid executing the context menu multiple times.
        static double lastClickActionTime;

        static bool abortClickAction()
        {
            // Prevent executing multiple times when right-clicking.
            if (EditorApplication.timeSinceStartup - lastClickActionTime < 0.5)
                return true;

            lastClickActionTime = EditorApplication.timeSinceStartup;
            return false;
        }

        static Regex invalidPathCharactersRegex = new Regex(@"[^a-zA-Z0-9-_. ()]+");

        static string sanitizePath(string path)
        {
            return invalidPathCharactersRegex.Replace(path, "");
        }

        [MenuItem("Assets/Make Mesh/Double-sided", priority = 500)]
        public static void MakeAssetsDoubleSided()
        {
            MakeAssetsDoubleSided(SubMeshBehaviour.Preserve);
        }

        [MenuItem("Assets/Make Mesh/Double-sided and duplicate sub meshes", priority = 501)]
        public static void MakeAssetsDoubleSidedAndDuplicateSubMeshes()
        {
            MakeAssetsDoubleSided(SubMeshBehaviour.Duplicate);
        }

        public static void MakeAssetsDoubleSided(SubMeshBehaviour subMeshBehaviour)
        {
            if (abortClickAction())
                return;

            var selectedObjects = Selection.GetFiltered(typeof(GameObject), SelectionMode.DeepAssets);

            if (selectedObjects == null || selectedObjects.Length == 0)
            {
                Debug.LogWarning($"No assets selected. <color=orange>Aborting Action</color>.");
                return;
            }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Make double-sided");
            int undoGroup = Undo.GetCurrentGroup();

            int count = 0;
            float total = selectedObjects.Length;
            foreach (var obj in selectedObjects)
            {
                count++;

                var go = obj as GameObject;
                if (go == null)
                    continue;

                if (PrefabUtility.IsPartOfImmutablePrefab(go))
                {
                    var assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
                    EditorUtility.DisplayDialog(
                        "Can not modify MODEL PREFAB",
                        "A double-sided mesh will be generated but you won't see it on your model!\n\n" +
                        "Here is why:\n\n" +
                        "The asset (" + assetPath + ") you are trying to modify is a MODEL PREFAB. " +
                        "These are immutable files and thus can not be modified (that's a Unity limitation).\n\n" +
                        "WHAT NOW?\n\n" +
                        "A double-sided model asset will be created anyhow but it can not be assigned to the MeshFilter within the model prefab. You'll have to do this manually." +
                        "\n\n" +
                        "SOLUTION:\n\n" +
                        "Instantiate the model within a scene or a normal prefab first and then make the mesh double-sided in there OR assign the already created double-sided mesh to the MeshFilter.",
                        "Understood");
                }

                EditorUtility.DisplayProgressBar("Making meshes double-sided.", go.name + " - generating meshes.", count / total);

                MakeMeshesInGameObjectDoubleSided(go, subMeshBehaviour);

                // Ensure that modified prefab assets are saved
                if (PrefabUtility.IsPartOfAnyPrefab(go) && !PrefabUtility.IsPartOfImmutablePrefab(go))
                {
                    var root = getRoot(go);
                    PrefabUtility.SavePrefabAsset(root);

                    EditorUtility.SetDirty(obj);
                    AssetDatabase.SaveAssetIfDirty(obj);

                    var assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
                    AssetDatabase.ImportAsset(assetPath);
                }
            }

            Undo.CollapseUndoOperations(undoGroup);

            EditorUtility.ClearProgressBar();
        }

        public static GameObject getRoot(GameObject go)
        {
            var result = go.transform;
            while (result.transform.parent != null)
                result = result.transform.parent;

            return result.gameObject;
        }

        [MenuItem("Tools/Make Mesh/Double-sided", priority = 100)]
        [MenuItem("GameObject/Make Mesh/Double-sided", priority = 500)]
        public static void MakeSelectedDoubleSided()
        {
            MakeSelectedDoubleSided(SubMeshBehaviour.Preserve);
        }

        [MenuItem("Tools/Make Mesh/Double-sided and duplicate sub meshes", priority = 100)]
        [MenuItem("GameObject/Make Mesh/Double-sided and duplicate sub meshes", priority = 500)]
        public static void MakeSelectedDoubleSidedAndDuplicateSubMeshes()
        {
            MakeSelectedDoubleSided(SubMeshBehaviour.Duplicate);
        }

        public static void MakeSelectedDoubleSided(SubMeshBehaviour subMeshBehaviour)
        {
            if (abortClickAction())
                return;

            var selectedObjects = Selection.gameObjects;

            if (selectedObjects == null || selectedObjects.Length == 0)
            {
                Debug.LogWarning($"No objects selected. <color=orange>Aborting Action</color>.");
                return;
            }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Make Mesh double-sided");
            int undoGroup = Undo.GetCurrentGroup();

            foreach (var go in selectedObjects)
            {
                if (go == null)
                    continue;

                MakeMeshesInGameObjectDoubleSided(go, subMeshBehaviour);
            }

            Undo.CollapseUndoOperations(undoGroup);
        }

        public static void MakeMeshesInGameObjectDoubleSided(GameObject go, SubMeshBehaviour subMeshBehaviour)
        {
            if (go == null)
                return;

            // If there are multiple objects which use the same mesh then we can
            // skip converting the mesh multiple times and reuse it.
            var oldToNewMeshMap = new Dictionary<Mesh, Mesh>();

            var meshFilters = go.GetComponentsInChildren<MeshFilter>(includeInactive: true);
            if (meshFilters != null && meshFilters.Length > 0)
            {
                foreach (var meshFilter in meshFilters)
                {
                    Undo.RegisterCompleteObjectUndo(meshFilter, "Make Mesh double sided");
                    if (oldToNewMeshMap.ContainsKey(meshFilter.sharedMesh))
                    {
                        // Mesh has already has been made double-sided. Use the existing one.
                        meshFilter.sharedMesh = oldToNewMeshMap[meshFilter.sharedMesh];
                    }
                    else
                    {
                        var oldMesh = meshFilter.sharedMesh;

                        MakeDoubleSided(meshFilter, subMeshBehaviour);

                        if (oldMesh != meshFilter.sharedMesh)
                            oldToNewMeshMap.Add(oldMesh, meshFilter.sharedMesh);
                    }
                }
            }

            var skinnedRenderers = go.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true);
            if (skinnedRenderers != null && skinnedRenderers.Length > 0)
            {
                foreach (var skinnedRenderer in skinnedRenderers)
                {
                    Undo.RegisterCompleteObjectUndo(skinnedRenderer, "Make Mesh double sided");
                    if (oldToNewMeshMap.ContainsKey(skinnedRenderer.sharedMesh))
                    {
                        // Mesh has already has been made double-sided. Use the existing one.
                        skinnedRenderer.sharedMesh = oldToNewMeshMap[skinnedRenderer.sharedMesh];
                    }
                    else
                    {
                        var oldMesh = skinnedRenderer.sharedMesh;

                        MakeDoubleSided(skinnedRenderer, subMeshBehaviour);

                        if (oldMesh != skinnedRenderer.sharedMesh)
                            oldToNewMeshMap.Add(oldMesh, skinnedRenderer.sharedMesh);
                    }
                }
            }
        }

        enum CreationChoice { UsingExisting = 0, ReplaceExisting = 2, CreateWithNewName = 1 };

        public static void MakeDoubleSided(MeshFilter meshFilter, SubMeshBehaviour subMeshBehaviour)
        {
            if (meshFilter == null)
                return;

            if (meshFilter.sharedMesh == null)
                return;

            // Path of the new mesh asset.
            string newAssetPath = getNewAssetPath(meshFilter);

            // Skip if it already is double sided.
            if (meshFilter.sharedMesh.name.EndsWith(doubleSidedNamePart))
            {
                Debug.Log($"<color=orange>Aborting Action</color> because {meshFilter.sharedMesh.name} in {meshFilter.name} is already double-sided '{newAssetPath}'.");
                return;
            }

            // Check if the asset already exists.
            var choice = askHowToHandleExistingMesh(newAssetPath, out Mesh existingMesh);
            switch (choice)
            {
                case CreationChoice.UsingExisting:
                    meshFilter.sharedMesh = existingMesh;
                    return;

                case CreationChoice.CreateWithNewName:
                    newAssetPath = AssetDatabase.GenerateUniqueAssetPath(newAssetPath);
                    break;
            }

            Mesh doubleSidedMesh;
            if (subMeshBehaviour == SubMeshBehaviour.Preserve)
                doubleSidedMesh = MakeDoubleSided(meshFilter.sharedMesh);
            else
                doubleSidedMesh = MakeDoubleSidedAndDuplicateSubMeshes(meshFilter.sharedMesh);

            var meshRenderer = meshFilter.gameObject.GetComponent<MeshRenderer>();

            // If shadow casting was on then switch to two-sided as it will yield better results
            // see: https://docs.unity3d.com/Manual/ShadowPerformance.html
            if (meshRenderer != null && meshRenderer.shadowCastingMode == ShadowCastingMode.On)
                meshRenderer.shadowCastingMode = ShadowCastingMode.TwoSided;

            // Add as new asset to a directory
            SaveMeshAsAsset(doubleSidedMesh, newAssetPath);

            // Replace mesh reference
            // Actually we could replace the reference in immutable prefabs asset
            // (models) too, yet these changes will not persist. Thus we do not
            // do it as it would be very confusting to the user.
            bool canChangeSharedMesh = !PrefabUtility.IsPartOfImmutablePrefab(meshFilter.gameObject) || PrefabUtility.IsPartOfPrefabInstance(meshFilter.gameObject);
            if (canChangeSharedMesh)
            {
                meshFilter.sharedMesh = doubleSidedMesh;
                if (subMeshBehaviour == SubMeshBehaviour.Duplicate)
                {
                    matchMaterialsToDuplicatedSubMeshes(doubleSidedMesh, meshRenderer);
                }
            }
        }

        private static CreationChoice askHowToHandleExistingMesh(string newAssetPath, out Mesh existingMesh)
        {
            existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(newAssetPath);
            if (existingMesh != null)
            {
                CreationChoice choice = (CreationChoice) EditorUtility.DisplayDialogComplex(
                    "Double-sided mesh already exists!",
                    "Would you like to use the existing double-sided mesh ('" + newAssetPath + "')?" +
                    "\n\n" +
                    "You can also make a completely new mesh with a different name.",

                    "Yes (use existing)", "Make new mesh", "No (replace existing)"
                    );

                return choice;
            }

            return CreationChoice.ReplaceExisting;
        }

        private static void matchMaterialsToDuplicatedSubMeshes(Mesh doubleSidedMesh, Renderer meshRenderer)
        {
            if (meshRenderer != null && meshRenderer.sharedMaterials.Length <= doubleSidedMesh.subMeshCount / 2)
            {
                var materials = new Material[doubleSidedMesh.subMeshCount];
                for (int i = 0; i < meshRenderer.sharedMaterials.Length; i++)
                {
                    materials[i] = meshRenderer.sharedMaterials[i];
                    materials[i + doubleSidedMesh.subMeshCount / 2] = meshRenderer.sharedMaterials[i];
                }
                meshRenderer.sharedMaterials = materials;
            }
        }

        public static void MakeDoubleSided(SkinnedMeshRenderer meshRenderer, SubMeshBehaviour subMeshBehaviour)
        {
            if (meshRenderer == null)
                return;

            if (meshRenderer.sharedMesh == null)
                return;

            // Path of the new mesh asset.
            string newAssetPath = getNewAssetPath(meshRenderer);

            // Skip if it already is double sided.
            if (meshRenderer.sharedMesh.name.EndsWith(doubleSidedNamePart))
            {
                Debug.Log($"<color=orange>Aborting Action</color> because {meshRenderer.sharedMesh.name} in {meshRenderer.name} is already double-sided '{newAssetPath}'.");
                return;
            }

            // Check if the asset already exists.
            var choice = askHowToHandleExistingMesh(newAssetPath, out Mesh existingMesh);
            switch (choice)
            {
                case CreationChoice.UsingExisting:
                    meshRenderer.sharedMesh = existingMesh;
                    return;

                case CreationChoice.CreateWithNewName:
                    newAssetPath = AssetDatabase.GenerateUniqueAssetPath(newAssetPath);
                    break;
            }

            Mesh doubleSidedMesh;
            if (subMeshBehaviour == SubMeshBehaviour.Preserve)
                doubleSidedMesh = MakeDoubleSided(meshRenderer.sharedMesh);
            else
                doubleSidedMesh = MakeDoubleSidedAndDuplicateSubMeshes(meshRenderer.sharedMesh);

            // If shadow casting was on then switch to two-sided as it will yield better results
            // see: https://docs.unity3d.com/Manual/ShadowPerformance.html
            if (meshRenderer.shadowCastingMode == ShadowCastingMode.On)
                meshRenderer.shadowCastingMode = ShadowCastingMode.TwoSided;

            // Add as new asset to a directory
            SaveMeshAsAsset(doubleSidedMesh, newAssetPath);

            // Replace mesh reference
            // Actually we could replace the reference in immutable prefabs asset
            // (models) too, yet these changes will not persist. Thus we do not
            // do it as it would be very confusting to the user.
            bool canChangeSharedMesh = !PrefabUtility.IsPartOfImmutablePrefab(meshRenderer.gameObject) || PrefabUtility.IsPartOfPrefabInstance(meshRenderer.gameObject);
            if (canChangeSharedMesh)
            {
                meshRenderer.sharedMesh = doubleSidedMesh;
                if (subMeshBehaviour == SubMeshBehaviour.Duplicate)
                {
                    matchMaterialsToDuplicatedSubMeshes(doubleSidedMesh, meshRenderer);
                }
            }
        }

        /// <summary>
        /// Duplicates all vertices and triangles and inverts them.
        /// <br />
        /// Sub meshes are preserved. This means that the resulting mesh
        /// will have the same number of sub meshes as the original and
        /// each new triangle will be assigned to the same sub mesh as the 
        /// original.
        /// </summary>
        /// <param name="mesh"></param>
        /// <returns></returns>
        public static Mesh MakeDoubleSided(Mesh mesh)
        {
            if (mesh != null)
            {
                int halfVertexCount = mesh.vertexCount;
                int newVertexCount = mesh.vertexCount * 2;
                Mesh newMesh = new Mesh();
                newMesh.indexFormat = newVertexCount > 65536 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                newMesh.vertices = doubleArray(mesh.vertices);

                // double normals and invert the second half of them 
                var normals = doubleArray(mesh.normals);
                for (int i = normals.Length / 2; i < normals.Length; i++)
                {
                    normals[i] = -normals[i];
                }
                newMesh.normals = normals;

                newMesh.tangents = doubleArray(mesh.tangents);
                newMesh.uv = doubleArray(mesh.uv);
                newMesh.SetBoneWeights(doubleArray(mesh.GetBonesPerVertex()), doubleArray(mesh.GetAllBoneWeights()));
                newMesh.bindposes = mesh.bindposes;
                newMesh.subMeshCount = mesh.subMeshCount;
                for (int m = 0; m < mesh.subMeshCount; m++)
                {
                    // Double the triangles
                    var subMeshTris = doubleArray(mesh.GetTriangles(m));

                    // shift them to the second half so they use the second part of the vertices,normals,uvs, ...
                    for (int i = subMeshTris.Length / 2; i < subMeshTris.Length; i += 3)
                    {
                        subMeshTris[i + 0] += halfVertexCount;
                        subMeshTris[i + 1] += halfVertexCount;
                        subMeshTris[i + 2] += halfVertexCount;
                    }

                    // invert the second half of them
                    int halfStartIndex = subMeshTris.Length / 2;
                    int halfRevertEndIndex = halfStartIndex + subMeshTris.Length / 4;
                    for (int i = halfStartIndex; i < halfRevertEndIndex; i++)
                    {
                        int temp = subMeshTris[i];
                        subMeshTris[i] = subMeshTris[subMeshTris.Length - (i - halfStartIndex) - 1];
                        subMeshTris[subMeshTris.Length - (i - halfStartIndex) - 1] = temp;
                    }
                    newMesh.SetTriangles(subMeshTris, m);
                }

                newMesh.RecalculateTangents();

                newMesh.name = mesh.name + " " + doubleSidedNamePart;
                return newMesh;
            }

            return null;
        }

        /// <summary>
        /// Duplicates all vertices and triangles and inverts them.
        /// <br />
        /// Sub meshes are duplicated. This means that the resulting mesh
        /// will have double the number of sub meshes as the original.
        /// Each new triangle will be assigned to a "copy" sub mesh.<br />
        /// This us useful if for example you have a single sided quad
        /// and want to assign a different material to each side.
        /// </summary>
        /// <param name="mesh"></param>
        /// <returns></returns>
        public static Mesh MakeDoubleSidedAndDuplicateSubMeshes(Mesh mesh)
        {
            if (mesh != null)
            {
                int halfVertexCount = mesh.vertexCount;
                int newVertexCount = mesh.vertexCount * 2;
                Mesh newMesh = new Mesh();
                newMesh.indexFormat = newVertexCount > 65536 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                newMesh.vertices = doubleArray(mesh.vertices);

                // double normals and invert the second half of them 
                var normals = doubleArray(mesh.normals);
                for (int i = normals.Length / 2; i < normals.Length; i++)
                {
                    normals[i] = -normals[i];
                }
                newMesh.normals = normals;

                newMesh.tangents = doubleArray(mesh.tangents);
                newMesh.uv = doubleArray(mesh.uv);
                newMesh.SetBoneWeights(doubleArray(mesh.GetBonesPerVertex()), doubleArray(mesh.GetAllBoneWeights()));
                newMesh.bindposes = mesh.bindposes;
                newMesh.subMeshCount = mesh.subMeshCount * 2;
                for (int m = 0; m < mesh.subMeshCount; m++)
                {
                    var subMeshTris = mesh.GetTriangles(m);

                    // SUB MESH A
                    // is just a copy
                    newMesh.SetTriangles(subMeshTris, m);

                    // SUB MESH B
                    // shift the tris, so they use the second part of the vertices,normals,uvs, ...
                    for (int i = 0; i < subMeshTris.Length; i += 3)
                    {
                        subMeshTris[i + 0] += halfVertexCount;
                        subMeshTris[i + 1] += halfVertexCount;
                        subMeshTris[i + 2] += halfVertexCount;
                    }

                    // invert the triangles
                    for (int i = 0; i < subMeshTris.Length / 2; i++)
                    {
                        int temp = subMeshTris[i];
                        subMeshTris[i] = subMeshTris[subMeshTris.Length - i - 1];
                        subMeshTris[subMeshTris.Length - i - 1] = temp;
                    }

                    // assign to new sub mesh index by shifting the index
                    newMesh.SetTriangles(subMeshTris, m + mesh.subMeshCount);
                }

                newMesh.RecalculateTangents();

                newMesh.name = mesh.name + " " + doubleSidedNamePart;
                return newMesh;
            }

            return null;
        }

        static T[] doubleArray<T>(T[] arr)
        {
            var newArray = new T[arr.Length * 2];
            Array.Copy(arr, newArray, arr.Length);
            Array.Copy(arr, 0, newArray, arr.Length, arr.Length);
            return newArray;
        }

        static NativeArray<T> doubleArray<T>(NativeArray<T> arr) where T : struct
        {
            var newArray = new NativeArray<T>(arr.Length * 2, Allocator.Temp); // Temp allocs are auto disposed at frame end.
            NativeArray<T>.Copy(arr, newArray, arr.Length);
            NativeArray<T>.Copy(arr, 0, newArray, arr.Length, arr.Length);
            return newArray;
        }

        public static void AddMeshToAsset(Mesh mesh, string assetPath)
        {
            if (mesh == null)
                return;

            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);

            AssetDatabase.AddObjectToAsset(mesh, assetPath);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            AssetDatabase.Refresh();

            Debug.Log($"Added new double-sided mesh to <color=yellow>'{assetPath}'</color>.");
        }

        public static void SaveMeshAsAsset(Mesh mesh, string assetPath)
        {
            if (mesh == null)
                return;

            // List to remember the affected components.
            List<Component> componentsWithExistingMesh = null;

            // Check if the asset already exists.
            var existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (existingMesh != null)
            {
                Undo.RegisterCompleteObjectUndo(existingMesh, "Double-sided mesh updated.");

                // Remember the affected components.
                componentsWithExistingMesh = getComponentsInLoadedScenesWithReferenceToMesh(existingMesh);
            }

            AssetDatabase.CreateAsset(mesh, assetPath);
            AssetDatabase.SaveAssets();
            // Important to force the reimport to avoid the "SkinnedMeshRenderer: Mesh has
            // been changed to one which is not compatibile with the expected mesh data size
            // and vertex stride." error.
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();

            // Sadly "Undo.RegisterCreatedObjectUndo" does not work here. TODO: investigate
            // if (existingMesh == null)
            //    Undo.RegisterCreatedObjectUndo(mesh, "Make Mesh double-sided new asset");

            Debug.Log($"Saved new double-sided mesh under <color=yellow>'{assetPath}'</color>.");

            // Patch old references in scenes so they now all point to the new mesh.
            if (componentsWithExistingMesh != null)
                setComponentMeshInLoadedScenes(componentsWithExistingMesh, mesh);
        }

        /// <summary>
        /// Returns a list of all the MeshRenderer or SkinnedMeshRenderer components in
        /// the loaded scene (in the hierarchy) which have a reference to given "mesh".
        /// </summary>
        /// <param name="mesh"></param>
        /// <returns></returns>
        static List<Component> getComponentsInLoadedScenesWithReferenceToMesh(Mesh mesh)
        {
            var components = new List<Component>();

            int numOfScenes = EditorSceneManager.sceneCount;
            for (int i = 0; i < numOfScenes; i++)
            {
                var scene = EditorSceneManager.GetSceneAt(i);
                var rootObjects = scene.GetRootGameObjects();
                for (int r = 0; r < rootObjects.Length; r++)
                {
                    var root = rootObjects[r];

                    var meshFilters = root.GetComponentsInChildren<MeshFilter>(includeInactive: true);
                    foreach (var meshFilter in meshFilters)
                    {
                        if (meshFilter.sharedMesh == mesh)
                            components.Add(meshFilter);
                    }

                    var meshRenderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true);
                    foreach (var meshRenderer in meshRenderers)
                    {
                        if (meshRenderer.sharedMesh == mesh)
                            components.Add(meshRenderer);
                    }
                }
            }

            return components;
        }

        static void setComponentMeshInLoadedScenes(List<Component> componentsWithExistingMesh, Mesh newMesh)
        {
            foreach (var component in componentsWithExistingMesh)
            {
                if (component is MeshFilter meshFilter)
                {
                    meshFilter.sharedMesh = newMesh;
                }
                if (component is SkinnedMeshRenderer skinnedMeshRenderer)
                {
                    skinnedMeshRenderer.sharedMesh = newMesh;
                }
            }
        }

        static string getNewAssetPath(MeshFilter meshFilter)
        {
            string sharedMeshName = meshFilter.sharedMesh.name.Replace("." + doubleSidedNamePart, "");

            // Try to get the prefab path first.
            string filePath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(meshFilter);

            // Maybe it's an object within the prefab stage?
            if (string.IsNullOrEmpty(filePath))
                filePath = getPrefabStageAssetPath();

            // Not a prefab -> get path from the scene.
            if (string.IsNullOrEmpty(filePath))
                filePath = getFilePathForSceneObject(meshFilter.gameObject);

            return getNewAssetPath(filePath, sharedMeshName);
        }

        static string getNewAssetPath(SkinnedMeshRenderer meshRenderer)
        {
            string sharedMeshName = meshRenderer.sharedMesh.name.Replace("." + doubleSidedNamePart, "");

            // We want the models to be created in the same folder as the source object (prefab, scene, ..)
            // Try to get the prefab path first.
            string filePath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(meshRenderer);

            // Maybe it's an object within the prefab stage?
            if (string.IsNullOrEmpty(filePath))
                filePath = getPrefabStageAssetPath();

            // Not a prefab -> get path from the scene.
            if (string.IsNullOrEmpty(filePath))
                filePath = getFilePathForSceneObject(meshRenderer.gameObject);

            return getNewAssetPath(filePath, sharedMeshName);
        }

        static string getPrefabStageAssetPath()
        {
#if UNITY_2021_2_OR_NEWER
            var prefabStage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
#else
            var prefabStage = UnityEditor.Experimental.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
#endif
            if (prefabStage != null)
                return prefabStage.assetPath;

            return null;
        }

        static string getFilePathForSceneObject(GameObject go)
        {
            string path;
            if (Directory.Exists("Assets/Models"))
                path = "Assets/Models/";
            else
                path = "Assets/";
            string sceneName = go.scene == null ? "" : go.scene.name;
            sceneName = sanitizePath(sceneName);

            string objectName = go.name;
            objectName = sanitizePath(objectName);

            path = path + sceneName + "." + objectName;
            return path;
        }

        static string getNewAssetPath(string filePath, string sharedMeshName)
        {
            // Fallback in case the file path is invalid.
            if (string.IsNullOrEmpty(filePath))
            {
                return "Assets/" + sharedMeshName + "." + doubleSidedNamePart + ".asset";
            }

            int lastIndex = filePath.LastIndexOf('/');
            string path = filePath.Substring(0, lastIndex + 1);

            // avoid chaining the same name if called multiple times
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            if (sharedMeshName.StartsWith(fileName + "."))
                sharedMeshName = sharedMeshName.Substring(fileName.Length + 1);

            sharedMeshName = sanitizePath(sharedMeshName);
            return path + fileName + "." + sharedMeshName + "." + doubleSidedNamePart + ".asset";
        }

        [MenuItem("Tools/Make Mesh/Open Manual", priority = 400)]
        public static void OpenManual()
        {
            Application.OpenURL("https://kamgam.com/unity/MakeMeshDoubleSidedManual.pdf");
        }

        [MenuItem("Tools/Make Mesh/Please leave a review :-)", priority = 400)]
        public static void Review()
        {
            Application.OpenURL("https://assetstore.unity.com/packages/slug/231044");
        }
    }
}
#endif