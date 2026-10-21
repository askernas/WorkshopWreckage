using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace WorkshopWreckage.EditorTools
{
    /// <summary>Windows build for the engine comparison. Shared data is copied into
    /// StreamingAssets/ww only for the duration of the build (no duplicate in git).
    /// CLI: Unity.exe -batchmode -quit -projectPath . -executeMethod WorkshopWreckage.EditorTools.WWBuild.BuildWindows</summary>
    public static class WWBuild
    {
        public const string Output = "Builds/Windows/WorkshopWreckage.exe";

        [MenuItem("Workshop Wreckage/Build Windows Player")]
        public static void BuildWindows()
        {
            PlayerSettings.enableFrameTimingStats = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.companyName = "Workshop Wreckage";
            PlayerSettings.productName = "Workshop Wreckage";
            var sw = Stopwatch.StartNew();
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/SampleScene.unity" },
                locationPathName = Output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            var s = report.summary;
            var line = $"[build] {s.result} in {sw.Elapsed.TotalSeconds:0.0} s, size {s.totalSize / 1048576.0:0.0} MB, errors {s.totalErrors}, warnings {s.totalWarnings}";
            UnityEngine.Debug.Log(line);
            File.WriteAllText("Builds/last_build.txt", line + "\n");
            if (Application.isBatchMode) EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
        }
    }

    public class CopySharedData : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;
        static string Dest => Path.Combine(Application.dataPath, "StreamingAssets", "ww");
        static string Src => Path.GetFullPath(Path.Combine(Application.dataPath, "../../shared"));

        public void OnPreprocessBuild(BuildReport report)
        {
            foreach (var sub in new[] { "data", "generated", "meshes" })
            {
                var from = Path.Combine(Src, sub);
                var to = Path.Combine(Dest, sub);
                Directory.CreateDirectory(to);
                foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
            }
            AssetDatabase.Refresh();
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            var root = Path.Combine(Application.dataPath, "StreamingAssets");
            if (Directory.Exists(root)) Directory.Delete(root, true);
            if (File.Exists(root + ".meta")) File.Delete(root + ".meta");
            AssetDatabase.Refresh();
        }
    }
}
