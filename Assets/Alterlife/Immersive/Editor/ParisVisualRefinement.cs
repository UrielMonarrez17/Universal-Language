using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Alterlife.Immersive.Editor
{
    public static class ParisVisualRefinement
    {
        [MenuItem("Alterlife/Restaurant/Refine terrace composition")]
        public static void Run()
        {
            var scene = EditorSceneManager.OpenScene(ParisPrototypeBuilder.ScenePath);
            var root = GameObject.Find("Paris Restaurant Prototype").transform;
            root.Find("Eiffel Tower").position = new Vector3(7, -16, 53);
            for (int i = 0; i < 6; i++)
            {
                var building = root.Find("Paris city building " + i);
                building.position = new Vector3(i < 3 ? -28 - i * 10 : 28 + (i - 3) * 10, -6, 43 + (i % 2) * 12);
                building.rotation = Quaternion.Euler(0, 180, 0);
            }
            root.Find("Paris waiter").rotation = Quaternion.Euler(0, -40, 0);
            var menu = root.Find("Physical French menu");
            menu.SetPositionAndRotation(new Vector3(-.24f, .85f, .12f), Quaternion.identity);
            menu.GetComponent<Rigidbody>().angularDamping = 2;
            root.Find("Conversation journal").localScale = Vector3.one * .72f;
            root.Find("Conversation journal").position = new Vector3(-1.25f, 1.05f, .12f);
            root.Find("Journal stand").position = new Vector3(-1.25f, .32f, .16f);
            root.Find("Restart restaurant").position = new Vector3(-1.25f, .58f, .12f);
            root.Find("Restart restaurant").localScale = new Vector3(.8f, .12f, .04f);
            foreach (var top in root.GetComponentsInChildren<Transform>().Where(t => t.name == "Round wooden tabletop"))
            {
                var capsule = top.GetComponent<CapsuleCollider>();
                if (capsule) Object.DestroyImmediate(capsule);
                if (!top.GetComponent<MeshCollider>()) top.gameObject.AddComponent<MeshCollider>().sharedMesh = top.GetComponent<MeshFilter>().sharedMesh;
            }
            var fabricPath = "Assets/Alterlife/Immersive/Paris/Terracotta upholstery.mat";
            var fabric = AssetDatabase.LoadAssetAtPath<Material>(fabricPath);
            if (!fabric)
            {
                fabric = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(.46f, .22f, .12f) };
                fabric.SetFloat("_Smoothness", .08f);
                AssetDatabase.CreateAsset(fabric, fabricPath);
            }
            foreach (var chair in root.Cast<Transform>().Where(t => t.name == "LoungeChair"))
                foreach (var r in chair.GetComponentsInChildren<Renderer>())
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var c = mats[i].color;
                        if (c.b > c.r * 1.2f && c.b > c.g * 1.2f || mats[i].name.Contains("GradientBlue")) mats[i] = fabric;
                    }
                    r.sharedMaterials = mats;
                }
            EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            Debug.Log("ALTERLIFE_PARIS_REFINED");
        }
    }
}
