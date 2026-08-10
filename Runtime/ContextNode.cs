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

        /// <summary>
        /// Name this node contributes to the topology path. Empty means the GameObject's own name,
        /// which is the usual case - set it only when the logical name differs from the scene name.
        /// </summary>
        public string TopologySegment
        {
            get => _topologySegment;
            set => _topologySegment = value;
        }

        /// <summary>
        /// Node this one hangs under in the topology, when that is not the nearest ancestor carrying
        /// a ContextNode. Lets the logical machine structure diverge from transform parenting, which
        /// is arranged for the scene rather than for meaning.
        /// </summary>
        public ContextNode TopologyParent
        {
            get => _topologyParent;
            set => _topologyParent = value;
        }

        /// <summary>The segment actually used in the path: the override when set, the name otherwise.</summary>
        public string ResolvedSegment =>
            string.IsNullOrWhiteSpace(_topologySegment) ? gameObject.name : _topologySegment.Trim();

        [SerializeField]
        private List<ContextEntry> _entries = new();

        [SerializeField]
        private string _topologySegment;

        [SerializeField]
        private ContextNode _topologyParent;

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
