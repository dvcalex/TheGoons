#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using LibreFracture;
using LF = LibreFracture.LibreFracture;

public static class AutoFracture
{
    private const string k_OutputFolder = "Assets/FracturedPrefabs";
    private const string k_MeshFolder = k_OutputFolder + "/Meshes";
    private const string k_Prefix = "Fractured_";

    // Tuning knobs for bounds-based fracturing
    private const float k_ChunksPerCubicUnit = 20f;
    private const int k_MinChunks = 5;
    private const int k_MaxChunks = 60;

    [MenuItem("Tools/LibreFracture/Auto Fracture Prefabs In Selected Folder")]
    public static void Run()
    {
        // Use the folder selected in the Project window, fall back to Assets
        string root = "Assets";
        var selected = Selection.activeObject;
        if (selected != null)
        {
            var p = AssetDatabase.GetAssetPath(selected);
            if (AssetDatabase.IsValidFolder(p)) root = p;
        }

        if (LF.FractureLayer == -1)
            return; // error already logged

        EnsureFolder(k_OutputFolder);
        EnsureFolder(k_MeshFolder);

        // Only prefabs directly in the selected folder (no recursion), never our own output
        var paths = new List<string>();
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { root }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.StartsWith(k_OutputFolder)) continue;

            var dir = Path.GetDirectoryName(path).Replace('\\', '/');
            if (dir != root) continue;

            paths.Add(path);
        }

        int done = 0;
        try
        {
            for (int i = 0; i < paths.Count; i++)
            {
                if (EditorUtility.DisplayCancelableProgressBar("Auto Fracture", paths[i], (float)i / paths.Count))
                    break;

                try
                {
                    if (ProcessPrefab(paths[i])) done++;
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[AutoFracture] Failed on {paths[i]}: {e}");
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        Debug.Log($"[AutoFracture] Done. Fractured {done}/{paths.Count} prefabs into {k_OutputFolder}");
    }

    private static bool ProcessPrefab(string prefabPath)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null) return false;

        // 1. Place prefab in scene and unpack completely
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        try
        {
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            // Snapshot mesh objects up front: fracturing adds new MeshFilters to the hierarchy.
            var meshFilters = new List<MeshFilter>(instance.GetComponentsInChildren<MeshFilter>(true));
            meshFilters.RemoveAll(mf => mf.sharedMesh == null || mf.GetComponent<MeshRenderer>() == null);

            if (meshFilters.Count == 0)
            {
                Debug.LogWarning($"[AutoFracture] No MeshFilter + MeshRenderer found in {prefab.name}, skipping.");
                return false;
            }

            string meshDir = $"{k_MeshFolder}/{Sanitize(prefab.name)}";
            EnsureFolder(meshDir);
            int fracturedCount = 0;

            // 2. Loop through each mesh object
            foreach (var mf in meshFilters)
            {
                GameObject original = mf.gameObject;

                if (!LF.IsFracturable(original))
                {
                    Debug.LogWarning($"[AutoFracture] {prefab.name}/{original.name} is not fracturable, skipping.");
                    continue;
                }

                // Fracture based on bounds
                GameObject fractured = CreateFractured(mf, BuildParameters(mf));
                if (fractured == null) continue;

                // Parent fractured object under its source mesh object (identity local transform
                // == same world transform as the original)
                fractured.transform.SetParent(original.transform, false);
                fractured.name = k_Prefix + original.name;

                // Chunk meshes only exist in memory; save them so the prefab keeps them
                PersistMeshes(fractured, meshDir);

                // Mesh object gets ToggleFractureMesh + collider
                var toggle = original.AddComponent<ToggleFractureMesh>();
                toggle.fracturedObject = fractured; // <-- adjust if your field is named differently

                if (!original.TryGetComponent<MeshCollider>(out var col))
                    col = original.AddComponent<MeshCollider>();
                col.sharedMesh = mf.sharedMesh;

                fracturedCount++;
            }

            if (fracturedCount == 0)
                return false;

            // Put everything (including chunks) on the Nonbroken layer
            int nonbroken = LayerMask.NameToLayer("Nonbroken");
            if (nonbroken == -1)
                Debug.LogWarning("[AutoFracture] Layer 'Nonbroken' not found, leaving layers unchanged.");
            else
                foreach (var t in instance.GetComponentsInChildren<Transform>(true))
                    t.gameObject.layer = nonbroken;

            // 3. Save the root as a new prefab
            instance.name = k_Prefix + prefab.name;
            string outPath = AssetDatabase.GenerateUniqueAssetPath($"{k_OutputFolder}/{Sanitize(k_Prefix + prefab.name)}.prefab");
            PrefabUtility.SaveAsPrefabAsset(instance, outPath, out bool success);
            if (!success)
                Debug.LogError($"[AutoFracture] Failed to save prefab at {outPath}");
            return success;
        }
        finally
        {
            if (instance != null) Object.DestroyImmediate(instance);
        }
    }

    /// <summary>
    /// Fractures a standalone proxy (just mesh + renderer) instead of a copy of the whole
    /// object, so child objects and other components aren't duplicated into the result.
    /// </summary>
    private static GameObject CreateFractured(MeshFilter source, FractureParameters parameters)
    {
        var proxy = new GameObject(source.gameObject.name);
        proxy.AddComponent<MeshFilter>().sharedMesh = source.sharedMesh;
        proxy.AddComponent<MeshRenderer>().sharedMaterials = source.GetComponent<MeshRenderer>().sharedMaterials;

        LF.Fracture(proxy, parameters);

        if (proxy.transform.childCount == 0)
        {
            Object.DestroyImmediate(proxy);
            return null;
        }

        return proxy;
    }

    /// <summary>
    /// Fracture settings derived from the object's bounds (mesh bounds scaled to world size).
    /// FractureParameters is abstract, so this uses VoronoiParameters.
    /// Swap in ClusteredParameters / SlicingParameters here for a different fracture type.
    /// </summary>
    private static FractureParameters BuildParameters(MeshFilter mf)
    {
        Vector3 size = Vector3.Scale(mf.sharedMesh.bounds.size, mf.transform.lossyScale);
        float volume = Mathf.Abs(size.x * size.y * size.z);

        return new VoronoiParameters
        {
            totalChunks = Mathf.Clamp(Mathf.RoundToInt(volume * k_ChunksPerCubicUnit), k_MinChunks, k_MaxChunks),
            totalObjectMass = Mathf.Max(0.1f, volume), // rough: 1 unit^3 ~ 1 kg
            enableIslands = false,
            // insideMaterial left null -> chunks reuse the outside material
        };
    }

    private static void PersistMeshes(GameObject root, string folder)
    {
        int n = 0;

        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            SaveMesh(mf.sharedMesh, folder, $"{mf.gameObject.name}_{n++}");

        // MeshColliders (including VHACD ones) may reference non-persistent meshes too
        foreach (var mc in root.GetComponentsInChildren<MeshCollider>(true))
            SaveMesh(mc.sharedMesh, folder, $"{mc.gameObject.name}_col_{n++}");
    }

    private static void SaveMesh(Mesh mesh, string folder, string name)
    {
        if (mesh == null || EditorUtility.IsPersistent(mesh)) return;
        name = Sanitize(name);
        mesh.name = name;
        // Unique path so re-running never overwrites meshes used by earlier output prefabs
        AssetDatabase.CreateAsset(mesh, AssetDatabase.GenerateUniqueAssetPath($"{folder}/{name}.asset"));
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
#endif