using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Alterlife.Immersive.Editor
{
    public static class ParisPrototypeValidation
    {
        const string Pending = "Alterlife.Paris.Validation";
        static IEnumerator routine;
        static int frame;
        static double started;
        static Camera preview;
        static RenderTexture target;
        [InitializeOnLoadMethod] static void Resume()
        {
            if (!SessionState.GetBool(Pending, false)) return;
            EditorApplication.playModeStateChanged += OnState;
            if (EditorApplication.isPlaying) Begin();
        }
        [MenuItem("Alterlife/Restaurant/Validate Paris prototype")]
        public static void Run()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ImmersiveHubBuilder.ScenePath);
            // The optional desktop simulator UI assumes keyboard bindings; batch mode has no keyboard.
            // This is an unsaved test-only change, not a modification of the user's simulator setup.
            if (Application.isBatchMode) GameObject.Find("XR Device Simulator")?.SetActive(false);
            SessionState.SetBool(Pending, true);
            EditorApplication.playModeStateChanged -= OnState; EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }
        static void OnState(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode) Begin();
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Pending, false))
            {
                SessionState.SetBool(Pending, false);
                if (Application.isBatchMode) EditorApplication.Exit(SessionState.GetInt(Pending + ".result", 1));
            }
        }
        static void Begin() { started = EditorApplication.timeSinceStartup; frame = -1; routine = null; SceneManager.sceneLoaded -= DisablePersistence; SceneManager.sceneLoaded += DisablePersistence; EditorApplication.update -= Tick; EditorApplication.update += Tick; }
        static void DisablePersistence(Scene scene, LoadSceneMode mode)
        {
            var session = UnityEngine.Object.FindAnyObjectByType<LearningSession>();
            if (session) session.PersistenceEnabled = false;
            if (Application.isBatchMode) GameObject.Find("XR Device Simulator")?.SetActive(false);
        }
        static void Tick()
        {
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup - started < 3 || frame == Time.frameCount) return;
            frame = Time.frameCount;
            try
            {
                if (EditorApplication.timeSinceStartup - started > 180) throw new Exception("Validation timeout");
                if (routine == null) routine = Check();
                if (routine.MoveNext()) return;
                Debug.Log("ALTERLIFE_PARIS_VALIDATION_PASSED"); SessionState.SetInt(Pending + ".result", 0);
            }
            catch (Exception ex) { Debug.LogException(ex); SessionState.SetInt(Pending + ".result", 1); }
            EditorApplication.update -= Tick;
            SceneManager.sceneLoaded -= DisablePersistence;
            if (target) target.Release();
            EditorApplication.ExitPlaymode();
        }
        static void Assert(bool condition, string message)
        { if (!condition) throw new Exception("FAILED: " + message); Debug.Log("PASS: " + message); }
        static IEnumerator Check()
        {
            var session = UnityEngine.Object.FindAnyObjectByType<LearningSession>(); session.PersistenceEnabled = false; session.ResetInMemory();
            var movement = CheckTeleport(new Vector3(1.6f, 0, 3.5f));
            while (movement.MoveNext()) yield return null;
            var globe = UnityEngine.Object.FindAnyObjectByType<AnchoredGlobe>();
            var before = globe.transform.rotation; globe.enabled = false;
            globe.ApplyRotation(Quaternion.Euler(40, 60, 25), Quaternion.identity, before);
            Assert(Quaternion.Angle(globe.transform.rotation, Quaternion.AngleAxis(-60, Vector3.up) * before) < .01f, "Globe front follows horizontal controller direction; no pitch or roll");
            globe.transform.rotation = before; globe.enabled = true;
            var france = UnityEngine.Object.FindAnyObjectByType<FranceSelection>();
            Assert(!UnityEngine.Object.FindAnyObjectByType<InteractiveBook>(), "World selection scene contains no book");
            yield return null; yield return null;
            var viewer = Camera.main;
            Assert(Vector3.Dot(france.marker.forward, (france.marker.position - viewer.transform.position).normalized) > .99f, "Flag faces the player independently of globe orientation");
            france.TargetFrance();
            Assert(session.FranceSelected, "Country and language saved in session");
            while (SceneManager.GetActiveScene().path != SceneJourney.BookScene) yield return null;
            yield return null; yield return null;
            session = UnityEngine.Object.FindAnyObjectByType<LearningSession>();
            Assert(!UnityEngine.Object.FindAnyObjectByType<AnchoredGlobe>(), "Book selection is a separate scene without the globe");
            var head = Camera.main.transform.position;
            Assert(Vector2.Distance(new Vector2(head.x, head.z), new Vector2(0, 7.8f)) < .05f, "Book scene spawns in front of the desk");
            Assert(UnityEngine.Object.FindObjectsByType<Unity.XR.CoreUtils.XROrigin>().Length == 1, "Book scene has one XR rig");
            Assert(UnityEngine.Object.FindAnyObjectByType<InteractiveBook>() != null, "Existing book preserved");
            movement = CheckTeleport(new Vector3(1, 0, 7.6f));
            while (movement.MoveNext()) yield return null;
            var book = UnityEngine.Object.FindAnyObjectByType<InteractiveBook>(); book.Open();
            float deadline = Time.time + 1.5f; while (Time.time < deadline) yield return null;
            Assert(book.IsOpen, "Book opens after scene transition");
            book.Next(); deadline = Time.time + 1.2f; while (Time.time < deadline) yield return null;
            Assert(book.CurrentSpread == 1, "Physical pages still turn in standalone book scene");
            foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include)) AssertNoMissing(t.gameObject);
            var navigator = UnityEngine.Object.FindAnyObjectByType<ExperienceNavigator>();
            navigator.Select(session.catalog.First(e => e.id == "apartment"));
            Assert(navigator.Selected == null && !navigator.confirmControl.activeSelf, "Unimplemented chapters cannot be selected in beta");
            navigator.Select(session.catalog.First(e => e.id == "restaurant"));
            navigator.Confirm();
            while (SceneManager.GetActiveScene().path != ParisPrototypeBuilder.ScenePath) yield return null;
            yield return null; yield return null;
            session = UnityEngine.Object.FindAnyObjectByType<LearningSession>(); session.PersistenceEnabled = false; session.ResetInMemory(); session.SelectFrance();
            var eval = UnityEngine.Object.FindAnyObjectByType<RestaurantEvaluation>();
            var encounter = UnityEngine.Object.FindAnyObjectByType<CommunicativeEncounter>();
            Assert(UnityEngine.Object.FindObjectsByType<Unity.XR.CoreUtils.XROrigin>().Length == 1, "Exactly one active tracked rig in Paris");
            Assert(eval && encounter.restaurantEvaluation == eval && eval.voice.encounter == encounter, "Existing encounter integrates restaurant and microphone adapter");
            foreach (var go in GameObject.Find("Paris Restaurant Prototype").GetComponentsInChildren<Transform>(true))
                AssertNoMissing(go.gameObject);
            Assert(eval.experience.scenePath == ParisPrototypeBuilder.ScenePath && Application.CanStreamedLevelBeLoaded(eval.experience.scenePath), "Book restaurant definition loads Paris");
            Assert(!eval.voice.IsConfigured, "No fake speech/AI service or embedded credential configured");
            SetupPreview();
            Assert(GameObject.Find("Paris cityscape") && GameObject.Find("Cafe terrace detailing"), "Authored city and terrace detailing are present");
            var visible = UnityEngine.Object.FindObjectsByType<MeshRenderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            Debug.Log("PARIS_VISIBLE_MESH_RENDERERS=" + visible.Length);
            Assert(visible.All(r => r.sharedMaterials.All(m => m && m.shader && m.shader.isSupported)), "Visible city and Nicrom materials are supported");
            View(new Vector3(0, 1.65f, -1.35f), new Vector3(.1f, 1.3f, 1.5f));
            deadline = Time.time + 2; while (Time.time < deadline) yield return null;
            Capture("01-terrace-player");
            Assert(eval.menu.position.y > .8f && eval.menu.position.y < 1f, "Physical menu remains on the table before interaction");
            View(new Vector3(0, 1.8f, -.85f), new Vector3(0, .85f, .15f));
            yield return null; yield return null; Capture("04-physical-menu");
            View(new Vector3(-3.1f, 2.5f, -2.1f), new Vector3(.3f, 1.1f, 2.3f));
            yield return null; yield return null; Capture("02-terrace-overview");
            encounter.OnVoiceTranscriptionReceived("s'il vous plaît");
            Assert(eval.Stage == RestaurantStage.Welcome, "Politeness alone cannot complete an objective");
            encounter.OnVoiceTranscriptionReceived(""); Assert(eval.Attempts == 1, "Empty microphone result does not count as a language failure");
            encounter.OnVoiceTranscriptionReceived("Bonjour, merci beaucoup !"); Assert(eval.Stage == RestaurantStage.ReadMenu, "Greeting milestone");
            encounter.OnVoiceTranscriptionReceived("Je suis prêt à commander"); Assert(eval.Stage == RestaurantStage.ReadMenu, "Cannot skip physical menu interaction");
            var hand = new GameObject("Test hand"); hand.transform.position = eval.menu.position + Vector3.up * .1f;
            hand.AddComponent<SphereCollider>().isTrigger = true;
            var direct = hand.AddComponent<XRDirectInteractor>();
            var manager = UnityEngine.Object.FindAnyObjectByType<XRInteractionManager>();
            var menuGrab = eval.menu.GetComponent<XRGrabInteractable>();
            manager.SelectEnter((IXRSelectInteractor)direct, (IXRSelectInteractable)menuGrab);
            Assert(eval.MenuInspected, "XRI menu grab registers inspection");
            manager.SelectExit((IXRSelectInteractor)direct, (IXRSelectInteractable)menuGrab); UnityEngine.Object.Destroy(hand);
            eval.ShowHelp(); eval.UseAnswerA(); Assert(eval.Stage == RestaurantStage.Recommendation, "Assisted readiness advances with recorded help");
            encounter.OnVoiceTranscriptionReceived("Qu'est-ce que vous me conseillez ?"); Assert(eval.Stage == RestaurantStage.Meal, "Ask about menu milestone");
            encounter.OnVoiceTranscriptionReceived("Je ne veux pas de salade"); Assert(eval.Stage == RestaurantStage.Meal, "Negated dish is not accepted as an order");
            encounter.OnVoiceTranscriptionReceived("Une salade s'il vous plaît"); Assert(eval.Dish == "salade" && eval.Stage == RestaurantStage.Drink, "Menu choice recorded");
            encounter.OnVoiceTranscriptionReceived("De l'eau s'il vous plaît"); Assert(eval.Stage == RestaurantStage.Water, "Waiter asks water clarification");
            encounter.OnVoiceTranscriptionReceived("Plate, s'il vous plaît"); Assert(eval.Stage == RestaurantStage.Confirmation && eval.CurrentPrompt.Contains("gazeuse"), "Listening challenge deliberately repeats wrong water");
            encounter.OnVoiceTranscriptionReceived("Oui merci"); Assert(eval.Stage == RestaurantStage.Confirmation, "Must correct misheard order instead of blindly confirming");
            encounter.OnVoiceTranscriptionReceived("Non, de l'eau plate, s'il vous plaît");
            Assert(eval.Stage == RestaurantStage.Complete && eval.CompletedObjectives == 6 && eval.AssistedObjectives == 1, "Six objectives completed; only actual assistance counted");
            Assert(session.GetProgress(eval.experience).completed && session.GetProgress(eval.experience).milestones.Count == 6, "Milestones persisted through existing session API");
            var progress = session.GetProgress(eval.experience);
            progress.milestones.Remove("read-menu"); progress.milestones.Add("retired-objective");
            Assert(!session.GetProgress(eval.experience).completed && Mathf.Approximately(progress.progress, 5f / 6), "Old save objectives cannot falsely complete a changed lesson");
            session.RecordMilestone(eval.experience, "read-menu", false);
            View(new Vector3(.4f, 1.7f, -.8f), new Vector3(1.4f, 1.4f, 1.4f));
            yield return null; yield return null; Capture("03-evaluation-result");
            eval.BeginPractice(); Assert(eval.Stage == RestaurantStage.Welcome, "Offline practice restarts guided conversation without pretending to be AI");
            eval.Begin(); eval.InspectMenu();
            foreach (string line in new[] { "Bonjour merci", "Je suis prete a commander", "Que recommandez vous", "Un croque monsieur", "Un cafe", "Oui c'est ca" }) encounter.OnVoiceTranscriptionReceived(line);
            Assert(eval.Stage == RestaurantStage.Complete && eval.CompletedObjectives == 6 && eval.AssistedObjectives == 0, "Alternative coffee branch and accent-insensitive answers work without assistance");
            byte[] wav = RestaurantVoiceBridge.EncodeWav(new float[1600], 1, 16000);
            Assert(wav.Length == 3244 && System.Text.Encoding.ASCII.GetString(wav, 0, 4) == "RIFF", "Microphone WAV encoding has valid PCM header and length");
            eval.Begin(); eval.InspectMenu(); eval.ShowHelp();
            eval.ReturnToBook();
            while (SceneManager.GetActiveScene().path != SceneJourney.BookScene) yield return null;
            yield return null; yield return null;
            Assert(UnityEngine.Object.FindAnyObjectByType<InteractiveBook>() && !UnityEngine.Object.FindAnyObjectByType<RestaurantEvaluation>(), "Return from Paris loads the book scene and unloads Paris");
            GameObject.Find("Return to world selection").GetComponent<PhysicalControl>().Press();
            while (SceneManager.GetActiveScene().path != SceneJourney.WorldScene) yield return null;
            yield return null; yield return null;
            Assert(UnityEngine.Object.FindAnyObjectByType<AnchoredGlobe>() && !UnityEngine.Object.FindAnyObjectByType<InteractiveBook>(), "Book return control loads world selection without a duplicate room");
        }
        static IEnumerator CheckTeleport(Vector3 destination)
        {
            var rig = UnityEngine.Object.FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
            var provider = UnityEngine.Object.FindAnyObjectByType<UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationProvider>();
            var floor = UnityEngine.Object.FindAnyObjectByType<ClearTeleportArea>();
            var original = rig.Camera.transform.position; original.y = rig.transform.position.y;
            Assert(floor && floor.enabled && floor.IsClear(destination), "Menu floor accepts a clear teleport destination");
            foreach (var point in new[] { destination, original })
            {
                Assert(provider.QueueTeleportRequest(new UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportRequest { destinationPosition = point }), "Menu teleport request accepted");
                float until = Time.time + .5f; while (Time.time < until) yield return null;
                var head = rig.Camera.transform.position;
                Assert(Vector2.Distance(new Vector2(head.x, head.z), new Vector2(point.x, point.z)) < .05f, "Menu teleport moves the tracked camera to the requested floor position");
            }
        }
        static void AssertNoMissing(GameObject go) { if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go) > 0) throw new Exception("Missing script: " + go.name); }
        static void SetupPreview()
        {
            foreach (var cam in UnityEngine.Object.FindObjectsByType<Camera>()) cam.enabled = false;
            preview = new GameObject("Paris validation camera", typeof(Camera)).GetComponent<Camera>();
            preview.nearClipPlane = .03f; preview.farClipPlane = 180; preview.fieldOfView = 68; preview.clearFlags = CameraClearFlags.Skybox;
            target = new RenderTexture(1600, 1000, 24); target.Create(); preview.targetTexture = target;
        }
        static void View(Vector3 from, Vector3 to) { preview.transform.position = from; preview.transform.LookAt(to); }
        static void Capture(string name)
        {
            var old = RenderTexture.active; RenderTexture.active = target;
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply();
            File.WriteAllBytes("Artifacts/Paris/" + name + ".png", texture.EncodeToPNG());
            UnityEngine.Object.Destroy(texture); RenderTexture.active = old;
        }
    }
}
