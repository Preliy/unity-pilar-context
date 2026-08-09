using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace PILAR.Context.Editor.Tests
{
    public class ContextComponentFilterTests
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
            UnityEngine.Object.DestroyImmediate(_go);
        }

        private string[] Names() => ContextComponentFilter.GetRelevantComponentNames(_go).ToArray();

        /// <summary>Stands in for a real behaviour worth reporting to a context author.</summary>
        private class SampleBehaviour : MonoBehaviour
        {
        }

        [Test]
        public void BareGameObject_ReportsNothing()
        {
            // Every GameObject has a Transform, and Transform is filtered out.
            CollectionAssert.IsEmpty(Names());
        }

        [Test]
        public void ExcludesContextNodeItself()
        {
            _go.AddComponent<ContextNode>();

            CollectionAssert.DoesNotContain(Names(), nameof(ContextNode));
        }

        [Test]
        public void ExcludesRenderingAndCollisionNoise()
        {
            _go.AddComponent<BoxCollider>();
            _go.AddComponent<MeshFilter>();
            _go.AddComponent<MeshRenderer>();

            // CAD imports bury the meaningful components under these; they say nothing about function.
            CollectionAssert.IsEmpty(Names());
        }

        [Test]
        public void IncludesOrdinaryBehaviours()
        {
            _go.AddComponent<SampleBehaviour>();

            CollectionAssert.Contains(Names(), nameof(SampleBehaviour));
        }

        [Test]
        public void ReportsShortTypeName_NotFullName()
        {
            _go.AddComponent<SampleBehaviour>();

            var names = Names();
            CollectionAssert.Contains(names, "SampleBehaviour");
            Assert.IsFalse(names.Any(n => n.Contains(".")), "names must be Type.Name, not FullName");
        }

        [Test]
        public void KeepsMeaningfulComponentsAlongsideFilteredOnes()
        {
            _go.AddComponent<BoxCollider>();
            _go.AddComponent<SampleBehaviour>();
            _go.AddComponent<ContextNode>();

            CollectionAssert.AreEqual(new[] { nameof(SampleBehaviour) }, Names());
        }
    }
}
