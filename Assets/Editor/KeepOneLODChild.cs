using UnityEngine;
using UnityEditor;

public class KeepOneLODChild
{
    [MenuItem("Tools/Prefabs/Keep One LOD Child")]
    private static void KeepOneChild()
    {
        if (!EditorUtility.DisplayDialog(
            "Keep One LOD Child",
            "This will keep the first child inside each " + "'LOD Group' GameObject and delete the others " + "in all prefabs.\n\nContinue?",
            "Continue",
            "Cancel"
        ))
        {
            return;
        }

        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab");
        int modifiedCount = 0;
        int deletedCount = 0;

        try
        {
            for (int i = 0; i < prefabGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
                EditorUtility.DisplayProgressBar("Processing Prefabs", path, (float)i / prefabGuids.Length);
                GameObject prefab = PrefabUtility.LoadPrefabContents(path);

                try
                {
                    Transform[] transforms = prefab.GetComponentsInChildren<Transform>(true);
                    bool modified = false;

                    foreach (Transform lodGroup in transforms)
                    {
                        if (lodGroup == null || lodGroup.name != "LOD Group")
                        {
                            continue;
                        }

                        // Keep the prefab hierarchy intact.
                        // Only process children of "LOD Group".
                        while (lodGroup.childCount > 1)
                        {
                            Transform child = lodGroup.GetChild(lodGroup.childCount - 1);
                            Object.DestroyImmediate(child.gameObject);
                            deletedCount++;
                            modified = true;
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
            $"Processed {prefabGuids.Length} prefabs.\n" + $"Modified prefabs: {modifiedCount}\n" + $"Deleted children: {deletedCount}",
            "OK"
        );
    }
}
