using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Alterlife.Menu.Editor
{
    /// <summary>Authors ordinary, editable scene objects and persistent button callbacks.</summary>
    public static class MainMenuBuilder
    {
        public const string ScenePath = "Assets/Scenes/MainMenu.unity";
        const string Root = "Assets/Alterlife/Menu";
        const string RigPath = "Assets/Samples/XR Interaction Toolkit/3.5.1/Starter Assets/Prefabs/XR Origin (XR Rig).prefab";
        static TMP_FontAsset font;
        static readonly Color Cream = new Color32(243, 232, 210, 255);
        static readonly Color Brown = new Color32(88, 74, 64, 255);

        [MenuItem("Alterlife/Create Main Menu Scene")]
        public static void Create()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (File.Exists(ScenePath))
                throw new InvalidOperationException("MainMenu already exists. Edit the scene directly; the builder will not overwrite it.");

            ImportArt();
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/VRTemplateAssets/Fonts/Inter/Inter-Regular SDF.asset");
            if (font == null) throw new InvalidOperationException("The template Inter font is missing.");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(RigPath));
            rig.name = "Menu XR Origin";
            // Menu-only instance: keep the existing controller actions, but prevent locomotion while choosing.
            foreach (var provider in rig.GetComponentsInChildren<LocomotionProvider>(true)) provider.enabled = false;
            foreach (var interactor in rig.GetComponentsInChildren<NearFarInteractor>(true)) interactor.enableUIInteraction = true;
            var camera = rig.GetComponentInChildren<Camera>(true);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.nearClipPlane = .05f;
            new GameObject("XR Interaction Manager", typeof(XRInteractionManager));
            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(XRUIInputModule));
            eventSystem.GetComponent<XRUIInputModule>().enableXRInput = true;
            eventSystem.GetComponent<XRUIInputModule>().enableMouseInput = true;

            var canvasObject = new GameObject("Alterlife Main Menu", typeof(RectTransform), typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster),
                typeof(TrackedDeviceGraphicRaycaster), typeof(MainMenuController));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)canvas.transform;
            rect.sizeDelta = new Vector2(1440, 1024);
            rect.position = new Vector3(0, 1.36144f, 2f);
            rect.localScale = Vector3.one * .0016f;
            var scaler = canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 1;
            var controller = canvasObject.GetComponent<MainMenuController>();

            var world = Box("WorldSelection", rect, 0, 0, 1440, 1024);
            Fill("Background", world, 0, 0, 1440, 1024, Color.black);
            Label("Title", world, 288, 130, 930, 58, "ALTERLIFE VR:  TRAVEL. LEARN. LIVE", 48, Cream, TextAlignmentOptions.Left);
            Round("InstructionPanel", world, 303, 712, 864, 192, new Color32(76, 76, 76, 255), 72);
            var instructions = Label("Instructions", world, 331, 777, 825, 88,
                "Spin the world and select the start other life", 24, Cream);
            instructions.fontStyle = FontStyles.Normal;
            instructions.characterSpacing = 16;

            var globeMask = Round("GlobeMask", world, 479, 237, 512, 550, Color.black, 252);
            globeMask.gameObject.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic = true;
            var globeImage = Art("Globe", globeMask.rectTransform, -2, 19, 512, 531, "GlobeAtlas");
            globeImage.uvRect = new Rect(.9498f / 2.9007f, 1f - 1.2818f / 1.5615f, 1f / 2.9007f, 1f / 1.5615f);
            globeImage.raycastTarget = true;
            var globe = globeImage.gameObject.AddComponent<GlobeSelector>();
            Set(globe, "globeImage", globeImage);
            Set(globe, "dragSurface", globeImage.rectTransform);
            Set(globe, "menuCanvas", canvas);

            var tooltip = Round("FranceProgress", world, 693, 453, 126, 90, new Color32(96, 96, 96, 163), 17).gameObject;
            Label("Country", tooltip.transform, 12, 11, 102, 24, "France", 24, Color.white);
            Label("ProgressLabel", tooltip.transform, -12, 32, 149, 25, "Language Progress", 12, Color.white);
            Round("ProgressTrack", tooltip.transform, 16, 62, 98, 13, Color.black, 4);
            Label("Progress", tooltip.transform, 54, 63, 28, 12, "0%", 12, Color.white);
            tooltip.SetActive(false);
            // Larger transparent target preserves the small Figma pin while making it usable with a Quest ray.
            var pin = Hit("SelectFrance", world, 698, 404, 72, 84);
            var pinArt = Art("FrancePin", pin.transform, 22, 20, 29, 44, "CountryPins");
            pinArt.uvRect = new Rect(.2201f / 4.9934f, 1f - .9993f / 1.8544f, 1f / 4.9934f, 1f / 1.8544f);
            Feedback(pin, pinArt.rectTransform, 1.41f, tooltip);
            UnityEventTools.AddPersistentListener(pin.onClick, controller.SelectFrance);
            Set(globe, "franceMarker", pin.gameObject);
            Set(globe, "franceTooltip", tooltip);
            var left = ArtButton("RotateLeft", world, 364, 432, 115, 141, "ArrowLeft", 1.22f);
            var right = ArtButton("RotateRight", world, 991, 432, 115, 141, "ArrowRight", 1.22f);
            UnityEventTools.AddPersistentListener(left.onClick, globe.RotateLeft);
            UnityEventTools.AddPersistentListener(right.onClick, globe.RotateRight);

            var difficulty = Box("DifficultySelection", rect, 0, 0, 1440, 1024);
            difficulty.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            Art("Room", difficulty, -138, 0, 1833, 1024, "Room");
            Art("Book", difficulty, 89.883f, 160.79f, 1261, 703, "Book");
            var apartment = ArtButton("Apartment", difficulty, 196, 371, 329, 329, "Apartment", 1.149f);
            var customs = ArtButton("Customs", difficulty, 915, 371, 337, 337, "Customs", 1.14f);
            var cafe = ArtButton("Cafe", difficulty, 567, 386, 306, 306, "Cafe", 1.137f);
            Label("ApartmentTitle", difficulty, 249, 314, 276, 97, "LA VIE A LA  MAISON\n(Live in a House)", 24, Brown);
            Label("CafeTitle", difficulty, 557, 323, 329, 92, "LE CAFÉ DU QUARTIER\n(Coffé of the neighborhood)", 24, Brown);
            Label("CustomsTitle", difficulty, 915, 318, 313, 97, "LE CONTROLE DOUANIER\n(Airport Control)", 24, Brown);
            Rating(difficulty, 144, 644, 357, 675, 1);
            Rating(difficulty, 485, 644, 698, 675, 3);
            Rating(difficulty, 840, 652, 1053, 683, 5);
            UnityEventTools.AddIntPersistentListener(apartment.onClick, controller.SelectScenario, 0);
            UnityEventTools.AddIntPersistentListener(cafe.onClick, controller.SelectScenario, 1);
            UnityEventTools.AddIntPersistentListener(customs.onClick, controller.SelectScenario, 2);
            var back = Hit("BackToWorld", difficulty, 40, 926, 230, 64);
            Round("BackSurface", back.transform, 0, 0, 230, 64, new Color32(45, 35, 28, 230), 22);
            Label("BackLabel", back.transform, 0, 0, 230, 64, "‹  World", 24, Cream);
            UnityEventTools.AddPersistentListener(back.onClick, controller.ShowWorld);
            var status = Label("SelectedScenario", difficulty, 330, 930, 1020, 54, string.Empty, 24, Cream);

            Set(controller, "globePanel", world.gameObject);
            Set(controller, "difficultyPanel", difficulty.gameObject);
            Set(controller, "globe", globe);
            Set(controller, "selectionLabel", status);
            var serialized = new SerializedObject(controller);
            var selections = serialized.FindProperty("scenarios");
            selections.arraySize = 3;
            selections.GetArrayElementAtIndex(0).objectReferenceValue = apartment.GetComponent<MenuFeedback>();
            selections.GetArrayElementAtIndex(1).objectReferenceValue = cafe.GetComponent<MenuFeedback>();
            selections.GetArrayElementAtIndex(2).objectReferenceValue = customs.GetComponent<MenuFeedback>();
            serialized.ApplyModifiedPropertiesWithoutUndo();
            difficulty.gameObject.SetActive(false);
            PrefabUtility.SaveAsPrefabAssetAndConnect(canvasObject, Root + "/MainMenu.prefab", InteractionMode.AutomatedAction);
            canvas.worldCamera = camera;
            PrefabUtility.RecordPrefabInstancePropertyModifications(canvas);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) }
                .Concat(EditorBuildSettings.scenes.Where(s => s.path != ScenePath)).ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("ALTERLIFE_MENU_CREATED " + ScenePath);
        }

        static void ImportArt()
        {
            AssetDatabase.Refresh();
            foreach (var path in Directory.GetFiles(Root + "/Art", "*.png"))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path.Replace('\\', '/'));
                importer.textureType = TextureImporterType.Default;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.maxTextureSize = 2048;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
        }

        static RectTransform Box(string name, Transform parent, float x, float y, float w, float h)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(w, h);
            rect.anchoredPosition = new Vector2(x + w / 2, -y - h / 2);
            rect.gameObject.layer = 5;
            return rect;
        }

        static UnityEngine.UI.Image Fill(string name, Transform parent, float x, float y, float w, float h, Color color)
        {
            var image = Box(name, parent, x, y, w, h).gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        static RoundedGraphic Round(string name, Transform parent, float x, float y, float w, float h, Color color, float radius)
        {
            var graphic = Box(name, parent, x, y, w, h).gameObject.AddComponent<RoundedGraphic>();
            graphic.color = color;
            graphic.Radius = radius;
            graphic.raycastTarget = false;
            return graphic;
        }

        static UnityEngine.UI.RawImage Art(string name, Transform parent, float x, float y, float w, float h, string asset)
        {
            var image = Box(name, parent, x, y, w, h).gameObject.AddComponent<UnityEngine.UI.RawImage>();
            image.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Art/" + asset + ".png");
            if (image.texture == null) throw new InvalidOperationException("Missing Figma asset: " + asset);
            image.raycastTarget = false;
            // CSS object-cover: preserve source proportions and crop, never stretch the illustration.
            float sourceAspect = (float)image.texture.width / image.texture.height;
            float targetAspect = w / h;
            if (sourceAspect > targetAspect)
            {
                float width = targetAspect / sourceAspect;
                image.uvRect = new Rect((1 - width) / 2, 0, width, 1);
            }
            else
            {
                float height = sourceAspect / targetAspect;
                image.uvRect = new Rect(0, (1 - height) / 2, 1, height);
            }
            return image;
        }

        static TMP_Text Label(string name, Transform parent, float x, float y, float w, float h, string text, float size, Color color,
            TextAlignmentOptions alignment = TextAlignmentOptions.Center)
        {
            var label = Box(name, parent, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.fontSize = size;
            label.fontStyle = FontStyles.Bold;
            label.characterSpacing = -font.boldSpacing;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.alignment = alignment;
            label.color = color;
            label.text = text;
            label.raycastTarget = false;
            return label;
        }

        static UnityEngine.UI.Button Hit(string name, Transform parent, float x, float y, float w, float h)
        {
            var image = Fill(name, parent, x, y, w, h, Color.clear);
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = image;
            button.transition = UnityEngine.UI.Selectable.Transition.None;
            return button;
        }

        static UnityEngine.UI.Button ArtButton(string name, Transform parent, float x, float y, float w, float h, string asset, float scale)
        {
            var button = Hit(name, parent, x, y, w, h);
            var visual = Art("Illustration", button.transform, 0, 0, w, h, asset);
            Feedback(button, visual.rectTransform, scale);
            return button;
        }

        static void Feedback(UnityEngine.UI.Button button, RectTransform visual, float scale, GameObject tooltip = null)
        {
            var feedback = button.gameObject.AddComponent<MenuFeedback>();
            Set(feedback, "visual", visual);
            Set(feedback, "tooltip", tooltip);
            var data = new SerializedObject(feedback);
            data.FindProperty("highlightedScale").floatValue = scale;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        static void Rating(Transform parent, float x, float y, float starsX, float starsY, int filled)
        {
            Label("Difficulty" + filled, parent, x, y, 276, 97, "Difficulty", 24, Brown);
            for (int i = 0; i < 5; i++)
            {
                var star = Art("Star" + filled + "_" + i, parent, starsX + i * 35 + .8575f, starsY,
                    33.285f, 31.6575f, i < filled ? "StarFilled" : "StarEmpty");
                star.uvRect = new Rect(0, 0, 1, 1);
            }
        }

        static void Set(UnityEngine.Object target, string field, UnityEngine.Object value)
        {
            var data = new SerializedObject(target);
            data.FindProperty(field).objectReferenceValue = value;
            data.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
