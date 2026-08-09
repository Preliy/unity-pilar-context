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

        [Test]
        public void NoProviders_YieldNeutralValues()
        {
            ContextMetadataRegistry.OverrideProviders(new IContextMetadataProvider[0]);

            Assert.AreEqual(string.Empty, ContextMetadataRegistry.Path(T));
            Assert.IsNull(ContextMetadataRegistry.LinkState(T));
            Assert.AreEqual(string.Empty, ContextMetadataRegistry.Role(T));
            Assert.IsFalse(ContextMetadataRegistry.AnyDevice(T));
            Assert.IsFalse(ContextMetadataRegistry.AnyRelevant(T));
            CollectionAssert.IsEmpty(ContextMetadataRegistry.Notes(T).ToArray());
        }

        [Test]
        public void SingleProvider_AnswersAreUsed()
        {
            var provider = new FakeMetadataProvider { PathFunc = _ => "MAIN.Target" };
            provider.DeviceNames.Add("Target");
            provider.RelevantNames.Add("Target");
            ContextMetadataRegistry.OverrideProviders(new IContextMetadataProvider[] { provider });

            Assert.AreEqual("MAIN.Target", ContextMetadataRegistry.Path(T));
            Assert.IsTrue(ContextMetadataRegistry.AnyDevice(T));
            Assert.IsTrue(ContextMetadataRegistry.AnyRelevant(T));
        }

        [Test]
        public void EarlierProviderWins_WhenBothAnswer()
        {
            var winner = new FakeMetadataProvider { PathFunc = _ => "winner" };
            var loser = new FakeMetadataProvider { PathFunc = _ => "loser" };
            ContextMetadataRegistry.OverrideProviders(new IContextMetadataProvider[] { winner, loser });

            Assert.AreEqual("winner", ContextMetadataRegistry.Path(T));
        }

        [Test]
        public void EmptyAnswer_FallsThroughToTheNextProvider()
        {
            var silent = new FakeMetadataProvider { PathFunc = _ => string.Empty };
            var answering = new FakeMetadataProvider { PathFunc = _ => "MAIN.Target" };
            ContextMetadataRegistry.OverrideProviders(new IContextMetadataProvider[] { silent, answering });

            Assert.AreEqual("MAIN.Target", ContextMetadataRegistry.Path(T));
        }

        [Test]
        public void NullLinkState_FallsThroughButFalseDoesNot()
        {
            var silent = new FakeMetadataProvider { LinkStateFunc = _ => null };
            var answering = new FakeMetadataProvider { LinkStateFunc = _ => false };
            ContextMetadataRegistry.OverrideProviders(new IContextMetadataProvider[] { silent, answering });

            // false is a real answer ("device, but not PLC linked") and must not be skipped as if
            // the provider had declined to answer.
            Assert.AreEqual(false, ContextMetadataRegistry.LinkState(T));
        }

        [Test]
        public void Notes_AreConcatenatedAcrossProviders()
        {
            var a = new FakeMetadataProvider { NotesFunc = _ => new[] { "note-a" } };
            var b = new FakeMetadataProvider { NotesFunc = _ => new[] { "note-b", string.Empty } };
            ContextMetadataRegistry.OverrideProviders(new IContextMetadataProvider[] { a, b });

            CollectionAssert.AreEqual(new[] { "note-a", "note-b" }, ContextMetadataRegistry.Notes(T).ToArray());
        }

        [Test]
        public void Discovery_ReturnsAUsableSetWhenNotOverridden()
        {
            ContextMetadataRegistry.Invalidate();

            // Whether any provider is installed depends on the project's packages, so assert only
            // that automatic discovery works and never hands back null.
            Assert.IsNotNull(ContextMetadataRegistry.Providers);
            Assert.DoesNotThrow(() => ContextMetadataRegistry.Path(T));
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
