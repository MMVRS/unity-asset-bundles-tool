using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Model.AssetBundles;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Build1.UnityAssetBundlesTool.Editor.Tests
{
    public sealed class AssetBundlesBuilderWebGLVariantTests
    {
        private string _temporaryRoot;

        [SetUp]
        public void SetUp()
        {
            _temporaryRoot = Path.Combine(Path.GetTempPath(), $"webgl-bundle-builder-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_temporaryRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_temporaryRoot))
                Directory.Delete(_temporaryRoot, true);
        }

        [TestCase(WebGLTextureSubtarget.DXT)]
        [TestCase(WebGLTextureSubtarget.ASTC)]
        public void ExplicitVariantBuild_PassesExactParametersAndPreservesCompleteOutputOnFailure(
            WebGLTextureSubtarget textureSubtarget)
        {
            var outputPath = Path.Combine(_temporaryRoot, textureSubtarget.ToString().ToLowerInvariant());
            var siblingPath = Path.Combine(_temporaryRoot, "sibling");
            Directory.CreateDirectory(outputPath);
            Directory.CreateDirectory(siblingPath);
            File.WriteAllText(Path.Combine(siblingPath, "keep.txt"), "sibling");

            const string bundleId = "ui";
            const string previousBundleContents = "previous bundle";
            var bundleHash = ComputeSha256(previousBundleContents);
            var bundleFile = $"{bundleId}.{bundleHash.Substring(0, 16)}.bundle";
            File.WriteAllText(Path.Combine(outputPath, bundleFile), previousBundleContents);

            var previousManifest = new AssetBundlesManifestDto
            {
                schemaVersion = 1,
                unityVersion = Application.unityVersion,
                buildTarget = BuildTarget.WebGL.ToString(),
                bundleOptions = BuildAssetBundleOptions.StrictMode.ToString(),
                generatedAtUtc = DateTime.UtcNow.ToString("O"),
                bundles = new[]
                {
                    new AssetBundlesManifestBundleDto
                    {
                        id = bundleId,
                        file = bundleFile,
                        sha256 = bundleHash,
                        bytes = previousBundleContents.Length,
                        dependencies = Array.Empty<string>()
                    }
                }
            };
            var manifestPath = Path.Combine(outputPath, "asset-bundles.json");
            File.WriteAllText(manifestPath, JsonUtility.ToJson(previousManifest, true));
            var sidecarPath = Path.Combine(outputPath, "asset-bundles-variant.json");
            File.WriteAllText(
                sidecarPath,
                $"{{\n  \"schemaVersion\": 1,\n  \"buildTarget\": \"WebGL\",\n  \"textureSubtarget\": \"{textureSubtarget}\",\n  \"assetBundlesManifestSha256\": \"{ComputeSha256(File.ReadAllText(manifestPath))}\"\n}}\n");

            var completeFiles = new Dictionary<string, string>
            {
                ["asset-bundles.json"] = File.ReadAllText(manifestPath),
                ["asset-bundles-variant.json"] = File.ReadAllText(sidecarPath),
                [bundleFile] = File.ReadAllText(Path.Combine(outputPath, bundleFile))
            };

            var options = BuildAssetBundleOptions.StrictMode | BuildAssetBundleOptions.ChunkBasedCompression;
            var buildInvoked = false;
            var captured = new BuildAssetBundlesParameters();

            var succeeded = AssetBundlesBuilder.BuildWebGLVariant(
                textureSubtarget,
                outputPath,
                options,
                () => true,
                parameters =>
                {
                    buildInvoked = true;
                    captured = parameters;
                    File.WriteAllText(Path.Combine(outputPath, "ui"), "partial raw bundle");
                    File.WriteAllText(Path.Combine(outputPath, "ui.manifest"), "partial Unity manifest");
                    return null;
                });

            Assert.That(succeeded, Is.False);
            Assert.That(buildInvoked, Is.True);
            Assert.That(captured.targetPlatform, Is.EqualTo(BuildTarget.WebGL));
            Assert.That(captured.subtarget, Is.EqualTo((int)textureSubtarget));
            Assert.That(captured.options, Is.EqualTo(options));
            Assert.That(captured.outputPath, Is.EqualTo(Path.GetFullPath(outputPath)));
            Assert.That(File.Exists(Path.Combine(outputPath, "ui")), Is.False);
            Assert.That(File.Exists(Path.Combine(outputPath, "ui.manifest")), Is.False);
            Assert.That(File.ReadAllText(Path.Combine(siblingPath, "keep.txt")), Is.EqualTo("sibling"));

            foreach (var file in completeFiles)
                Assert.That(File.ReadAllText(Path.Combine(outputPath, file.Key)), Is.EqualTo(file.Value));
        }

        [Test]
        public void ExplicitVariantBuild_RejectsUnsupportedSubtargetAndStreamingAssetsDescendant()
        {
            var checkInvoked = false;
            Func<bool> check = () =>
            {
                checkInvoked = true;
                return true;
            };

            Assert.Throws<ArgumentOutOfRangeException>(() => AssetBundlesBuilder.BuildWebGLVariant(
                WebGLTextureSubtarget.Generic,
                Path.Combine(_temporaryRoot, "generic"),
                BuildAssetBundleOptions.None,
                check,
                _ => null));

            Assert.Throws<ArgumentException>(() => AssetBundlesBuilder.BuildWebGLVariant(
                WebGLTextureSubtarget.DXT,
                Path.Combine(Application.streamingAssetsPath, "forbidden-variant"),
                BuildAssetBundleOptions.None,
                check,
                _ => null));

            Assert.That(checkInvoked, Is.False, "Invalid destinations must fail before build discovery or mutation.");
        }

        private static string ComputeSha256(string contents)
        {
            using var sha = SHA256.Create();
            return string.Concat(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(contents))
                .Select(value => value.ToString("x2")));
        }
    }
}
