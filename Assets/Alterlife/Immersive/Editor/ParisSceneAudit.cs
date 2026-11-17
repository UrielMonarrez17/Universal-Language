using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Alterlife.Immersive.Editor
{
    public static class ParisSceneAudit
    {
        public static void Run()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Paris.unity");
            var report = new StringBuilder();
            foreach (var go in scene.GetRootGameObjects())
            {
                var renderers = go.GetComponentsInChildren<Renderer>();
                report.AppendLine(go.name + " at " + go.transform.position + " rotation " + go.transform.eulerAngles);
                if (renderers.Length == 0) continue;
                var b = renderers[0].bounds;
                foreach (var r in renderers.Skip(1)) b.Encapsulate(r.bounds);
                report.AppendLine("bounds=" + b + " renderers=" + renderers.Length + " source=" + PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go));
                report.AppendLine("meshes=" + string.Join(";", go.GetComponentsInChildren<MeshFilter>().Take(8).Select(m => m.name + ":" + m.sharedMesh.vertexCount)));
            }
            Directory.CreateDirectory("Artifacts/Paris");
            File.WriteAllText("Artifacts/Paris/audit.txt", report.ToString());
            Debug.Log(report.ToString());
        }
    }
}
