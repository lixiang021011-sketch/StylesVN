using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Styles.Core;

namespace Styles.EditorTools
{
    /// <summary>工程配置、场景生成与打包。既可在菜单里点，也可用 -executeMethod 在命令行跑。</summary>
    public static class ProjectBootstrap
    {
        const string Company = "LorXer Studio";
        const string Product = "Styles";
        const string BundleId = "com.lorxer.styles";
        const string BootScenePath = "Assets/Scenes/Boot.unity";

        // ---------------------------------------------------------
        [MenuItem("Styles/1. 配置工程（PC + 移动端）", false, 1)]
        public static void ConfigureProject()
        {
            PlayerSettings.companyName = Company;
            PlayerSettings.productName = Product;
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.runInBackground = true;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, BundleId + ".android");
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Standalone, BundleId);
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;

            // PC
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.allowFullscreenSwitch = true;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);

            // Android
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetIl2CppCompilerConfiguration(BuildTargetGroup.Android, Il2CppCompilerConfiguration.Release);
            PlayerSettings.Android.bundleVersionCode = 1;
            PlayerSettings.Android.renderOutsideSafeArea = false;
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.Android.forceSDCardPermission = false;
            PlayerSettings.Android.forceInternetPermission = false;
            try { PlayerSettings.SplashScreen.showUnityLogo = false; } catch { /* Personal 版无法关闭，忽略 */ }
            try { PlayerSettings.SplashScreen.show = false; } catch { }

            // 画质档位
            var names = new[] { "Mobile_Low", "Mobile_Mid", "PC_High", "PC_Ultra" };
            for (int i = 0; i < names.Length && i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.vSyncCount = 1;
                QualitySettings.antiAliasing = i >= 2 ? 4 : 0;
                QualitySettings.shadows = i >= 2 ? ShadowQuality.HardOnly : ShadowQuality.Disable;
                QualitySettings.anisotropicFiltering = i >= 2 ? AnisotropicFiltering.Enable : AnisotropicFiltering.Disable;
            }
            QualitySettings.SetQualityLevel(2, true);
            Application.targetFrameRate = 60;

            AssetDatabase.SaveAssets();
            Debug.Log("[Styles] 工程配置完成：PC + Android（ARM64 / IL2CPP / Vulkan+GLES3）");
        }

        // ---------------------------------------------------------
        [MenuItem("Styles/2. 生成启动场景", false, 2)]
        public static void BuildBootScene()
        {
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("MainCamera", typeof(Camera));
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Styles.UI.UiTheme.Ink;
            cam.orthographic = true;
            camGo.tag = "MainCamera";

            var game = new GameObject("Game");
            game.AddComponent<AudioDirector>();
            game.AddComponent<Styles.UI.GameUI>();
            // 体检组件平时完全不工作（没带 -styles-selfcheck 时自己关掉自己）
            game.AddComponent<Styles.Core.SystemCheck>();

            EditorSceneManager.SaveScene(scene, BootScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(BootScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[Styles] 启动场景已生成：" + BootScenePath);
        }

        // ---------------------------------------------------------
        [MenuItem("Styles/3. 打包 Windows（PC）", false, 20)]
        public static void BuildWindows()
        {
            var ok = Build(BuildTarget.StandaloneWindows64, "Builds/Windows/Styles.exe", BuildTargetGroup.Standalone);
            if (!ok) throw new Exception("[Styles] Windows 打包失败");
        }

        [MenuItem("Styles/4. 打包 Android（APK）", false, 21)]
        public static void BuildAndroid()
        {
            EnsureAndroidToolchain();
            EditorUserBuildSettings.buildAppBundle = false;
            PlayerSettings.Android.useCustomKeystore = false;
            var ok = Build(BuildTarget.Android, "Builds/Android/Styles.apk", BuildTargetGroup.Android);
            if (!ok) throw new Exception("[Styles] Android 打包失败");
        }

        static bool Build(BuildTarget target, string outPath, BuildTargetGroup group)
        {
            ConfigureProject();
            BuildBootScene();
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { BootScenePath },
                target = target,
                locationPathName = outPath,
                options = BuildOptions.None
            };
            var report = BuildPipeline.BuildPlayer(opts);
            var s = report.summary;
            Debug.Log("[Styles] 打包 " + target + " → " + outPath + "  结果=" + s.result + "  大小=" + (s.totalSize / 1024 / 1024) + "MB  用时=" + s.totalTime);
            return s.result == BuildResult.Succeeded;
        }

        static void EnsureAndroidToolchain()
        {
            var editorRoot = Path.GetDirectoryName(EditorApplication.applicationPath);
            var androidRoot = Path.Combine(editorRoot ?? "", "Data/PlaybackEngines/AndroidPlayer");
            var sdk = Path.Combine(androidRoot, "SDK");
            var ndk = Path.Combine(androidRoot, "NDK");
            var jdk = Path.Combine(androidRoot, "OpenJDK");
            if (File.Exists(Path.Combine(jdk, "bin", "java.exe")) || Directory.Exists(jdk))
            {
                if (Directory.Exists(sdk)) EditorPrefs.SetString("AndroidSdkRoot", sdk);
                if (Directory.Exists(ndk)) EditorPrefs.SetString("AndroidNdkRoot", ndk);
                EditorPrefs.SetString("JdkPath", jdk);
                Debug.Log("[Styles] Android 工具链：" + sdk + " | " + ndk + " | " + jdk);
            }
            else
            {
                Debug.LogWarning("[Styles] 未找到内置 OpenJDK，请确认 Unity 已安装 Android Build Support（含 SDK/NDK/OpenJDK）。");
            }
        }

        // ---------------------------------------------------------
        /// <summary>命令行一键流程：配置 → 字体 → 场景 → 校验（→ 可选打包）。</summary>
        public static void RunAll()
        {
            ConfigureProject();
            TmpFontBuilder.BuildCjkFontAsset();
            BuildBootScene();
            var ok = ContentValidator.ValidateAll(true);
            Debug.Log("[Styles] RunAll 完成，内容校验 " + (ok ? "通过" : "有警告"));
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 2);
        }

        /// <summary>
        /// 只做内容与素材校验，**不改动工程文件**（给 check.cmd / check.sh / CI 用）。
        /// 与 RunAll 的区别是不重建启动场景：BuildBootScene() 每次都重新生成一份场景，
        /// Unity 会分配新的 fileID，于是跑一次检查就会把 Assets/Scenes/Boot.unity 弄脏
        /// （几百行无意义 diff）。日常检查用这个，改过场景生成代码后再跑 RunAll。
        /// </summary>
        public static void ValidateOnly()
        {
            var ok = ContentValidator.ValidateAll(true);
            Debug.Log("[Styles] 内容校验完成：" + (ok ? "通过" : "有警告"));
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 2);
        }

        public static void RunAllAndBuildWindows()
        {
            ConfigureProject();
            TmpFontBuilder.BuildCjkFontAsset();
            BuildBootScene();
            ContentValidator.ValidateAll(true);
            var ok = Build(BuildTarget.StandaloneWindows64, "Builds/Windows/Styles.exe", BuildTargetGroup.Standalone);
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 3);
        }
    }
}
