
using UnityEngine;
using UnityEditor;

public class KeepOneLODChild
{
    [MenuItem("Tools/Prefabs/Keep One LOD Child")]
    private static void KeepOneChild()
    {
        if (!EditorUtility.DisplayDialog(
                "Keep One LOD Child",
                "This will process all prefabs.\n\n" +
                "For every GameObject containing an LODGroup:\n" +
                "- Remove the LODGroup component\n" +
                "- Keep the first child\n" +
                "- Delete all remaining children\n\nContinue?",
                "Continue",
                "Cancel"))
        {
            return;
        }

        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab");

        int modifiedCount = 0;
        int removedLODGroupCount = 0;
        int deletedCount = 0;

        try
        {
            for (int i = 0; i < prefabGuids.Length; i++)
            {
                string path =
                    AssetDatabase.GUIDToAssetPath(prefabGuids[i]);

                EditorUtility.DisplayProgressBar(
                    "Processing Prefabs",
                    path,
                    (float)i / prefabGuids.Length);

                GameObject prefab =
                    PrefabUtility.LoadPrefabContents(path);

                try
                {
                    // Include inactive children and nested hierarchy objects.
                    LODGroup[] lodGroups =
                        prefab.GetComponentsInChildren<LODGroup>(true);

                    bool modified = false;

                    foreach (LODGroup lodGroup in lodGroups)
                    {
                        if (lodGroup == null)
                            continue;

                        Transform lodTransform = lodGroup.transform;

                        // Remove the LODGroup component itself.
                        Object.DestroyImmediate(lodGroup);
                        removedLODGroupCount++;
                        modified = true;

                        // Keep the first direct child and delete the rest.
                        while (lodTransform.childCount > 1)
                        {
                            Transform child =
                                lodTransform.GetChild(
                                    lodTransform.childCount - 1);

                            Object.DestroyImmediate(child.gameObject);
                            deletedCount++;
                        }
                    }

                    if (modified)
                    {
                        PrefabUtility.SaveAsPrefabAsset(prefab, path);
                        modifiedCount++;
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(prefab);
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog(
            "Complete",
            $"Processed {prefabGuids.Length} prefabs.\n" +
            $"Modified prefabs: {modifiedCount}\n" +
            $"Removed LODGroup components: {removedLODGroupCount}\n" +
            $"Deleted children: {deletedCount}",
            "OK");
    }
}