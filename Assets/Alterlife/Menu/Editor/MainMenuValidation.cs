using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Alterlife.Menu.Editor
{
    /// <summary>Batch Play Mode smoke checks and actual Unity renders; no headset emulation claims.</summary>
    public static class MainMenuValidation
    {
        const string Pending = "Alterlife.Menu.ValidationPending";
        static double started;
        static bool tested;
        static IEnumerator checks;
        static int lastFrame;

        [InitializeOnLoadMethod]
        static void Resume()
        {
            if (!SessionState.GetBool(Pending, false)) return;
            EditorApplication.playModeStateChanged += PlayStateChanged;
            if (EditorApplication.isPlaying) BeginChecks();
        }

        [MenuItem("Alterlife/Validate Main Menu")]
        public static void Run()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(MainMenuBuilder.ScenePath);
            SessionState.SetBool(Pending, true);
            EditorApplication.playModeStateChanged -= PlayStateChanged;
            EditorApplication.playModeStateChanged += PlayStateChanged;
            EditorApplication.EnterPlaymode();
        }

        static void PlayStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode) BeginChecks();
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Pending, false))
            {
                SessionState.SetBool(Pending, false);
                if (Application.isBatchMode) EditorApplication.Exit(SessionState.GetInt("Alterlife.Menu.ValidationResult", 1));
            }
        }

        static void BeginChecks()
        {
            started = EditorApplication.timeSinceStartup;
            tested = false;
            checks = null;
            lastFrame = -1;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (tested || !EditorApplication.isPlaying || EditorApplication.timeSinceStartup - started < 3) return;
            if (Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            try
            {
                if (checks == null) checks = Check();
                if (checks.MoveNext()) return;
                Debug.Log("ALTERLIFE_MENU_VALIDATION_PASSED: Play Mode flow, pointer raycasts, drag, references and previews.");
                SessionState.SetInt("Alterlife.Menu.ValidationResult", 0);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                SessionState.SetInt("Alterlife.Menu.ValidationResult", 1);
            }
            tested = true;
            EditorApplication.update -= Tick;
            EditorApplication.ExitPlaymode();
        }

        static IEnumerator Check()
        {
            var menu = UnityEngine.Object.FindAnyObjectByType<MainMenuController>();
            Require(menu != null, "Main menu is present");
            var canvas = menu.GetComponent<Canvas>();
            var globe = menu.GetComponentInChildren<GlobeSelector>(true);
            Require(UnityEngine.Object.FindObjectsByType<EventSystem>().Length == 1, "Exactly one EventSystem");
            Require(EventSystem.current.GetComponent<XRUIInputModule>().enableXRInput, "XR UI input enabled");
            Require(canvas.GetComponent<TrackedDeviceGraphicRaycaster>() != null, "Tracked device raycaster exists");
            Require(UnityEngine.Object.FindObjectsByType<NearFarInteractor>(FindObjectsInactive.Include).Count(x => x.enableUIInteraction) >= 2,
                "Both controller rays support UI");
            foreach (var image in menu.GetComponentsInChildren<UnityEngine.UI.RawImage>(true))
                Require(image.texture != null, "Texture assigned: " + image.name);
            foreach (var button in menu.GetComponentsInChildren<UnityEngine.UI.Button>(true))
                Require(button.onClick.GetPersistentEventCount() > 0 && button.onClick.GetPersistentTarget(0) != null,
                    "Persistent callback wired: " + button.name);

            var previewObject = new GameObject("Validation Camera", typeof(Camera));
            foreach (var existingCamera in UnityEngine.Object.FindObjectsByType<Camera>())
                if (existingCamera.gameObject != previewObject) existingCamera.enabled = false;
            var camera = previewObject.GetComponent<Camera>();
            camera.transform.position = new Vector3(0, canvas.transform.position.y, 0);
            camera.orthographic = true;
            camera.orthographicSize = 1024 * .0016f / 2;
            camera.aspect = 1440f / 1024;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.cullingMask = ~0;
            camera.nearClipPlane = .01f;
            canvas.worldCamera = camera;
            var target = new RenderTexture(1440, 1024, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = target;
            Canvas.ForceUpdateCanvases();
            yield return null;

            Require(!menu.IsChoosingDifficulty && globe.FranceVisible, "Initial world state is Europe");
            Capture(camera, "World-Europe");
            Click(menu, camera, "RotateLeft");
            yield return null;
            Require(globe.Orientation == 0 && !globe.FranceVisible, "Left arrow shows America");
            menu.SelectFrance();
            Require(!menu.IsChoosingDifficulty, "Hidden France cannot be selected");
            Capture(camera, "World-America");
            Click(menu, camera, "RotateLeft");
            yield return null;
            Require(globe.Orientation == 2, "Left wraps to Asia");
            Capture(camera, "World-Asia");
            Click(menu, camera, "RotateRight");
            Require(globe.Orientation == 0, "Right wraps to America");

            var dragData = new PointerEventData(EventSystem.current);
            dragData.position = camera.WorldToScreenPoint(globe.transform.position);
            ExecuteEvents.Execute(globe.gameObject, dragData, ExecuteEvents.beginDragHandler);
            dragData.position += Vector2.left * 110;
            ExecuteEvents.Execute(globe.gameObject, dragData, ExecuteEvents.dragHandler);
            ExecuteEvents.Execute(globe.gameObject, dragData, ExecuteEvents.endDragHandler);
            Require(globe.Orientation == 1, "Dragging turns globe to Europe");
            var marker = FindButton(menu, "SelectFrance");
            ExecuteEvents.Execute(marker.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerEnterHandler);
            Require(menu.transform.Find("WorldSelection/FranceProgress").gameObject.activeSelf, "Country hover reveals progress");
            yield return null;
            Capture(camera, "World-FranceHover");
            Click(menu, camera, "SelectFrance");
            yield return null;
            Require(menu.IsChoosingDifficulty && menu.SelectedCountry == "France", "France opens difficulty selection");
            Capture(camera, "Difficulty");

            int callbacks = 0;
            menu.ScenarioSelected.AddListener(_ => callbacks++);
            string[] scenarios = { "Apartment", "Cafe", "Customs" };
            for (int i = 0; i < scenarios.Length; i++)
            {
                Click(menu, camera, scenarios[i]);
                yield return null;
                Require((int)menu.SelectedScenario == i + 1, "Scenario selection " + scenarios[i]);
            }
            Require(callbacks == 3, "One scenario event per selection");
            menu.SelectScenario(-1);
            menu.SelectScenario(3);
            Require(callbacks == 3, "Invalid difficulty ignored");
            Capture(camera, "Difficulty-Selected");
            Click(menu, camera, "BackToWorld");
            yield return null;
            Require(!menu.IsChoosingDifficulty && menu.SelectedScenario == FrenchScenario.None && menu.SelectedCountry == null,
                "Back resets selection");
            Click(menu, camera, "SelectFrance");
            Require(menu.IsChoosingDifficulty && menu.SelectedScenario == FrenchScenario.None, "Repeat navigation starts clean");
            menu.ShowWorld();
            camera.targetTexture = null;
            target.Release();
            UnityEngine.Object.Destroy(target);
            UnityEngine.Object.Destroy(previewObject);
        }

        static UnityEngine.UI.Button FindButton(MainMenuController menu, string name) =>
            menu.GetComponentsInChildren<UnityEngine.UI.Button>(true).Single(x => x.name == name);

        static void Click(MainMenuController menu, Camera camera, string name)
        {
            Canvas.ForceUpdateCanvases();
            var button = FindButton(menu, name);
            var data = new PointerEventData(EventSystem.current) { position = camera.WorldToScreenPoint(button.transform.position), button = PointerEventData.InputButton.Left };
            // Check actual UI hit testing, not only direct controller method calls.
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(data, hits);
            Require(hits.Count > 0 && hits[0].gameObject.GetComponentInParent<UnityEngine.UI.Button>() == button,
                "Pointer reaches " + name + "; hits: " + string.Join(", ", hits.Select(x => x.gameObject.name)));
            var tracked = new TrackedDeviceEventData(EventSystem.current)
            {
                rayPoints = new List<Vector3> { camera.transform.position,
                    camera.transform.position + (button.transform.position - camera.transform.position) * 2 },
                layerMask = ~0
            };
            var trackedHits = new List<RaycastResult>();
            menu.GetComponent<TrackedDeviceGraphicRaycaster>().Raycast(tracked, trackedHits);
            Require(trackedHits.Count > 0 && trackedHits[0].gameObject.GetComponentInParent<UnityEngine.UI.Button>() == button,
                "Tracked XR ray reaches " + name);
            ExecuteEvents.Execute(button.gameObject, data, ExecuteEvents.pointerClickHandler);
        }

        static void Capture(Camera camera, string name)
        {
            // Read the completed automatic render from the preceding Play Mode frame.
            // Rendering the same TMP canvas repeatedly within one editor tick gives stale batches.
            var previous = RenderTexture.active;
            RenderTexture.active = camera.targetTexture;
            var texture = new Texture2D(1440, 1024, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, 1440, 1024), 0, 0);
            texture.Apply();
            Directory.CreateDirectory("Artifacts/Menu");
            File.WriteAllBytes("Artifacts/Menu/" + name + ".png", texture.EncodeToPNG());
            RenderTexture.active = previous;
            UnityEngine.Object.Destroy(texture);
        }

        static void Require(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException("Menu validation failed: " + description);
        }
    }
}
