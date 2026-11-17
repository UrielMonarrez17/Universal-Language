using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

namespace Alterlife.Immersive.Editor
{
    public static class ParisPrototypeBuilder
    {
        public const string ScenePath = "Assets/Scenes/Paris.unity";
        const string Root = "Assets/Alterlife/Immersive/Paris";
        static TMP_FontAsset font;
        static Material ivory, stone, wood, green, metal, gold, ink;
        static Transform root;
        static readonly System.Collections.Generic.Dictionary<Material, Material> converted = new System.Collections.Generic.Dictionary<Material, Material>();

        [MenuItem("Alterlife/Restaurant/Build Paris prototype")]
        public static void Build()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(Root); AssetDatabase.Refresh();
            var scene = EditorSceneManager.OpenScene(ScenePath);
            if (scene.GetRootGameObjects().Any(g => g.name == "Paris Restaurant Prototype"))
                throw new InvalidOperationException("Paris prototype already exists. Edit/refine the authored scene rather than overwriting it.");
            // Keep the user's complete imported layout available and editable, rather than deleting it.
            var originals = scene.GetRootGameObjects();
            var archive = new GameObject("Original Paris layout — preserved");
            foreach (var go in originals) go.transform.SetParent(archive.transform, true);
            archive.SetActive(false);
            root = new GameObject("Paris Restaurant Prototype").transform;
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/VRTemplateAssets/Fonts/Inter/Inter-Regular SDF.asset");
            ivory = Mat("Ivory", new Color32(243, 229, 198, 255));
            stone = Mat("Terrace limestone", new Color32(188, 167, 139, 255));
            wood = Mat("Warm walnut", new Color32(125, 76, 42, 255));
            green = Mat("Sage canvas", new Color32(61, 91, 71, 255));
            metal = Mat("Graphite iron", new Color32(29, 43, 48, 255));
            gold = Mat("Brass", new Color32(194, 144, 66, 255));
            ink = Mat("Menu ink", new Color32(44, 35, 26, 255));
            var rendererSettings = root.gameObject.AddComponent<HubRendering>();
            rendererSettings.pipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Alterlife/Immersive/Generated/Hub Quest Pipeline.asset");
            RenderSettings.skybox = AssetDatabase.LoadAssetAtPath<Material>("Assets/SkySeries Freebie/FluffballDay.mat");
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.64f, .7f, .78f);
            RenderSettings.ambientEquatorColor = new Color(.47f, .43f, .36f);
            RenderSettings.ambientGroundColor = new Color(.22f, .20f, .16f);
            var light = new GameObject("Paris late afternoon sun", typeof(Light)).GetComponent<Light>();
            light.transform.SetParent(root); light.type = LightType.Directional; light.color = new Color(1, .91f, .76f);
            light.intensity = 1.25f; light.shadows = LightShadows.Hard; light.shadowStrength = .6f;
            light.transform.rotation = Quaternion.Euler(40, -35, 0);
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Samples/XR Interaction Toolkit/3.5.1/Starter Assets/Prefabs/XR Origin (XR Rig).prefab"));
            rig.name = "Paris XR Origin"; rig.transform.SetParent(root); rig.transform.position = new Vector3(0, .02f, -1.35f); rig.layer = 2;
            var origin = rig.GetComponent<Unity.XR.CoreUtils.XROrigin>();
            origin.RequestedTrackingOriginMode = Unity.XR.CoreUtils.XROrigin.TrackingOriginMode.Floor;
            var camera = rig.GetComponentInChildren<Camera>(true); camera.nearClipPlane = .04f; camera.farClipPlane = 180; camera.clearFlags = CameraClearFlags.Skybox;
            foreach (var caster in rig.GetComponentsInChildren<CurveInteractionCaster>(true)) caster.hitDetectionType = CurveInteractionCaster.HitDetectionType.Raycast;
            foreach (var input in rig.GetComponentsInChildren<ControllerInputActionManager>(true))
            {
                var so = new SerializedObject(input);
                so.FindProperty("m_SmoothMotionEnabled").boolValue = false; so.FindProperty("m_SmoothTurnEnabled").boolValue = false; so.ApplyModifiedPropertiesWithoutUndo();
            }
            var character = rig.GetComponent<CharacterController>(); character.radius = .22f; character.stepOffset = .2f;
            var veilMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Alterlife/Immersive/Generated/Head collision veil.mat");
            var veil = Shape(PrimitiveType.Sphere, "Comfort fade", camera.transform, Vector3.zero, Vector3.one * .38f, veilMat, false);
            var fade = camera.gameObject.AddComponent<HeadCollisionFade>(); fade.veil = veil.GetComponent<Renderer>(); fade.veil.enabled = false;
            new GameObject("Paris interaction manager", typeof(XRInteractionManager)).transform.SetParent(root);
            var floor = Box("Terrace floor", root, new Vector3(0, -.15f, .5f), new Vector3(8, .3f, 7), stone);
            var area = floor.AddComponent<ClearTeleportArea>(); area.teleportationProvider = rig.GetComponentInChildren<TeleportationProvider>(true);
            area.interactionLayers = InteractionLayerMask.GetMask("Teleport"); area.filterSelectionByHitNormal = true;
            area.colliders.Clear(); area.colliders.Add(floor.GetComponent<Collider>());
            for (int i = -4; i <= 4; i++) Box("Tile grout", root, new Vector3(i, .002f, .5f), new Vector3(.012f, .004f, 7), ivory, false);
            for (int i = -3; i <= 4; i++) Box("Tile grout", root, new Vector3(0, .003f, i), new Vector3(8, .004f, .012f), ivory, false);
            Rail(new Vector3(0, 0, 4), 8, false); Rail(new Vector3(-4, 0, .5f), 7, true); Rail(new Vector3(4, 0, .5f), 7, true);
            Facade();
            // Source models are normalized into metres; the original scene remains under the inactive archive.
            Model("Eiffel Tower", "Assets/Assets-paris/miraculous_ladybug_torre_ifel.glb", new Vector3(10, -4, 63), 36, Quaternion.Euler(270, 70.92f, 0), 0, false);
            Model("Existing cafe", "Assets/Assets-paris/cafe_shopfor_2k_likes.glb", new Vector3(-9, -.1f, -1), 0, Quaternion.Euler(270, 70.92f, 0), 10, false);
            for (int i = 0; i < 6; i++)
            {
                float x = i < 3 ? -18 + i * 9 : 22 + (i - 3) * 9;
                Model("Paris city building " + i, "Assets/Assets-paris/LoafbrrAssets/Bricktown/prefabs/Buildings/Building_" + (i + 1) + ".prefab",
                    new Vector3(x, -5, 26 + (i % 2) * 5), 12 + i % 3 * 2, Quaternion.identity, 0, false);
            }
            Box("City ground", root, new Vector3(0, -5.3f, 40), new Vector3(140, .2f, 100), stone, false);
            var table = Table("Your table", new Vector3(0, 0, .1f), 1.4f);
            Table("Terrace table left", new Vector3(-2.6f, 0, 2.3f), .9f);
            Table("Terrace table right", new Vector3(2.6f, 0, 2.3f), .9f);
            Chair(new Vector3(-3.2f, 0, 1.8f), 25); Chair(new Vector3(-2, 0, 2.8f), 205);
            Chair(new Vector3(2, 0, 1.8f), -25); Chair(new Vector3(3.2f, 0, 2.8f), 155);
            Office("Plant2", new Vector3(-3.5f, 0, 3.5f), 1.15f);
            Office("Plant1", new Vector3(3.5f, 0, 3.5f), 1.15f);
            Office("Mug", new Vector3(.45f, .82f, .2f), .11f);
            var plate = Shape(PrimitiveType.Cylinder, "Dinner plate", root, new Vector3(.25f, .825f, 0), new Vector3(.26f, .009f, .26f), ivory, false);
            Box("Napkin", root, new Vector3(.51f, .83f, -.05f), new Vector3(.12f, .01f, .21f), ivory, false);
            Box("Cutlery", root, new Vector3(.5f, .842f, -.05f), new Vector3(.012f, .005f, .17f), gold, false);

