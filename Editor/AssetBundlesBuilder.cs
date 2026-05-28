#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Model.AssetBundles;
using UnityEditor;
using UnityEngine;

namespace Build1.UnityAssetBundlesTool.Editor
{
    internal static class AssetBundlesBuilder
    {
        private const int    ManifestSchemaVersion = 1;
        private const string ManifestFileName      = "asset-bundles.json";
        private const string HashedBundleExtension = ".bundle";
        
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
        
        public static void Build(BuildTarget target, BuildAssetBundleOptions options, bool async = true, Action onComplete = null)
        {
            var start = DateTime.UtcNow;
            
            Log($"Building for {target} with {options}...");
            
            if (!CheckAssetBundlesExist(false))
            {
                Log("No bundles defined.");
                return;
            }

            if (!async)
            {
                BuildImpl(target, options);
                Log($"Done in {DateTime.UtcNow - start:mm\\:ss}");
                onComplete?.Invoke();
                return;
            }
            
            EditorApplication.delayCall += () =>
            {
                BuildImpl(target, options);
                Log($"Done in {DateTime.UtcNow - start:mm\\:ss}");
                onComplete?.Invoke();
            };
        }

        private static void BuildImpl(BuildTarget target, BuildAssetBundleOptions options)
        {
            if (!Directory.Exists(Application.streamingAssetsPath))
                Directory.CreateDirectory(Application.streamingAssetsPath);

            if (target == BuildTarget.WebGL)
                ClearWebGLHashedOutput(CollectWebGLCleanupBundleNames());

            var output = BuildPipeline.BuildAssetBundles(Application.streamingAssetsPath, options, target);
            if (output != null)
            {
                if (target == BuildTarget.WebGL)
                    WriteWebGLHashedOutput(output, options);
                return;
            }
            
            Log("No asset bundles to build. Cleaning existing bundles...");
            ClearImpl();
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

            ClearWebGLHashedOutput(names);
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
         * WebGL hashed output.
         */

        private static List<string> CollectWebGLCleanupBundleNames()
        {
            var names = AssetDatabase.GetAllAssetBundleNames().ToList();
            names.Add(Path.GetFileName(Application.streamingAssetsPath));

            var previousManifestPath = Path.Combine(Application.streamingAssetsPath, ManifestFileName);
            if (!File.Exists(previousManifestPath))
                return names;

            AssetBundlesManifestDto previousManifest = null;
            try
            {
                previousManifest = JsonUtility.FromJson<AssetBundlesManifestDto>(File.ReadAllText(previousManifestPath));
            }
            catch (Exception exception)
            {
                Log($"Ignoring previous malformed {ManifestFileName}: {exception.Message}");
            }
            
            if (previousManifest?.bundles == null)
                return names;

            for (var i = 0; i < previousManifest.bundles.Length; i++)
            {
                var bundle = previousManifest.bundles[i];
                if (bundle != null && !string.IsNullOrWhiteSpace(bundle.id) && !names.Contains(bundle.id))
                    names.Add(bundle.id);
            }

            return names;
        }

        private static void ClearWebGLHashedOutput(List<string> bundleNames)
        {
            if (!Directory.Exists(Application.streamingAssetsPath))
                return;

            DeleteFileAndMeta(Path.Combine(Application.streamingAssetsPath, ManifestFileName));

            var hashedBundlePaths = Directory.GetFiles(Application.streamingAssetsPath, "*" + HashedBundleExtension, SearchOption.TopDirectoryOnly);
            foreach (var hashedBundlePath in hashedBundlePaths)
                DeleteFileAndMeta(hashedBundlePath);

            var legacyBundlePaths = Directory.GetFiles(Application.streamingAssetsPath, "*", SearchOption.TopDirectoryOnly);
            foreach (var legacyBundlePath in legacyBundlePaths)
            {
                if (!string.IsNullOrEmpty(Path.GetExtension(legacyBundlePath)))
                    continue;

                DeleteFileAndMeta(legacyBundlePath);
                DeleteFileAndMeta(legacyBundlePath + ".manifest");
            }

            foreach (var bundleName in bundleNames)
                ClearAssetBundle(bundleName);
        }

        private static void WriteWebGLHashedOutput(AssetBundleManifest unityManifest, BuildAssetBundleOptions options)
        {
            var bundleNames = unityManifest.GetAllAssetBundles();
            Array.Sort(bundleNames, StringComparer.Ordinal);

            var manifest = new AssetBundlesManifestDto
            {
                schemaVersion = ManifestSchemaVersion,
                unityVersion = Application.unityVersion,
                buildTarget = BuildTarget.WebGL.ToString(),
                bundleOptions = options.ToString(),
                generatedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                bundles = new AssetBundlesManifestBundleDto[bundleNames.Length]
            };

            for (var i = 0; i < bundleNames.Length; i++)
            {
                var bundleName = bundleNames[i];
                var bundlePath = Path.Combine(Application.streamingAssetsPath, bundleName);
                var hash = ComputeSha256(bundlePath);
                var hashedFileName = $"{bundleName}.{hash[..16]}{HashedBundleExtension}";
                var hashedFilePath = Path.Combine(Application.streamingAssetsPath, hashedFileName);
                var dependencies = unityManifest.GetAllDependencies(bundleName);
                Array.Sort(dependencies, StringComparer.Ordinal);

                DeleteFileAndMeta(hashedFilePath);
                File.Move(bundlePath, hashedFilePath);

                var bundleMetaPath = bundlePath + ".meta";
                if (File.Exists(bundleMetaPath))
                    File.Move(bundleMetaPath, hashedFilePath + ".meta");

                DeleteFileAndMeta(bundlePath + ".manifest");

                manifest.bundles[i] = new AssetBundlesManifestBundleDto
                {
                    id = bundleName,
                    file = hashedFileName,
                    sha256 = hash,
                    bytes = new FileInfo(hashedFilePath).Length,
                    dependencies = dependencies
                };
            }

            ClearAssetBundle(Path.GetFileName(Application.streamingAssetsPath));
            File.WriteAllText(Path.Combine(Application.streamingAssetsPath, ManifestFileName), JsonUtility.ToJson(manifest, true));
            AssetDatabase.Refresh();
        }

        private static string ComputeSha256(string path)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(path);
            var hash = sha256.ComputeHash(stream);
            var builder = new StringBuilder(hash.Length * 2);
            
            for (var i = 0; i < hash.Length; i++)
                builder.Append(hash[i].ToString("x2"));

            return builder.ToString();
        }

        private static void DeleteFileAndMeta(string path)
        {
            if (File.Exists(path))
                File.Delete(path);

            var metaPath = path + ".meta";
            if (File.Exists(metaPath))
                File.Delete(metaPath);
        }
        
        /*
         * Private.
         */

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
