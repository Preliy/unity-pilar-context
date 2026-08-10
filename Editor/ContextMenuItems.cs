using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
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

        // PILAR is the root for the whole add-in family; each add-in owns a submenu under it.
        [MenuItem("PILAR/Context/Export Machine Context (JSON)")]
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

            WarnIfNeverSynced(root.transform);
        }

        [MenuItem("PILAR/Context/Sync Framework Metadata")]
        private static void SyncFrameworkMetadata()
        {
            var root = ResolveRoot();
            if (root == null) return;

            if (ContextMetadataRegistry.Providers.Count == 0)
            {
                Debug.LogWarning(
                    "PILAR Context: no metadata provider is installed, so there is nothing to sync. " +
                    "Install a twin framework integration such as com.open-commissioning.core.");
                return;
            }

            var changed = 0;
            foreach (var node in root.GetComponentsInChildren<ContextNode>(true))
            {
                Undo.RecordObject(node, "Sync context metadata");
                if (!ContextMetadataSync.Apply(node)) continue;
                EditorUtility.SetDirty(node);
                changed++;
            }

            if (changed > 0) EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            Debug.Log(
                $"PILAR Context: synced framework metadata under '{root.name}' — " +
                $"{changed} node(s) updated.");
        }

        /// <summary>
        /// An export used to query providers itself, so metadata simply appeared. It now writes down
        /// what the nodes hold, which means a scene nobody synced exports none — and looks complete
        /// while doing it. Say so rather than letting that ship silently.
        /// </summary>
        private static void WarnIfNeverSynced(Transform root)
        {
            if (ContextMetadataRegistry.Providers.Count == 0) return;
            if (ContextMetadataSync.HasDerivedEntries(root)) return;

            Debug.LogWarning(
                $"PILAR Context: {ContextMetadataRegistry.Providers.Count} metadata provider(s) are " +
                "installed but no node under the export root carries any framework metadata. " +
                "Run PILAR ▸ Context ▸ Sync Framework Metadata and export again.");
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
