#if UNITY_EDITOR

using UnityEditor;
using UnityEngine;

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
        static AssetBundlesBuildPlayerHandler()
        {
            BuildPlayerWindow.RegisterBuildPlayerHandler(BuildWithAssetBundles);
        }

        private static void BuildWithAssetBundles(BuildPlayerOptions options)
        {
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