            var systems = new GameObject("Restaurant learning systems"); systems.transform.SetParent(root);
            var session = systems.AddComponent<LearningSession>();
            session.catalog = new[] { "apartment", "restaurant", "customs" }.Select(id => AssetDatabase.LoadAssetAtPath<ExperienceDefinition>("Assets/Alterlife/Immersive/Data/" + id + ".asset")).ToArray();
            var definition = session.catalog[1]; definition.scenePath = ScenePath;
            definition.milestoneIds = new[] { "greet-waiter", "read-menu", "ask-about-menu", "place-order", "respond-to-waiter", "confirm-order" }; EditorUtility.SetDirty(definition);
            var evaluation = systems.AddComponent<RestaurantEvaluation>(); evaluation.session = session; evaluation.experience = definition;
            evaluation.lesson = CreateLesson();
            var encounter = systems.AddComponent<CommunicativeEncounter>(); encounter.restaurantEvaluation = evaluation; evaluation.encounter = encounter;
            var voice = systems.AddComponent<RestaurantVoiceBridge>(); voice.evaluation = evaluation; voice.encounter = encounter; evaluation.voice = voice;
            var actor = Model("Paris waiter", "Assets/Assets-paris/fluxcoffee_barista.glb", new Vector3(-.8f, 0, 1.25f), 1.72f, Quaternion.Euler(270, 207.18f, 0), 0, true);
            voice.speaker = actor.AddComponent<AudioSource>(); voice.speaker.spatialBlend = 1; voice.speaker.minDistance = 1; voice.speaker.maxDistance = 8;
            var bubble = Group("Waiter dialogue", root, new Vector3(-.8f, 2.05f, 1.25f));
            Box("Dialogue backing", bubble, new Vector3(0, 0, .025f), new Vector3(1.4f, .48f, .025f), green, false);
            evaluation.speechText = Text("French dialogue", bubble, "Bonjour !", Vector3.zero, new Vector2(1.32f, .44f), .055f, ivory.color);
            var presentation = systems.AddComponent<WaiterPresentation>(); presentation.actor = actor.transform; presentation.speech = bubble;
            var menu = BuildMenu(evaluation); evaluation.menu = menu.transform;
            var companion = BuildCompanion(camera); evaluation.companion = companion;

