using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;

namespace BonGames.EasyBuilder
{
    public abstract partial class ProjectBuilder : IProjectBuilder 
    {
        public virtual IPreProcessBuildWithReportPreset PreProcessBuildWithReport { get; protected set; }

        public virtual IPostProcessBuildWithReportPreset PostProcessingWithReport { get; protected set; }

        public virtual IPreBuildProcess PreBuildProcess { get; protected set; }

        public virtual IPostBuildProcess PostBuildProcess { get; protected set; }

        public EAppTarget AppTarget { get; private set; }

        public BuildTarget BuildTarget { get; private set; }

        public EEnvironment Environment { get; private set; }

        protected string BuildInformationDirectory => BuilderUtils.BuildInformationDirectory();

        protected virtual List<string> PlatformSpecifiedSymbols { get; private set; } = new List<string>()
        {
            BuildDefines.EnableIL2CppScriptBackend,
        };

        protected BuildPlayerOptions BuildPlayerOptions { get; set; }

        protected BuildVersion Version { get; } = new BuildVersion();

        public ProjectBuilder(EAppTarget appTarget, BuildTarget buildTarget, EEnvironment environment)
        {
            AppTarget = appTarget;
            BuildTarget = buildTarget;
            Environment = environment;
        }
        

        public void Build()
        {
            Prepare();
            BonGames.Tools.EnvironmentArguments.Load();
            // Switch to build target
            BuildTargetGroup buildGroup = BuilderUtils.GetBuildTargetGroup(BuildTarget);
            if (buildGroup != BuilderUtils.GetActiveBuildTargetGroup() ||  BuildTarget !=  BuilderUtils.GetActiveBuildTarget())
            {
                EditorUserBuildSettings.SwitchActiveBuildTarget(buildGroup, BuildTarget);
            }

            // Create build options
            Version.LoadVersion();
            BuildPlayerOptions = CreateBuildPlayerOptions();
            // Pre Build
            if (PreBuildProcess != null)
            {
                PreBuildProcess.OnPreBuild(this);
            }

            // Setup general product info
            SetProductInformation();

            // Behide the scene informations
            SetupInternally();

            // Clean up current symbols, use buildPlayerOptions.extraScriptingDefines for testing or does not really create an impact            
            BuilderUtils.SetScriptingDefineSymbolsToActiveBuildTarget(BuildPlayerOptions.extraScriptingDefines);

            // Build
            UnityEditor.Build.Reporting.BuildReport report = BuildPipeline.BuildPlayer(BuildPlayerOptions);

            // Post Build
            if (PostBuildProcess != null)
            {
                PostBuildProcess.OnPostBuild(this);
            }

            if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                EditorUtility.RevealInFinder(BuildPlayerOptions.locationPathName);
            }
            else
            {
                UnityEngine.Debug.LogError($"Build failed with result {report.summary.result}");
            }
        }

        protected virtual void Prepare()
        {
            PreProcessBuildWithReport = new DefaultPreProcessBuildWithReportPreset();
            PreProcessBuildWithReport.Tasks.Add(new IL2CppPreprocessBuild());

            PostProcessingWithReport = new DefaultPostProcessBuildWithReportPreset();
        }
        protected void SetProductInformation()
        {
            SetProductName();
            UpdateAppVersion();
            SignApp();
        }

        protected virtual void SetupInternally()
        {
            
        }

        protected virtual void UpdateAppVersion()
        {
            int buildNumber = BuildArguments.GetBuildNumber(-1);
            buildNumber = buildNumber > 0 ? buildNumber : Version.Build;

            // Set all in once here, reduce complexity, if you want to set by your own logic, lets override this method
            PlayerSettings.bundleVersion = Version.BundleVersion;
            PlayerSettings.Android.bundleVersionCode = buildNumber;
            PlayerSettings.iOS.buildNumber = $"{buildNumber}";
            PlayerSettings.WSA.packageVersion = new System.Version(Version.Major, Version.Minor, buildNumber, Version.Revision);
        }

