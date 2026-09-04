#if UNITY_EDITOR

using System;
using UnityEditor;
using UnityEngine;
using TMPro;

namespace Build1.UnityAssetBundlesTool.Editor
{
    // Restores one-click "the player build also compiles asset bundles" on Unity 6.
    // Unity 6 forbids BuildPipeline.BuildAssetBundles while a player build is in progress,
    // so bundles cannot be built from a build callback (that runs inside the player build).
    // This handler owns the Build / Build And Run invocation and runs the two builds
    // sequentially, before any player-build lock is taken: bundles first, then the player.
    [InitializeOnLoad]
    internal static class AssetBundlesBuildPlayerHandler
    {
        private static Func<BuildPlayerOptions, bool> _buildPlayerOverride;
        private static bool                           _invokingBuildPlayerOverride;

        static AssetBundlesBuildPlayerHandler()
        {
            BuildPlayerWindow.RegisterBuildPlayerHandler(BuildWithAssetBundles);
        }

        public static void RegisterBuildPlayerOverride(Func<BuildPlayerOptions, bool> buildPlayerOverride)
        {
            if (buildPlayerOverride == null)
                throw new ArgumentNullException(nameof(buildPlayerOverride));

            if (_buildPlayerOverride != null && _buildPlayerOverride != buildPlayerOverride)
                throw new InvalidOperationException("A Build Player override is already registered.");

            _buildPlayerOverride = buildPlayerOverride;
        }

        public static void UnregisterBuildPlayerOverride(Func<BuildPlayerOptions, bool> buildPlayerOverride)
        {
            if (buildPlayerOverride == null)
                throw new ArgumentNullException(nameof(buildPlayerOverride));

            if (_buildPlayerOverride != buildPlayerOverride)
                throw new InvalidOperationException("The supplied Build Player override is not registered.");

            _buildPlayerOverride = null;
        }

        private static void BuildWithAssetBundles(BuildPlayerOptions options)
        {
            if (_invokingBuildPlayerOverride)
            {
                Debug.LogError("AssetBundles: recursive Build Player override invocation was blocked.");
                return;
            }

            if (_buildPlayerOverride != null)
            {
                _invokingBuildPlayerOverride = true;
                try
                {
                    if (_buildPlayerOverride.Invoke(options))
                        return;
                }
                finally
                {
                    _invokingBuildPlayerOverride = false;
                }
            }

            // Use TextMesh Pro's version-matched prebuild processor so bundle inputs are
            // canonicalized by the same qualifying-font policy as the subsequent Player build.
            new TMP_PreBuildProcessor().OnPreprocessBuild(null);

            // AssetBundlesBuilder.Build returns true when there are no bundles to build or the
            // build succeeds, and false only on an actual bundle build failure. On failure we
            // abort loudly and do not build the player. No fallback path.
            if (!AssetBundlesBuilder.Build(options.target, AssetBundlesBuilder.DefaultBuildOptions, false))
            {
                Debug.LogError($"AssetBundles: bundle build failed for {options.target}. Player build aborted.");
                return;
            }

            BuildPipeline.BuildPlayer(options);
        }
    }
}

#endif
