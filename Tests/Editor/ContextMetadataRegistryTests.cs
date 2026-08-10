using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace PILAR.Context.Editor.Tests
{
    public class ContextMetadataRegistryTests
    {
        private GameObject _go;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("Target");
        }

        [TearDown]
        public void TearDown()
        {
            ContextMetadataRegistry.OverrideProviders(null);
            UnityEngine.Object.DestroyImmediate(_go);
        }

        private Transform T => _go.transform;

        private static void Install(params IContextMetadataProvider[] providers)
        {
            ContextMetadataRegistry.OverrideProviders(providers);
        }

        private static string[] Pairs(IEnumerable<ContextEntry> entries)
        {
            return entries.Select(e => $"{e.key}={e.value}").ToArray();
        }

        [Test]
        public void NoProviders_YieldNeutralValues()
        {
            Install();

            CollectionAssert.IsEmpty(ContextMetadataRegistry.Metadata(T));
            Assert.IsFalse(ContextMetadataRegistry.AnyDevice(T));
            Assert.IsFalse(ContextMetadataRegistry.AnyRelevant(T));
        }

        [Test]
        public void SingleProvider_AnswersAreUsed()
        {
            var provider = FakeMetadataProvider.Emitting(("plcPath", "MAIN.Target"));
            provider.DeviceNames.Add("Target");
            provider.RelevantNames.Add("Target");
            Install(provider);

            CollectionAssert.AreEqual(
                new[] { "plcPath=MAIN.Target" },
                Pairs(ContextMetadataRegistry.Metadata(T)));
            Assert.IsTrue(ContextMetadataRegistry.AnyDevice(T));
            Assert.IsTrue(ContextMetadataRegistry.AnyRelevant(T));
        }

        [Test]
        public void ProviderEntryOrder_IsPreserved()
        {
            Install(FakeMetadataProvider.Emitting(("b", "1"), ("a", "2"), ("c", "3")));

            // Not sorted: a provider orders its own facts most-significant-first, and the export
            // should read the way the framework meant it to.
            CollectionAssert.AreEqual(
                new[] { "b=1", "a=2", "c=3" },
                Pairs(ContextMetadataRegistry.Metadata(T)));
        }

        [Test]
        public void EarlierProviderWins_ForTheSameKey()
        {
            Install(
                FakeMetadataProvider.Emitting(("plcPath", "winner")),
                FakeMetadataProvider.Emitting(("plcPath", "loser")));

            CollectionAssert.AreEqual(
                new[] { "plcPath=winner" },
                Pairs(ContextMetadataRegistry.Metadata(T)));
        }

        [Test]
        public void LaterProvider_StillContributesUnclaimedKeys()
        {
            Install(
                FakeMetadataProvider.Emitting(("plcPath", "MAIN.Target")),
                FakeMetadataProvider.Emitting(("plcPath", "ignored"), ("robotFrame", "Base")));

            CollectionAssert.AreEqual(
                new[] { "plcPath=MAIN.Target", "robotFrame=Base" },
                Pairs(ContextMetadataRegistry.Metadata(T)));
        }

        [Test]
        public void BlankKeysAndValues_AreDropped()
        {
            // An absent fact must be an absent entry: a blank value would read downstream as a fact
            // the framework stated, which it did not.
            Install(FakeMetadataProvider.Emitting(
                ("plcPath", "MAIN.Target"),
                ("empty", ""),
                ("whitespace", "   "),
                ("", "orphaned")));

            CollectionAssert.AreEqual(
                new[] { "plcPath=MAIN.Target" },
                Pairs(ContextMetadataRegistry.Metadata(T)));
        }

        [Test]
        public void NullReturn_IsTolerated()
        {
            Install(
                new FakeMetadataProvider { MetadataFunc = _ => null },
                FakeMetadataProvider.Emitting(("plcPath", "MAIN.Target")));

            CollectionAssert.AreEqual(
                new[] { "plcPath=MAIN.Target" },
                Pairs(ContextMetadataRegistry.Metadata(T)));
        }

        [Test]
        public void Discovery_ReturnsAUsableSetWhenNotOverridden()
        {
            ContextMetadataRegistry.Invalidate();

            // Whether any provider is installed depends on the project's packages, so assert only
            // that automatic discovery works and never hands back null.
            Assert.IsNotNull(ContextMetadataRegistry.Providers);
            Assert.DoesNotThrow(() => ContextMetadataRegistry.Metadata(T));
        }

        [Test]
        public void Discovery_IgnoresTestFakes()
        {
            ContextMetadataRegistry.Invalidate();

            // FakeMetadataProvider has an internal constructor precisely so it cannot leak into a
            // real Editor session through TypeCache discovery.
            CollectionAssert.DoesNotContain(
                ContextMetadataRegistry.Providers.Select(p => p.GetType()).ToArray(),
                typeof(FakeMetadataProvider));
        }
    }
}
