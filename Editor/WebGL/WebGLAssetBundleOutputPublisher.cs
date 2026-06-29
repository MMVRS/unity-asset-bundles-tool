#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Model.AssetBundles;
using UnityEditor;
using UnityEngine;

namespace Build1.UnityAssetBundlesTool.Editor.WebGL
{
    internal sealed class WebGLAssetBundleOutputPublisher
    {
        private const int    ManifestSchemaVersion = 1;
        private const string ManifestFileName      = "asset-bundles.json";
        private const string HashedBundleExtension = ".bundle";

        private readonly string _streamingAssetsPath;

        public WebGLAssetBundleOutputPublisher(string streamingAssetsPath)
        {
            _streamingAssetsPath = streamingAssetsPath;
        }

        public void PublishSuccessfulBuild(AssetBundleManifest unityManifest, BuildAssetBundleOptions options)
        {
            var cleanupBundleNames = CollectCleanupBundleNames();
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
                var bundlePath = Path.Combine(_streamingAssetsPath, bundleName);
                var hash = ComputeSha256(bundlePath);
                var hashedFileName = $"{bundleName}.{hash[..16]}{HashedBundleExtension}";
                var hashedFilePath = Path.Combine(_streamingAssetsPath, hashedFileName);
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

            ClearRawBundle(Path.GetFileName(_streamingAssetsPath));
            CleanStaleOutput(manifest, cleanupBundleNames);
            File.WriteAllText(Path.Combine(_streamingAssetsPath, ManifestFileName), JsonUtility.ToJson(manifest, true));
            AssetDatabase.Refresh();
        }

        public void CleanExplicitOutput(List<string> bundleNames)
        {
            if (!Directory.Exists(_streamingAssetsPath))
                return;

            DeleteFileAndMeta(Path.Combine(_streamingAssetsPath, ManifestFileName));

            var hashedBundlePaths = Directory.GetFiles(_streamingAssetsPath, "*" + HashedBundleExtension, SearchOption.TopDirectoryOnly);
            foreach (var hashedBundlePath in hashedBundlePaths)
                DeleteFileAndMeta(hashedBundlePath);

            var legacyBundlePaths = Directory.GetFiles(_streamingAssetsPath, "*", SearchOption.TopDirectoryOnly);
            foreach (var legacyBundlePath in legacyBundlePaths)
            {
                if (!string.IsNullOrEmpty(Path.GetExtension(legacyBundlePath)))
                    continue;

                DeleteFileAndMeta(legacyBundlePath);
                DeleteFileAndMeta(legacyBundlePath + ".manifest");
            }

            foreach (var bundleName in bundleNames)
                ClearRawBundle(bundleName);
        }

        public void CleanFailedBuildArtifacts()
        {
            if (!Directory.Exists(_streamingAssetsPath))
                return;

            var paths = Directory.GetFiles(_streamingAssetsPath, "*", SearchOption.TopDirectoryOnly);
            foreach (var path in paths)
            {
                if (!IsPartialBuildOutput(path))
                    continue;

                if (Path.GetExtension(path) == ".meta")
                    DeleteFileAndMeta(path.Substring(0, path.Length - ".meta".Length));
                else
                    DeleteFileAndMeta(path);
            }

            AssetDatabase.Refresh();
        }

        private List<string> CollectCleanupBundleNames()
        {
            var names = AssetDatabase.GetAllAssetBundleNames();
            var cleanupBundleNames = new List<string>(names.Length + 1);

            for (var i = 0; i < names.Length; i++)
                cleanupBundleNames.Add(names[i]);

            cleanupBundleNames.Add(Path.GetFileName(_streamingAssetsPath));

            var previousManifestPath = Path.Combine(_streamingAssetsPath, ManifestFileName);
            if (!File.Exists(previousManifestPath))
                return cleanupBundleNames;

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
                return cleanupBundleNames;

            for (var i = 0; i < previousManifest.bundles.Length; i++)
            {
                var bundle = previousManifest.bundles[i];
                if (bundle != null && !string.IsNullOrWhiteSpace(bundle.id) && !cleanupBundleNames.Contains(bundle.id))
                    cleanupBundleNames.Add(bundle.id);
            }

            return cleanupBundleNames;
        }

