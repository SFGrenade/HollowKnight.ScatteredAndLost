using HK8YPlando.Scripts.Lib;
using HK8YPlando.Scripts.SharedLib;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.SceneManagement;

internal class TilemapShiftPopup : EditorWindow
{
    string xStr = "0";
    string yStr = "0";

    private void OnGUI()
    {
        xStr = EditorGUILayout.TextField("X", xStr);
        yStr = EditorGUILayout.TextField("Y", yStr);

        if (GUILayout.Button("Shift"))
        {
            int ix = int.Parse(xStr);
            int iy = int.Parse(yStr);
            MainMenuExtensions.ShiftTilemap(ix, iy);
            Close();
        }

        if (GUILayout.Button("Cancel")) Close();
    }
}

internal class RenameScenePopup : EditorWindow
{
    internal string newName = "";

    private void OnGUI()
    {
        newName = EditorGUILayout.TextField("New Name", newName);

        if (GUILayout.Button("Rename"))
        {
            MainMenuExtensions.RenameActiveScene(newName);
            Close();
        }

        if (GUILayout.Button("Cancel")) Close();
    }
}

internal class SearchScenesPopup : EditorWindow
{
    internal string behaviourName = "";

    private void OnGUI()
    {
        behaviourName = EditorGUILayout.TextField("Behaviour Name", behaviourName);

        if (GUILayout.Button("Search"))
        {
            MainMenuExtensions.SearchAllScenes(behaviourName);
            Close();
        }

        if (GUILayout.Button("Cancel")) Close();
    }
}

public class MainMenuExtensions
{
    [MenuItem("HK8YPlando/Scene/Rename")]
    static void RenameActiveScene()
    {
        var popup = ScriptableObject.CreateInstance<RenameScenePopup>();
        popup.newName = SceneManager.GetActiveScene().name;
        popup.ShowPopup();
    }

