using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PILAR.Context.Editor
{
    public static class ContextMenuItems
    {
        /// <summary>
        /// Root GameObject name Open Commissioning projects conventionally use. Tried before falling
        /// back to the scene's own roots, so OC scenes keep working with no selection.
        /// </summary>
        private const string ConventionalRoot = "Project";

        [MenuItem("PILAR Context/Export Machine Context (JSON)")]
        private static void ExportMachineContext()
        {
            var root = ResolveRoot();
            if (root == null) return;

            var json = ContextTreeFactory.BuildJson(root.transform);

            var sceneName = SceneManager.GetActiveScene().name.Replace(" ", "_");
            var directory = Path.Combine(Application.dataPath, "StreamingAssets");
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

            var path = Path.Combine(directory, $"{sceneName}_Context.json");
            File.WriteAllText(path, json);

            AssetDatabase.Refresh();
            Debug.Log($"PILAR Context: exported machine context of '{root.name}' to {path}");
        }

        /// <summary>
        /// An explicit selection wins, then the conventional "Project" root, then a scene that has
        /// exactly one root anyway. Reports rather than guessing when the scene has several roots.
        ///
        /// Everything here logs instead of opening a dialog: this menu item is also driven headlessly
        /// through the Unity CLI, where a modal would block the calling process.
        /// </summary>
        private static GameObject ResolveRoot()
        {
            if (Selection.gameObjects.Length == 1) return Selection.gameObjects[0];

            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                Debug.LogError("PILAR Context: no active scene to export.");
                return null;
            }

            var roots = scene.GetRootGameObjects();

            var conventional = roots.FirstOrDefault(go => go.name == ConventionalRoot);
            if (conventional != null) return conventional;

            if (roots.Length == 1) return roots[0];

            Debug.LogError(
                $"PILAR Context: could not decide what to export. The scene '{scene.name}' has " +
                $"{roots.Length} root GameObjects and none is named '{ConventionalRoot}'. " +
                "Select the root you want to export and run the menu item again. " +
                $"Roots: {string.Join(", ", roots.Select(go => go.name))}");
            return null;
        }
    }
}