        protected virtual void SetProductName() 
        {
            string product = BuildArguments.GetProductName();
            if (!string.IsNullOrEmpty(product))
            {
                switch (Environment)
                {
                    case EEnvironment.Debug:                        
                    case EEnvironment.Development:
                        product = $"{product} (Dev)";
                        break;
                    case EEnvironment.Staging:
                        product = $"{product} (Stag)";
                        break;
                    case EEnvironment.Release:                        
                    case EEnvironment.Distribution:
                        break;
                }
                PlayerSettings.productName = product;
            }
        }

        protected virtual void SignApp() { }

        protected virtual BuildPlayerOptions CreateBuildPlayerOptions()
        {
            string buildLocation = BuilderUtils.GetPlatformBuildFolder(BuildTarget, AppTarget);
            BuildPlayerOptions buildPlayerOptions = GetDefaultBuildPlayerOptions();
            buildPlayerOptions.locationPathName = Path.Combine(buildLocation, BuilderUtils.GetDefaultProductName() + BuilderUtils.GetBuildTargetAppExtension(BuildTarget));
            buildPlayerOptions.targetGroup = BuilderUtils.GetBuildTargetGroup(BuildTarget);
            buildPlayerOptions.subtarget = BuilderUtils.GetSubBuildTarget(AppTarget, BuildTarget);
            buildPlayerOptions.scenes = GetActiveScenes();
            buildPlayerOptions.target = BuildTarget;
            OnBuildPlayerOptionCreate(ref buildPlayerOptions);
            return buildPlayerOptions;
        }

        protected virtual void OnBuildPlayerOptionCreate(ref BuildPlayerOptions ops)
        {

        }

        public BuildPlayerOptions GetDefaultBuildPlayerOptions()
        {
            BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions();
            NamedBuildTarget namedBuildTarget = BuilderUtils.GetNamedBuildTarget(AppTarget, BuildTarget);
            PlayerSettings.GetScriptingDefineSymbols(namedBuildTarget, out string[] currentSymbols);

            BuildOptions options = BuildOptions.None;
            List<string> defines = null;
            switch (Environment)
            {
                case EEnvironment.Debug:
                case EEnvironment.Development:
                    {
                        options = BuildOptions.Development;
                        defines = new List<string>()
                        {
                            BuildDefines.EnableLog,
                            BuildDefines.DevelopmentBuild,
                            BuildDefines.InternalBuild,
                        };
                    }
                    break;
                case EEnvironment.Staging:
                    {
                        defines = new List<string>()
                        {
                            BuildDefines.EnableLog,
                            BuildDefines.StagingBuild,
                            BuildDefines.InternalBuild,
                        };
                    }
                    break;
                case EEnvironment.Release:
                    {
                        defines = new List<string>()
                        {
                            BuildDefines.ReleaseBuild,
                        };
                    }
                    break;
            }

            if (currentSymbols != null && currentSymbols.Length > 0)
            {
                List<string> allInternalSymbols = BuilderUtils.GetAllScriptSymbols();

                for (int i = 0; i < currentSymbols.Length; i++)
                {
                    string symbol = currentSymbols[i];

                    if (allInternalSymbols.Contains(symbol))
                        continue; // Ingnore Internal Symbols

                    // Keep the symbols from external
                    defines.Add(symbol);
                }
            }

            if (PlatformSpecifiedSymbols != null)
            {
                defines.AddRange(PlatformSpecifiedSymbols);
            }

            buildPlayerOptions.options = options;
            buildPlayerOptions.extraScriptingDefines = defines.ToArray();
            return buildPlayerOptions;
        }

        protected virtual string[] GetActiveScenes()
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            List<string> activeScenes = new List<string>();
            for (int i = 0; i < scenes.Length; i++)
            {
                EditorBuildSettingsScene sceneSettings = scenes[i];
                if (sceneSettings.enabled)
                {
                    activeScenes.Add(sceneSettings.path);
                }
            }
            return activeScenes.ToArray();
        }
    }

    internal class DefaultPreProcessBuildWithReportPreset : IPreProcessBuildWithReportPreset
    {
        public List<IPreProcessBuildWithReportTask> Tasks { get; private set; } = new();
    }
    
    internal class DefaultPostProcessBuildWithReportPreset : IPostProcessBuildWithReportPreset
    {
        public List<IPostProcessBuildWithReportTask> Tasks { get; private set; } = new();
    }
}
