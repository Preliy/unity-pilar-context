using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

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
            CollectionAssert.IsEmpty(ContextMetadataRegistry.Namespaces);
            Assert.IsFalse(ContextMetadataRegistry.AnyDevice(T));
            Assert.IsFalse(ContextMetadataRegistry.AnyRelevant(T));
        }

        [Test]
        public void Metadata_ComesBackUnderTheProvidersNamespace()
        {
            var provider = FakeMetadataProvider.Emitting(("plcPath", "MAIN.Target"));
            provider.Namespace = "oc";
            provider.DeviceNames.Add("Target");
            provider.RelevantNames.Add("Target");
            Install(provider);

            // The provider yields a bare key; prefixing is the registry's job, so a provider cannot
            // forget it and two providers cannot collide.
            CollectionAssert.AreEqual(
                new[] { "oc.plcPath=MAIN.Target" },
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
                new[] { "fake.b=1", "fake.a=2", "fake.c=3" },
                Pairs(ContextMetadataRegistry.Metadata(T)));
        }

        [Test]
        public void ProvidersInDifferentNamespaces_BothContribute()
        {
            Install(
                FakeMetadataProvider.Emitting("oc", ("plcPath", "MAIN.Target")),
                FakeMetadataProvider.Emitting("kuka", ("plcPath", "R1.Target")));

            // The same bare key from two frameworks is no longer a collision - the namespaces keep
            // them apart, so nothing is silently dropped.
            CollectionAssert.AreEqual(
                new[] { "oc.plcPath=MAIN.Target", "kuka.plcPath=R1.Target" },
                Pairs(ContextMetadataRegistry.Metadata(T)));
        }

        [Test]
        public void RepeatedKeyFromOneProvider_KeepsTheFirstAnswer()
        {
            Install(new FakeMetadataProvider
            {
                MetadataFunc = _ => new[]
                {
                    new ContextEntry { key = "plcPath", value = "winner" },
                    new ContextEntry { key = "plcPath", value = "loser" }
                }
            });

            CollectionAssert.AreEqual(
                new[] { "fake.plcPath=winner" },
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
                new[] { "fake.plcPath=MAIN.Target" },
                Pairs(ContextMetadataRegistry.Metadata(T)));
        }

        [Test]
        public void NullReturn_IsTolerated()
        {
            Install(
                new FakeMetadataProvider { Namespace = "silent", MetadataFunc = _ => null },
                FakeMetadataProvider.Emitting(("plcPath", "MAIN.Target")));

            CollectionAssert.AreEqual(
                new[] { "fake.plcPath=MAIN.Target" },
                Pairs(ContextMetadataRegistry.Metadata(T)));
        }

        // ------------------------------------------------------------ admission

        [Test]
        public void ProviderWithoutANamespace_IsRefused()
        {
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("declares no Namespace"));

            Install(FakeMetadataProvider.Emitting("  ", ("plcPath", "MAIN.Target")));

            // Half-supporting it would write entries nothing could ever identify as derived.
            CollectionAssert.IsEmpty(ContextMetadataRegistry.Providers);
        }

        [Test]
        public void ProviderWithASeparatorInItsNamespace_IsRefused()
        {
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("the separator itself"));

            Install(FakeMetadataProvider.Emitting("a.b", ("plcPath", "MAIN.Target")));

            CollectionAssert.IsEmpty(ContextMetadataRegistry.Providers);
        }

        [Test]
        public void TwoProvidersClaimingOneNamespace_KeepsOnlyTheFirst()
        {
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("both claim the namespace"));

            Install(
                FakeMetadataProvider.Emitting("oc", ("plcPath", "first")),
                FakeMetadataProvider.Emitting("oc", ("plcPath", "second")));

            // A shared prefix would leave a sync unable to tell whose entry is whose.
            Assert.AreEqual(1, ContextMetadataRegistry.Providers.Count);
            CollectionAssert.AreEqual(
                new[] { "oc.plcPath=first" },
                Pairs(ContextMetadataRegistry.Metadata(T)));
        }

        // ---------------------------------------------------------- key opinions

        [Test]
        public void IsDerivedKey_IsTrueOnlyForAnInstalledNamespace()
        {
            Install(FakeMetadataProvider.Emitting("oc", ("plcPath", "MAIN.Target")));

            Assert.IsTrue(ContextMetadataRegistry.IsDerivedKey("oc.plcPath"));
            Assert.IsTrue(ContextMetadataRegistry.IsDerivedKey("oc.anything"), "the key need not be one the provider emits");

            // An orphan from an uninstalled framework: not ours to rewrite or delete.
            Assert.IsFalse(ContextMetadataRegistry.IsDerivedKey("kuka.plcPath"));
            // And an author's own dotted key is documentation, not metadata.
            Assert.IsFalse(ContextMetadataRegistry.IsDerivedKey("Motor.Speed"));
            Assert.IsFalse(ContextMetadataRegistry.IsDerivedKey("Function"));
            Assert.IsFalse(ContextMetadataRegistry.IsDerivedKey(".leading"));
            Assert.IsFalse(ContextMetadataRegistry.IsDerivedKey(null));
        }

        [Test]
        public void IsNamespacedKey_AsksNothingAboutInstalledProviders()
        {
            Install();

            Assert.IsTrue(ContextMetadataRegistry.IsNamespacedKey("oc.plcPath"));
            Assert.IsFalse(ContextMetadataRegistry.IsNamespacedKey("Function"));
            Assert.IsFalse(ContextMetadataRegistry.IsNamespacedKey(".leading"));
            Assert.IsFalse(ContextMetadataRegistry.IsNamespacedKey(null));
        }

        // ------------------------------------------------------------ discovery

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
