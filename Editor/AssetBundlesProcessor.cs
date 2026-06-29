#if UNITY_EDITOR

using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Build1.UnityAssetBundlesTool.Editor
{
    [InitializeOnLoad]
    internal sealed class AssetBundlesProcessor : IPreprocessBuildWithReport
    {
        public const string LocalBuildTarget              = "Build1_AssetBundlesTool_LocalBuildTarget";
        public const string AutoRebuildKey                = "Build1_AssetBundlesTool_AutoRebuildEnabled";
        public const string CleanCacheAfterPlayEnabledKey = "Build1_AssetBundlesTool_CleanCacheAfterPlayEnabled";

        static AssetBundlesProcessor()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        /*
         * Public.
         */

        public static bool GetAutoRebuildEnabled()
        {
            if (EditorPrefs.HasKey(AutoRebuildKey))
                return EditorPrefs.GetBool(AutoRebuildKey);
            return true;
        }

        public static bool SetAutoRebuildEnabled(bool enabled)
        {
            if (GetAutoRebuildEnabled() == enabled)
                return false;

            EditorPrefs.SetBool(AutoRebuildKey, enabled);

            Debug.Log(enabled
                          ? "AssetBundles: Auto Rebuild enabled."
                          : "AssetBundles: Auto Rebuild disabled.");

            return true;
        }

        public static bool GetCleanCacheAfterPlayEnabled()
        {
            if (EditorPrefs.HasKey(CleanCacheAfterPlayEnabledKey))
                return EditorPrefs.GetBool(CleanCacheAfterPlayEnabledKey);
            return false;
        }

        public static bool SetCleanCacheAfterPlayEnabled(bool enabled)
        {
            if (GetCleanCacheAfterPlayEnabled() == enabled)
                return false;

            EditorPrefs.SetBool(CleanCacheAfterPlayEnabledKey, enabled);

            Debug.Log(enabled
                          ? "AssetBundles: Clean cache after Play enabled."
                          : "AssetBundles: Clean cache after Play disabled.");

            return true;
        }

        public static AssetBundleBuildTarget GetLocalBuildTarget()
        {
            if (!EditorPrefs.HasKey(LocalBuildTarget))
                return AssetBundleBuildTarget.Current;
            
            var str = EditorPrefs.GetString(LocalBuildTarget);
            if (str == "CurrentBuildTarget")
            {
                EditorPrefs.SetString(LocalBuildTarget, AssetBundleBuildTarget.Current.ToString());
                return AssetBundleBuildTarget.Current;
            }
            
            return (AssetBundleBuildTarget)Enum.Parse(typeof(AssetBundleBuildTarget), str, true);
        }

        public static BuildTarget GetLocalBuildTargetTyped()
        {
            var buildTarget = GetLocalBuildTarget();
            if (buildTarget == AssetBundleBuildTarget.Current)
                return EditorUserBuildSettings.activeBuildTarget;
            return (BuildTarget)buildTarget;
        }

        public static void SetLocalBuildTarget(AssetBundleBuildTarget buildTarget)
        {
            if (GetLocalBuildTarget() == buildTarget)
                return;

            EditorPrefs.SetString(LocalBuildTarget, buildTarget.ToString());

            if (buildTarget == AssetBundleBuildTarget.Current)
                Debug.Log("AssetBundles: Local build target set to Current Build Target.");
            else
                Debug.Log($"AssetBundles: Local build target set to {buildTarget}.");
        }

        /*
         * Build.
         */

        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            var buildAssetBundles = AssetBundlesBuilder.CheckAssetBundlesExist(true);
            if (!buildAssetBundles)
                return;

            var buildTarget = report.summary.platform;
            if (!AssetBundlesBuilder.Build(buildTarget, AssetBundlesBuilder.DefaultBuildOptions, false))
                throw new BuildFailedException($"Asset bundle build failed for {buildTarget}. See the Console for the asset-bundle error.");

            Debug.Log($"AssetBundles: Bundles were built for {buildTarget} before Building project");
        }

        /*
         * Private.
         */

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.ExitingEditMode:
                {
                    if (GetAutoRebuildEnabled() && AssetBundlesBuilder.CheckAssetBundlesExist(true))
                        AssetBundlesBuilder.Build(GetLocalBuildTargetTyped(), AssetBundlesBuilder.DefaultBuildOptions, false);
                    break;
                }
                case PlayModeStateChange.EnteredPlayMode:
                {
                    break;
                }
                case PlayModeStateChange.ExitingPlayMode:
                {
                    if (GetCleanCacheAfterPlayEnabled() && AssetBundlesBuilder.CheckAssetBundlesExist(true))
                    {
                        AssetBundle.UnloadAllAssetBundles(false);
                        Caching.ClearCache();
                        Debug.Log("AssetBundles: Cache cleaned");
                    }
                    break;
                }
            }
        }
    }
}

#endif
