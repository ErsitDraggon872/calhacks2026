using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Global namespace so the CLI can call it as BuildScript.PerformBuild.
public static class BuildScript
{
    public const string LevelScene = "Assets/Scenes/Level.unity";
    public const string OutputPath = "Build/Windows/CalHacks2026.exe";

    /// <summary>Builds Level.unity for Windows. Used by: unity build . --target StandaloneWindows64 --execute-method BuildScript.PerformBuild</summary>
    [MenuItem("Game/Build Windows Player")]
    public static void PerformBuild()
    {
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { LevelScene },
            locationPathName = OutputPath,
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.None,
        });
        var summary = report.summary;
        Debug.Log($"[Build] {summary.result}: {summary.outputPath} ({summary.totalSize / (1024 * 1024)} MB, {summary.totalErrors} errors, {summary.totalTime})");
        if (Application.isBatchMode && summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
    }
}
