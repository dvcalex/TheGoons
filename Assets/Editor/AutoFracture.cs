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
            if (AssetDatabase.IsValidFolder(p)) root = p;
        }

        if (LibreFracture.LibreFracture.FractureLayer == -1)
            return; // error already logged

        EnsureFolder(k_OutputFolder);
        EnsureFolder(k_MeshFolder);

        // For each prefab in folder & subfolders
        var guids = AssetDatabase.FindAssets("t:Prefab", new[] { root });
        var paths = new List<string>();
        foreach (var g in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(g);
            if (path.StartsWith(k_OutputFolder)) continue; // don't re-fracture our output
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

        // Place prefab in scene
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

        // Unpack prefab completely
        PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

        // Get child with mesh filter
        var meshFilter = instance.GetComponentInChildren<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            Debug.LogWarning($"[AutoFracture] No MeshFilter found in {prefab.name}, skipping.");
            Object.DestroyImmediate(instance);
            return false;
        }

        GameObject original = meshFilter.gameObject;

        // Unparent child with mesh filter
        if (original != instance)
        {
            original.transform.SetParent(null, true);
            Object.DestroyImmediate(instance); // leftover container is no longer needed
        }

        original.name = prefab.name;
        if (!LibreFracture.LibreFracture.IsFracturable(original))
        {
            Debug.LogWarning($"[AutoFracture] {original.name} is not fracturable, skipping.");
            Object.DestroyImmediate(original);
            return false;
        }

        // Get bounds of child (world space, includes scale)
        Bounds bounds = GetBounds(original);

        // Select child
        Selection.activeGameObject = original;

        // Fracture child based on bounds
        var parameters = BuildParameters(bounds);
        GameObject fractured = LibreFracture.LibreFracture.CreateFracturedCopyOf(original, parameters);

        // Parent fractured GO under the original mesh
        fractured.transform.SetParent(original.transform, true);
        fractured.name = k_Prefix + original.name;

        // Chunk meshes only live in memory, so persist them or the prefab will lose them
        string meshDir = $"{k_MeshFolder}/{original.name}";
        EnsureFolder(meshDir);
        PersistMeshes(fractured, meshDir);

        // Fractured version stays hidden until ToggleFractureMesh enables it
        fractured.SetActive(false);

        // Add ToggleFractureMesh component to original mesh with reference to fractured GO
        var toggle = original.AddComponent<ToggleFractureMesh>();
        toggle.fracturedObject = fractured; // <-- adjust if your field is named differently

        // Prefab the original mesh with fractured GO as child, name to Fractured_<original name>
        string outPath = AssetDatabase.GenerateUniqueAssetPath($"{k_OutputFolder}/{k_Prefix}{prefab.name}.prefab");
        original.name = k_Prefix + prefab.name;
        PrefabUtility.SaveAsPrefabAsset(original, outPath);

        Object.DestroyImmediate(original);
        return true;
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

    private static Bounds GetBounds(GameObject go)
    {
        var r = go.GetComponentInChildren<Renderer>();
        return r != null ? r.bounds : new Bounds(go.transform.position, Vector3.one);
    }

    private static void PersistMeshes(GameObject root, string folder)
    {
        int n = 0;

        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null || EditorUtility.IsPersistent(mf.sharedMesh)) continue;
            var mesh = mf.sharedMesh;
            AssetDatabase.CreateAsset(mesh, $"{folder}/{mf.gameObject.name}_{n++}.asset");
        }

        // MeshColliders (including VHACD ones) may reference non-persistent meshes too
        foreach (var mc in root.GetComponentsInChildren<MeshCollider>(true))
        {
            if (mc.sharedMesh == null || EditorUtility.IsPersistent(mc.sharedMesh)) continue;
            AssetDatabase.CreateAsset(mc.sharedMesh, $"{folder}/{mc.gameObject.name}_col_{n++}.asset");
        }
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