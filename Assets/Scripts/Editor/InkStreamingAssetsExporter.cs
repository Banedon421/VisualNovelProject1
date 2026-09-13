#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Keeps a build-safe copy of every compiled ink story in
// Assets/StreamingAssets/Ink/, so the game can load it at runtime with a
// plain File.ReadAllText call -- no AssetDatabase, no Editor-only APIs,
// works identically in the Editor and in an exported build.
//
// You should never need to touch this file or the exported .json directly
// -- just keep editing main.ink and saving. This runs automatically
// whenever a .ink file is (re)imported and keeps StreamingAssets/Ink/
// in sync behind the scenes.
//
// Place this file anywhere under an "Editor" folder (e.g.
// Assets/Scripts/Editor/) -- Unity excludes anything in a folder named
// "Editor" from builds automatically. The #if UNITY_EDITOR wrapper is a
// second safety net in case it ever gets moved out of one.
public class InkStreamingAssetsExporter : AssetPostprocessor
{
    private const string OutputFolder = "Assets/StreamingAssets/Ink";

    // Runs after ink's own postprocessor (which does the actual
    // compiling), so the compiled JSON is already up to date by the time
    // we read it.
    public override int GetPostprocessOrder() => 1000;

    private static void OnPostprocessAllAssets(
        string[] importedAssets, string[] deletedAssets,
        string[] movedAssets, string[] movedFromAssetPaths)
    {
        foreach (var path in importedAssets)
        {
            if (Path.GetExtension(path) != ".ink") continue;
            ExportOne(path);
        }
    }

    // Manual safety net: run this once right after adding this script (it
    // won't retroactively fire for files that were already imported
    // before this script existed), or any time you want to force a
    // re-export.
    [MenuItem("Tools/Ink/Export All Ink Files to StreamingAssets")]
    private static void ExportAllManually()
    {
        foreach (var guid in AssetDatabase.FindAssets(string.Empty))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetExtension(path) == ".ink") ExportOne(path);
        }
        AssetDatabase.Refresh();
        Debug.Log("Ink export: finished exporting all .ink files.");
    }

    private static void ExportOne(string inkAssetPath)
    {
        var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(inkAssetPath);
        if (asset == null) return;

        string json = FindCompiledJsonViaReflection(asset);
        if (string.IsNullOrEmpty(json))
        {
            // Likely an INCLUDE-d fragment rather than a root story file --
            // those don't have their own standalone compiled JSON. Not an error.
            return;
        }

        if (!Directory.Exists(OutputFolder)) Directory.CreateDirectory(OutputFolder);

        string fileName = Path.GetFileNameWithoutExtension(inkAssetPath) + ".json";
        string outputPath = Path.Combine(OutputFolder, fileName);

        File.WriteAllText(outputPath, json);
        AssetDatabase.ImportAsset(outputPath);

        Debug.Log($"Ink export: wrote {outputPath}");
    }

    private static string FindCompiledJsonViaReflection(UnityEngine.Object asset)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var type = asset.GetType();

        foreach (var field in type.GetFields(flags))
        {
            if (field.FieldType != typeof(string)) continue;
            if (field.GetValue(asset) is string s && s.Contains("\"inkVersion\""))
                return s;
        }

        foreach (var prop in type.GetProperties(flags))
        {
            if (prop.PropertyType != typeof(string) || !prop.CanRead) continue;
            try
            {
                if (prop.GetValue(asset) is string s && s.Contains("\"inkVersion\""))
                    return s;
            }
            catch { /* a few properties throw when accessed this way; safe to ignore */ }
        }

        return null;
    }
}
#endif
