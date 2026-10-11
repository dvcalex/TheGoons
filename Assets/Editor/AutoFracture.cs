#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using LibreFracture;

public static class AutoFracture
{
    private const string k_OutputFolder = "Assets/FracturedPrefabs";
    private const string k_MeshFolder = k_OutputFolder + "/Meshes";
    private const string k_Prefix = "Fractured_";
    [MenuItem("Tools/LibreFracture/Auto Fracture Prefabs In Selected Folder")]
    public static void Run()
    {
        // Use the folder selected in the Project window, fall back to Assets
        string root = "Assets";
        var selected = Selection.activeObject;

        if (selected != null)
        {
            var p = AssetDatabase.GetAssetPath(selected);

            if (AssetDatabase.IsValidFolder(p))
            {
                root = p;
            }
        }

        if (LibreFracture.LibreFracture.FractureLayer == -1)
        {
            return; // error already logged
        }

        EnsureFolder(k_OutputFolder);
        EnsureFolder(k_MeshFolder);

        // For each prefab in folder & subfolders
        var guids = AssetDatabase.FindAssets("t:Prefab", new[] { root });
        var paths = new List<string>();

        foreach (var g in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(g);

            if (path.StartsWith(k_OutputFolder))
            {
                continue; // don't re-fracture our output
            }

            // Only include prefabs that are directly in the selected folder (no recursion)
            var dir = Path.GetDirectoryName(path).Replace('\\', '/');

            if (dir != root)
            {
                continue;
            }

            paths.Add(path);
        }

        int done = 0;

        try
        {
            for (int i = 0; i < paths.Count; i++)
            {
                if (EditorUtility.DisplayCancelableProgressBar("Auto Fracture", paths[i], (float)i / paths.Count))
                {
                    break;
                }

                try
                {
                    if (ProcessPrefab(paths[i]))
                    {
                        done++;
                    }
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

        if (prefab == null)
        {
            return false;
        }

        // Place prefab in scene and unpack completely
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

        // Snapshot every mesh object up front (including inactive ones).
        // Fracturing adds new MeshFilters to the hierarchy, so we must not query live.
        var meshFilters = new List<MeshFilter>(instance.GetComponentsInChildren<MeshFilter>(true));
        meshFilters.RemoveAll(mf => mf.sharedMesh == null);

        if (meshFilters.Count == 0)
        {
            Debug.LogWarning($"[AutoFracture] No MeshFilter found in {prefab.name}, skipping.");
            Object.DestroyImmediate(instance);
            return false;
        }

        string meshDir = $"{k_MeshFolder}/{prefab.name}";
        EnsureFolder(meshDir);
        int meshCounter = 0; // shared across the whole prefab so asset names never collide
        int fracturedCount = 0;

        foreach (var mf in meshFilters)
        {
            GameObject original = mf.gameObject;

            if (!LibreFracture.LibreFracture.IsFracturable(original))
            {
                Debug.LogWarning($"[AutoFracture] {prefab.name}/{original.name} is not fracturable, skipping.");
                continue;
            }

            Bounds bounds = GetBounds(original);
            Selection.activeGameObject = original;
            var parameters = BuildParameters(bounds);
            GameObject fractured = LibreFracture.LibreFracture.CreateFracturedCopyOf(original, parameters);

            if (fractured == null)
            {
                continue;
            }

            // Parent fractured GO under its source mesh object
            fractured.transform.SetParent(original.transform, true);
            fractured.name = k_Prefix + original.name;

            // Chunk meshes only live in memory, so persist them or the prefab will lose them
            PersistMeshes(fractured, meshDir, ref meshCounter);
            var toggle = original.AddComponent<ToggleFractureMesh>();
            original.AddComponent<MeshCollider>();
            toggle.fracturedObject = fractured; // <-- adjust if your field is named differently
            fracturedCount++;
        }

        if (fracturedCount == 0)
        {
            Object.DestroyImmediate(instance);
            return false;
        }

        // Set layer on everything in the prefab
        int nonbroken = LayerMask.NameToLayer("Nonbroken");

        if (nonbroken == -1)
        {
            Debug.LogWarning("[AutoFracture] Layer 'Nonbroken' not found, leaving layers unchanged.");
        }
        else
        {
            foreach (var t in instance.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer = nonbroken;
            }
        }

        instance.name = k_Prefix + prefab.name;
        string outPath = AssetDatabase.GenerateUniqueAssetPath($"{k_OutputFolder}/{k_Prefix}{prefab.name}.prefab");
        PrefabUtility.SaveAsPrefabAsset(instance, outPath);
        Object.DestroyImmediate(instance);
        return true;
    }

    // Bounds of just this object's own renderer (not children, which may be separate meshes)
    private static Bounds GetBounds(GameObject go)
    {
        var r = go.GetComponent<Renderer>();
        return r != null ? r.bounds : new Bounds(go.transform.position, Vector3.one);
    }

    private static void PersistMeshes(GameObject root, string folder, ref int n)
    {
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null || EditorUtility.IsPersistent(mf.sharedMesh))
            {
                continue;
            }

            AssetDatabase.CreateAsset(mf.sharedMesh, $"{folder}/{mf.gameObject.name}_{n++}.asset");
        }

        // MeshColliders (including VHACD ones) may reference non-persistent meshes too
        foreach (var mc in root.GetComponentsInChildren<MeshCollider>(true))
        {
            if (mc.sharedMesh == null || EditorUtility.IsPersistent(mc.sharedMesh))
            {
                continue;
            }

            AssetDatabase.CreateAsset(mc.sharedMesh, $"{folder}/{mc.gameObject.name}_col_{n++}.asset");
        }
    }

    // Tuning knobs for bounds-based fracturing
    private const float k_ChunksPerCubicUnit = 20f;
    private const int k_MinChunks = 5;
    private const int k_MaxChunks = 60;

    /// <summary>
    /// Fracture settings derived from the object's bounds.
    /// FractureParameters is abstract, so this uses VoronoiParameters.
    /// Swap in ClusteredParameters / SlicingParameters here if you want a different fracture type.
    /// </summary>
    private static FractureParameters BuildParameters(Bounds bounds)
    {
        float volume = bounds.size.x * bounds.size.y * bounds.size.z;
        var p = new VoronoiParameters
        {
            totalChunks = Mathf.Clamp(Mathf.RoundToInt(volume * k_ChunksPerCubicUnit), k_MinChunks, k_MaxChunks),
            totalObjectMass = Mathf.Max(0.1f, volume), // rough: 1 unit^3 ~ 1 kg
            enableIslands = false,

            // insideMaterial left null -> chunks reuse the outside material
        };
        return p;
    }

    private static void PersistMeshes(GameObject root, string folder)
    {
        int n = 0;

        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null || EditorUtility.IsPersistent(mf.sharedMesh))
            {
                continue;
            }

            var mesh = mf.sharedMesh;
            AssetDatabase.CreateAsset(mesh, $"{folder}/{mf.gameObject.name}_{n++}.asset");
        }

        // MeshColliders (including VHACD ones) may reference non-persistent meshes too
        foreach (var mc in root.GetComponentsInChildren<MeshCollider>(true))
        {
            if (mc.sharedMesh == null || EditorUtility.IsPersistent(mc.sharedMesh))
            {
                continue;
            }

            AssetDatabase.CreateAsset(mc.sharedMesh, $"{folder}/{mc.gameObject.name}_col_{n++}.asset");
        }
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        var parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}

#endif
