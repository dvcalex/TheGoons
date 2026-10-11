using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class RandomPrefabReplacer : EditorWindow
{
    private List<GameObject> prefabList = new List<GameObject>();
    private bool keepOriginalScale = true;
    private bool keepOriginalRotation = true;
    private Vector2 scrollPosition;

    [MenuItem("Tools/Random Prefab Replacer")]
    public static void ShowWindow()
    {
        GetWindow<RandomPrefabReplacer>("Prefab Replacer");
    }

    private void OnGUI()
    {
        GUILayout.Label("Random Prefab Replacer", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // Options
        keepOriginalScale = EditorGUILayout.Toggle("Keep Original Scale", keepOriginalScale);
        keepOriginalRotation = EditorGUILayout.Toggle("Keep Original Rotation", keepOriginalRotation);
        EditorGUILayout.Space();

        // Prefab List GUI
        GUILayout.Label("Prefab List", EditorStyles.boldLabel);

        // List size control
        int newSize = EditorGUILayout.IntField("Size", prefabList.Count);
        if (newSize != prefabList.Count)
        {
            ResizeList(newSize);
        }

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(150));
        for (int i = 0; i < prefabList.Count; i++)
        {
            prefabList[i] = (GameObject)EditorGUILayout.ObjectField($"Prefab {i + 1}", prefabList[i], typeof(GameObject), false);
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space();

        if (GUILayout.Button("Replace Selected Objects", GUILayout.Height(40)))
        {
            ReplaceObjects();
        }
    }

    private void ResizeList(int newSize)
    {
        if (newSize < 0)
            newSize = 0;
        while (prefabList.Count < newSize)
        {
            prefabList.Add(null);
        }
        if (prefabList.Count > newSize)
        {
            prefabList.RemoveRange(newSize, prefabList.Count - newSize);
        }
    }

    private void ReplaceObjects()
    {
        // Filter out null entries from the prefab list
        List<GameObject> validPrefabs = new List<GameObject>();
        foreach (var prefab in prefabList)
        {
            if (prefab != null)
                validPrefabs.Add(prefab);
        }

        if (validPrefabs.Count == 0)
        {
            EditorUtility.DisplayDialog("Error", "Please add at least one valid prefab to the list.", "OK");
            return;
        }

        GameObject[] selectedObjects = Selection.gameObjects;
        if (selectedObjects.Length == 0)
        {
            EditorUtility.DisplayDialog("Error", "Please select at least one object in the scene to replace.", "OK");
            return;
        }

        Undo.RegisterCompleteObjectUndo(selectedObjects, "Replace Objects with Prefabs");

        foreach (GameObject oldObj in selectedObjects)
        {
            // Pick a random prefab
            GameObject randomPrefab = validPrefabs[Random.Range(0, validPrefabs.Count)];

            // Instantiate prefab as a prefab instance linked to the asset
            GameObject newObj = (GameObject)PrefabUtility.InstantiatePrefab(randomPrefab);

            if (newObj != null)
            {
                // Match position
                newObj.transform.position = oldObj.transform.position;

                // Match rotation
                if (keepOriginalRotation)
                {
                    newObj.transform.rotation = oldObj.transform.rotation;
                }
                else
                {
                    newObj.transform.rotation = randomPrefab.transform.rotation;
                }

                // Match scale
                if (keepOriginalScale)
                {
                    newObj.transform.localScale = oldObj.transform.localScale;
                }
                else
                {
                    newObj.transform.localScale = randomPrefab.transform.localScale;
                }

                // Parent the new object to the old object's parent
                newObj.transform.SetParent(oldObj.transform.parent);

                // Preserve sibling index to maintain hierarchy order
                newObj.transform.SetSiblingIndex(oldObj.transform.GetSiblingIndex());

                // Register creation for Undo support
                Undo.RegisterCreatedObjectUndo(newObj, "Replace with Prefab");

                // Destroy the old object
                Undo.DestroyObjectImmediate(oldObj);
            }
        }

        Debug.Log($"Successfully replaced {selectedObjects.Length} objects with random prefabs.");
    }
}
