using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PILAR.Context.Editor
{
    /// <summary>
    /// Shared filter used by both the ContextNode inspector and the export pipeline so the
    /// component list an author sees while writing context matches what actually gets exported.
    /// </summary>
    public static class ContextComponentFilter
    {
        public static IEnumerable<string> GetRelevantComponentNames(GameObject gameObject)
        {
            return gameObject.GetComponents<Component>()
                .Where(c => c != null)
                .Where(c => c is not Transform && c is not ContextNode && c is not Collider && c is not Renderer && c is not MeshFilter)
                .Select(c => c.GetType().Name);
        }
    }
}
