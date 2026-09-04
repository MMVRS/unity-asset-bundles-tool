using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;

namespace Build1.UnityAssetBundlesTool.Editor.Tests
{
    public sealed class AssetBundlesProcessorWebGLVariantTests
    {
        private string _temporaryRoot;

        [SetUp]
        public void SetUp()
        {
            _temporaryRoot = Path.Combine(Path.GetTempPath(), $"webgl-bundle-processor-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_temporaryRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_temporaryRoot))
                Directory.Delete(_temporaryRoot, true);
        }

        [Test]
        public void VerificationScope_ValidatesTargetRejectsNestingAndCanBeReusedAfterDispose()
        {
            var dxtOutput = CreatePublishedOutput("dxt", WebGLTextureSubtarget.DXT);
            var astcOutput = CreatePublishedOutput("astc", WebGLTextureSubtarget.ASTC);
            var processor = new AssetBundlesProcessor();

            using (AssetBundlesProcessor.UseWebGLVariantForPlayerBuild(WebGLTextureSubtarget.DXT, dxtOutput))
            {
                Assert.DoesNotThrow(() =>
                    processor.ValidatePublishedBundlesForBuild(BuildTarget.WebGL, true));
                Assert.Throws<BuildFailedException>(() =>
                    processor.ValidatePublishedBundlesForBuild(BuildTarget.StandaloneOSX, true));
                Assert.Throws<InvalidOperationException>(() =>
                    AssetBundlesProcessor.UseWebGLVariantForPlayerBuild(WebGLTextureSubtarget.ASTC, astcOutput));
            }

            using (AssetBundlesProcessor.UseWebGLVariantForPlayerBuild(WebGLTextureSubtarget.ASTC, astcOutput))
            {
                Assert.DoesNotThrow(() =>
                    processor.ValidatePublishedBundlesForBuild(BuildTarget.WebGL, true));
            }
        }

        [Test]
        public void VerificationScope_RejectsCrossWiredVariantAndStillClearsOnDispose()
        {
            var astcOutput = CreatePublishedOutput("cross-wired", WebGLTextureSubtarget.ASTC);
            var processor = new AssetBundlesProcessor();

            using (AssetBundlesProcessor.UseWebGLVariantForPlayerBuild(WebGLTextureSubtarget.DXT, astcOutput))
            {
                Assert.Throws<BuildFailedException>(() =>
                    processor.ValidatePublishedBundlesForBuild(BuildTarget.WebGL, true));
            }

            using (AssetBundlesProcessor.UseWebGLVariantForPlayerBuild(WebGLTextureSubtarget.ASTC, astcOutput))
            {
                Assert.DoesNotThrow(() =>
                    processor.ValidatePublishedBundlesForBuild(BuildTarget.WebGL, true));
            }
        }

        private string CreatePublishedOutput(string name, WebGLTextureSubtarget textureSubtarget)
        {
            var root = Path.Combine(_temporaryRoot, name);
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "ui.complete.bundle"), "bundle");
            var manifestPath = Path.Combine(root, "asset-bundles.json");
            File.WriteAllText(
                manifestPath,
                "{\"schemaVersion\":1,\"bundles\":[{\"id\":\"ui\",\"file\":\"ui.complete.bundle\"}]}");
            File.WriteAllText(
                Path.Combine(root, "asset-bundles-variant.json"),
                "{\"schemaVersion\":1,\"buildTarget\":\"WebGL\",\"textureSubtarget\":\"" +
                textureSubtarget + "\",\"assetBundlesManifestSha256\":\"" + ComputeSha256(manifestPath) + "\"}");
            return root;
        }

        private static string ComputeSha256(string path)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            return string.Concat(sha.ComputeHash(stream).Select(value => value.ToString("x2")));
        }
    }
}