    internal static void RenameActiveScene(string newName)
    {
        var origScene = SceneManager.GetActiveScene();
        var origPath = origScene.path;
        var origName = origScene.name;
        var sceneEdits = new Dictionary<string, HashSet<string>>();
        foreach (var transition in origScene.FindComponentsRecursive<TransitionPoint>())
            sceneEdits.GetOrAddNew(transition.targetScene).Add(transition.entryPoint);

        foreach (var entry in sceneEdits)
        {
            var path = origPath.Replace(origName, entry.Key);
            try { EditorSceneManager.OpenScene(path); }
            catch
            {
                Debug.Log($"Couldn't fix transitions in scene '{path}'");
                continue;
            }

            var scene = SceneManager.GetActiveScene();
            foreach (var transition in scene.FindComponentsRecursive<TransitionPoint>())
                if (entry.Value.Contains(transition.name))
                {
                    transition.targetScene = newName;
                    UnityEditorShims.MarkDirty(transition);
                }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        var newPath = origPath.Replace(origName, newName);
        AssetDatabase.RenameAsset(origPath, newName);
        EditorSceneManager.OpenScene(newPath);
    }

    [MenuItem("HK8YPlando/Scene/Shift Tilemap")]
    static void ShiftTilemap() => ScriptableObject.CreateInstance<TilemapShiftPopup>().ShowPopup();

    [MenuItem("HK8YPlando/Scene/Re-center Tilemap")]
    static void RecenterTilemap()
    {
        var tilemap = Object.FindObjectOfType<Tilemap>();
        var oBounds = tilemap.cellBounds;
        int minX = oBounds.max.x;
        int minY = oBounds.max.y;
        for (int x = oBounds.min.x; x < oBounds.max.x; x++)
        {
            for (int y = oBounds.min.y; y < oBounds.max.y; y++)
            {
                var tile = tilemap.GetTile(new Vector3Int(x, y, 0));
                if (tile != null)
                {
                    minX = System.Math.Min(minX, x);
                    minY = System.Math.Min(minY, y);
                    break;
                }
            }
        }

        ShiftTilemap(-minX, -minY);
    }

    internal static void ShiftTilemap(int dx, int dy)
    {
        if (dx == 0 && dy == 0) return;

        var tilemap = Object.FindObjectOfType<Tilemap>();
        var oSize = tilemap.size;

        int bx = dx > 0 ? dx : 0;
        int by = dy > 0 ? dy : 0;
        foreach (var x in MathExt.Seq(0, oSize.x, dx > 0 ? -1 : 1))
        {
            foreach (var y in MathExt.Seq(0, oSize.y, dy > 0 ? -1 : 1))
            {
                var source = Vector3Int.zero;
                var dest = Vector3Int.zero;
                source.x = (dx > 0) ? x : (x - dx);
                source.y = (dy > 0) ? y : (y - dy);
                dest.x = (dx > 0) ? (x + dx) : x;
                dest.y = (dy > 0) ? (y + dy) : y;

                tilemap.SetTile(dest, tilemap.GetTile(source));
                tilemap.SetTile(source, null);
            }
        }
        tilemap.ResizeBounds();

        // Move all game objects not at origin.
        var scene = SceneManager.GetActiveScene();
        foreach (var obj in scene.GetRootGameObjects()) ShiftObject(obj, dx, dy);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    private static void ShiftObject(GameObject obj, int dx, int dy)
    {
        var pos = obj.transform.position;
        if (pos.x == 0 && pos.y == 0) foreach (var child in obj.Children()) ShiftObject(child, dx, dy);
        else obj.transform.position += new Vector3(dx, dy, 0);
    }

    [MenuItem("HK8YPlando/Scene/Optimize")]
    static void OptimizeCurrentScene()
    {
        var updates = OptimizerFinder.FixScene();
        if (updates.Count > 0)
        {
            Debug.Log("Optimized scene");
            foreach (var update in updates) Debug.Log(update);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }
        else Debug.Log("Scene already optimized");
    }

    private static string AssetBundleName(string sceneName) => sceneName.Replace("_", "").ToLower();

    [MenuItem("HK8YPlando/All Scenes/Search")]
    static void Search() => ScriptableObject.CreateInstance<SearchScenesPopup>().ShowPopup();

    internal static void SearchAllScenes(string behaviourName)
    {
        var count = new Dictionary<string, int>();
        foreach (var path in ForEachScene())
        {
            foreach (var b in Object.FindObjectsOfType<MonoBehaviour>())
            {
                if (b.GetType().Name == behaviourName)
                {
                    int c = count.GetOrAddNew(path);
                    count[path] = c + 1;
                }
            }
        }

        var results = new List<(int, string)>();
        foreach (var entry in count) results.Add((entry.Value, entry.Key));
        results.Sort();
        results.Reverse();
        var printed = new List<string>();
        foreach (var (c, p) in results) printed.Add($"{p}: {c}");

        Debug.Log($"Search results:\n  {string.Join("\n  ", printed.ToArray())}");
    }

    [MenuItem("HK8YPlando/All Scenes/Optimize")]
    static void OptimizeAllScenes()
    {
        var origPath = SceneManager.GetActiveScene().path;

        int scenesFixed = 0;
        int scenesUnfixed = 0;
        int scenesErrored = 0;
        var sceneNames = new HashSet<string>();
        foreach (var path in ForEachScene())
        {
            var scene = SceneManager.GetActiveScene();
            sceneNames.Add(scene.name);
            var importer = AssetImporter.GetAtPath(path);

            bool changed = false;
            var newName = AssetBundleName(scene.name);
            if (importer.assetBundleName != newName)
            {
                importer.assetBundleName = newName;
                changed = true;
            }

            try
            {
                var updates = OptimizerFinder.FixScene();
                if (updates.Count > 0 || changed)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    Debug.Log($"Updated {scene.name}: [{string.Join(", ", updates)}]");
                    ++scenesFixed;
                }
                else ++scenesUnfixed;
            }
            catch (System.Exception ex)
            {
                Debug.Log($"Failed to optimize {scene.name}: {ex}");
                ++scenesErrored;
            }
        }

        AssetDatabase.RemoveUnusedAssetBundleNames();

        Debug.Log($"Optimized {scenesFixed + scenesUnfixed + scenesErrored} scenes; updated {scenesFixed}, {scenesUnfixed} already optimal, {scenesErrored} errors");
        EditorSceneManager.OpenScene(origPath);
        EditorUtility.ClearProgressBar();
    }

    private static IEnumerable<string> ForEachScene()
    {
        var origPath = SceneManager.GetActiveScene().path;

        string[] guids = AssetDatabase.FindAssets("t:Scene");
        for (int i = 0; i < guids.Length; i++)
        {
            EditorUtility.DisplayProgressBar("Processing Scenes", $"Processed {i} of {guids.Length} scenes...", i * 1f / guids.Length);

            var path = AssetDatabase.GUIDToAssetPath(guids[i]);
            EditorSceneManager.OpenScene(path);
            yield return path;
        }

        EditorSceneManager.OpenScene(origPath);
        EditorUtility.ClearProgressBar();
    }

    [MenuItem("HK8YPlando/Scene/Build")]
    static void BuildSceneSpecificBundle() => BuildSceneSpecificBundle(BuildTarget.StandaloneWindows);

    [MenuItem("HK8YPlando/Scene/Build (Linux)")]
    static void BuildSceneSpecificBundleLinux() => BuildSceneSpecificBundle(BuildTarget.StandaloneLinux64);

    [MenuItem("HK8YPlando/Objects Bundle/Build")]
    static void BuildObjectsBundle() => BuildSpecificBundle("objectsbundle", BuildTarget.StandaloneWindows);

    [MenuItem("HK8YPlando/Objects Bundle/Build (Linux)")]
    static void BuildObjectsBundleLinux() => BuildSpecificBundle("objectsbundle", BuildTarget.StandaloneLinux64);

    [MenuItem("HK8YPlando/All Scenes/Build")]
    static void BuildAllAssetBundles()
    {

        OptimizeAllScenes();
        BuildAllAssetBundles(BuildTarget.StandaloneWindows);
    }

    [MenuItem("HK8YPlando/All Scenes/Build (Linux)")]
    static void BuildAllAssetBundlesLinux()
    {
        OptimizeAllScenes();
        BuildAllAssetBundles(BuildTarget.StandaloneLinux64);
    }

    [MenuItem("HK8YPlando/All Scenes/Build (Release)")]
    static void BuildAllAssetBundlesRelease()
    {
        OptimizeAllScenes();
        BuildAllAssetBundles(BuildTarget.StandaloneWindows);
        BuildAllAssetBundles(BuildTarget.StandaloneLinux64);
    }

    private static string AssetBundlesDir()
    {
        string assetBundleDirectory = "Assets/AssetBundles";
        if (!Directory.Exists(assetBundleDirectory)) Directory.CreateDirectory(assetBundleDirectory);
        return assetBundleDirectory;
    }

    private static void BuildAllAssetBundles(BuildTarget buildTarget)
    {
        BuildPipeline.BuildAssetBundles(AssetBundlesDir(),
                                        BuildAssetBundleOptions.None,
                                        buildTarget);
    }

    private static void BuildSceneSpecificBundle(BuildTarget buildTarget)
    {
        OptimizeCurrentScene();
        var scene = SceneManager.GetActiveScene();
        var bundleName = AssetBundleName(scene.name);

        BuildSpecificBundle(bundleName, buildTarget);
    }

    private static void BuildSpecificBundle(string bundleName, BuildTarget buildTarget)
    {
        var build = new AssetBundleBuild();
        build.assetBundleName = bundleName;
        build.assetNames = AssetDatabase.GetAssetPathsFromAssetBundle(bundleName);

        BuildPipeline.BuildAssetBundles(AssetBundlesDir(), new AssetBundleBuild[] { build }, BuildAssetBundleOptions.None, buildTarget);
    }
}