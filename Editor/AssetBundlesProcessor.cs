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

        private static string                 _webGLVariantOutputPath;
        private static WebGLTextureSubtarget? _webGLVariantSubtarget;

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

        public static IDisposable UseWebGLVariantForPlayerBuild(WebGLTextureSubtarget textureSubtarget,
                                                                string outputPath)
        {
            if (_webGLVariantOutputPath != null)
                throw new InvalidOperationException("A WebGL asset-bundle verification scope is already active.");

            _webGLVariantOutputPath = AssetBundlesBuilder.ValidateWebGLVariantArguments(textureSubtarget, outputPath);
            _webGLVariantSubtarget = textureSubtarget;
            return new WebGLVariantVerificationScope(_webGLVariantOutputPath, textureSubtarget);
        }

        /*
         * Build.
         */

        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            // Unity 6 forbids BuildPipeline.BuildAssetBundles while a player build is in progress,
            // so bundles must be built before the player build, not from inside this callback.
            // Verify the bundles are already published for the target and let the player build
            // package them. Build/refresh them via Tools/Build1/Asset Bundles/Rebuild beforehand.
            ValidatePublishedBundlesForBuild(
                report.summary.platform,
                AssetBundlesBuilder.CheckAssetBundlesExist(true));
        }

        internal void ValidatePublishedBundlesForBuild(BuildTarget buildTarget, bool assetBundlesExist)
        {
            if (!assetBundlesExist)
                return;

            if (_webGLVariantOutputPath != null)
            {
                if (buildTarget != BuildTarget.WebGL || !_webGLVariantSubtarget.HasValue)
                {
                    throw new BuildFailedException(
                        $"The scoped WebGL bundle output cannot be used for a {buildTarget} Player build.");
                }

                if (AssetBundlesBuilder.CheckWebGLVariantPublished(_webGLVariantSubtarget.Value,
                                                                   _webGLVariantOutputPath))
                {
                    Debug.Log(
                        $"AssetBundles: Verified published WebGL {_webGLVariantSubtarget.Value} bundles at " +
                        $"{_webGLVariantOutputPath} for the Player build.");
                    return;
                }

                throw new BuildFailedException(
                    $"The scoped WebGL {_webGLVariantSubtarget.Value} asset bundles are not completely published at " +
                    $"{_webGLVariantOutputPath}.");
            }

            if (AssetBundlesBuilder.CheckAssetBundlesPublished(buildTarget))
            {
                Debug.Log($"AssetBundles: Verified published bundles for {buildTarget}; packaging existing output.");
                return;
            }

            throw new BuildFailedException(
                $"Asset bundles are not built for {buildTarget}. " +
                "Build them first via Tools/Build1/Asset Bundles/Rebuild (or the Asset Bundles tool window), then build the player. " +
                "Unity 6 cannot build asset bundles during a player build.");
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

        private sealed class WebGLVariantVerificationScope : IDisposable
        {
            private readonly string                _outputPath;
            private readonly WebGLTextureSubtarget _textureSubtarget;
            private bool                           _disposed;

            public WebGLVariantVerificationScope(string outputPath, WebGLTextureSubtarget textureSubtarget)
            {
                _outputPath = outputPath;
                _textureSubtarget = textureSubtarget;
            }

            public void Dispose()
            {
                if (_disposed)
                    return;

                if (_webGLVariantOutputPath != _outputPath || _webGLVariantSubtarget != _textureSubtarget)
                    throw new InvalidOperationException("The active WebGL asset-bundle verification scope changed unexpectedly.");

                _webGLVariantOutputPath = null;
                _webGLVariantSubtarget = null;
                _disposed = true;
            }
        }
    }
}

#endif
