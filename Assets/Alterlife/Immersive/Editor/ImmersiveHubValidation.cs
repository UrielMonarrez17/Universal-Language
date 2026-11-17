using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

namespace Alterlife.Immersive.Editor
{
    /// <summary>Real Editor Play Mode checks; deliberately does not claim hardware tracking/Quest performance validation.</summary>
    public static class ImmersiveHubValidation
    {
        const string Pending = "Alterlife.Immersive.Validation";
        static IEnumerator checks;
        static int lastFrame;
        static double started;
        static bool finished;
        static Camera preview;
        static RenderTexture target;

        [InitializeOnLoadMethod]
        static void Resume()
        {
            if (!SessionState.GetBool(Pending, false)) return;
            EditorApplication.playModeStateChanged += StateChanged;
            if (EditorApplication.isPlaying) Begin();
        }
        [MenuItem("Alterlife/Immersive/Validate Hub")]
        public static void Run()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ImmersiveHubBuilder.ScenePath);
            SessionState.SetBool(Pending, true);
            EditorApplication.playModeStateChanged -= StateChanged;
            EditorApplication.playModeStateChanged += StateChanged;
            EditorApplication.EnterPlaymode();
        }
        static void StateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode) Begin();
            if (state != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(Pending, false)) return;
            SessionState.SetBool(Pending, false);
            if (Application.isBatchMode) EditorApplication.Exit(SessionState.GetInt(Pending + ".Result", 1));
        }
        static void Begin()
        {
            started = EditorApplication.timeSinceStartup; lastFrame = -1; checks = null; finished = false;
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
        }
        static void Tick()
        {
            if (finished || !EditorApplication.isPlaying || EditorApplication.timeSinceStartup - started < 3 || lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;
            try
            {
                if (EditorApplication.timeSinceStartup - started > 120) throw new Exception("Validation exceeded 120 seconds.");
                if (checks == null) checks = Check();
                if (checks.MoveNext()) return;
                Debug.Log("ALTERLIFE_IMMERSIVE_VALIDATION_PASSED");
                SessionState.SetInt(Pending + ".Result", 0);
            }
            catch (Exception ex) { Debug.LogException(ex); SessionState.SetInt(Pending + ".Result", 1); }
            finished = true; EditorApplication.update -= Tick;
            if (target) target.Release();
            EditorApplication.ExitPlaymode();
        }
        static void Require(bool value, string message)
        {
            if (!value) throw new Exception("FAILED: " + message);
            Debug.Log("PASS: " + message);
        }
        static IEnumerator Check()
        {
            var session = UnityEngine.Object.FindAnyObjectByType<LearningSession>();
            session.PersistenceEnabled = false;
            session.ResetInMemory();
            Require(UnityEngine.Object.FindObjectsByType<Unity.XR.CoreUtils.XROrigin>().Length == 1, "One XR Origin");
            var controllers = UnityEngine.Object.FindObjectsByType<ControllerInputActionManager>(FindObjectsInactive.Include);
            Require(controllers.Length == 2 && controllers.All(x => !x.smoothMotionEnabled && !x.smoothTurnEnabled), "Two controllers, teleport and snap turn defaults");
            Require(UnityEngine.Object.FindObjectsByType<NearFarInteractor>(FindObjectsInactive.Include).Length == 2, "Both controllers have near and far interaction");
            Require(UnityEngine.Object.FindAnyObjectByType<TeleportationProvider>().enabled, "Teleport provider enabled");
            Require(UnityEngine.Object.FindObjectsByType<Canvas>().Length == 0, "No flat Canvas menu in immersive hub");
            var study = GameObject.Find("Study floor").GetComponent<ClearTeleportArea>();
            var floor = GameObject.Find("Observatory floor").GetComponent<ClearTeleportArea>();
            Require(!study.enabled, "Study teleportation initially locked");
            Require(floor.IsClear(new Vector3(1.6f, 0, 2)), "Open floor accepts teleportation");
            Require(!floor.IsClear(new Vector3(0, 0, 2)), "Globe pedestal rejects teleportation");
            Require(!floor.IsClear(new Vector3(3.95f, 0, 2)), "Wall rejects teleportation");
            Require(!study.IsClear(new Vector3(0, 0, 9.2f)), "Desk rejects teleportation");
            var locomotion = UnityEngine.Object.FindAnyObjectByType<TeleportationProvider>();
            Require(locomotion.QueueTeleportRequest(new TeleportRequest { destinationPosition = new Vector3(1.6f, 0, 3.5f),
                destinationRotation = Quaternion.identity, matchOrientation = MatchOrientation.WorldSpaceUp }), "Teleport request accepted");
            float teleportDeadline = Time.time + 1;
            while (Time.time < teleportDeadline) yield return null;
            var origin = UnityEngine.Object.FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
            var head = origin.Camera.transform.position;
            Require(Vector2.Distance(new Vector2(head.x, head.z), new Vector2(1.6f, 3.5f)) < .05f, "Teleport provider actually moves the tracked origin to destination");
            foreach (var go in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
                RequireNoMissingScripts(go.gameObject);
            foreach (var renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include))
                if (!renderer.sharedMaterials.All(m => m && m.shader && m.shader.isSupported)) throw new Exception("Unsupported material: " + renderer.name);
            Require(true, "All renderer materials supported; no missing scripts");
            var globe = UnityEngine.Object.FindAnyObjectByType<AnchoredGlobe>();
            var france = UnityEngine.Object.FindAnyObjectByType<FranceSelection>();
            var countryCollider = france.country.GetComponent<MeshCollider>();
            Require(globe.GetComponent<Renderer>().sharedMaterial.mainTexture != null, "Provided Earth map assigned");
            Require(HitCountry(countryCollider, 2.35f, 48.86f), "Paris hits France geographic surface");
            Require(HitCountry(countryCollider, 9.15f, 42.31f), "Corte hits Corsica");
            Require(!HitCountry(countryCollider, -.13f, 51.51f), "London cannot select France");
            Require(!HitCountry(countryCollider, 13.4f, 52.52f), "Berlin cannot select France");
            Require(!HitCountry(countryCollider, -3.7f, 40.4f), "Madrid cannot select France");
            var position = globe.transform.position;
            var rotation = globe.transform.rotation;
            globe.ApplyRotation(Quaternion.Euler(20, 140, 30), Quaternion.identity, rotation);
            Physics.SyncTransforms();
            Require(Vector3.Distance(position, globe.transform.position) < .0001f, "Globe rotation preserves pedestal anchor");
            Require(HitCountry(countryCollider, 2.35f, 48.86f) && !HitCountry(countryCollider, -.13f, 51.51f), "Geographic targeting follows globe rotation");
            globe.transform.rotation = rotation;
            SetupCamera();
            View(new Vector3(-2.7f, 2.25f, -1.25f), new Vector3(0, 1.3f, 3));
            yield return null; yield return null;
            Capture("01-observatory");
            // Drive the actual XRI selection event path without claiming a tracked physical controller.
            var manager = UnityEngine.Object.FindAnyObjectByType<XRInteractionManager>();
            var handObject = new GameObject("Validation direct interactor");
            handObject.transform.position = new Vector3(0, 5, 0);
            handObject.AddComponent<SphereCollider>().isTrigger = true;
            var testHand = handObject.AddComponent<XRDirectInteractor>();
            manager.SelectEnter((IXRSelectInteractor)testHand, (IXRSelectInteractable)france.country);
            Require(france.Pending && france.confirmControl.activeSelf, "XRI France selection reveals confirmation");
            manager.SelectExit((IXRSelectInteractor)testHand, (IXRSelectInteractable)france.country);
            france.confirmControl.GetComponent<PhysicalControl>().Press();
            float until = Time.time + 1.5f;
            while (Time.time < until) yield return null;
            Require(session.FranceSelected && france.door.IsOpen && study.enabled, "Confirm saves France/French and opens study access");
            View(new Vector3(0, 1.7f, .1f), new Vector3(.35f, 1.5f, 2));
            yield return null; yield return null; Capture("02-france");
            View(new Vector3(-2.1f, 2.2f, 6.55f), new Vector3(.3f, 1.2f, 9.6f));
            yield return null; yield return null; Capture("03-study");
            var book = UnityEngine.Object.FindAnyObjectByType<InteractiveBook>();
            Require(!book.IsOpen && book.spreads.All(s => !s.activeSelf), "Closed book has no interactive chapters");
            book.openControl.GetComponent<PhysicalControl>().Press();
            until = Time.time + 1.5f; while (Time.time < until) yield return null;
            Require(book.IsOpen && book.spreads[0].activeSelf, "Physical cover opens to contents");
            Require(book.coverPivot.localPosition.y < .03f, "Opened cover settles beneath visible page text");
            View(new Vector3(0, 1.95f, 8.05f), new Vector3(0, 1.07f, 8.95f));
            yield return null; yield return null; Capture("04-book-contents");
            for (int index = 1; index <= 3; index++)
            {
                book.nextControl.GetComponent<PhysicalControl>().Press();
                Require(book.IsTurning && book.spreads.All(s => !s.activeSelf), "Page turn disables previous chapter interaction");
                until = Time.time + .35f; while (Time.time < until) yield return null;
                Require(book.turningPage.gameObject.activeSelf && book.turningPage.localEulerAngles.z > 5, "Page occupies real 3D turning pose");
                if (index == 1) Capture("05-page-turn");
                until = Time.time + .65f; while (Time.time < until) yield return null;
                Require(book.CurrentSpread == index && book.spreads.Count(s => s.activeSelf) == 1, "Exactly one current chapter: " + index);
                var chapter = book.spreads[index].GetComponent<BookChapter>();
                Require(chapter.definition.difficulty == index * 2 - 1, "Experience-specific difficulty: " + chapter.definition.difficulty);
                var miniature = chapter.GetComponentsInChildren<PhysicalControl>().First(x => x.name == "Selectable 3D miniature");
                miniature.Press();
                Require(session.Data.selectedExperience == chapter.definition.id && book.navigator.Selected == chapter.definition, "Miniature selects and saves " + chapter.definition.id);
                Require(chapter.selectionStamp.activeSelf, "Selected chapter highlighted on its own page");
                yield return null; yield return null; Capture("0" + (5 + index) + "-" + chapter.definition.id);
            }
            book.navigator.confirmControl.GetComponent<PhysicalControl>().Press();
            Require(book.navigator.IsPreparing, "Unimplemented scene shows immersive Coming Soon preparation");
            View(new Vector3(3.4f, 1.8f, 9.2f), new Vector3(2.7f, 1.5f, 10.7f));
            yield return null; yield return null; Capture("09-preparation");
            book.navigator.preparation.GetComponentsInChildren<PhysicalControl>().Single().Press();
            Require(!book.navigator.IsPreparing, "Physical return control restores book selection");
            book.previousControl.GetComponent<PhysicalControl>().Press();
            until = Time.time + 1; while (Time.time < until) yield return null;
            Require(book.CurrentSpread == 2 && book.navigator.Selected == null, "Backward page turn clears stale selection");
            session.RecordMilestone(session.catalog[0], session.catalog[0].milestoneIds[0], true);
            var saved = JsonUtility.FromJson<LearningSave>(JsonUtility.ToJson(session.Data));
            Require(saved.experiences[0].assistanceUses == 1 && saved.experiences[0].milestones.Count == 1 && saved.experiences[0].progress > 0, "Milestone, assistance, and progress serialization round trip");
            var comfort = UnityEngine.Object.FindAnyObjectByType<ComfortOptions>(); comfort.ToggleSmoothMovement();
            Require(comfort.leftController.smoothMotionEnabled, "Optional smooth movement available");
            comfort.ToggleSmoothMovement();
            Require(!comfort.leftController.smoothMotionEnabled, "Can return to teleport comfort mode");
            UnityEngine.Object.Destroy(testHand.gameObject);
        }
        static void RequireNoMissingScripts(GameObject go)
        {
            if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go) > 0) throw new Exception("Missing script: " + go.name);
        }
        static bool HitCountry(MeshCollider collider, float lon, float lat)
        {
            Vector3 normal = collider.transform.TransformDirection(AnchoredGlobe.GeographicPoint(lon, lat, 1));
            return collider.Raycast(new Ray(collider.transform.position + normal * 2, -normal), out _, 2);
        }
        static void SetupCamera()
        {
            foreach (var c in UnityEngine.Object.FindObjectsByType<Camera>()) c.enabled = false;
            preview = new GameObject("Validation preview camera", typeof(Camera)).GetComponent<Camera>();
            preview.fieldOfView = 65; preview.nearClipPlane = .02f; preview.farClipPlane = 70;
            preview.clearFlags = CameraClearFlags.Skybox;
            target = new RenderTexture(1600, 1100, 24);
            target.Create(); preview.targetTexture = target;
        }
        static void View(Vector3 position, Vector3 lookAt) { preview.transform.position = position; preview.transform.LookAt(lookAt); }
        static void Capture(string name)
        {
            var old = RenderTexture.active; RenderTexture.active = target;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
            Directory.CreateDirectory("Artifacts/Immersive"); File.WriteAllBytes("Artifacts/Immersive/" + name + ".png", image.EncodeToPNG());
            UnityEngine.Object.Destroy(image); RenderTexture.active = old;
        }
    }
}
