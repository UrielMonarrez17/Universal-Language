using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Alterlife.Immersive.Editor
{
    public static class MenuSceneSplit
    {
        public static void Run()
        {
            if (File.Exists(SceneJourney.BookScene)) throw new InvalidOperationException("Book scene already exists; edit it instead of splitting again.");
            var scene = EditorSceneManager.OpenScene(SceneJourney.WorldScene);
            EditorSceneManager.SaveScene(scene, SceneJourney.BookScene, true);
            scene = EditorSceneManager.OpenScene(SceneJourney.BookScene);
            var environment = GameObject.Find("Architecture and furniture").transform;
            var comfort = environment.Find("Comfort station");
            if (comfort) comfort.position = new Vector3(-2.3f, 1.6f, 7.1f);
            foreach (var t in environment.Cast<Transform>().ToArray())
                if (t.position.z < 6 || t.name == "Sliding study door") UnityEngine.Object.DestroyImmediate(t.gameObject);
            DestroyNamed("Study access"); DestroyNamed("Observatory light");
            var floor = GameObject.Find("Study floor").GetComponent<ClearTeleportArea>(); floor.enabled = true;
            var rig = UnityEngine.Object.FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
            rig.transform.SetPositionAndRotation(new Vector3(0, .02f, 7.8f), Quaternion.identity);
            var journey = GameObject.Find("Learning systems").AddComponent<SceneJourney>(); journey.bookRoom = true;
            Wall(environment, new Vector3(0, 1.7f, 6), "Reading room entrance wall", "Warm plaster");
            var back = GameObject.CreatePrimitive(PrimitiveType.Cube); back.name = "Return to world selection";
            back.transform.SetParent(environment); back.transform.position = new Vector3(2.6f, 1.1f, 9.0f);
            back.transform.localScale = new Vector3(.95f, .2f, .07f);
            back.GetComponent<Renderer>().sharedMaterial = Material("Sage green");
            var button = back.AddComponent<PhysicalControl>(); button.feedbackRenderer = back.GetComponent<Renderer>();
            var interaction = back.GetComponent<XRSimpleInteractable>(); interaction.colliders.Clear(); interaction.colliders.Add(back.GetComponent<Collider>());
            UnityEventTools.AddPersistentListener(button.pressed, journey.OpenWorld);
            var caption = new GameObject("Return caption", typeof(TextMeshPro)).GetComponent<TextMeshPro>();
            caption.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/VRTemplateAssets/Fonts/Inter/Inter-Regular SDF.asset");
            caption.text = "WORLD SELECTION"; caption.fontSize = .36f; caption.alignment = TextAlignmentOptions.Center;
            caption.rectTransform.sizeDelta = new Vector2(.9f, .18f); caption.transform.position = back.transform.position + Vector3.back * .04f;
            caption.transform.SetParent(back.transform, true);
            EditorSceneManager.SaveScene(scene);

            scene = EditorSceneManager.OpenScene(SceneJourney.WorldScene);
            environment = GameObject.Find("Architecture and furniture").transform;
            foreach (var t in environment.Cast<Transform>().ToArray())
                if (t.position.z > 6 || t.name == "Sliding study door" || t.name == "Door sign") UnityEngine.Object.DestroyImmediate(t.gameObject);
            DestroyNamed("Experience navigation"); DestroyNamed("Study access"); DestroyNamed("Study warm light"); DestroyNamed("Book arrival point");
            Wall(environment, new Vector3(0, 1.7f, 6), "Observatory rear wall", "Midnight blue");
            journey = GameObject.Find("Learning systems").AddComponent<SceneJourney>();
            var france = UnityEngine.Object.FindAnyObjectByType<FranceSelection>();
            france.sceneJourney = journey; france.door = null; france.bookArrival = null;
            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = EditorBuildSettings.scenes.Where(s => s.path != SceneJourney.BookScene)
                .Concat(new[] { new EditorBuildSettingsScene(SceneJourney.BookScene, true) }).ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("ALTERLIFE_MENU_SCENES_SPLIT");
        }
        public static void RefineAndValidate()
        {
            var scene = EditorSceneManager.OpenScene(SceneJourney.BookScene);
            GameObject.Find("Return to world selection").transform.position = new Vector3(2.6f, 1.1f, 9.0f);
            EditorSceneManager.SaveScene(scene);
            ParisPrototypeValidation.Run();
        }
        static void DestroyNamed(string name) { var go = GameObject.Find(name); if (go) UnityEngine.Object.DestroyImmediate(go); }
        static Material Material(string name) => AssetDatabase.LoadAssetAtPath<Material>("Assets/Alterlife/Immersive/Generated/" + name + ".mat");
        static void Wall(Transform parent, Vector3 position, string name, string mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(parent);
            go.transform.position = position; go.transform.localScale = new Vector3(8, 3.4f, .2f); go.GetComponent<Renderer>().sharedMaterial = Material(mat);
        }
    }
}
