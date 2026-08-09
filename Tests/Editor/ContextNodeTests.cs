using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace PILAR.Context.Editor.Tests
{
    public class ContextNodeTests
    {
        private GameObject _go;
        private ContextNode _node;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("ContextNodeTests");
            _node = _go.AddComponent<ContextNode>();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_go);
        }

        [Test]
        public void Entries_IsEmpty_OnFreshComponent()
        {
            Assert.IsNotNull(_node.Entries);
            Assert.AreEqual(0, _node.Entries.Count);
        }

        [Test]
        public void Add_StoresKeyAndValue()
        {
            _node.Add("Function", "Reads the RFID tag.");

            Assert.IsTrue(_node.ContainsKey("Function"));
            Assert.IsTrue(_node.TryGetValue("Function", out var value));
            Assert.AreEqual("Reads the RFID tag.", value);
        }

        [Test]
        public void Add_Throws_OnDuplicateKey()
        {
            _node.Add("Function", "first");

            Assert.Throws<ArgumentException>(() => _node.Add("Function", "second"));
            Assert.AreEqual(1, _node.Entries.Count, "the failed Add must not have mutated the list");
        }

        [Test]
        public void Add_Throws_OnNullKey()
        {
            Assert.Throws<ArgumentException>(() => _node.Add(null, "value"));
        }

        [Test]
        public void Add_Throws_OnEmptyKey()
        {
            Assert.Throws<ArgumentException>(() => _node.Add(string.Empty, "value"));
        }

        [Test]
        public void Add_PreservesInsertionOrder()
        {
            _node.Add("a", "1");
            _node.Add("b", "2");
            _node.Add("c", "3");

            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, _node.Entries.Select(e => e.key).ToArray());
        }

        [Test]
        public void Set_InsertsWhenKeyIsAbsent()
        {
            _node.Set("Function", "value");

            Assert.AreEqual(1, _node.Entries.Count);
            Assert.IsTrue(_node.TryGetValue("Function", out var value));
            Assert.AreEqual("value", value);
        }

        [Test]
        public void Set_UpdatesInPlace_PreservingIndex()
        {
            _node.Add("a", "1");
            _node.Add("b", "2");
            _node.Add("c", "3");

            _node.Set("a", "updated");

            // Export ordering is user-visible, so an upsert must not move the entry to the end.
            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, _node.Entries.Select(e => e.key).ToArray());
            Assert.AreEqual(3, _node.Entries.Count);
            Assert.AreEqual("updated", _node.Entries[0].value);
        }

        [Test]
        public void Set_Throws_OnNullOrEmptyKey()
        {
            Assert.Throws<ArgumentException>(() => _node.Set(null, "value"));
            Assert.Throws<ArgumentException>(() => _node.Set(string.Empty, "value"));
        }

        [Test]
        public void Remove_ReturnsFalse_WhenKeyIsAbsent()
        {
            Assert.IsFalse(_node.Remove("nope"));
        }

        [Test]
        public void Remove_ReturnsTrue_AndDropsTheEntry()
        {
            _node.Add("a", "1");
            _node.Add("b", "2");

            Assert.IsTrue(_node.Remove("a"));
            Assert.IsFalse(_node.ContainsKey("a"));
            CollectionAssert.AreEqual(new[] { "b" }, _node.Entries.Select(e => e.key).ToArray());
        }

        [Test]
        public void TryGetValue_YieldsNullNotEmpty_WhenKeyIsAbsent()
        {
            Assert.IsFalse(_node.TryGetValue("nope", out var value));
            Assert.IsNull(value, "absent keys report null so callers can distinguish them from an empty value");
        }

        [Test]
        public void TryGetValue_DistinguishesStoredEmptyValue_FromAbsentKey()
        {
            _node.Add("present", string.Empty);

            Assert.IsTrue(_node.TryGetValue("present", out var stored));
            Assert.AreEqual(string.Empty, stored);
        }

        [Test]
        public void Clear_RemovesEverything()
        {
            _node.Add("a", "1");
            _node.Add("b", "2");

            _node.Clear();

            Assert.AreEqual(0, _node.Entries.Count);
            Assert.IsFalse(_node.ContainsKey("a"));
        }

        [Test]
        public void MixedOperations_KeepOrderStable()
        {
            _node.Add("a", "1");
            _node.Add("b", "2");
            _node.Add("c", "3");
            _node.Remove("b");
            _node.Set("d", "4");
            _node.Set("a", "1-updated");

            CollectionAssert.AreEqual(new[] { "a", "c", "d" }, _node.Entries.Select(e => e.key).ToArray());
            CollectionAssert.AreEqual(new[] { "1-updated", "3", "4" }, _node.Entries.Select(e => e.value).ToArray());
        }

        [Test]
        public void Entries_ExposesTheLiveList_DocumentingCurrentBehaviour()
        {
            _node.Add("a", "1");

            // Entries is documented as a read-only view but hands back the backing List, and
            // ContextEntry is a mutable class besides. This test pins today's behaviour so that
            // hardening it later is a deliberate, visible change rather than an accident.
            Assert.IsInstanceOf<List<ContextEntry>>(_node.Entries);
            _node.Entries[0].value = "mutated";
            Assert.IsTrue(_node.TryGetValue("a", out var value));
            Assert.AreEqual("mutated", value);
        }
    }
}
