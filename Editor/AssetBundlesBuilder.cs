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

        // Verifies that bundle output already exists on disk for the given target, without rebuilding.
        // WebGL output is hashed + described by the JSON manifest; other targets keep raw named files.
        public static bool CheckAssetBundlesPublished(BuildTarget target)
        {
            if (target == BuildTarget.WebGL)
                return CreateWebGLOutputPublisher().IsOutputPublished();
            return CheckAssetBundles();
        }

        public static bool CheckWebGLVariantPublished(WebGLTextureSubtarget textureSubtarget, string outputPath)
        {
            var normalizedOutputPath = ValidateWebGLVariantArguments(textureSubtarget, outputPath);
            return new WebGLAssetBundleOutputPublisher(normalizedOutputPath).IsOutputPublished(textureSubtarget);
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

        public static bool BuildWebGLVariant(WebGLTextureSubtarget textureSubtarget,
                                             string outputPath,
                                             BuildAssetBundleOptions options)
        {
            return BuildWebGLVariant(
                textureSubtarget,
                outputPath,
                options,
                () => CheckAssetBundlesExist(false),
                BuildPipeline.BuildAssetBundles);
        }

        internal static bool BuildWebGLVariant(WebGLTextureSubtarget textureSubtarget,
                                               string outputPath,
                                               BuildAssetBundleOptions options,
                                               Func<bool> checkAssetBundlesExist,
                                               Func<BuildAssetBundlesParameters, AssetBundleManifest> buildAssetBundles)
        {
            var normalizedOutputPath = ValidateWebGLVariantArguments(textureSubtarget, outputPath);
            var start = DateTime.UtcNow;

            Log($"Building WebGL {textureSubtarget} bundles into {normalizedOutputPath} with {options}...");

            if (!checkAssetBundlesExist())
            {
                Log("No bundles defined.");
                return true;
            }

            if (!Directory.Exists(normalizedOutputPath))
                Directory.CreateDirectory(normalizedOutputPath);

            var publisher = new WebGLAssetBundleOutputPublisher(normalizedOutputPath);
            var parameters = new BuildAssetBundlesParameters
            {
                outputPath = normalizedOutputPath,
                targetPlatform = BuildTarget.WebGL,
                subtarget = (int)textureSubtarget,
                options = options
            };
            var output = buildAssetBundles(parameters);

            if (output != null)
            {
                publisher.PublishSuccessfulBuild(output, options, textureSubtarget);
                Log($"Done in {DateTime.UtcNow - start:mm\\:ss}");
                return true;
            }

            publisher.CleanFailedBuildArtifacts();
            Log($"Failed in {DateTime.UtcNow - start:mm\\:ss}. Existing WebGL {textureSubtarget} output was preserved.");
            return false;
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

        internal static string ValidateWebGLVariantArguments(WebGLTextureSubtarget textureSubtarget, string outputPath)
        {
            if (textureSubtarget != WebGLTextureSubtarget.DXT && textureSubtarget != WebGLTextureSubtarget.ASTC)
                throw new ArgumentOutOfRangeException(nameof(textureSubtarget), textureSubtarget,
                                                      "Only the named DXT and ASTC WebGL variants are supported.");

            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("A WebGL variant output path is required.", nameof(outputPath));

            var normalizedOutputPath = Path.GetFullPath(outputPath);
            var pathRoot = Path.GetPathRoot(normalizedOutputPath);
            if (string.Equals(normalizedOutputPath, pathRoot, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("A WebGL variant cannot publish to a filesystem root.", nameof(outputPath));

            var streamingAssetsPath = Path.GetFullPath(Application.streamingAssetsPath);
            if (IsSameOrChildPath(normalizedOutputPath, streamingAssetsPath))
                throw new ArgumentException(
                    "The isolated WebGL variant route cannot publish into the active StreamingAssets path.",
                    nameof(outputPath));

            return normalizedOutputPath;
        }

        private static bool IsSameOrChildPath(string path, string parentPath)
        {
            if (string.Equals(path, parentPath, StringComparison.OrdinalIgnoreCase))
                return true;

            var parentPrefix = parentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                               Path.DirectorySeparatorChar;
            return path.StartsWith(parentPrefix, StringComparison.OrdinalIgnoreCase);
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
