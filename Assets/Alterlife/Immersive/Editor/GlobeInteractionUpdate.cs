using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Alterlife.Immersive.Editor
{
    public static class GlobeInteractionUpdate
    {
        const string Request = "Artifacts/Immersive/globe-update.request";
        [InitializeOnLoadMethod]
        static void Resume()
        {
            if (File.Exists(Request)) EditorApplication.update += Tick;
        }
        static void Tick()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            EditorApplication.update -= Tick;
            try { Apply(); File.Delete(Request); }
            catch (Exception e) { File.WriteAllText("Artifacts/Immersive/globe-update-result.txt", e.ToString()); Debug.LogException(e); }
        }
        [MenuItem("Alterlife/Immersive/Update Globe Interaction")]
        public static void Apply()
        {
            var scene = SceneManager.GetSceneByPath(ImmersiveHubBuilder.ScenePath);
            bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(ImmersiveHubBuilder.ScenePath, OpenSceneMode.Additive);
            var globe = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<AnchoredGlobe>(true)).Single();
            var selection = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<FranceSelection>(true)).Single();
            if (!selection.marker)
            {
                var marker = new GameObject("France flag pin").transform;
                marker.SetParent(globe.transform, false);
                marker.localPosition = AnchoredGlobe.GeographicPoint(2.35f, 48.86f, .72f);
                marker.rotation = Quaternion.identity;
                selection.marker = marker;
                var collider = marker.gameObject.AddComponent<BoxCollider>();
                collider.size = new Vector3(.15f, .13f, .035f);
                selection.country.colliders.Add(collider);
                AddStripe(marker, -.044f, "Flag blue");
                AddStripe(marker, 0, "Warm white");
                AddStripe(marker, .044f, "Flag red");
                var label = new GameObject("France hover name", typeof(TextMeshPro));
                label.transform.SetParent(marker, false);
                label.transform.localPosition = new Vector3(0, -.105f, -.025f);
                var text = label.GetComponent<TextMeshPro>();
                text.font = selection.status.font;
                text.text = "France\nLanguage: French";
                text.fontSize = .25f;
                text.alignment = TextAlignmentOptions.Center;
                text.color = Color.white;
                text.rectTransform.sizeDelta = new Vector2(.36f, .1f);
                selection.markerTooltip = label;
                label.SetActive(false);
                var stem = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                stem.name = "France pin stem";
                stem.transform.SetParent(globe.transform, false);
                var direction = AnchoredGlobe.GeographicPoint(2.35f, 48.86f, 1);
                stem.transform.localPosition = direction * .65f;
                stem.transform.localRotation = Quaternion.FromToRotation(Vector3.up, direction);
                stem.transform.localScale = new Vector3(.012f, .055f, .012f);
                stem.GetComponent<Renderer>().sharedMaterial = Material("Antique brass");
                UnityEngine.Object.DestroyImmediate(stem.GetComponent<Collider>());
            }
            var instruction = globe.transform.parent.Find("Globe instructions").GetComponent<TMP_Text>();
            instruction.text = "GRAB & TURN\nHold grip and sweep left or right.\nPoint at the French flag to select France.\nOther destinations: Coming Soon";
            Quaternion original = globe.transform.rotation;
            globe.ApplyRotation(Quaternion.Euler(65, 0, 40), Quaternion.identity, original);
            if (Quaternion.Angle(original, globe.transform.rotation) > .01f) throw new Exception("Pitch/roll changed globe rotation.");
            globe.ApplyRotation(Quaternion.Euler(35, 60, 25), Quaternion.identity, original);
            if (Quaternion.Angle(Quaternion.AngleAxis(60, Vector3.up) * original, globe.transform.rotation) > .01f)
                throw new Exception("Horizontal globe rotation failed.");
            globe.transform.rotation = original;
            if (!selection.country.colliders.Contains(selection.marker.GetComponent<Collider>())) throw new Exception("Pin collider not wired to France.");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (opened) EditorSceneManager.CloseScene(scene, true);
            File.WriteAllText("Artifacts/Immersive/globe-update-result.txt", "PASS: horizontal-only rotation; pitch/roll rejected; France flag pin and hover label wired; scene saved.");
            Debug.Log("ALTERLIFE_GLOBE_UPDATE_PASSED");
        }
        static Material Material(string name) => AssetDatabase.LoadAssetAtPath<Material>("Assets/Alterlife/Immersive/Generated/" + name + ".mat");
        static void AddStripe(Transform parent, float x, string material)
        {
            var stripe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stripe.name = material;
            stripe.transform.SetParent(parent, false);
            stripe.transform.localPosition = new Vector3(x, 0, 0);
            stripe.transform.localScale = new Vector3(.044f, .09f, .012f);
            stripe.GetComponent<Renderer>().sharedMaterial = Material(material);
            UnityEngine.Object.DestroyImmediate(stripe.GetComponent<Collider>());
        }
    }
}
