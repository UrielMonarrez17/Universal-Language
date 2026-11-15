using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.Linq;
using TMPro;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;

namespace Alterlife.Immersive.Editor
{
    public static class ImmersiveHubRefinement
    {
        public static void ApplyAndValidate() { Apply(); ImmersiveHubValidation.Run(); }
        [MenuItem("Alterlife/Immersive/Apply Precision Ray Configuration")]
        public static void Apply()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene(ImmersiveHubBuilder.ScenePath);
            foreach (var caster in Object.FindObjectsByType<CurveInteractionCaster>(FindObjectsInactive.Include))
            {
                caster.hitDetectionType = CurveInteractionCaster.HitDetectionType.Raycast;
                PrefabUtility.RecordPrefabInstancePropertyModifications(caster);
            }
            Polish();
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        static Material Material(string name) => AssetDatabase.LoadAssetAtPath<Material>("Assets/Alterlife/Immersive/Generated/" + name + ".mat");
        static void Polish()
        {
            Object.FindAnyObjectByType<AnchoredGlobe>().transform.localRotation = Quaternion.Euler(-36, 0, 0);
            var book = Object.FindAnyObjectByType<InteractiveBook>();
            book.leftPageBlock = book.transform.Find("Left page block").gameObject;
            var backCover = book.transform.Find("Back cover");
            backCover.localPosition = new Vector3(.35f, 0, 0);
            backCover.localScale = new Vector3(.71f, .035f, .88f);
            foreach (var layer in book.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Visible paper layers"))
            {
                layer.localPosition = new Vector3(.35f, layer.localPosition.y, layer.localPosition.z);
                layer.localScale = new Vector3(.66f, .002f, .002f);
            }
            foreach (var chapter in book.GetComponentsInChildren<BookChapter>(true))
            {
                var stars = chapter.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Difficulty star").ToArray();
                for (int i = 0; i < stars.Length; i++) stars[i].localPosition = new Vector3(-.47f + i * .08f, .069f, -.31f);
                chapter.selectionControlLabel = chapter.GetComponentsInChildren<PhysicalControl>(true)
                    .First(c => c.name == "Select " + chapter.definition.id).GetComponentInChildren<TMP_Text>(true);
                if (chapter.selectionStamp && chapter.selectionStamp.GetComponent<TMP_Text>())
                {
                    Object.DestroyImmediate(chapter.selectionStamp);
                    chapter.selectionStamp = null;
                }
                if (chapter.selectionStamp)
                {
                    continue;
                }
                var stamp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                stamp.name = "Selected chapter stamp";
                stamp.transform.SetParent(chapter.transform, false);
                stamp.transform.localPosition = new Vector3(.61f, .075f, -.23f);
                stamp.transform.localScale = new Vector3(.035f, .006f, .035f);
                stamp.GetComponent<Renderer>().sharedMaterial = Material("Antique brass");
                Object.DestroyImmediate(stamp.GetComponent<Collider>());
                chapter.selectionStamp = stamp; stamp.SetActive(false);
            }
            var instructions = GameObject.Find("Book instructions").transform;
            instructions.position = new Vector3(3.7f, 1.9f, 8.6f);
            instructions.rotation = Quaternion.Euler(0, 90, 0);
            var instructionsPlaque = GameObject.Find("Book instructions physical plaque");
            if (instructionsPlaque)
            {
                instructionsPlaque.transform.position = instructions.position + instructions.forward * .045f;
                instructionsPlaque.transform.rotation = instructions.rotation;
            }
            var rig = Object.FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
            var body = rig.GetComponent<CharacterController>();
            body.radius = .22f; body.stepOffset = .2f;
            PrefabUtility.RecordPrefabInstancePropertyModifications(body);
            var desk = GameObject.Find("Desk1");
            var bounds = Bounds(desk);
            Debug.Log("Desk before refinement: " + bounds.size);
            desk.transform.localScale = Vector3.Scale(desk.transform.localScale, new Vector3(2.35f / bounds.size.x, .9f / bounds.size.y, 1.15f / bounds.size.z));
            var window = GameObject.Find("Window");
            Debug.Log("Window bounds: " + Bounds(window).size);
            if (Bounds(window).size.z > Bounds(window).size.x) window.transform.rotation = Quaternion.Euler(0, 90, 0);
            var chair = GameObject.Find("OfficeChair");
            Debug.Log("Chair bounds: " + Bounds(chair).size);
            foreach (var id in AssetDatabase.FindAssets("t:Material", new[] { "Assets/Alterlife/Immersive/Generated" }))
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(id));
                if (!mat.name.StartsWith("Office ")) continue;
                Color color;
                if (mat.name.Contains("DarkGreen") || mat.name.Contains("DarkRed")) color = new Color32(43, 78, 64, 255);
                else if (mat.name.Contains("Green") || mat.name.Contains("Red")) color = new Color32(79, 119, 84, 255);
                else if (mat.name.Contains("Blue")) color = new Color32(42, 68, 83, 255);
                else if (mat.name.Contains("DarkBrown")) color = new Color32(76, 47, 30, 255);
                else if (mat.name.Contains("Brown") || mat.name.Contains("Orange")) color = new Color32(145, 97, 55, 255);
                else continue;
                mat.mainTexture = null; mat.color = color;
                EditorUtility.SetDirty(mat);
            }
            var sun = GameObject.Find("Soft skylight").GetComponent<Light>();
            sun.shadows = LightShadows.Hard; sun.shadowStrength = .55f; sun.shadowBias = .03f;
            foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include))
            {
                if (renderer.GetComponent<MeshFilter>() && renderer.transform.parent &&
                    (renderer.GetComponentInParent<BookChapter>() || renderer.name.Contains("desk") || renderer.name.Contains("chair") || renderer.name.Contains("Chair")))
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                else renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
            const string pipelinePath = "Assets/Alterlife/Immersive/Generated/Hub Quest Pipeline.asset";
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
            if (!pipeline)
            {
                pipeline = Object.Instantiate(AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/Project Configuration/Performance URP Config.asset"));
                AssetDatabase.CreateAsset(pipeline, pipelinePath);
            }
            pipeline.mainLightShadowmapResolution = 1024;
            pipeline.shadowDistance = 12;
            pipeline.shadowCascadeCount = 1;
            var pipelineSettings = new SerializedObject(pipeline);
            pipelineSettings.FindProperty("m_AdditionalLightsRenderingMode").intValue = (int)LightRenderingMode.PerVertex;
            pipelineSettings.ApplyModifiedPropertiesWithoutUndo();
            pipeline.msaaSampleCount = 4;
            EditorUtility.SetDirty(pipeline);
            var rendering = Object.FindAnyObjectByType<HubRendering>();
            if (!rendering) rendering = GameObject.Find("Learning systems").AddComponent<HubRendering>();
            rendering.pipeline = pipeline;
            foreach (var t in Object.FindObjectsByType<Transform>())
                if ((t.name == "Brass plaque frame" || t.name == "Lectern stem" || t.name == "Lectern foot") && !t.GetComponent<Collider>())
                    t.gameObject.AddComponent<BoxCollider>();
            if (GameObject.Find("Study molding")) return; // Safe to repeat precision/refinement without duplicating decor.
            var details = new GameObject("Study molding").transform;
            foreach (float x in new[] { -3.86f, 3.86f })
            {
                Box("Ceiling crown molding", details, new Vector3(x, 3.28f, 9.5f), new Vector3(.1f, .12f, 7), Material("Ivory paper"));
                for (int z = 7; z <= 12; z++)
                    Box("Wainscot vertical molding", details, new Vector3(x, .48f, z), new Vector3(.08f, .85f, .035f), Material("Antique brass"));
            }
            Box("Back crown molding", details, new Vector3(0, 3.28f, 12.83f), new Vector3(8, .12f, .1f), Material("Ivory paper"));
            // Give the freestanding instructions physical lecterns, never camera-following UI.
            Plaque(GameObject.Find("Globe instructions").transform, new Vector2(1.55f, .7f), true);
            Plaque(GameObject.Find("Comfort instructions").transform, new Vector2(2, .8f), true);
            Plaque(GameObject.Find("Controls").transform, new Vector2(2, 1), true);
            Plaque(GameObject.Find("Selected chapter summary").transform, new Vector2(1.7f, .85f), true);
            Plaque(GameObject.Find("Book instructions").transform, new Vector2(1.55f, .85f), true);
            // Framed window and practical shelving books use the imported furniture mesh assets.
            for (int x = -1; x <= 1; x += 2)
                Box("Window side trim", details, new Vector3(x * 1.05f, 2.1f, 12.75f), new Vector3(.08f, 1.4f, .09f), Material("Walnut"));
            foreach (float y in new[] { 1.4f, 2.8f })
                Box("Window horizontal trim", details, new Vector3(0, y, 12.75f), new Vector3(2.18f, .08f, .09f), Material("Walnut"));
            Box("Window center frame", details, new Vector3(0, 2.1f, 12.74f), new Vector3(.06f, 1.4f, .08f), Material("Walnut"));
            foreach (float x in new[] { -2.7f, 2.7f })
                for (int i = 0; i < 4; i++)
                {
                    var pile = Object.Instantiate(GameObject.Find("BookPile1"), details);
                    pile.name = "Shelf book collection";
                    pile.transform.position = new Vector3(x + (i % 2 == 0 ? -.22f : .22f), .55f + (i / 2) * .66f, 12.25f);
                    pile.transform.localScale = Vector3.one * .75f;
                    foreach (var c in pile.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                }
            // Small contact patches provide depth under the globe without expensive extra shadow lights.
            var contact = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            contact.name = "Pedestal contact shadow"; contact.transform.SetParent(details);
            contact.transform.position = new Vector3(0, .003f, 2); contact.transform.localScale = new Vector3(1.05f, .003f, 1.05f);
            contact.GetComponent<Renderer>().sharedMaterial = Material("Ink"); Object.DestroyImmediate(contact.GetComponent<Collider>());
        }
        static Bounds Bounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }
        static void Box(string name, Transform parent, Vector3 position, Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
            go.transform.SetParent(parent, false); go.transform.localPosition = position; go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = material;
            if (name != "Brass plaque frame" && name != "Lectern stem" && name != "Lectern foot") Object.DestroyImmediate(go.GetComponent<Collider>());
        }
        static void Plaque(Transform text, Vector2 size, bool stand)
        {
            var backing = new GameObject(text.name + " physical plaque").transform;
            backing.position = text.position + text.forward * .045f;
            backing.rotation = text.rotation;
            Box("Brass plaque frame", backing, Vector3.zero, new Vector3(size.x + .035f, size.y + .035f, .055f), Material("Antique brass"));
            Box("Engraved slate", backing, new Vector3(0, 0, -.031f), new Vector3(size.x, size.y, .02f), Material("Midnight blue"));
            if (stand)
            {
                float height = backing.position.y - size.y / 2;
                Box("Lectern stem", backing, new Vector3(0, -size.y / 2 - height / 2, .06f), new Vector3(.06f, height, .06f), Material("Walnut"));
                Box("Lectern foot", backing, new Vector3(0, -backing.position.y + .025f, .06f), new Vector3(.45f, .05f, .3f), Material("Walnut"));
            }
        }
    }
}
