using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PILAR.Context
{
    [Serializable]
    public class ContextEntry
    {
        public string key;
        public string value;
    }

    // Groups the component under the same PILAR/Context root as the menu item. Discovery only -
    // the class name and script GUID are unchanged, so serialized references keep resolving.
    [AddComponentMenu("PILAR/Context/Context Node")]
    public class ContextNode : MonoBehaviour
    {
        public IReadOnlyList<ContextEntry> Entries => _entries;

        [SerializeField]
        private List<ContextEntry> _entries = new();

        public bool ContainsKey(string key)
        {
            return _entries.Any(entry => entry.key == key);
        }

        public bool TryGetValue(string key, out string value)
        {
            var entry = _entries.FirstOrDefault(e => e.key == key);
            value = entry?.value;
            return entry != null;
        }

        public void Add(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("Key must not be null or empty.", nameof(key));
            if (ContainsKey(key)) throw new ArgumentException($"Key '{key}' already exists.", nameof(key));

            _entries.Add(new ContextEntry { key = key, value = value });
        }

        public void Set(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("Key must not be null or empty.", nameof(key));

            var entry = _entries.FirstOrDefault(e => e.key == key);
            if (entry != null)
            {
                entry.value = value;
            }
            else
            {
                _entries.Add(new ContextEntry { key = key, value = value });
            }
        }

        public bool Remove(string key)
        {
            var entry = _entries.FirstOrDefault(e => e.key == key);
            if (entry == null) return false;

            _entries.Remove(entry);
            return true;
        }

        public void Clear()
        {
            _entries.Clear();
        }
    }
}