            var journal = Group("Conversation journal", root, new Vector3(-1.65f, 1.18f, -.05f));
            Box("Journal wood frame", journal, new Vector3(0, 0, .04f), new Vector3(1.1f, 1.1f, .06f), wood);
            evaluation.objectiveText = Text("Current objective", journal, "YOUR GOAL", new Vector3(0, .3f, -.005f), new Vector2(.99f, .35f), .045f, ivory.color);
            evaluation.feedbackText = Text("Conversation feedback", journal, "", new Vector3(0, -.15f, -.005f), new Vector2(.99f, .48f), .035f, ivory.color);
            Box("Journal stand", root, new Vector3(-1.65f, .32f, -.01f), new Vector3(.08f, .64f, .08f), metal);
            var speak = Button("Microphone control", root, new Vector3(0, .87f, -.49f), new Vector3(.48f, .03f, .19f), "START SPEAKING", voice.ToggleRecording, true);
            voice.microphoneLabel = speak.GetComponentInChildren<TMP_Text>();
            Button("Help control", root, new Vector3(-.48f, .87f, -.49f), new Vector3(.31f, .03f, .19f), "HELP", evaluation.ShowHelp, true);
            Button("Repeat control", root, new Vector3(.48f, .87f, -.49f), new Vector3(.31f, .03f, .19f), "REPEAT", evaluation.Repeat, true);
            var cards = Group("Assisted response cards", root, new Vector3(1.55f, 1.2f, -.05f));
            evaluation.helpCards = cards.gameObject;
            var a = Button("Response A", cards, new Vector3(0, .19f, 0), new Vector3(1.0f, .32f, .035f), "", evaluation.UseAnswerA);
            var b = Button("Response B", cards, new Vector3(0, -.19f, 0), new Vector3(1.0f, .32f, .035f), "", evaluation.UseAnswerB);
            evaluation.answerA = a.GetComponentInChildren<TMP_Text>(); evaluation.answerB = b.GetComponentInChildren<TMP_Text>(); cards.gameObject.SetActive(false);
            var result = Group("Evaluation summary", root, new Vector3(1.8f, 1.8f, 1.7f));
            evaluation.resultsText = Text("Results", result, "", Vector3.zero, new Vector2(1.3f, 1.1f), .05f, ink.color);
            evaluation.practiceControl = Button("Continue conversation", root, new Vector3(1.65f, .85f, .7f), new Vector3(1.05f, .17f, .05f), "KEEP PRACTICING", evaluation.BeginPractice).gameObject;
            evaluation.practiceControl.SetActive(false);
            Button("Restart restaurant", root, new Vector3(-1.65f, .48f, -.05f), new Vector3(1.0f, .15f, .05f), "REPLAY CONVERSATION", evaluation.Begin);
            Button("Return to experience book", root, new Vector3(1.65f, .5f, -.05f), new Vector3(1.0f, .17f, .05f), "RETURN TO BOOK", evaluation.ReturnToBook);
            Text("Menu instruction", root, "Pick up the menu to read it.\nSelect HELP for response cards.", new Vector3(0, 1.02f, .65f), new Vector2(.85f, .22f), .035f, ink.color);
            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).Concat(new[] { new EditorBuildSettingsScene(ScenePath, true) }).ToArray();
            AssetDatabase.SaveAssets();
            ConfigureHub();
            ParisVisualRefinement.Run();
            Debug.Log("ALTERLIFE_PARIS_PROTOTYPE_CREATED");
        }

        public static void ConfigureHub()
        {
            var scene = EditorSceneManager.OpenScene(ImmersiveHubBuilder.ScenePath);
            var france = UnityEngine.Object.FindAnyObjectByType<FranceSelection>();
            var arrival = GameObject.Find("Book arrival point");
            if (!arrival) arrival = new GameObject("Book arrival point");
            arrival.transform.position = new Vector3(0, .01f, 7.8f); arrival.transform.rotation = Quaternion.identity;
            france.bookArrival = arrival.transform; france.teleportation = UnityEngine.Object.FindAnyObjectByType<TeleportationProvider>(); france.travelOnSelection = true;
            var instructions = GameObject.Find("Globe instructions").GetComponent<TMP_Text>();
            instructions.text = "GRAB & TURN\nSweep left or right to rotate Earth.\nSelect the French flag to visit the book.\nOther destinations: Coming Soon";
            EditorSceneManager.SaveScene(scene);
        }
        static RestaurantLesson CreateLesson()
        {
            var data = ScriptableObject.CreateInstance<RestaurantLesson>();
            data.lines = new[] {
                Line("greet-waiter", "Bonjour ! Bienvenue. Voici le menu.", "Greet the waiter and say thank you.", "Bonjour, merci beaucoup !", "Bonjour et merci !"),
                Line("read-menu", "Je vous laisse regarder le menu. Dites-moi quand vous êtes prêt.", "Read the physical menu, then say you are ready to order.", "Je suis prêt à commander.", "Je suis prête à commander."),
                Line("ask-about-menu", "Vous voulez un conseil ?", "Ask the waiter for a recommendation.", "Oui, qu'est-ce que vous me conseillez ?", "Qu'est-ce que vous recommandez ?"),
                Line("place-order", "Je vous conseille le croque-monsieur. Il contient du jambon et du fromage. Vous préférez un plat chaud ou une salade ?", "Order one dish from the menu.", "Je voudrais un croque-monsieur, s'il vous plaît.", "Une salade, s'il vous plaît."),
                Line("respond-to-waiter", "Et comme boisson ?", "Order water or coffee.", "De l'eau, s'il vous plaît.", "Un café, s'il vous plaît."),
                Line("respond-to-waiter", "De l'eau plate ou gazeuse ?", "Choose still or sparkling water.", "Plate, s'il vous plaît.", "Gazeuse, s'il vous plaît."),
                Line("confirm-order", "C'est bien ça ?", "Listen to the order. Confirm it or correct a mistake.", "Oui, c'est ça. Merci !", "Pouvez-vous répéter, s'il vous plaît ?")
            };
            AssetDatabase.CreateAsset(data, Root + "/RestaurantLesson.asset"); return data;
        }
        static RestaurantLine Line(string id, string prompt, string hint, string example, string alternative)
            => new RestaurantLine { milestoneId = id, prompt = prompt, hint = hint, example = example, alternative = alternative };
        static Material Mat(string name, Color color)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Paris " + name, color = color, enableInstancing = true };
            mat.SetFloat("_Smoothness", .18f); AssetDatabase.CreateAsset(mat, Root + "/" + name + ".mat"); return mat;
        }
        static Transform Group(string name, Transform parent, Vector3 position)
        { var go = new GameObject(name).transform; go.SetParent(parent, false); go.localPosition = position; return go; }
        static GameObject Box(string name, Transform parent, Vector3 position, Vector3 scale, Material mat, bool collider = true)
            => Shape(PrimitiveType.Cube, name, parent, position, scale, mat, collider);
        static GameObject Shape(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, Material mat, bool collider = true)
        {
            var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.SetParent(parent, false);
            go.transform.localPosition = position; go.transform.localScale = scale; go.GetComponent<Renderer>().sharedMaterial = mat;
            if (!collider) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>()); return go;
        }
        static TMP_Text Text(string name, Transform parent, string content, Vector3 pos, Vector2 size, float height, Color color, bool page = false)
        {
            var t = new GameObject(name, typeof(TextMeshPro)).GetComponent<TextMeshPro>(); t.transform.SetParent(parent, false);
            t.transform.localPosition = pos; t.transform.localRotation = page ? Quaternion.Euler(90, 0, 0) : Quaternion.identity;
            t.font = font; t.fontSize = height * 10; t.text = content; t.color = color;
            t.alignment = TextAlignmentOptions.Center; t.rectTransform.sizeDelta = size; t.textWrappingMode = TextWrappingModes.Normal; return t;
        }
        static PhysicalControl Button(string name, Transform parent, Vector3 pos, Vector3 size, string caption, UnityAction callback, bool page = false)
        {
            var go = Box(name, parent, pos, size, green);
            var control = go.AddComponent<PhysicalControl>(); control.feedbackRenderer = go.GetComponent<Renderer>();
            var interaction = go.GetComponent<XRSimpleInteractable>(); interaction.colliders.Clear(); interaction.colliders.Add(go.GetComponent<Collider>());
            UnityEventTools.AddPersistentListener(control.pressed, callback);
            var label = Text(name + " label", parent, caption, pos + (page ? Vector3.up * (size.y / 2 + .002f) : Vector3.back * (size.z / 2 + .002f)),
                new Vector2(size.x * .93f, page ? size.z * .9f : size.y * .9f), page ? .024f : .038f, ivory.color, page);
            label.transform.SetParent(go.transform, true); return control;
        }
        static void Rail(Vector3 center, float width, bool side)
        {
            var rail = Group("Terrace railing", root, center); rail.rotation = Quaternion.Euler(0, side ? 90 : 0, 0);
            Box("Railing handrail", rail, new Vector3(0, 1.05f, 0), new Vector3(width, .06f, .06f), metal);
            Box("Railing lower rail", rail, new Vector3(0, .25f, 0), new Vector3(width, .04f, .04f), metal, false);
            for (float x = -width / 2; x <= width / 2; x += .4f) Box("Railing baluster", rail, new Vector3(x, .54f, 0), new Vector3(.025f, 1.05f, .025f), metal, false);
            var guard = Box("Railing safety collider", rail, new Vector3(0, .7f, 0), new Vector3(width, 1.4f, .12f), metal); guard.GetComponent<Renderer>().enabled = false;
        }
        static void Facade()
        {
            Box("Restaurant facade", root, new Vector3(0, 1.55f, -3), new Vector3(8, 3.1f, .2f), ivory);
            for (int i = -1; i <= 1; i++)
            {
                Box("Cafe window frame", root, new Vector3(i * 2.3f, 1.45f, -2.85f), new Vector3(1.6f, 2.3f, .1f), metal);
                Box("Cafe window glass", root, new Vector3(i * 2.3f, 1.45f, -2.78f), new Vector3(1.43f, 2.1f, .03f), green, false);
                Box("Cafe mullion", root, new Vector3(i * 2.3f, 1.45f, -2.74f), new Vector3(.05f, 2.1f, .02f), gold, false);
            }
            for (int i = 0; i < 12; i++) Box("Striped terrace awning", root, new Vector3(-3.65f + i * .66f, 2.9f, -1.7f), new Vector3(.66f, .06f, 2.7f), i % 2 == 0 ? ivory : green, false);
        }
        static Transform Table(string name, Vector3 pos, float diameter)
        {
            var t = Group(name, root, pos);
            Shape(PrimitiveType.Cylinder, "Round wooden tabletop", t, new Vector3(0, .78f, 0), new Vector3(diameter, .035f, diameter), wood);
            Shape(PrimitiveType.Cylinder, "Bistro table stem", t, new Vector3(0, .39f, 0), new Vector3(.09f, .39f, .09f), metal);
            Box("Table cross foot", t, new Vector3(0, .025f, 0), new Vector3(.7f, .05f, .09f), metal);
            Box("Table cross foot", t, new Vector3(0, .025f, 0), new Vector3(.09f, .05f, .7f), metal);
            return t;
        }
        static void Chair(Vector3 position, float yaw)
        {
            var chair = Office("LoungeChair", position, .8f); chair.transform.rotation = Quaternion.Euler(0, yaw, 0);
        }
        static GameObject Office(string name, Vector3 pos, float height)
            => Model(name, "Assets/nappin/OfficeEssentialsPack/Prefabs/(Prb)" + name + ".prefab", pos, height, Quaternion.identity, 0, true);
        static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(); var bounds = rs[0].bounds;
            foreach (var r in rs.Skip(1)) bounds.Encapsulate(r.bounds); return bounds;
        }
        static GameObject Model(string name, string path, Vector3 position, float height, Quaternion rotation, float width, bool collider)
        {
            var wrap = Group(name, root, position);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            go.transform.position = Vector3.zero; go.transform.rotation = rotation;
            var b = Bounds(go); float scale = width > 0 ? width / b.size.x : height / b.size.y;
            go.transform.localScale *= scale; b = Bounds(go);
            go.transform.position += new Vector3(-b.center.x, -b.min.y, -b.center.z);
            go.transform.SetParent(wrap, false);
            foreach (var c in go.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(c);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (r.name.EndsWith("-colonly")) { r.enabled = false; continue; }
                r.sharedMaterials = r.sharedMaterials.Select(Convert).ToArray();
                r.shadowCastingMode = collider ? ShadowCastingMode.On : ShadowCastingMode.Off;
            }
            if (collider)
            {
                b = Bounds(wrap.gameObject); var box = wrap.gameObject.AddComponent<BoxCollider>();
                box.center = wrap.InverseTransformPoint(b.center); box.size = b.size;
            }
            return wrap.gameObject;
        }
        static Material Convert(Material source)
        {
            if (!source) return ivory;
            if (source.shader && (source.shader.name.Contains("Universal") || source.shader.name.Contains("glTF"))) return source;
            if (converted.TryGetValue(source, out var existing)) return existing;
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Paris " + source.name, mainTexture = source.mainTexture, color = source.HasProperty("_Color") ? source.color : Color.white };
            m.SetFloat("_Smoothness", .2f);
            AssetDatabase.CreateAsset(m, AssetDatabase.GenerateUniqueAssetPath(Root + "/Imported " + source.name.Replace('/', '_') + ".mat")); converted.Add(source, m); return m;
        }
        static GameObject BuildMenu(RestaurantEvaluation evaluation)
        {
            var menu = Group("Physical French menu", root, new Vector3(-.2f, .87f, .1f)); menu.rotation = Quaternion.Euler(-12, 0, 0);
            Box("Menu leather", menu, Vector3.zero, new Vector3(.38f, .012f, .5f), green, false);
            Box("Menu paper", menu, new Vector3(0, .008f, 0), new Vector3(.35f, .003f, .47f), ivory, false);
            Text("Menu title", menu, "LE CAFÉ DE PARIS\nMENU", new Vector3(0, .012f, .16f), new Vector2(.33f, .12f), .028f, ink.color, true);
            Text("French menu dishes", menu, "Croque-monsieur · 12 €\nJambon et fromage\n\nSalade · 10 €\nTomates et fromage\n\nEau plate / gazeuse · 3 €\nCafé · 3 €", new Vector3(0, .012f, -.055f), new Vector2(.33f, .3f), .021f, ink.color, true);
            var col = menu.gameObject.AddComponent<BoxCollider>(); col.size = new Vector3(.38f, .025f, .5f);
            var rb = menu.gameObject.AddComponent<Rigidbody>(); rb.mass = .2f; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var grab = menu.gameObject.AddComponent<XRGrabInteractable>(); grab.colliders.Clear(); grab.colliders.Add(col);
            menu.gameObject.AddComponent<RestaurantMenu>().evaluation = evaluation; return menu.gameObject;
        }
        static HummingbirdCompanion BuildCompanion(Camera camera)
        {
            var bird = Group("Restaurant hummingbird helper", root, new Vector3(.4f, 1.4f, -1));
            Shape(PrimitiveType.Sphere, "Hummingbird body", bird, Vector3.zero, new Vector3(.06f, .07f, .11f), green, false);
            Shape(PrimitiveType.Sphere, "Hummingbird head", bird, new Vector3(0, .035f, .04f), Vector3.one * .045f, gold, false);
            Box("Hummingbird beak", bird, new Vector3(0, .03f, .08f), new Vector3(.007f, .007f, .06f), metal, false);
            for (int sign = -1; sign <= 1; sign += 2) Shape(PrimitiveType.Sphere, "Hummingbird wing", bird, new Vector3(sign * .055f, .01f, 0), new Vector3(.09f, .009f, .04f), green, false);
            var label = Text("French vocabulary hint", bird, "le menu", new Vector3(0, .15f, 0), new Vector2(.4f, .1f), .03f, ink.color);
            var companion = bird.gameObject.AddComponent<HummingbirdCompanion>();
            var so = new SerializedObject(companion); so.FindProperty("headCamera").objectReferenceValue = camera.transform;
            so.FindProperty("vocabLabelRoot").objectReferenceValue = label.gameObject; so.FindProperty("vocabLabelText").objectReferenceValue = label;
            so.ApplyModifiedPropertiesWithoutUndo(); return companion;
        }
    }
}
