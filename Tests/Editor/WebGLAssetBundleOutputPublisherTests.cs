using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Build1.UnityAssetBundlesTool.Editor.WebGL;
using Model.AssetBundles;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Build1.UnityAssetBundlesTool.Editor.Tests
{
    public sealed class WebGLAssetBundleOutputPublisherTests
    {
        private string _temporaryRoot;

        [SetUp]
        public void SetUp()
        {
            _temporaryRoot = Path.Combine(Path.GetTempPath(), $"webgl-bundle-publisher-tests-{Guid.NewGuid():N}");
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
        public void SuccessfulPublication_WritesBoundVariantSidecar(WebGLTextureSubtarget textureSubtarget)
        {
            File.WriteAllText(Path.Combine(_temporaryRoot, "zeta"), "zeta bytes");
            File.WriteAllText(Path.Combine(_temporaryRoot, "alpha"), "alpha bytes");
            var dependencies = new Dictionary<string, string[]>
            {
                ["alpha"] = Array.Empty<string>(),
                ["zeta"] = new[] { "alpha" }
            };
            var options = BuildAssetBundleOptions.StrictMode | BuildAssetBundleOptions.ChunkBasedCompression;
            var publisher = new WebGLAssetBundleOutputPublisher(_temporaryRoot);

            publisher.PublishSuccessfulBuild(
                new[] { "zeta", "alpha" },
                name => dependencies[name],
                options,
                textureSubtarget);

            var manifestPath = Path.Combine(_temporaryRoot, "asset-bundles.json");
            var manifest = JsonUtility.FromJson<AssetBundlesManifestDto>(File.ReadAllText(manifestPath));
            Assert.That(manifest.buildTarget, Is.EqualTo(BuildTarget.WebGL.ToString()));
            Assert.That(manifest.bundleOptions, Is.EqualTo(options.ToString()));
            Assert.That(manifest.bundles.Select(bundle => bundle.id), Is.EqualTo(new[] { "alpha", "zeta" }));
            Assert.That(manifest.bundles[1].dependencies, Is.EqualTo(new[] { "alpha" }));

            foreach (var bundle in manifest.bundles)
            {
                var bundlePath = Path.Combine(_temporaryRoot, bundle.file);
                Assert.That(File.Exists(bundlePath), Is.True);
                Assert.That(bundle.file, Is.EqualTo($"{bundle.id}.{bundle.sha256.Substring(0, 16)}.bundle"));
                Assert.That(ComputeSha256(bundlePath), Is.EqualTo(bundle.sha256));
                Assert.That(new FileInfo(bundlePath).Length, Is.EqualTo(bundle.bytes));
            }

            var manifestHash = ComputeSha256(manifestPath);
            var sidecar = File.ReadAllText(Path.Combine(_temporaryRoot, "asset-bundles-variant.json"));
            StringAssert.Contains("\"buildTarget\": \"WebGL\"", sidecar);
            StringAssert.Contains($"\"textureSubtarget\": \"{textureSubtarget}\"", sidecar);
            StringAssert.Contains($"\"assetBundlesManifestSha256\": \"{manifestHash}\"", sidecar);
            Assert.That(publisher.IsOutputPublished(textureSubtarget), Is.True);
            Assert.That(
                publisher.IsOutputPublished(
                    textureSubtarget == WebGLTextureSubtarget.DXT
                        ? WebGLTextureSubtarget.ASTC
                        : WebGLTextureSubtarget.DXT),
                Is.False);
        }

        private static string ComputeSha256(string path)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            return string.Concat(sha.ComputeHash(stream).Select(value => value.ToString("x2")));
        }
    }
}
