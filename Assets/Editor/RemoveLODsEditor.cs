
using UnityEngine;
using UnityEditor;

public class RemoveLODsFromEditor
{
    [MenuItem("Tools/Prefabs/Remove LODs From All Prefabs")]
    private static void RemoveLODs()
    {
        if (!EditorUtility.DisplayDialog(
                "Remove LODs From All Prefabs",
                "This will remove all LODGroup components " +
                "from prefabs in the project. This operation " +
                "will modify prefab assets.\n\nContinue?",
                "Remove LODs",
                "Cancel"))
        {
            return;
        }

        string[] prefabGuids =
            AssetDatabase.FindAssets("t:Prefab");

        int modifiedCount = 0;
        int total = prefabGuids.Length;

        try
        {
            for (int i = 0; i < total; i++)
            {
                string path =
                    AssetDatabase.GUIDToAssetPath(prefabGuids[i]);

                EditorUtility.DisplayProgressBar(
                    "Removing LODs",
                    path,
                    total == 0 ? 1f : (float)i / total);

                GameObject prefabRoot =
                    PrefabUtility.LoadPrefabContents(path);

                bool modified = false;

                try
                {
                    LODGroup[] lodGroups =
                        prefabRoot.GetComponentsInChildren<LODGroup>(true);

                    if (lodGroups.Length > 0)
                    {
                        foreach (LODGroup lodGroup in lodGroups)
                        {
                            Object.DestroyImmediate(lodGroup, true);
                        }

                        PrefabUtility.SaveAsPrefabAsset(
                            prefabRoot, path);

                        modified = true;
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(prefabRoot);
                }

                if (modified)
                    modifiedCount++;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog(
            "Remove LODs Complete",
            $"Processed {total} prefabs.\n" +
            $"Modified {modifiedCount} prefabs.",
            "OK");
    }
}