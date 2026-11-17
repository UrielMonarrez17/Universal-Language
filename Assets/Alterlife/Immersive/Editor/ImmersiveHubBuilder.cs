using System;
using System.Collections.Generic;
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
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

namespace Alterlife.Immersive.Editor
{
    /// <summary>Creates an editable scene and assets through Unity serialization, never edits vendor prefabs.</summary>
    public static class ImmersiveHubBuilder
    {
        public const string ScenePath = "Assets/Scenes/ImmersiveHub.unity";
        const string Root = "Assets/Alterlife/Immersive";
        const string Office = "Assets/nappin/OfficeEssentialsPack/Prefabs/";
        static TMP_FontAsset font;
        static Material cream, navy, wood, gold, ink, paper, teal, floor, white;
        static readonly Dictionary<Material, Material> converted = new Dictionary<Material, Material>();
        static TeleportationProvider teleport;
        static LearningSession session;
        static Transform environment;

        [MenuItem("Alterlife/Immersive/Create Hub")]
        public static void Create()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (File.Exists(ScenePath)) throw new InvalidOperationException("Hub already exists. Edit the authored scene; creation never overwrites it.");
            Directory.CreateDirectory(Root + "/Generated");
            AssetDatabase.Refresh();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/VRTemplateAssets/Fonts/Inter/Inter-Regular SDF.asset");
            cream = Mat("Warm plaster", new Color32(218, 198, 158, 255));
            navy = Mat("Midnight blue", new Color32(24, 42, 61, 255));
            wood = Mat("Walnut", new Color32(84, 49, 31, 255));
            gold = Mat("Antique brass", new Color32(206, 155, 68, 255), .55f);
            ink = Mat("Ink", new Color32(55, 40, 30, 255));
            paper = Mat("Ivory paper", new Color32(242, 220, 177, 255));
            teal = Mat("Sage green", new Color32(54, 91, 81, 255));
            floor = Mat("Stone floor", new Color32(95, 104, 111, 255));
            white = Mat("Warm white", new Color32(244, 240, 218, 255));
            SetupLighting();
            environment = new GameObject("Architecture and furniture").transform;
            var systems = new GameObject("Learning systems");
            session = systems.AddComponent<LearningSession>();
            session.catalog = CreateCatalog();
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Samples/XR Interaction Toolkit/3.5.1/Starter Assets/Prefabs/XR Origin (XR Rig).prefab"));
            rig.name = "Immersive XR Origin";
            rig.transform.position = new Vector3(0, .02f, -1);
            rig.layer = 2; // Do not treat the player's own CharacterController as a teleport obstacle.
            var cam = rig.GetComponentInChildren<Camera>(true);
            cam.nearClipPlane = .04f;
            cam.farClipPlane = 70;
            cam.clearFlags = CameraClearFlags.Skybox;
            var origin = rig.GetComponent<Unity.XR.CoreUtils.XROrigin>();
            origin.RequestedTrackingOriginMode = Unity.XR.CoreUtils.XROrigin.TrackingOriginMode.Floor;
            var fade = cam.gameObject.AddComponent<HeadCollisionFade>();
            var fadeMaterial = Mat("Head collision veil", new Color(0, 0, 0, 0));
            fadeMaterial.shader = Shader.Find("Universal Render Pipeline/Unlit");
            fadeMaterial.SetFloat("_Surface", 1);
            fadeMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            fadeMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            fadeMaterial.SetFloat("_ZWrite", 0);
            fadeMaterial.SetFloat("_Cull", (float)CullMode.Front);
            fadeMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            fadeMaterial.renderQueue = 4000;
            var veil = Shape(PrimitiveType.Sphere, "Head collision comfort veil", cam.transform, Vector3.zero, Vector3.one * .38f, fadeMaterial, false);
            fade.veil = veil.GetComponent<Renderer>(); fade.veil.enabled = false;
            teleport = rig.GetComponentInChildren<TeleportationProvider>(true);
            foreach (var controller in rig.GetComponentsInChildren<ControllerInputActionManager>(true))
            {
                var serialized = new SerializedObject(controller);
                serialized.FindProperty("m_SmoothMotionEnabled").boolValue = false;
                serialized.FindProperty("m_SmoothTurnEnabled").boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            foreach (var caster in rig.GetComponentsInChildren<CurveInteractionCaster>(true))
                caster.hitDetectionType = CurveInteractionCaster.HitDetectionType.Raycast;
            new GameObject("XR Interaction Manager", typeof(XRInteractionManager));
            CreateRooms();
            var door = CreateDoor();
            CreateGlobe(door);
            CreateStudy();
            CreateComfort(rig);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) }
                .Concat(EditorBuildSettings.scenes.Where(s => s.path != ScenePath)).ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("ALTERLIFE_IMMERSIVE_HUB_CREATED " + ScenePath);
        }

        static Material Mat(string name, Color color, float metallic = 0)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name, color = color, enableInstancing = true };
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", .25f);
            AssetDatabase.CreateAsset(material, Root + "/Generated/" + name + ".mat");
            return material;
        }
        static void SetupLighting()
        {
            var source = AssetDatabase.LoadAssetAtPath<Material>("Assets/AllSkyFree/Space_AnotherPlanet/AllSky_Space_AnotherPlanet Equirect.mat");
            var sky = new Material(source) { name = "Observatory space sky" };
            AssetDatabase.CreateAsset(sky, Root + "/Generated/SpaceSky.mat");
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.42f, .44f, .49f);
            RenderSettings.ambientEquatorColor = new Color(.32f, .29f, .23f);
            RenderSettings.ambientGroundColor = new Color(.18f, .15f, .12f);
            var sun = new GameObject("Soft skylight", typeof(Light)).GetComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1, .9f, .72f);
            sun.intensity = 1.1f;
            sun.shadows = LightShadows.None;
            sun.transform.rotation = Quaternion.Euler(45, -32, 0);
            PointLight("Observatory light", new Vector3(0, 3, 2), new Color(.65f, .8f, 1), 2, 7);
            PointLight("Study warm light", new Vector3(0, 3, 9.5f), new Color(1, .72f, .4f), 2, 6);
        }
        static Light PointLight(string name, Vector3 position, Color color, float intensity, float range)
        {
            var light = new GameObject(name, typeof(Light)).GetComponent<Light>();
            light.transform.position = position;
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
            return light;
        }
        static GameObject Cube(string name, Transform parent, Vector3 pos, Vector3 size, Material material, bool collider = true)
            => Shape(PrimitiveType.Cube, name, parent, pos, size, material, collider);
        static GameObject Shape(PrimitiveType shape, string name, Transform parent, Vector3 pos, Vector3 size, Material material, bool collider = true)
        {
            var go = GameObject.CreatePrimitive(shape);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = material;
            if (!collider) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }
        static Transform Group(string name, Transform parent, Vector3 localPosition)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPosition;
            return t;
        }
        static TMP_Text Text(string name, Transform parent, string content, Vector3 position, Vector2 size, float letterHeight,
            Color color, Quaternion rotation = default)
        {
            var go = new GameObject(name, typeof(TextMeshPro));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation == default ? Quaternion.identity : rotation;
            var text = go.GetComponent<TextMeshPro>();
            text.font = font;
            text.text = content;
            text.fontSize = letterHeight * 10;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            text.rectTransform.sizeDelta = size;
            return text;
        }
        static PhysicalControl Button(string name, Transform parent, Vector3 position, Vector3 size, string caption,
            UnityAction action, bool onPage = false)
        {
            var go = Cube(name, parent, position, size, teal);
            var control = go.AddComponent<PhysicalControl>();
            var interactive = go.GetComponent<XRSimpleInteractable>();
            interactive.colliders.Clear();
            interactive.colliders.Add(go.GetComponent<Collider>());
            control.feedbackRenderer = go.GetComponent<Renderer>();
            UnityEventTools.AddPersistentListener(control.pressed, action);
            // Keep lettering in world units, independent of the physical button scale.
            var label = Text(name + " label", parent, caption,
                position + (onPage ? Vector3.up * (size.y / 2 + .002f) : Vector3.back * (size.z / 2 + .002f)),
                new Vector2(size.x * .93f, onPage ? size.z : size.y), onPage ? .024f : .045f, paper.color,
                onPage ? Quaternion.Euler(90, 0, 0) : Quaternion.identity);
            label.transform.SetParent(go.transform, true);
            return control;
        }
        static void CreateRooms()
        {
            var observatory = Cube("Observatory floor", environment, new Vector3(0, -.12f, 2), new Vector3(8, .24f, 8), floor);
            AddTeleport(observatory, true);
            var study = Cube("Study floor", environment, new Vector3(0, -.12f, 9.5f), new Vector3(8, .24f, 7), wood);
            AddTeleport(study, false);
            // Physical planks and inlay add depth without a background image.
            for (int i = 0; i < 16; i++)
                Cube("Floor seam", environment, new Vector3(-3.75f + i * .5f, .002f, 9.5f), new Vector3(.012f, .004f, 7), ink, false);
            for (int x = -1; x <= 1; x += 2)
            {
                Cube("Observatory lower wall", environment, new Vector3(x * 4, .5f, 2), new Vector3(.2f, 1, 8), navy);
                Cube("Observatory upper beam", environment, new Vector3(x * 4, 3.2f, 2), new Vector3(.24f, .3f, 8), navy);
                for (int z = -2; z <= 6; z += 2)
                    Cube("Window mullion", environment, new Vector3(x * 4, 2, z), new Vector3(.22f, 2.4f, .15f), gold);
                // Invisible collision plane seals the large observation windows.
                var barrier = Cube("Observation window collision", environment, new Vector3(x * 4, 2, 2), new Vector3(.12f, 2.5f, 8), navy);
                barrier.GetComponent<Renderer>().enabled = false;
                Cube("Study wall", environment, new Vector3(x * 4, 1.7f, 9.5f), new Vector3(.2f, 3.4f, 7), cream);
                Cube("Study wainscot", environment, new Vector3(x * 3.87f, .48f, 9.5f), new Vector3(.12f, .96f, 7), teal);
                Cube("Study brass rail", environment, new Vector3(x * 3.79f, 1.01f, 9.5f), new Vector3(.04f, .045f, 7), gold, false);
                Cube("Shared wall", environment, new Vector3(x * 2.6f, 1.7f, 6), new Vector3(2.8f, 3.4f, .25f), navy);
                Cube("Door jamb", environment, new Vector3(x * 1.15f, 1.35f, 5.83f), new Vector3(.15f, 2.7f, .2f), gold);
            }
            Cube("Entry wall", environment, new Vector3(0, 1.7f, -2), new Vector3(8, 3.4f, .25f), navy);
            Cube("Study back wall", environment, new Vector3(0, 1.7f, 13), new Vector3(8, 3.4f, .25f), cream);
            Cube("Door lintel", environment, new Vector3(0, 3, 6), new Vector3(2.4f, .8f, .25f), navy);
            Cube("Study ceiling", environment, new Vector3(0, 3.45f, 9.5f), new Vector3(8, .15f, 7), cream);
            // Observatory has an open glazed roof and solid rafters, framing the actual space skybox.
            for (int z = -2; z <= 6; z += 2)
                Cube("Skylight rafter", environment, new Vector3(0, 3.45f, z), new Vector3(8, .13f, .12f), gold);
            Text("World heading", environment, "ALTERLIFE\nA WORLD OF CONVERSATIONS", new Vector3(0, 2.8f, 5.75f), new Vector2(5, .5f), .16f, paper.color);
            Text("Welcome", environment, "01 / THE OBSERVATORY\nTurn the world. Find your next conversation.", new Vector3(0, 2.8f, -.7f), new Vector2(5, .4f), .095f, paper.color, Quaternion.Euler(0, 180, 0));
            Asset("Plant1", environment, new Vector3(-3.2f, 0, 4.8f), 1.6f);
            Asset("Plant2", environment, new Vector3(3.2f, 0, 4.8f), 1.5f);
            Asset("LoungeChair", environment, new Vector3(-2.8f, 0, 1.2f), .9f, 65);
            Asset("LoungeChair", environment, new Vector3(2.8f, 0, 1.2f), .9f, -65);
        }
        static ClearTeleportArea AddTeleport(GameObject floorObject, bool enabled)
        {
            var area = floorObject.AddComponent<ClearTeleportArea>();
            area.colliders.Clear();
            area.colliders.Add(floorObject.GetComponent<Collider>());
            area.teleportationProvider = teleport;
            area.matchOrientation = MatchOrientation.WorldSpaceUp;
            area.filterSelectionByHitNormal = true;
            area.interactionLayers = InteractionLayerMask.GetMask("Teleport");
            if (area.interactionLayers.value == 0) area.interactionLayers = 1 << 1;
            area.enabled = enabled;
            return area;
        }
        static StudyDoor CreateDoor()
        {
            var door = new GameObject("Study access").AddComponent<StudyDoor>();
            door.panel = Cube("Sliding study door", environment, new Vector3(0, 1.3f, 6), new Vector3(2.15f, 2.6f, .15f), teal).transform;
            Cube("Door brass detail", door.panel, new Vector3(0, 0, -.55f), new Vector3(.8f, .012f, .05f), gold, false);
            door.sign = Text("Door sign", environment, "THE READING ROOM\nSelect France at the globe to enter", new Vector3(0, 2.5f, 5.68f), new Vector2(2.8f, .4f), .07f, paper.color);
            door.studyTeleportAreas = new Behaviour[] { GameObject.Find("Study floor").GetComponent<ClearTeleportArea>() };
            return door;
        }

        [Serializable] public sealed class Geography { public Vector2[] vertices; public int[] triangles; }
        static void CreateGlobe(StudyDoor door)
        {
            var pedestal = Group("Globe pedestal", environment, new Vector3(0, 0, 2));
            Shape(PrimitiveType.Cylinder, "Pedestal base", pedestal, new Vector3(0, .08f, 0), new Vector3(.9f, .08f, .9f), navy);
            Shape(PrimitiveType.Cylinder, "Pedestal column", pedestal, new Vector3(0, .46f, 0), new Vector3(.37f, .36f, .37f), wood);
            Shape(PrimitiveType.Cylinder, "Brass cradle", pedestal, new Vector3(0, .83f, 0), new Vector3(.74f, .04f, .74f), gold);
            var globe = new GameObject("Earth — anchored rotation", typeof(MeshFilter), typeof(MeshRenderer), typeof(SphereCollider), typeof(XRSimpleInteractable), typeof(AnchoredGlobe));
            globe.transform.SetParent(pedestal, false);
            globe.transform.localPosition = new Vector3(0, 1.46f, 0);
            globe.transform.localRotation = Quaternion.Euler(-36, 0, 0); // Present Europe toward the arriving player.
            var mesh = SphereMesh(.59f);
            AssetDatabase.CreateAsset(mesh, Root + "/Generated/GeographicEarth.asset");
            globe.GetComponent<MeshFilter>().sharedMesh = mesh;
            var earth = Mat("Earth surface", Color.white);
            earth.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Planet Earth Free/Materials/Earth2kTexture.png");
            earth.SetFloat("_Smoothness", .13f);
            globe.GetComponent<MeshRenderer>().sharedMaterial = earth;
            globe.GetComponent<SphereCollider>().radius = .59f;
            var globeInteractable = globe.GetComponent<XRSimpleInteractable>();
            globeInteractable.colliders.Clear();
            globeInteractable.colliders.Add(globe.GetComponent<SphereCollider>());

            var geo = JsonUtility.FromJson<Geography>(File.ReadAllText(Root + "/Data/FranceGeometry.json"));
            var points = geo.vertices.Select(v => AnchoredGlobe.GeographicPoint(v.x, v.y, .595f)).ToArray();
            var indices = geo.triangles;
            for (int i = 0; i < indices.Length; i += 3)
            {
                var a = points[indices[i]]; var b = points[indices[i + 1]]; var c = points[indices[i + 2]];
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), a) < 0) (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
            }
            var countryMesh = new Mesh { name = "France geographic surface", vertices = points, triangles = indices };
            countryMesh.RecalculateNormals();
            countryMesh.RecalculateBounds();
            AssetDatabase.CreateAsset(countryMesh, Root + "/Generated/FranceSurface.asset");
            var france = new GameObject("France — geographic collider", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider), typeof(XRSimpleInteractable));
            france.transform.SetParent(globe.transform, false);
            france.GetComponent<MeshFilter>().sharedMesh = countryMesh;
            france.GetComponent<MeshCollider>().sharedMesh = countryMesh;
            france.GetComponent<MeshRenderer>().sharedMaterial = gold;
            var interactable = france.GetComponent<XRSimpleInteractable>();
            interactable.colliders.Clear();
            interactable.colliders.Add(france.GetComponent<MeshCollider>());
            var selection = pedestal.gameObject.AddComponent<FranceSelection>();
            selection.session = session;
            selection.country = interactable;
            selection.highlight = france.GetComponent<Renderer>();
            selection.highlight.enabled = false;
            selection.door = door;
            var label = Group("France spatial label", pedestal, new Vector3(1.2f, 1.5f, -.1f));
            selection.label = label.gameObject;
            selection.status = Text("France details", label, "France", Vector3.zero, new Vector2(1.3f, .7f), .067f, paper.color);
            var blue = Mat("Flag blue", new Color32(0, 38, 124, 255));
            var red = Mat("Flag red", new Color32(223, 38, 52, 255));
            Cube("Flag blue", label, new Vector3(-.12f, .48f, 0), new Vector3(.12f, .24f, .015f), blue, false);
            Cube("Flag white", label, new Vector3(0, .48f, 0), new Vector3(.12f, .24f, .015f), white, false);
            Cube("Flag red", label, new Vector3(.12f, .48f, 0), new Vector3(.12f, .24f, .015f), red, false);
            selection.confirmControl = Button("Confirm France", pedestal, new Vector3(1.2f, 1, -.15f), new Vector3(.8f, .17f, .08f), "CONFIRM FRANCE", selection.Confirm).gameObject;
            Text("Globe instructions", pedestal, "GRAB & ROTATE\nHold grip on Earth and turn your wrist.\nRelease, point at France, then select.\nOther destinations: Coming Soon", new Vector3(-1.35f, 1.35f, -.25f), new Vector2(1.5f, .8f), .062f, paper.color);
            // Thin meridian cradle frames the spherical Earth without covering its country collider.
            var ring = Group("Meridian stand", pedestal, new Vector3(0, 1.46f, 0));
            var line = ring.gameObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.widthMultiplier = .016f;
            line.sharedMaterial = gold;
            line.positionCount = 96;
            for (int i = 0; i < 96; i++) { float a = i * Mathf.PI * 2 / 96; line.SetPosition(i, new Vector3(Mathf.Sin(a) * .67f, Mathf.Cos(a) * .67f, 0)); }
        }
        static Mesh SphereMesh(float radius)
        {
            const int slices = 128, stacks = 64;
            var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
            for (int y = 0; y <= stacks; y++)
                for (int x = 0; x <= slices; x++)
                {
                    vertices.Add(AnchoredGlobe.GeographicPoint(x * 360f / slices - 180, y * 180f / stacks - 90, radius));
                    uv.Add(new Vector2((float)x / slices, (float)y / stacks));
                    if (x == slices || y == stacks) continue;
                    int a = y * (slices + 1) + x, b = a + slices + 1;
                    triangles.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                }
            var mesh = new Mesh { name = "Earth geographic UV sphere" };
            mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        static ExperienceDefinition[] CreateCatalog()
        {
            var names = new[] { "Life at Home", "The Neighborhood Café", "Customs Interview" };
            var ids = new[] { "apartment", "restaurant", "customs" };
            var themes = new[] { "La Vie à la Maison", "Le Café du Quartier", "Le Contrôle Douanier" };
            var descriptions = new[] { "Practice everyday communication in a home environment.", "Practice conversations in a café or restaurant.", "Practice communicating with airport or border-control staff." };
            var milestones = new[] { new[] { "understand-household-request", "ask-basic-question", "complete-home-task" }, new[] { "ask-about-menu", "place-order", "respond-to-waiter" }, new[] { "explain-visit", "answer-travel-questions", "complete-interview" } };
            var result = new ExperienceDefinition[3];
            for (int i = 0; i < result.Length; i++)
            {
                var d = ScriptableObject.CreateInstance<ExperienceDefinition>();
                d.id = ids[i]; d.displayName = names[i]; d.thematicTitle = themes[i]; d.description = descriptions[i];
                d.difficulty = i * 2 + 1; d.milestoneIds = milestones[i];
                AssetDatabase.CreateAsset(d, Root + "/Data/" + ids[i] + ".asset"); result[i] = d;
            }
            return result;
        }

        static void CreateStudy()
        {
            Asset("Desk1", environment, new Vector3(0, 0, 9.2f), .9f, 180, 2.35f);
            Asset("OfficeChair", environment, new Vector3(0, 0, 10.4f), 1.05f, 180);
            Asset("Shelves2", environment, new Vector3(-2.7f, 0, 12.35f), 2.3f);
            Asset("Shelves3", environment, new Vector3(2.7f, 0, 12.35f), 2.3f);
            Asset("Sofa1", environment, new Vector3(-3.05f, 0, 9.6f), .9f, 90);
            Asset("CoffeTable", environment, new Vector3(-2.35f, 0, 8.4f), .48f);
            Asset("BookPile1", environment, new Vector3(-2.35f, .49f, 8.4f), .16f);
            Asset("Plant2", environment, new Vector3(3.2f, 0, 7.1f), 1.4f);
            Asset("Window", environment, new Vector3(0, 1.5f, 12.81f), 1.3f, 180);
            var windowGlow = Cube("Window warm daylight", environment, new Vector3(0, 2.1f, 12.88f), new Vector3(2, 1.3f, .02f), white, false);
            Text("Study heading", environment, "02 / THE READING ROOM\nYour next chapter begins here", new Vector3(0, 2.98f, 12.75f), new Vector2(4.5f, .4f), .12f, ink.color);
            Cube("Study rug", environment, new Vector3(0, .008f, 8.7f), new Vector3(3.4f, .012f, 2.8f), teal, false);
            for (int x = -1; x <= 1; x += 2)
                Cube("Rug border", environment, new Vector3(x * 1.62f, .017f, 8.7f), new Vector3(.035f, .004f, 2.65f), gold, false);
            var lamp = Asset("DeskLight", environment, new Vector3(1.01f, .91f, 9.3f), .48f);
            var lampSwitch = lamp.AddComponent<LampSwitch>();
            lampSwitch.lamp = PointLight("Desk reading light", new Vector3(1, 1.45f, 9.2f), new Color(1, .75f, .42f), .7f, 2.2f);
            Button("Lamp switch", environment, new Vector3(1.03f, .93f, 8.9f), new Vector3(.12f, .04f, .12f), "LIGHT", lampSwitch.Toggle, true);
            var mug = Asset("Mug", environment, new Vector3(-.95f, .91f, 9.15f), .14f);
            foreach (var old in mug.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(old);
            var mugCollider = mug.AddComponent<BoxCollider>();
            var mugBounds = BoundsOf(mug); mugCollider.center = mug.transform.InverseTransformPoint(mugBounds.center); mugCollider.size = mugBounds.size;
            var rb = mug.AddComponent<Rigidbody>(); rb.mass = .2f; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var grab = mug.AddComponent<XRGrabInteractable>(); grab.colliders.Clear(); grab.colliders.Add(mugCollider);
            CreateBook();
        }

        static void CreateBook()
        {
            var bookRoot = Group("Interactive experience book", environment, new Vector3(0, 1.02f, 8.95f));
            bookRoot.localRotation = Quaternion.Euler(-22, 0, 0);
            var book = bookRoot.gameObject.AddComponent<InteractiveBook>();
            var navigator = new GameObject("Experience navigation").AddComponent<ExperienceNavigator>();
            navigator.session = session; book.navigator = navigator;
            Cube("Book support", environment, new Vector3(0, .96f, 9.1f), new Vector3(1.35f, .13f, .6f), wood);
            Cube("Back cover", bookRoot, new Vector3(.35f, 0, 0), new Vector3(.71f, .035f, .88f), navy);
            Cube("Spine", bookRoot, new Vector3(0, .035f, 0), new Vector3(.05f, .08f, .9f), gold);
            book.leftPageBlock = Cube("Left page block", bookRoot, new Vector3(-.35f, .04f, 0), new Vector3(.66f, .04f, .82f), paper);
            Cube("Right page block", bookRoot, new Vector3(.35f, .04f, 0), new Vector3(.66f, .04f, .82f), paper);
            for (int i = 0; i < 4; i++)
                Cube("Visible paper layers", bookRoot, new Vector3(0, .025f + i * .009f, -.411f), new Vector3(1.35f, .002f, .002f), gold, false);
            book.coverPivot = Group("Hinged front cover", bookRoot, new Vector3(0, .085f, 0));
            Cube("Leather front cover", book.coverPivot, new Vector3(.35f, 0, 0), new Vector3(.71f, .025f, .88f), navy);
            Text("Cover title", book.coverPivot, "ALTERLIFE\n\nA French Journey\n\nAn atlas of conversations", new Vector3(.35f, .015f, .06f), new Vector2(.6f, .55f), .04f, paper.color, Quaternion.Euler(90, 0, 0));
            book.openControl = Button("Open book", book.coverPivot, new Vector3(.35f, .025f, -.28f), new Vector3(.4f, .018f, .1f), "OPEN YOUR JOURNEY", book.Open, true).gameObject;
            book.turningPage = Group("Animated physical page", bookRoot, new Vector3(0, .075f, 0));
            Cube("Turning paper mesh", book.turningPage, new Vector3(.34f, 0, 0), new Vector3(.67f, .006f, .81f), paper, false);
            // Both sides carry ruled decoration; the page sweeps through real space above the binding.
            for (int i = 0; i < 5; i++)
                Cube("Page rule", book.turningPage, new Vector3(.34f, .004f, -.23f + i * .1f), new Vector3(.5f, .001f, .002f), gold, false);
            book.turningPage.gameObject.SetActive(false);
            book.previousControl = Button("Previous page corner", bookRoot, new Vector3(-.59f, .08f, -.35f), new Vector3(.15f, .02f, .1f), "BACK", book.Previous, true).gameObject;
            book.nextControl = Button("Next page corner", bookRoot, new Vector3(.59f, .08f, -.35f), new Vector3(.15f, .02f, .1f), "TURN", book.Next, true).gameObject;
            var spreads = new List<GameObject>();
            var intro = Group("00 — Contents", bookRoot, Vector3.zero);
            spreads.Add(intro.gameObject);
            PageText("Welcome page", intro, "YOUR FRENCH\nJOURNEY\n\nThree places.\nThree conversations.\nOne new perspective.", -.35f, .03f, .047f);
            PageText("Contents", intro, "CONTENTS\n\n01  Life at Home\n02  The Neighborhood Café\n03  Customs Interview\n\nTurn a page using its corner.\nSelect a miniature to begin.", .35f, .03f, .033f);
            for (int i = 0; i < 3; i++)
            {
                var d = session.catalog[i];
                var spread = Group("0" + (i + 1) + " — " + d.displayName, bookRoot, Vector3.zero);
                var chapter = spread.gameObject.AddComponent<BookChapter>();
                chapter.definition = d; chapter.navigator = navigator;
                Text("Chapter number", spread, "CHAPTER 0" + (i + 1) + " / FRANCE", new Vector3(-.35f, .066f, .32f), new Vector2(.59f, .08f), .024f, ink.color, Quaternion.Euler(90, 0, 0));
                Text("Chapter title", spread, d.displayName, new Vector3(-.35f, .067f, .2f), new Vector2(.58f, .18f), .052f, ink.color, Quaternion.Euler(90, 0, 0));
                Text("Thematic title", spread, d.thematicTitle, new Vector3(-.35f, .067f, .075f), new Vector2(.57f, .1f), .023f, ink.color, Quaternion.Euler(90, 0, 0));
                Text("Description", spread, d.description, new Vector3(-.35f, .067f, -.07f), new Vector2(.53f, .22f), .034f, ink.color, Quaternion.Euler(90, 0, 0));
                Text("Difficulty", spread, "DIFFICULTY  " + d.difficulty + "/5", new Vector3(-.35f, .067f, -.24f), new Vector2(.55f, .06f), .026f, ink.color, Quaternion.Euler(90, 0, 0));
                for (int star = 0; star < 5; star++) Star(spread, new Vector3(-.51f + star * .08f, .069f, -.31f), star < d.difficulty ? gold : cream);
                chapter.progressText = Text("Progress", spread, "Progress: 0%", new Vector3(.35f, .068f, .31f), new Vector2(.55f, .08f), .027f, ink.color, Quaternion.Euler(90, 0, 0));
                CreateMiniature(i, spread, chapter);
                Button("Select " + d.id, spread, new Vector3(.35f, .078f, -.23f), new Vector3(.43f, .017f, .12f), "SELECT EXPERIENCE", chapter.Select, true);
                spreads.Add(spread.gameObject);
            }
            book.spreads = spreads.ToArray();
            foreach (var spread in spreads) spread.SetActive(false);
            book.nextControl.SetActive(false); book.previousControl.SetActive(false);
            navigator.selectionText = Text("Selected chapter summary", environment, "Choose a miniature to select an experience.", new Vector3(-1.8f, 1.65f, 9.1f), new Vector2(1.6f, .7f), .065f, paper.color);
            var seal = Shape(PrimitiveType.Cylinder, "Selected chapter brass seal", environment, new Vector3(-1.8f, 1.15f, 9.1f), new Vector3(.12f, .02f, .12f), gold, false);
            seal.transform.rotation = Quaternion.Euler(90, 0, 0);
            navigator.selectionSeal = seal.GetComponent<Renderer>(); navigator.selectionSeal.enabled = false;
            navigator.confirmControl = Button("Confirm experience", environment, new Vector3(-1.8f, 1.05f, 9.1f), new Vector3(1.1f, .19f, .08f), "CONFIRM EXPERIENCE", navigator.Confirm).gameObject;
            navigator.confirmControl.SetActive(false);
            var prep = Group("Experience preparation alcove", environment, new Vector3(2.7f, 0, 10.7f));
            Cube("Preparation plinth", prep, new Vector3(0, .45f, 0), new Vector3(1.5f, .9f, 1), navy);
            Cube("Preparation brass top", prep, new Vector3(0, .92f, 0), new Vector3(1.54f, .04f, 1.04f), gold);
            Asset("BookOpen", prep, new Vector3(0, .95f, 0), .15f, 0, .55f);
            navigator.preparationText = Text("Preparation details", prep, "COMING SOON", new Vector3(0, 1.9f, -.25f), new Vector2(1.6f, 1.2f), .065f, paper.color);
            Button("Return to book", prep, new Vector3(0, 1.1f, -.55f), new Vector3(1.15f, .18f, .08f), "RETURN TO THE BOOK", navigator.ReturnToBook);
            navigator.preparation = prep.gameObject; prep.gameObject.SetActive(false);
            Text("Book instructions", environment, "OPEN • TURN • DISCOVER\nPoint and select the cover to open.\nUse the page corners to turn.\nSelect a miniature, then confirm.", new Vector3(1.8f, 1.9f, 9.3f), new Vector2(1.45f, .9f), .06f, paper.color);
        }
        static void PageText(string name, Transform parent, string value, float x, float z, float size)
            => Text(name, parent, value, new Vector3(x, .067f, z), new Vector2(.57f, .63f), size, ink.color, Quaternion.Euler(90, 0, 0));
        static void Star(Transform parent, Vector3 position, Material material)
        {
            var vertices = new Vector3[11]; var indices = new int[30];
            for (int i = 0; i < 10; i++)
            {
                float a = i * Mathf.PI / 5; float r = i % 2 == 0 ? .032f : .014f;
                vertices[i + 1] = new Vector3(Mathf.Sin(a) * r, 0, Mathf.Cos(a) * r);
                indices[i * 3] = 0; indices[i * 3 + 1] = i + 1; indices[i * 3 + 2] = (i + 1) % 10 + 1;
            }
            var mesh = new Mesh { vertices = vertices, triangles = indices }; mesh.RecalculateNormals();
            string path = Root + "/Generated/DifficultyStar.asset";
            var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!saved) { AssetDatabase.CreateAsset(mesh, path); saved = mesh; } else UnityEngine.Object.DestroyImmediate(mesh);
            var go = new GameObject("Difficulty star", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false); go.transform.localPosition = position;
            go.GetComponent<MeshFilter>().sharedMesh = saved; go.GetComponent<MeshRenderer>().sharedMaterial = material;
        }
        static void CreateMiniature(int index, Transform spread, BookChapter chapter)
        {
            var mini = Group("Selectable 3D miniature", spread, new Vector3(.35f, .075f, .02f));
            if (index == 0)
            {
                Asset("CoffeTable", mini, Vector3.zero, .12f);
                Asset("Mug", mini, new Vector3(.04f, .12f, 0), .04f);
                Asset("LoungeChair", mini, new Vector3(-.13f, 0, .11f), .15f, 145);
            }
            else if (index == 1)
            {
                Asset("ConferenceTable", mini, Vector3.zero, .11f, 0, .32f);
                Asset("Mug", mini, new Vector3(-.06f, .11f, 0), .04f);
                Asset("CoffePot", mini, new Vector3(.06f, .11f, 0), .07f);
                Cube("Miniature menu", mini, new Vector3(0, .16f, .05f), new Vector3(.06f, .1f, .009f), navy, false);
            }
            else
            {
                Cube("Miniature customs counter", mini, new Vector3(0, .06f, .07f), new Vector3(.34f, .12f, .1f), wood, false);
                Cube("Miniature passport", mini, new Vector3(0, .02f, -.07f), new Vector3(.14f, .025f, .19f), navy, false);
                Text("Passport emboss", mini, "PASSPORT", new Vector3(0, .034f, -.07f), new Vector2(.12f, .04f), .016f, gold.color, Quaternion.Euler(90, 0, 0));
                Shape(PrimitiveType.Sphere, "Passport seal", mini, new Vector3(0, .035f, -.11f), new Vector3(.025f, .002f, .025f), gold, false);
            }
            foreach (var c in mini.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(c);
            var collider = mini.gameObject.AddComponent<BoxCollider>(); collider.center = new Vector3(0, .11f, 0); collider.size = new Vector3(.4f, .25f, .32f);
            var control = mini.gameObject.AddComponent<PhysicalControl>();
            control.GetComponent<XRSimpleInteractable>().colliders.Clear(); control.GetComponent<XRSimpleInteractable>().colliders.Add(collider);
            UnityEventTools.AddPersistentListener(control.pressed, chapter.Select);
        }
        static void CreateComfort(GameObject rig)
        {
            var panel = Group("Comfort station", environment, new Vector3(-2.2f, 1.6f, -.4f));
            var comfort = panel.gameObject.AddComponent<ComfortOptions>();
            comfort.leftController = rig.GetComponentsInChildren<ControllerInputActionManager>(true).First(x => x.name.Contains("Left"));
            comfort.label = Text("Comfort instructions", panel, "TELEPORT", Vector3.zero, new Vector2(1.9f, .7f), .064f, paper.color);
            Button("Movement comfort toggle", panel, new Vector3(0, -.48f, 0), new Vector3(1.15f, .18f, .08f), "CHANGE MOVEMENT", comfort.ToggleSmoothMovement);
            Text("Controls", environment, "MOVE NATURALLY\nWalk within your real play space.\nRight stick left / right: snap turn.\nPoint + grip: interact or grab.\nKeep your Quest boundary enabled.", new Vector3(2.2f, 1.65f, -.4f), new Vector2(1.8f, .9f), .07f, paper.color);
        }
        static Bounds BoundsOf(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }
        static GameObject Asset(string name, Transform parent, Vector3 position, float height, float yaw = 0, float width = 0)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Office + "(Prb)" + name + ".prefab");
            if (!source) throw new InvalidOperationException("Missing supplied furniture: " + name);
            var wrapper = Group(name, parent, position);
            // Normalize outside the parent's tilted/scaled space; then parent into the final book/room.
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            instance.transform.position = Vector3.zero;
            instance.transform.rotation = Quaternion.Euler(0, yaw, 0);
            var initial = BoundsOf(instance);
            float scale = width > 0 ? width / initial.size.x : height / initial.size.y;
            instance.transform.localScale = Vector3.Scale(instance.transform.localScale, new Vector3(scale, height / initial.size.y, scale));
            var bounds = BoundsOf(instance);
            instance.transform.position += new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
            instance.transform.SetParent(wrapper, false);
            // SetParent(false) preserves normalized local coordinates constructed above.
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
            {
                renderer.sharedMaterials = renderer.sharedMaterials.Select(ConvertMaterial).ToArray();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
            foreach (var t in instance.GetComponentsInChildren<Transform>()) GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
            return wrapper.gameObject;
        }
        static Material ConvertMaterial(Material source)
        {
            if (!source) return cream;
            if (converted.TryGetValue(source, out var result)) return result;
            result = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Office " + source.name, enableInstancing = true };
            result.color = source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white;
            result.mainTexture = source.mainTexture;
            result.SetFloat("_Smoothness", .18f);
            AssetDatabase.CreateAsset(result, Root + "/Generated/Office " + source.name + ".mat");
            converted.Add(source, result);
            return result;
        }
    }
}
