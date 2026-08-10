using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace PILAR.Context.Editor.Tests
{
    public class ContextMetadataSyncTests
    {
        private GameObject _go;
        private ContextNode _node;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("P_Reader");
            _node = _go.AddComponent<ContextNode>();
        }

        [TearDown]
        public void TearDown()
        {
            ContextMetadataRegistry.OverrideProviders(null);
            UnityEngine.Object.DestroyImmediate(_go);
        }

        private static void Install(params (string key, string value)[] entries)
        {
            ContextMetadataRegistry.OverrideProviders(new IContextMetadataProvider[]
            {
                FakeMetadataProvider.Emitting("oc", entries)
            });
        }

        private string[] Keys() => _node.Entries.Select(e => e.key).ToArray();

        private string[] Pairs() => _node.Entries.Select(e => $"{e.key}={e.value}").ToArray();

        // ---------------------------------------------------------------- apply

        [Test]
        public void Apply_AddsWhatTheProviderKnows()
        {
            Install(("plcPath", "MAIN.P_Reader"), ("deviceType", "SensorBinary"));

            Assert.IsTrue(ContextMetadataSync.Apply(_node));

            CollectionAssert.AreEqual(
                new[] { "oc.plcPath=MAIN.P_Reader", "oc.deviceType=SensorBinary" }, Pairs());
        }

        [Test]
        public void Apply_IsIdempotent()
        {
            Install(("plcPath", "MAIN.P_Reader"));
            ContextMetadataSync.Apply(_node);

            // The second run must report no change, or every sync would dirty every scene.
            Assert.IsFalse(ContextMetadataSync.Apply(_node));
            CollectionAssert.AreEqual(new[] { "oc.plcPath=MAIN.P_Reader" }, Pairs());
        }

        [Test]
        public void Apply_UpdatesInPlaceWithoutReordering()
        {
            Install(("plcPath", "MAIN.Old"));
            ContextMetadataSync.Apply(_node);
            _node.Set("Function", "Reads the DMC.");

            Install(("plcPath", "MAIN.New"));
            Assert.IsTrue(ContextMetadataSync.Apply(_node));

            // A changed value must not become remove-then-append: the entry keeps its slot, so a
            // re-sync produces no scene diff beyond the value itself.
            CollectionAssert.AreEqual(
                new[] { "oc.plcPath=MAIN.New", "Function=Reads the DMC." }, Pairs());
        }

        [Test]
        public void Apply_RemovesAKeyTheProviderNoLongerAnswersFor()
        {
            Install(("plcPath", "MAIN.P_Reader"), ("hierarchyRole", "group"));
            ContextMetadataSync.Apply(_node);

            // The Hierarchy component was deleted from the scene.
            Install(("plcPath", "MAIN.P_Reader"));
            Assert.IsTrue(ContextMetadataSync.Apply(_node));

            CollectionAssert.AreEqual(new[] { "oc.plcPath=MAIN.P_Reader" }, Pairs());
        }

        [Test]
        public void Apply_NeverTouchesAuthoredEntries()
        {
            _node.Set("Function", "Reads the DMC on the carrier.");
            _node.Set("Interlocks", "Gate closed.");
            Install(("plcPath", "MAIN.P_Reader"));

            ContextMetadataSync.Apply(_node);
            Install(("plcPath", "MAIN.Renamed"));
            ContextMetadataSync.Apply(_node);
            Install();
            ContextMetadataSync.Apply(_node);

            // Through an add, an update and a full removal, the human's entries keep both their
            // values and their original positions.
            CollectionAssert.AreEqual(
                new[] { "Function=Reads the DMC on the carrier.", "Interlocks=Gate closed." }, Pairs());
        }

        [Test]
        public void Apply_LeavesAnAuthoredDottedKeyAlone()
        {
            // "Motor.Speed" is namespaced-looking but belongs to no installed provider, so it is
            // documentation and must survive.
            _node.Set("Motor.Speed", "1.4 m/s");
            Install(("plcPath", "MAIN.P_Reader"));

            ContextMetadataSync.Apply(_node);

            CollectionAssert.Contains(Keys(), "Motor.Speed");
        }

        [Test]
        public void Apply_LeavesOrphansOfUninstalledProvidersAlone()
        {
            _node.Set("kuka.frame", "R1.Base");
            Install(("plcPath", "MAIN.P_Reader"));

            ContextMetadataSync.Apply(_node);

            // Opening the project without a framework installed must not strip that framework's data
            // out of every node. The cost is that nothing can clean it up either.
            CollectionAssert.AreEqual(
                new[] { "kuka.frame=R1.Base", "oc.plcPath=MAIN.P_Reader" }, Pairs());
        }

        [Test]
        public void Apply_WithNoProvidersInstalled_ChangesNothing()
        {
            _node.Set("Function", "Reads the DMC.");
            ContextMetadataRegistry.OverrideProviders(new IContextMetadataProvider[0]);

            Assert.IsFalse(ContextMetadataSync.Apply(_node));
            CollectionAssert.AreEqual(new[] { "Function=Reads the DMC." }, Pairs());
        }

        [Test]
        public void Apply_ToleratesNull()
        {
            Assert.IsFalse(ContextMetadataSync.Apply(null));
        }

        // ----------------------------------------------------------------- plan

        [Test]
        public void Plan_ReportsDriftAndWritesNothing()
        {
            Install(("plcPath", "MAIN.Old"), ("hierarchyRole", "group"));
            ContextMetadataSync.Apply(_node);

            Install(("plcPath", "MAIN.New"), ("deviceType", "SensorBinary"));
            var plan = ContextMetadataSync.Plan(_node);

            CollectionAssert.AreEqual(new[] { "oc.deviceType" }, plan.Added);
            CollectionAssert.AreEqual(new[] { "oc.plcPath" }, plan.Updated);
            CollectionAssert.AreEqual(new[] { "oc.hierarchyRole" }, plan.Removed);
            Assert.IsFalse(plan.IsEmpty);

            // The drift report is a report: the node still holds what it held before.
            CollectionAssert.AreEqual(
                new[] { "oc.plcPath=MAIN.Old", "oc.hierarchyRole=group" }, Pairs());
        }

        [Test]
        public void Plan_IsEmptyOnceApplied()
        {
            Install(("plcPath", "MAIN.P_Reader"));
            ContextMetadataSync.Apply(_node);

            Assert.IsTrue(ContextMetadataSync.Plan(_node).IsEmpty);
        }

        // -------------------------------------------------------------- subtree

        [Test]
        public void PlanSubtree_ReturnsOnlyTheNodesThatWouldChange()
        {
            var child = new GameObject("Child");
            child.transform.SetParent(_go.transform);
            var childNode = child.AddComponent<ContextNode>();

            Install(("plcPath", "MAIN.Anything"));
            ContextMetadataSync.Apply(childNode);

            var plans = ContextMetadataSync.PlanSubtree(_go.transform).ToList();

            Assert.AreEqual(1, plans.Count);
            Assert.AreSame(_node, plans[0].Node);
        }

        [Test]
        public void HasDerivedEntries_SeesOnlyInstalledNamespaces()
        {
            Install(("plcPath", "MAIN.P_Reader"));
            Assert.IsFalse(ContextMetadataSync.HasDerivedEntries(_go.transform));

            _node.Set("Function", "Reads the DMC.");
            Assert.IsFalse(ContextMetadataSync.HasDerivedEntries(_go.transform), "authored entries are not metadata");

            _node.Set("kuka.frame", "R1.Base");
            Assert.IsFalse(ContextMetadataSync.HasDerivedEntries(_go.transform), "an orphan is nobody's metadata");

            ContextMetadataSync.Apply(_node);
            Assert.IsTrue(ContextMetadataSync.HasDerivedEntries(_go.transform));
        }
    }
}
