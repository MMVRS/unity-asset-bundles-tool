#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Build1.UnityAssetBundlesTool.Editor.WebGL;
using UnityEditor;
using UnityEngine;

namespace Build1.UnityAssetBundlesTool.Editor
{
    internal static class AssetBundlesBuilder
    {
        public const BuildAssetBundleOptions DefaultBuildOptions =
            BuildAssetBundleOptions.StrictMode |
            BuildAssetBundleOptions.ChunkBasedCompression |
            BuildAssetBundleOptions.AssetBundleStripUnityVersion;

        public const BuildAssetBundleOptions DefaultRebuildOptions =
            DefaultBuildOptions |
            BuildAssetBundleOptions.ForceRebuildAssetBundle;

        /*
         * Check.
         */

        public static bool CheckAssetBundlesBuilt()
        {
            if (!Directory.Exists(Application.streamingAssetsPath) && CheckAssetBundlesExist(false))
                return false;

            var bundlesNames = AssetDatabase.GetAllAssetBundleNames();
            foreach (var bundlesName in bundlesNames)
            {
                if (CheckAssetBundle(bundlesName))
                    return true;
            }
            
            return false;
        }

        public static bool CheckAssetBundlesExist(bool removeUnusedAssetBundles)
        {
            if (removeUnusedAssetBundles)
                AssetDatabase.RemoveUnusedAssetBundleNames();
            return AssetDatabase.GetAllAssetBundleNames().Length != 0;
        }
        
        /*
         * Build.
         */
        
        public static bool Build(BuildTarget target, BuildAssetBundleOptions options, bool async = true, Action onComplete = null)
        {
            var start = DateTime.UtcNow;
            
            Log($"Building for {target} with {options}...");
            
            if (!CheckAssetBundlesExist(false))
            {
                Log("No bundles defined.");
                return true;
            }

            if (!async)
            {
                var succeeded = BuildImpl(target, options);
                Log(succeeded
                        ? $"Done in {DateTime.UtcNow - start:mm\\:ss}"
                        : $"Failed in {DateTime.UtcNow - start:mm\\:ss}");
                onComplete?.Invoke();
                return succeeded;
            }
            
            EditorApplication.delayCall += () =>
            {
                var succeeded = BuildImpl(target, options);
                Log(succeeded
                        ? $"Done in {DateTime.UtcNow - start:mm\\:ss}"
                        : $"Failed in {DateTime.UtcNow - start:mm\\:ss}");
                onComplete?.Invoke();
            };

            return true;
        }

        private static bool BuildImpl(BuildTarget target, BuildAssetBundleOptions options)
        {
            if (!Directory.Exists(Application.streamingAssetsPath))
                Directory.CreateDirectory(Application.streamingAssetsPath);

            var output = BuildPipeline.BuildAssetBundles(Application.streamingAssetsPath, options, target);
            if (output != null)
            {
                if (target == BuildTarget.WebGL)
                    CreateWebGLOutputPublisher().PublishSuccessfulBuild(output, options);
                return true;
            }

            if (target == BuildTarget.WebGL)
                CreateWebGLOutputPublisher().CleanFailedBuildArtifacts();
            
            Log("Asset bundle build failed. Existing bundle output was preserved.");
            return false;
        }
        
        /*
         * Clear.
         */

        public static void Clean(bool async = true, Action onComplete = null)
        {
            if (!Directory.Exists(Application.streamingAssetsPath))
            {
                Log("Bundles folder not found. There is nothing to clear.");
                onComplete?.Invoke();
                return;
            }
            
            Log("Cleaning...");

            if (!async)
            {
                ClearImpl();
                Log("Cleaned.");
                onComplete?.Invoke();
                return;
            }
            
            EditorApplication.delayCall += () =>
            {
                ClearImpl();
                Log("Cleaned.");
                onComplete?.Invoke();
            };
        }
        
        private static void ClearImpl()
        {
            var names = AssetDatabase.GetAllAssetBundleNames().ToList();
            names.Add("StreamingAssets");
            
            foreach (var name in names)
                ClearAssetBundle(name);

            CreateWebGLOutputPublisher().CleanExplicitOutput(names);
        }

        private static void ClearAssetBundle(string bundleName)
        {
            var paths = GetBundleFilesPaths(bundleName);
            foreach (var path in paths)
                File.Delete(path);
        }
        
        /*
         * Check.
         */
        
        public static bool CheckAssetBundles()
        {
            if (!Directory.Exists(Application.streamingAssetsPath))
                return false;
            
            var names = AssetDatabase.GetAllAssetBundleNames().ToList();
            names.Add("StreamingAssets");
            return names.All(CheckAssetBundle);
        }

        private static bool CheckAssetBundle(string bundleName)
        {
            return GetBundleFilesPaths(bundleName).All(File.Exists);
        }
        
        /*
         * Private.
         */

        private static WebGLAssetBundleOutputPublisher CreateWebGLOutputPublisher()
        {
            return new WebGLAssetBundleOutputPublisher(Application.streamingAssetsPath);
        }

        private static IEnumerable<string> GetBundleFilesPaths(string bundleName)
        {
            return new[]
            {
                Path.Combine(Application.streamingAssetsPath, bundleName),
                Path.Combine(Application.streamingAssetsPath, bundleName) + ".manifest",
                Path.Combine(Application.streamingAssetsPath, bundleName) + ".manifest.meta",
                Path.Combine(Application.streamingAssetsPath, bundleName) + ".meta"
            };
        }

        /*
         * Logging.
         */

        private static void Log(string message)
        {
            Debug.Log($"AssetBundles: {message}");
        }
    }
}

#endif
