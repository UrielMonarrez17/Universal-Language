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

        [InitializeOnLoadMethod]
        static void Resume()
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
            if (Application.isBatchMode) GameObject.Find("XR Device Simulator")?.SetActive(false);
            SessionState.SetBool(Pending, true);
            EditorApplication.playModeStateChanged -= OnState;
            EditorApplication.playModeStateChanged += OnState;
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

        static void Begin()
        {
            RestaurantVoiceBridge.DisableGeminiForTests = true;
            RestaurantVoiceBridge.TestTurnHandler = null;
            started = EditorApplication.timeSinceStartup;
            frame = -1;
            routine = null;
            SceneManager.sceneLoaded -= DisablePersistence;
            SceneManager.sceneLoaded += DisablePersistence;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

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
                Debug.Log("ALTERLIFE_PARIS_VALIDATION_PASSED");
                SessionState.SetInt(Pending + ".result", 0);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                SessionState.SetInt(Pending + ".result", 1);
            }
            EditorApplication.update -= Tick;
            SceneManager.sceneLoaded -= DisablePersistence;
            RestaurantVoiceBridge.TestTurnHandler = null;
            RestaurantVoiceBridge.DisableGeminiForTests = false;
            if (target) target.Release();
            EditorApplication.ExitPlaymode();
        }

        static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception("FAILED: " + message);
            Debug.Log("PASS: " + message);
        }

        static IEnumerator Check()
        {
            // 1. ImmersiveHub -> BookSelection -> Paris scene flow
            var session = UnityEngine.Object.FindAnyObjectByType<LearningSession>();
            session.PersistenceEnabled = false;
            session.ResetInMemory();

            var movement = CheckTeleport(new Vector3(1.6f, 0, 3.5f));
            while (movement.MoveNext()) yield return null;

            var globe = UnityEngine.Object.FindAnyObjectByType<AnchoredGlobe>();
            var before = globe.transform.rotation;
            globe.enabled = false;
            globe.ApplyRotation(Quaternion.Euler(40, 60, 25), Quaternion.identity, before);
            Assert(Quaternion.Angle(globe.transform.rotation, Quaternion.AngleAxis(-60, Vector3.up) * before) < .01f,
                "Globe front follows horizontal controller direction; no pitch or roll");
            globe.transform.rotation = before;
            globe.enabled = true;

            var france = UnityEngine.Object.FindAnyObjectByType<FranceSelection>();
            Assert(!UnityEngine.Object.FindAnyObjectByType<InteractiveBook>(), "World selection scene contains no book");
            yield return null; yield return null;

            var viewer = Camera.main;
            Assert(Vector3.Dot(france.marker.forward, (france.marker.position - viewer.transform.position).normalized) > .99f,
                "Flag faces the player independently of globe orientation");
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

            var wristCheck = CheckOpenWrist();
            while (wristCheck.MoveNext()) yield return null;

            movement = CheckTeleport(new Vector3(1, 0, 7.6f));
            while (movement.MoveNext()) yield return null;

            var book = UnityEngine.Object.FindAnyObjectByType<InteractiveBook>();
            book.Open();
            float deadline = Time.time + 1.5f;
            while (Time.time < deadline) yield return null;
            Assert(book.IsOpen, "Book opens after scene transition");

            book.Next();
            deadline = Time.time + 1.2f;
            while (Time.time < deadline) yield return null;
            Assert(book.CurrentSpread == 1, "Physical pages still turn in standalone book scene");
            foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include)) AssertNoMissing(t.gameObject);

            var navigator = UnityEngine.Object.FindAnyObjectByType<ExperienceNavigator>();
            navigator.Select(session.catalog.First(e => e.id == "apartment"));
            Assert(navigator.Selected == null && !navigator.confirmControl.activeSelf, "Unimplemented chapters cannot be selected in beta");
            navigator.Select(session.catalog.First(e => e.id == "restaurant"));
            navigator.Confirm();

            while (SceneManager.GetActiveScene().path != ParisPrototypeBuilder.ScenePath) yield return null;
            yield return null; yield return null;

            // 2. Paris Scene Integrity, Expanded Menu, Player Transcript Panel, Dual Spatial Voices
            session = UnityEngine.Object.FindAnyObjectByType<LearningSession>();
            session.PersistenceEnabled = false;
            session.ResetInMemory();
            session.SelectFrance();

            var eval = UnityEngine.Object.FindAnyObjectByType<RestaurantEvaluation>();
            var encounter = UnityEngine.Object.FindAnyObjectByType<CommunicativeEncounter>();
            Assert(UnityEngine.Object.FindObjectsByType<Unity.XR.CoreUtils.XROrigin>().Length == 1, "Exactly one active tracked rig in Paris");
            Assert(eval && encounter.restaurantEvaluation == eval && eval.voice.encounter == encounter, "Encounter integrates evaluation and voice bridge");
            Assert(eval.TotalObjectives == 8 && eval.MinAutonomousToPass == 5, "Expanded 8-milestone intermediate rubric configured (min 5 autonomous)");
            Assert(eval.playerTranscriptText != null, "Player transcript panel is wired in Paris.unity");
            Assert(eval.voice.speaker != null && eval.voice.foxSpeaker != null && eval.voice.speaker != eval.voice.foxSpeaker,
                "Lucas waiter and Fox tutor have distinct spatial AudioSources");
            Assert(eval.waiterPersonality != null && eval.waiterPersonality.geminiVoiceName != eval.waiterPersonality.foxVoiceName,
                "Lucas and Fox have distinct Gemini TTS voice names");

            var menuDishes = GameObject.Find("French menu dishes")?.GetComponent<TMPro.TMP_Text>();
            Assert(menuDishes != null && menuDishes.text.Contains("Quiche lorraine") && menuDishes.text.Contains("Crème brûlée"),
                "Physical 3D café menu displays the expanded Parisian menu");

            foreach (var go in GameObject.Find("Paris Restaurant Prototype").GetComponentsInChildren<Transform>(true))
                AssertNoMissing(go.gameObject);

            Assert(eval.experience.scenePath == ParisPrototypeBuilder.ScenePath && Application.CanStreamedLevelBeLoaded(eval.experience.scenePath),
                "Book restaurant definition loads Paris");

            wristCheck = CheckOpenWrist();
            while (wristCheck.MoveNext()) yield return null;

            SetupPreview();
            Assert(GameObject.Find("Paris cityscape") && GameObject.Find("Cafe terrace detailing"), "Authored city and terrace detailing are present");
            var visible = UnityEngine.Object.FindObjectsByType<MeshRenderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            Assert(visible.All(r => r.sharedMaterials.All(m => m && m.shader && m.shader.isSupported)), "Visible city and Nicrom materials are supported");

            View(new Vector3(0, 1.65f, -1.35f), new Vector3(-0.25f, 1.35f, 0.7f));
            deadline = Time.time + 1.5f;
            while (Time.time < deadline) yield return null;
            Capture("01-terrace-player");

            Assert(eval.menu.position.y > .8f && eval.menu.position.y < 1f, "Physical menu remains on the table before interaction");
            View(new Vector3(-0.24f, 1.45f, -0.35f), new Vector3(-0.24f, 0.85f, 0.12f));
            yield return null; yield return null;
            Capture("04-physical-menu");

            // 3. Test Non-Linear Gemini Narrative, Physical Menu Gate, On-Demand Fox L1/L2/Tutor, Dynamic Challenge & Autonomy Passing
            // Physical menu gate check: read-menu cannot be unlocked before physical grab
            eval.ApplyGeminiTurn(new RestaurantVoiceBridge.GeminiWaiterTurn
            {
                speakerMode = "waiter",
                userTranscript = "Bonjour monsieur ! Qu'est-ce que vous me conseillez aujourd'hui ?",
                npcReplyFrench = "Bonjour ! Je vous conseille notre quiche lorraine ou la soupe à l'oignon. Jetez un œil à la carte devant vous !",
                npcReplyEnglish = "Hello! I recommend our quiche lorraine or French onion soup. Take a look at the menu in front of you!",
                feedbackEnglish = "Great polite greeting and natural recommendation question!",
                newlyCompletedMilestones = new[] { "greet-waiter", "read-menu", "ask-about-menu" },
                level1Keywords = new[] { "La carte — The menu", "Quiche lorraine — Savory bacon tart" },
                level1TipEnglish = "Pick up the menu on the table and tell Lucas what dish and drink you'd like.",
                nextFoxOptions = new[]
                {
                    new FoxHelpOption { french = "Merci ! Je vais prendre une quiche lorraine et un jus d'orange.", englishDescription = "Order quiche lorraine and fresh orange juice" },
                    new FoxHelpOption { french = "La soupe à l'oignon me tente beaucoup, avec de l'eau gazeuse.", englishDescription = "Order onion soup with sparkling water" },
                    new FoxHelpOption { french = "Est-ce que la salade niçoise peut être sans thon ?", englishDescription = "Ask if the niçoise salad can be made without tuna" }
                }
            });

            Assert(eval.IsMilestoneCompleted("greet-waiter") && eval.IsMilestoneCompleted("ask-about-menu"),
                "Multi-milestone turn awards greet-waiter and ask-about-menu together");
            Assert(!eval.IsMilestoneCompleted("read-menu"), "Physical menu gate blocks read-menu until menu is physically grabbed");
            Assert(eval.playerTranscriptText.text.Contains("Bonjour monsieur"), "Player transcript panel shows player's spoken French");
            Assert(eval.speechText.text.Contains("quiche lorraine"), "Waiter dialogue bubble displays Lucas's dynamic French reply");

            // On-demand Fox Help cycle: Observing -> Level 1 -> Level 2 -> Observing (no silence timer!)
            Assert(!eval.companion.IsShowingAnyHelp, "Fox starts quietly observing (no automatic timer interruption)");
            eval.ToggleFoxHelp();
            Assert(eval.companion.IsShowingLevel1, "First Y press opens Fox Level 1 subtle hint panel");
            eval.ToggleFoxHelp();
            Assert(eval.companion.IsShowingHelp, "Second Y press upgrades to Fox Level 2 dynamic 3-card rescue");
            eval.UseAnswerA();
            Assert(eval.companion.IsShowingHelp && eval.CompletedObjectives == 2,
                "Tapping a Level 2 card models pronunciation without auto-submitting");
            eval.ToggleFoxHelp();
            Assert(!eval.companion.IsShowingAnyHelp, "Third Y press closes Fox help cleanly");

            // English spoken question -> routed to Fox Tutor without breaking Lucas's turn or awarding milestones
            string waiterLineBeforeTutor = eval.speechText.text;
            eval.ApplyGeminiTurn(new RestaurantVoiceBridge.GeminiWaiterTurn
            {
                speakerMode = "fox_tutor",
                userTranscript = "What does quiche lorraine have in it?",
                foxTutorReplyEnglish = "Quiche lorraine is a warm savory tart with smoked bacon, eggs, and cream. You can ask Lucas 'Est-ce qu'il y a de la viande ?'",
                level1Keywords = new[] { "Lardons — Smoked bacon", "Sans viande — Meatless" },
                level1TipEnglish = "Now reply to Lucas in French!",
                nextFoxOptions = eval.GetCurrentContextualOptions()
            });
            Assert(eval.companion.IsShowingLevel1, "English question activates Fox Tutor panel");
            Assert(eval.speechText.text == waiterLineBeforeTutor, "Fox Tutor turn preserves Lucas's current dialogue without interrupting");
            Assert(eval.CompletedObjectives == 2, "Fox Tutor English turn does not falsely unlock French milestones");

            // Physically grab the 3D menu via XRInteractionManager
            var hand = new GameObject("Test hand");
            hand.transform.position = eval.menu.position + Vector3.up * .1f;
            hand.AddComponent<SphereCollider>().isTrigger = true;
            var direct = hand.AddComponent<XRDirectInteractor>();
            var manager = UnityEngine.Object.FindAnyObjectByType<XRInteractionManager>();
            var menuGrab = eval.menu.GetComponent<XRGrabInteractable>();
            manager.SelectEnter((IXRSelectInteractor)direct, (IXRSelectInteractable)menuGrab);
            Assert(eval.MenuInspected, "XRI menu grab registers physical inspection");
            manager.SelectExit((IXRSelectInteractor)direct, (IXRSelectInteractable)menuGrab);
            UnityEngine.Object.Destroy(hand);

            // Multi-objective natural order: read-menu + place-order + order-drink + Lucas issues subtle mix-up
            eval.ApplyGeminiTurn(new RestaurantVoiceBridge.GeminiWaiterTurn
            {
                speakerMode = "waiter",
                userTranscript = "J'ai bien regardé la carte. Je vais prendre une soupe à l'oignon et un thé à la menthe, s'il vous plaît.",
                npcReplyFrench = "Très bien ! Alors une soupe à l'oignon et un thé au citron. C'est bien ça ?",
                npcReplyEnglish = "Very well! So a French onion soup and a lemon tea. Is that right?",
                feedbackEnglish = "Excellent natural order! Listen closely to Lucas's drink recap.",
                newlyCompletedMilestones = new[] { "read-menu", "place-order", "order-drink" },
                detectedFood = "Soupe à l'oignon",
                detectedDrink = "Thé à la menthe",
                issuedConfirmationChallenge = true,
                level1Keywords = new[] { "Pardon — Excuse me", "À la menthe — Mint" },
                level1TipEnglish = "Lucas said lemon tea instead of mint tea — politely correct him in French!",
                nextFoxOptions = eval.GetCurrentContextualOptions()
            });
            Assert(eval.CompletedObjectives == 5 && eval.OrderedFood == "Soupe à l'oignon" && eval.OrderedDrink == "Thé à la menthe",
                "Open-ended menu order records exact food/drink and unlocks 3 milestones in one natural turn");
            Assert(eval.ConfirmationChallengeIssued && !eval.ConfirmationChallengeResolved,
                "Dynamic confirmation mix-up challenge is active");

            // Blind confirmation must NOT unlock confirm-order while challenge is unresolved
            eval.ApplyGeminiTurn(new RestaurantVoiceBridge.GeminiWaiterTurn
            {
                speakerMode = "waiter",
                userTranscript = "Oui merci.",
                npcReplyFrench = "Vous êtes sûr ? Un thé au citron, pas à la menthe ?",
                npcReplyEnglish = "Are you sure? Lemon tea, not mint?",
                newlyCompletedMilestones = new[] { "confirm-order" },
                resolvedConfirmationChallenge = false,
                nextFoxOptions = eval.GetCurrentContextualOptions()
            });
            Assert(!eval.IsMilestoneCompleted("confirm-order"), "Unresolved confirmation mix-up cannot falsely complete confirm-order");

            // Catch and politely correct the mistake -> unlocks confirm-order & serves food
            eval.ApplyGeminiTurn(new RestaurantVoiceBridge.GeminiWaiterTurn
            {
                speakerMode = "waiter",
                userTranscript = "Pardon, j'avais demandé un thé à la menthe, pas au citron !",
                npcReplyFrench = "Ah pardon, vous avez tout à fait raison ! Et voilà votre soupe à l'oignon et votre thé à la menthe, bon appétit ! Vous aimez le quartier ?",
                npcReplyEnglish = "Oh sorry, you are completely right! Here are your onion soup and mint tea, enjoy! Do you like the neighborhood?",
                newlyCompletedMilestones = new[] { "confirm-order" },
                resolvedConfirmationChallenge = true,
                servedOrder = true,
                nextFoxOptions = eval.GetCurrentContextualOptions()
            });
            Assert(eval.IsMilestoneCompleted("confirm-order") && eval.OrderServed,
                "Correcting the dynamic mix-up unlocks confirm-order and serves the meal");

            // Table chat + ask for bill & farewell -> completes all 8 milestones & passes autonomy threshold (5/8 autonomous!)
            eval.ApplyGeminiTurn(new RestaurantVoiceBridge.GeminiWaiterTurn
            {
                speakerMode = "waiter",
                userTranscript = "C'est délicieux, j'adore ce quartier ! L'addition, s'il vous plaît, et bonne journée !",
                npcReplyFrench = "Merci infiniment ! Voici l'addition. Passez une excellente journée à Paris et à bientôt !",
                npcReplyEnglish = "Thank you so much! Here is the bill. Have a wonderful day in Paris and see you soon!",
                newlyCompletedMilestones = new[] { "table-chat", "ask-bill-farewell" },
                finalSummaryEnglish = "Strong intermediate French! You naturally ordered off the expanded menu and politely caught Lucas's tea mix-up. Tip: try adding 's'il vous plaît' when asking for dessert too.",
                nextFoxOptions = eval.GetCurrentContextualOptions()
            });

            Assert(eval.Stage == RestaurantStage.Complete && eval.CompletedObjectives == 8 && eval.PassedExam,
                "Completing all 8 milestones with sufficient autonomy passes the Level 3/5 exam");
            Assert(eval.speechText.text.Contains("excellente journée"), "Lucas's authentic closing Gemini reply is preserved");
            Assert(eval.resultsText.text.Contains("EXAM PASSED") && eval.resultsText.text.Contains("Strong intermediate French"),
                "Final summary shows pass badge and personalized Gemini coaching report");
            Assert(session.GetProgress(eval.experience).completed && Mathf.Approximately(session.GetProgress(eval.experience).progress, 1f),
                "Passed exam marks restaurant experience 100% completed in LearningSession");

            View(new Vector3(.4f, 1.7f, -.8f), new Vector3(1.4f, 1.4f, 1.4f));
            yield return null; yield return null;
            Capture("03-evaluation-result");

            // Post-exam free conversation
            eval.BeginPractice();
            Assert(eval.Stage == RestaurantStage.Practice, "Passed exam allows entering free conversation mode");

            // 4. Test Autonomy Gate Failure (<5 autonomous milestones -> does NOT mark completed!)
            session.ResetInMemory();
            session.SelectFrance();
            eval.Begin();
            eval.InspectMenu();
            string[] all8 = { "greet-waiter", "read-menu", "ask-about-menu", "place-order", "order-drink", "confirm-order", "table-chat", "ask-bill-farewell" };
            for (int i = 0; i < all8.Length; i++)
            {
                if (i < 4) eval.ShowFoxLevel2Cards(); // Use Level-2 rescue on 4 milestones -> only 4/8 autonomous (< 5 required!)
                eval.ApplyGeminiTurn(new RestaurantVoiceBridge.GeminiWaiterTurn
                {
                    speakerMode = "waiter",
                    userTranscript = "Réponse test " + i,
                    npcReplyFrench = "Très bien !",
                    npcReplyEnglish = "Very good!",
                    newlyCompletedMilestones = new[] { all8[i] },
                    resolvedConfirmationChallenge = all8[i] == "confirm-order",
                    finalSummaryEnglish = i == all8.Length - 1 ? "Good effort, try using fewer rescue cards next time!" : "",
                    nextFoxOptions = eval.GetCurrentContextualOptions()
                });
            }
            Assert(eval.Stage == RestaurantStage.Complete && eval.CompletedObjectives == 8 && eval.AutonomousObjectives == 4 && !eval.PassedExam,
                "Completing 8/8 with only 4 autonomous objectives fails the 5/8 autonomy passing threshold");
            Assert(!session.GetProgress(eval.experience).completed && session.GetProgress(eval.experience).progress < 1f,
                "Failed autonomy run does not mark exam completed in LearningSession");
            eval.BeginPractice();
            Assert(eval.Stage == RestaurantStage.Welcome, "RETRY EXAM restarts the exam cleanly when autonomy threshold was not met");

            // 5. Audio encoding + Gemini WAV & raw PCM decoding verification
            byte[] wav = RestaurantVoiceBridge.EncodeWav(new float[1600], 1, 16000);
            Assert(wav.Length == 3244 && System.Text.Encoding.ASCII.GetString(wav, 0, 4) == "RIFF",
                "Microphone WAV encoding has valid PCM header and length");
            var decodedWav = RestaurantVoiceBridge.DecodeGeminiAudio(wav, "audio/wav", "TestWav");
            Assert(decodedWav != null && decodedWav.samples == 1600 && decodedWav.frequency == 16000,
                "Gemini TTS decoder parses RIFF/WAV containers accurately");
            UnityEngine.Object.Destroy(decodedWav);
            var decodedPcm = RestaurantVoiceBridge.DecodeGeminiAudio(new byte[4800], "audio/L16;codec=pcm;rate=24000", "TestPcm");
            Assert(decodedPcm != null && decodedPcm.samples == 2400 && decodedPcm.frequency == 24000,
                "Gemini TTS decoder parses raw L16 PCM audio accurately");
            UnityEngine.Object.Destroy(decodedPcm);

            // 6. Return navigation: Paris -> BookSelection -> ImmersiveHub
            eval.ReturnToBook();
            while (SceneManager.GetActiveScene().path != SceneJourney.BookScene) yield return null;
            yield return null; yield return null;
            Assert(UnityEngine.Object.FindAnyObjectByType<InteractiveBook>() && !UnityEngine.Object.FindAnyObjectByType<RestaurantEvaluation>(),
                "Return from Paris loads the book scene and unloads Paris");

            GameObject.Find("Return to world selection").GetComponent<PhysicalControl>().Press();
            while (SceneManager.GetActiveScene().path != SceneJourney.WorldScene) yield return null;
            yield return null; yield return null;
            Assert(UnityEngine.Object.FindAnyObjectByType<AnchoredGlobe>() && !UnityEngine.Object.FindAnyObjectByType<InteractiveBook>(),
                "Book return control loads world selection without a duplicate room");
        }

        static IEnumerator CheckOpenWrist()
        {
            var scene = SceneManager.GetActiveScene();
            var menu = UnityEngine.Object.FindAnyObjectByType<WristMenuController>();
            var settings = new SerializedObject(menu);
            settings.FindProperty("forceVisibleForDebug").boolValue = true;
            settings.ApplyModifiedPropertiesWithoutUndo();
            var rig = UnityEngine.Object.FindAnyObjectByType<QuestMultimodalRig>();
            var wrist = rig.leftController.activeInHierarchy ? rig.leftController : rig.leftHand;
            wrist.SetActive(true);
            wrist.transform.position = Camera.main.transform.TransformPoint(new Vector3(-.2f, -.1f, .45f));
            var stop = Time.time + .5f;
            while (Time.time < stop) yield return null;
            Assert(Vector3.Dot(-menu.transform.forward, (Camera.main.transform.position - menu.transform.position).normalized) > .99f,
                "The readable wrist face points towards the player");
            var back = settings.FindProperty("returnButtonObject").objectReferenceValue as GameObject;
            var button = back.GetComponent<XRSimpleInteractable>();
            var left = wrist.GetComponentInChildren<XRBaseInteractor>(true);
            button.selectEntered.Invoke(new SelectEnterEventArgs { interactorObject = left, interactableObject = button });
            stop = Time.time + .8f;
            while (Time.time < stop) yield return null;
            Assert(SceneManager.GetActiveScene() == scene, "Opening the menu and overlapping Back with the wearing hand keeps the current scene");
        }

        static IEnumerator CheckTeleport(Vector3 destination)
        {
            var rig = UnityEngine.Object.FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
            var provider = UnityEngine.Object.FindAnyObjectByType<UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationProvider>();
            var floor = UnityEngine.Object.FindAnyObjectByType<ClearTeleportArea>();
            var original = rig.Camera.transform.position;
            original.y = rig.transform.position.y;
            Assert(floor && floor.enabled && floor.IsClear(destination), "Menu floor accepts a clear teleport destination");
            foreach (var point in new[] { destination, original })
            {
                Assert(provider.QueueTeleportRequest(new UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportRequest { destinationPosition = point }),
                    "Menu teleport request accepted");
                float until = Time.time + .5f;
                while (Time.time < until) yield return null;
                var head = rig.Camera.transform.position;
                Assert(Vector2.Distance(new Vector2(head.x, head.z), new Vector2(point.x, point.z)) < .05f,
                    "Menu teleport moves the tracked camera to the requested floor position");
            }
        }

        static void AssertNoMissing(GameObject go)
        {
            if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go) > 0)
                throw new Exception("Missing script: " + go.name);
        }

        static void SetupPreview()
        {
            foreach (var cam in UnityEngine.Object.FindObjectsByType<Camera>()) cam.enabled = false;
            preview = new GameObject("Paris validation camera", typeof(Camera)).GetComponent<Camera>();
            preview.nearClipPlane = .03f;
            preview.farClipPlane = 180;
            preview.fieldOfView = 68;
            preview.clearFlags = CameraClearFlags.Skybox;
            target = new RenderTexture(1600, 1000, 24);
            target.Create();
            preview.targetTexture = target;
        }

        static void View(Vector3 from, Vector3 to)
        {
            preview.transform.position = from;
            preview.transform.LookAt(to);
        }

        static void Capture(string name)
        {
            Directory.CreateDirectory("Artifacts/Paris");
            var old = RenderTexture.active;
            RenderTexture.active = target;
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            texture.Apply();
            File.WriteAllBytes("Artifacts/Paris/" + name + ".png", texture.EncodeToPNG());
            UnityEngine.Object.Destroy(texture);
            RenderTexture.active = old;
        }
    }
}