        private void CleanStaleOutput(AssetBundlesManifestDto manifest, List<string> cleanupBundleNames)
        {
            var currentBundleIds = new HashSet<string>(StringComparer.Ordinal);
            var currentBundleFiles = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < manifest.bundles.Length; i++)
            {
                currentBundleIds.Add(manifest.bundles[i].id);
                currentBundleFiles.Add(manifest.bundles[i].file);
            }

            var hashedBundlePaths = Directory.GetFiles(_streamingAssetsPath, "*" + HashedBundleExtension, SearchOption.TopDirectoryOnly);
            foreach (var hashedBundlePath in hashedBundlePaths)
            {
                if (!currentBundleFiles.Contains(Path.GetFileName(hashedBundlePath)))
                    DeleteFileAndMeta(hashedBundlePath);
            }

            var legacyBundlePaths = Directory.GetFiles(_streamingAssetsPath, "*", SearchOption.TopDirectoryOnly);
            foreach (var legacyBundlePath in legacyBundlePaths)
            {
                var fileName = Path.GetFileName(legacyBundlePath);
                var extension = Path.GetExtension(legacyBundlePath);

                if (extension == ".manifest")
                {
                    DeleteFileAndMeta(legacyBundlePath);
                    continue;
                }

                if (extension == ".meta")
                {
                    var assetPath = legacyBundlePath.Substring(0, legacyBundlePath.Length - ".meta".Length);
                    if (Path.GetExtension(assetPath) == ".manifest")
                        DeleteFileAndMeta(assetPath);
                    continue;
                }

                if (!string.IsNullOrEmpty(extension) || currentBundleIds.Contains(fileName))
                    continue;

                DeleteFileAndMeta(legacyBundlePath);
                DeleteFileAndMeta(legacyBundlePath + ".manifest");
            }

            foreach (var bundleName in cleanupBundleNames)
            {
                if (!currentBundleIds.Contains(bundleName))
                    ClearRawBundle(bundleName);
            }
        }

        private bool IsPartialBuildOutput(string path)
        {
            var fileName = Path.GetFileName(path);
            var extension = Path.GetExtension(path);

            if (fileName == ManifestFileName || extension == HashedBundleExtension)
                return false;

            if (extension == ".manifest")
                return true;

            if (extension != ".meta")
                return string.IsNullOrEmpty(extension);

            var assetPath = path.Substring(0, path.Length - ".meta".Length);
            var assetFileName = Path.GetFileName(assetPath);
            var assetExtension = Path.GetExtension(assetPath);

            if (assetFileName == ManifestFileName || assetExtension == HashedBundleExtension)
                return false;

            return string.IsNullOrEmpty(assetExtension) || assetExtension == ".manifest";
        }

        private string ComputeSha256(string path)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(path);
            var hash = sha256.ComputeHash(stream);
            var builder = new StringBuilder(hash.Length * 2);

            for (var i = 0; i < hash.Length; i++)
                builder.Append(hash[i].ToString("x2"));

            return builder.ToString();
        }

        private void ClearRawBundle(string bundleName)
        {
            var paths = GetRawBundleFilePaths(bundleName);
            foreach (var path in paths)
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        private IEnumerable<string> GetRawBundleFilePaths(string bundleName)
        {
            return new[]
            {
                Path.Combine(_streamingAssetsPath, bundleName),
                Path.Combine(_streamingAssetsPath, bundleName) + ".manifest",
                Path.Combine(_streamingAssetsPath, bundleName) + ".manifest.meta",
                Path.Combine(_streamingAssetsPath, bundleName) + ".meta"
            };
        }

        private void DeleteFileAndMeta(string path)
        {
            if (File.Exists(path))
                File.Delete(path);

            var metaPath = path + ".meta";
            if (File.Exists(metaPath))
                File.Delete(metaPath);
        }

        private void Log(string message)
        {
            Debug.Log($"AssetBundles: {message}");
        }
    }
}

#endif
