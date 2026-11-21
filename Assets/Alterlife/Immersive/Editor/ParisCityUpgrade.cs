using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Alterlife.Immersive.Editor
{
    /// <summary>Authored 3D city and terrace; original imported assets and gameplay stay intact.</summary>
    public static class ParisCityUpgrade
    {
        const string Folder = "Assets/Alterlife/Immersive/Paris/City";
        static Transform root, city, terrace;
        static Material limestone, trim, roof, glass, iron, wood, canvas, terracotta, leaf, ground, road, brass;
        static readonly Dictionary<Material, Material> vegetationMaterials = new Dictionary<Material, Material>();
        static System.Random random;
        static Mesh canopy;

        // Delegates to the newer walkable street-level Parisian neighborhood builder (Blender Haussmann + Hybrid Bricktown).
        public static void Refine()
        {
            ParisWalkableCityBuilder.BuildWalkableParis();
        }

        public static void Run()
        {
            Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
            var scene = EditorSceneManager.OpenScene(ParisPrototypeBuilder.ScenePath);
            root = GameObject.Find("Paris Restaurant Prototype").transform;
            if (root.Find("Paris cityscape")) throw new InvalidOperationException("City already authored. Edit the scene rather than overwriting it.");
            random = new System.Random(173);
            vegetationMaterials.Clear();
            limestone = Mat("Honey limestone", new Color(.78f, .69f, .53f));
            trim = Mat("Pale carved stone", new Color(.94f, .85f, .67f));
            roof = Mat("Slate mansard roofs", new Color(.15f, .21f, .28f));
            glass = Mat("Blue window recess", new Color(.16f, .25f, .29f));
            iron = Mat("Paris ironwork", new Color(.055f, .085f, .085f));
            wood = Mat("Cafe oak", new Color(.65f, .36f, .17f));
            canvas = Mat("Cream awning", new Color(.97f, .85f, .63f));
            terracotta = Mat("Terracotta pots", new Color(.56f, .25f, .13f));
            leaf = Mat("Garden hedge", new Color(.28f, .39f, .12f));
            ground = Mat("Paris paving", new Color(.63f, .59f, .49f));
            road = Mat("Boulevard asphalt", new Color(.24f, .27f, .27f));
            brass = Mat("Warm lantern brass", new Color(.55f, .38f, .15f));
            AddWoodGrain();
            city = Group("Paris cityscape", root, Vector3.zero);
            terrace = Group("Cafe terrace detailing", root, Vector3.zero);
            var old = root.Find("Previous terrace styling — preserved");
            if (!old) old = Group("Previous terrace styling — preserved", root, Vector3.zero);
            foreach (var t in root.Cast<Transform>().ToArray())
                if (t.name.StartsWith("Paris city building") || new[] { "City ground", "LoungeChair", "Tile grout", "Terrace railing", "Striped terrace awning" }.Contains(t.name))
                    t.SetParent(old, true);
            old.gameObject.SetActive(false);
            // Keep actual gameplay objects, colliders, references and user script settings.
            if (!root.GetComponent<SceneJourney>()) root.gameObject.AddComponent<SceneJourney>();
            root.Find("Eiffel Tower").position = new Vector3(8, -14.2f, 72);
            root.Find("Eiffel Tower").localScale = Vector3.one * 1.15f;
            var towerIron = Mat("Eiffel painted iron", new Color(.34f, .30f, .25f));
            foreach (var r in root.Find("Eiffel Tower").GetComponentsInChildren<Renderer>()) r.sharedMaterials = r.sharedMaterials.Select(_ => towerIron).ToArray();
            var sky = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Paris summer sky.mat");
            if (!sky) { sky = new Material(Shader.Find("Skybox/Procedural")); AssetDatabase.CreateAsset(sky, Folder + "/Paris summer sky.mat"); }
            sky.SetColor("_SkyTint", new Color(.58f, .68f, .84f)); sky.SetFloat("_AtmosphereThickness", .8f); sky.SetFloat("_Exposure", 1.1f);
            sky.SetFloat("_SunSize", .025f); RenderSettings.skybox = sky;
            var camera = root.GetComponentInChildren<Camera>(); camera.farClipPlane = 240;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = .0045f; RenderSettings.fogColor = new Color(.68f, .77f, .81f);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.65f, .74f, .84f);
            RenderSettings.ambientEquatorColor = new Color(.57f, .52f, .42f);
            RenderSettings.ambientGroundColor = new Color(.27f, .24f, .20f);
            var sun = root.Find("Paris late afternoon sun").GetComponent<Light>();
            sun.color = new Color(1, .86f, .67f); sun.intensity = 1.25f; sun.shadowStrength = .75f;
            sun.transform.rotation = Quaternion.Euler(48, -40, 0);
            City(); Terrace();
            Combine(city, "City"); Combine(terrace, "Terrace");
            EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            FinishPresentation();
            Debug.Log("ALTERLIFE_PARIS_CITY_UPGRADED");
        }

        public static void FinishPresentation()
        {
            var scene = EditorSceneManager.OpenScene(ParisPrototypeBuilder.ScenePath);
            root = GameObject.Find("Paris Restaurant Prototype").transform;
            // Compact subtitles keep the city visible instead of covering it with a large solid panel.
            var dialogue = root.Find("Waiter dialogue");
            dialogue.Find("Dialogue backing").gameObject.SetActive(false);
            var text = dialogue.GetComponentInChildren<TMP_Text>();
            const string fontPath = Folder + "/Outlined dialogue font.mat";
            var fontMat = AssetDatabase.LoadAssetAtPath<Material>(fontPath);
            if (!fontMat)
            {
                fontMat = new Material(text.font.material); fontMat.SetFloat("_OutlineWidth", .18f);
                fontMat.SetColor("_OutlineColor", new Color(.10f,.10f,.08f)); fontMat.EnableKeyword("OUTLINE_ON");
                AssetDatabase.CreateAsset(fontMat, fontPath);
            }
            text.fontSharedMaterial = fontMat; text.color = new Color(.98f,.96f,.85f);
            var journal = root.Find("Conversation journal");
            journal.Find("Journal wood frame").GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Alterlife/Immersive/Paris/Ivory.mat");
            foreach (var label in journal.GetComponentsInChildren<TMP_Text>()) label.color = new Color(.19f,.16f,.12f);
            if (!root.Find("Paris sky clouds"))
            {
                var clouds = Group("Paris sky clouds", root, Vector3.zero);
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "/Faceted canopy.asset");
                var material = Mat("Summer cloud white", new Color(.96f,.95f,.9f)); material.shader = Shader.Find("Universal Render Pipeline/Unlit");
                for (int i = 0; i < 5; i++) for (int j = 0; j < 3; j++)
                {
                    var cloud = new GameObject("Stylized cloud volume", typeof(MeshFilter), typeof(MeshRenderer)); cloud.transform.SetParent(clouds,false);
                    cloud.transform.localPosition = new Vector3(-85 + i * 43 + j * 7, 28 + (i % 3) * 6 + (j == 1 ? 1.5f : 0), 150 + i % 2 * 12);
                    cloud.transform.localScale = new Vector3(18, j == 1 ? 6 : 3.5f, 6);
                    cloud.GetComponent<MeshFilter>().sharedMesh = mesh; cloud.GetComponent<Renderer>().sharedMaterial = material;
                }
                Combine(clouds, "Clouds");
            }
            EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            Debug.Log("ALTERLIFE_PARIS_PRESENTATION_FINISHED");
        }

        static void City()
        {
            Box("Continuous city ground", city, new Vector3(0, -5.2f, 55), new Vector3(220, .35f, 190), ground);
            Box("Tree-lined boulevard", city, new Vector3(0, -4.98f, 39), new Vector3(13, .08f, 83), road);
            Box("Cross street", city, new Vector3(0, -4.92f, 24), new Vector3(110, .06f, 8), road);
            for (int side = -1; side <= 1; side += 2)
            {
                Box("Boulevard pavement", city, new Vector3(side * 8.5f, -4.87f, 43), new Vector3(3.4f, .22f, 90), trim);
                for (int block = 0; block < 4; block++)
                {
                    // The central view corridor stays open to the landmark.
                    float z = 15 + block * 17;
                    Building(new Vector3(side * (17.5f + (block % 2) * 1.7f), -4.8f, z), 11, 10, 10.5f + block % 2 * 2.7f, side < 0 ? 22 : -18);
                    Tree(new Vector3(side * 7.9f, -4.75f, z - 4), 6.4f + block % 3, block % 3);
                    LampPost(new Vector3(side * 6.9f, -4.75f, z + 3));
                    for (int k = 0; k < 4; k++)
                        Box("Boulevard lane marking", city, new Vector3(.1f, -4.91f, z + k * 3.4f), new Vector3(.10f, .01f, 1.8f), trim);
                }
                Building(new Vector3(side * 35, -4.8f, 13), 12, 12, 15, side * -7);
                Building(new Vector3(side * 36, -4.8f, 42), 13, 12, 13, side * 4);
                Building(new Vector3(side * 38, -4.8f, 65), 12, 12, 15.5f, 0);
            }
            // Lower canopy forms an urban park between the terrace and the city blocks.
            for (int i = 0; i < 15; i++)
            {
                float x = -25 + i * 3.6f;
                Tree(new Vector3(x, -4.8f, 7.5f + (i % 3) * 1.8f), 4.7f + (i % 4) * .45f, i % 3);
            }
            for (int i = 0; i < 12; i++) Tree(new Vector3(-30 + i * 5.3f, -4.8f, 89 + i % 2 * 5), 7 + i % 3, i % 3);
            for (int i = 0; i < 7; i++)
                Box("Pedestrian crossing", city, new Vector3(-4.5f + i * 1.5f, -4.85f, 18.5f), new Vector3(.65f, .015f, 2.5f), trim);
            // Roofs behind the park add a distant urban horizon, rather than an empty ground plane.
            for (int i = 0; i < 7; i++) Building(new Vector3(-63 + i * 20, -5, 115), 14, 12, 13 + i % 3 * 3, 0);
        }

        static void Building(Vector3 pos, float w, float d, float h, float yaw)
        {
            var b = Group("Haussmann city block", city, pos); b.localRotation = Quaternion.Euler(0, yaw, 0);
            int tone = random.Next(4);
            var stoneTone = Mat("Facade limestone " + tone, new Color(.75f + tone * .035f, .67f + tone * .03f, .53f + tone * .035f));
            Box("Limestone building", b, new Vector3(0, h / 2, 0), new Vector3(w, h, d), stoneTone);
            for (int f = 0; f <= 4; f++) Box("Stone cornice", b, new Vector3(0, f * h / 4, 0), new Vector3(w + .24f, .18f, d + .24f), trim);
            Roof(b, w + .5f, d + .5f, h, 2.5f);
            for (int face = 0; face < 4; face++)
            {
                var facade = Group("Detailed street facade", b, Vector3.zero); facade.localRotation = Quaternion.Euler(0, face * 90, 0);
                float width = face % 2 == 0 ? w : d; float depth = face % 2 == 0 ? d : w;
                int count = Mathf.RoundToInt(width / 2.2f);
                for (int level = 0; level < 4; level++)
                {
                    float y = .5f + h / 4 * level;
                    for (int i = 0; i < count; i++)
                    {
                        float x = -width / 2 + (i + .5f) * width / count;
                        float paneHeight = h / 4 - .8f;
                        Window(facade, new Vector3(x, y + paneHeight / 2 + .15f, -depth / 2 - .08f), 1.05f, paneHeight, level == 1 || level == 3);
                    }
                }
                for (int i = 0; i < count; i++)
                {
                    float x = -width / 2 + (i + .5f) * width / count;
                    Box("Dormer stone surround", facade, new Vector3(x, h + .85f, -depth / 2 + .38f), new Vector3(1.12f, 1.45f, .48f), trim);
                    Box("Dormer inset window", facade, new Vector3(x, h + .86f, -depth / 2 + .12f), new Vector3(.7f, 1.03f, .04f), glass);
                    Box("Dormer cap", facade, new Vector3(x, h + 1.6f, -depth / 2 + .32f), new Vector3(1.32f, .16f, .7f), roof);
                }
            }
            for (int i = -1; i <= 1; i += 2)
            {
                Box("Paris chimney", b, new Vector3(i * w * .27f, h + 2.6f, .8f), new Vector3(.85f, 1.35f, .6f), trim);
                for (int j = 0; j < 3; j++) Shape(PrimitiveType.Cylinder, "Chimney pot", b, new Vector3(i * w * .27f - .25f + j * .25f, h + 3.4f, .8f), new Vector3(.14f, .25f, .14f), terracotta);
            }
        }
        static void Window(Transform parent, Vector3 p, float w, float h, bool balcony)
        {
            Box("Window moulding", parent, p, new Vector3(w + .22f, h + .22f, .16f), trim);
            Box("Recessed dark glass", parent, p + Vector3.back * .09f, new Vector3(w, h, .04f), glass);
            Box("French window centre bar", parent, p + Vector3.back * .13f, new Vector3(.05f, h, .04f), trim);
            Box("French window cross bar", parent, p + Vector3.back * .13f, new Vector3(w, .05f, .04f), trim);
            if (!balcony) return;
            Box("Balcony stone shelf", parent, p + new Vector3(0, -h / 2, -.27f), new Vector3(w + .42f, .13f, .65f), trim);
            Box("Balcony handrail", parent, p + new Vector3(0, -h / 2 + .68f, -.55f), new Vector3(w + .3f, .055f, .055f), iron);
            for (int i = 0; i < 5; i++) Box("Wrought iron balcony spindle", parent, p + new Vector3(-w / 2 + i * w / 4, -h / 2 + .34f, -.55f), new Vector3(.035f, .68f, .035f), iron);
        }
        static void Roof(Transform parent, float w, float d, float y, float height)
        {
            var vs = new[] { new Vector3(-w/2,y,-d/2), new Vector3(w/2,y,-d/2), new Vector3(w/2,y,d/2), new Vector3(-w/2,y,d/2),
                new Vector3(-w/2+1.3f,y+height,-d/2+1.3f), new Vector3(w/2-1.3f,y+height,-d/2+1.3f), new Vector3(w/2-1.3f,y+height,d/2-1.3f), new Vector3(-w/2+1.3f,y+height,d/2-1.3f) };
            var mesh = new Mesh { vertices = vs, triangles = new[] {0,4,5,0,5,1,1,5,6,1,6,2,2,6,7,2,7,3,3,7,4,3,4,0,4,7,6,4,6,5} };
            mesh.RecalculateNormals();
            var go = new GameObject("Mansard roof", typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh; go.GetComponent<Renderer>().sharedMaterial = roof;
        }

        static void Terrace()
        {
            var tileMats = Enumerable.Range(0, 5).Select(i => Mat("Terrace stone " + i, new Color(.60f + i * .035f, .49f + i * .033f, .36f + i * .03f))).ToArray();
            for (int z = 0; z < 10; z++) for (int x = 0; x < 12; x++)
                Box("Individual terrace paver", terrace, new Vector3(-3.66f + x * .665f, .006f, -2.65f + z * .7f), new Vector3(.65f, .012f, .685f), tileMats[random.Next(5)]);
            Rail(new Vector3(0, 0, 4), 8, 0); Rail(new Vector3(-4, 0, .5f), 7, 90); Rail(new Vector3(4, 0, .5f), 7, 90);
            foreach (var t in root.GetComponentsInChildren<Transform>().Where(t => t.name == "Round wooden tabletop")) t.GetComponent<Renderer>().sharedMaterial = wood;
            Chair(new Vector3(-3.15f, 0, 1.75f), 35); Chair(new Vector3(-2.1f, 0, 2.95f), 205);
            Chair(new Vector3(2.1f, 0, 1.75f), -35); Chair(new Vector3(3.15f, 0, 2.95f), 145);
            Pot(new Vector3(-3.5f, 0, -.9f), 1.6f); Pot(new Vector3(3.5f, 0, -.8f), 1.7f);
            for (int i = 0; i < 5; i++)
            {
                var p = new Vector3(-3.1f + i * 1.55f, .12f, 3.7f);
                Box("Balcony garden planter", terrace, p, new Vector3(1.0f, .28f, .34f), terracotta);
                Box("Planter soil", terrace, p + Vector3.up * .15f, new Vector3(.94f, .025f, .28f), wood);
                for (int j = 0; j < 3; j++) Plant("LPW_Flower_A1_H70cm_01", terrace, p + new Vector3(-.28f + j * .28f, .16f, 0), .34f);
            }
            foreach (var table in new[] { new Vector3(-2.6f, .82f, 2.3f), new Vector3(2.6f, .82f, 2.3f) })
            {
                Shape(PrimitiveType.Cylinder, "Table flower pot", terrace, table + Vector3.up * .065f, new Vector3(.16f, .065f, .16f), trim);
                Plant("LPW_Flower_A1_H70cm_01", terrace, table + Vector3.up * .13f, .25f);
            }
            Awning(); Facade();
            var journal = root.Find("Conversation journal"); journal.position = new Vector3(-1.35f, 1.1f, .55f); journal.localScale = Vector3.one * .58f;
            root.Find("Journal stand").position = new Vector3(-1.35f, .32f, .59f);
            root.Find("Restart restaurant").position = new Vector3(-1.35f, .57f, .55f);
            root.Find("Waiter dialogue").localScale = Vector3.one * .8f;
            root.Find("Waiter dialogue").position = new Vector3(-.8f, 1.98f, 1.25f);
            // Real table setting, small geometric details visible at arm's length.
            var plate = root.Find("Dinner plate"); plate.localScale = new Vector3(.30f, .012f, .30f); plate.GetComponent<Renderer>().sharedMaterial = trim;
            var rim = Shape(PrimitiveType.Cylinder, "Plate inset", terrace, plate.position + Vector3.up * .015f, new Vector3(.235f, .003f, .235f), canvas);
            for (int i = 0; i < 4; i++) Box("Fork tine", terrace, new Vector3(.5f - .009f + i * .006f, .845f, .05f), new Vector3(.003f, .003f, .035f), brass);
        }
        static void Rail(Vector3 position, float width, float yaw)
        {
            var rail = Group("Cross-braced terrace railing", terrace, position); rail.localRotation = Quaternion.Euler(0, yaw, 0);
            Box("Top rail", rail, new Vector3(0, 1.07f, 0), new Vector3(width, .085f, .07f), iron);
            Box("Lower rail", rail, new Vector3(0, .17f, 0), new Vector3(width, .055f, .045f), iron);
            Box("Upper detail", rail, new Vector3(0, .91f, 0), new Vector3(width, .035f, .035f), iron);
            int panels = Mathf.RoundToInt(width / 1.33f);
            for (int i = 0; i <= panels; i++) Box("Railing post", rail, new Vector3(-width / 2 + i * width / panels, .55f, 0), new Vector3(.06f, 1.1f, .06f), iron);
            for (int i = 0; i < panels; i++)
            {
                float x = -width / 2 + (i + .5f) * width / panels;
                Beam("Cross brace", rail, new Vector3(x - width / panels / 2, .18f, 0), new Vector3(x + width / panels / 2, .9f, 0), .025f, iron);
                Beam("Cross brace", rail, new Vector3(x + width / panels / 2, .18f, 0), new Vector3(x - width / panels / 2, .9f, 0), .025f, iron);
                Shape(PrimitiveType.Sphere, "Iron rosette", rail, new Vector3(x, .54f, -.015f), new Vector3(.11f, .11f, .04f), brass);
            }
            var guard = rail.gameObject.AddComponent<BoxCollider>(); guard.center = new Vector3(0, .7f, 0); guard.size = new Vector3(width, 1.4f, .12f);
        }
        static void Chair(Vector3 position, float yaw)
        {
            var c = Group("Paris folding bistro chair", terrace, position); c.localRotation = Quaternion.Euler(0, yaw, 0);
            for (int i = 0; i < 4; i++) Box("Wooden seat slat", c, new Vector3(0, .45f, -.18f + i * .115f), new Vector3(.43f, .028f, .095f), wood);
            for (int side = -1; side <= 1; side += 2)
            {
                Beam("Folding iron leg", c, new Vector3(side * .20f, .02f, -.24f), new Vector3(side * .20f, .5f, .19f), .026f, iron);
                Beam("Folding iron leg", c, new Vector3(side * .20f, .02f, .25f), new Vector3(side * .20f, .5f, -.19f), .026f, iron);
                Beam("Chair back upright", c, new Vector3(side * .20f, .42f, .18f), new Vector3(side * .20f, .94f, .26f), .025f, iron);
            }
            for (int i = 0; i < 3; i++) Box("Backrest oak slat", c, new Vector3(0, .69f + i * .1f, .235f + i * .015f), new Vector3(.47f, .075f, .024f), wood);
            var col = c.gameObject.AddComponent<BoxCollider>(); col.center = new Vector3(0, .46f, .02f); col.size = new Vector3(.49f, .92f, .53f);
        }
        static void Awning()
        {
            for (int i = 0; i < 16; i++)
            {
                var stripe = Box("Canvas awning stripe", terrace, new Vector3(-3.75f + i * .5f, 2.95f, -.9f), new Vector3(.5f, .045f, 4.2f), i % 3 == 0 ? trim : canvas);
                stripe.transform.localRotation = Quaternion.Euler(4, 0, 0);
                Box("Awning scalloped valance", terrace, new Vector3(-3.75f + i * .5f, 2.72f, 1.2f), new Vector3(.49f, .20f, .035f), i % 3 == 0 ? trim : canvas);
            }
            for (int side = -1; side <= 1; side += 2) Beam("Awning support", terrace, new Vector3(side * 3.6f, 2.2f, -2.85f), new Vector3(side * 3.6f, 2.85f, 1.2f), .035f, iron);
        }
        static void Facade()
        {
            var side = Group("Cafe window corner", terrace, new Vector3(-2.05f, 0, -.7f));
            side.localRotation = Quaternion.Euler(0, 90, 0);
            var wall = Box("Cafe side wall", side, new Vector3(0, 1.55f, 0), new Vector3(2.8f, 3.1f, .18f), trim);
            wall.AddComponent<BoxCollider>();
            // The terrace sees the positive local Z side of this corner.
            var windowFace = Group("Terrace facing cafe window", side, Vector3.zero); windowFace.localRotation = Quaternion.Euler(0,180,0);
            Window(windowFace, new Vector3(0, 1.45f, -.12f), 1.75f, 2.2f, false);
            root.Find("Restaurant facade").GetComponent<Renderer>().sharedMaterial = trim;
            foreach (var t in root.Cast<Transform>().Where(t => t.name == "Cafe window glass")) t.GetComponent<Renderer>().sharedMaterial = glass;
            for (int i = -1; i <= 1; i++)
            {
                float x = i * 2.3f;
                Box("Window stone sill", terrace, new Vector3(x, .33f, -2.65f), new Vector3(1.85f, .13f, .35f), limestone);
                Box("Window transom", terrace, new Vector3(x, 1.95f, -2.72f), new Vector3(1.45f, .055f, .045f), iron);
                Lantern(new Vector3(x + .94f, 2.1f, -2.57f));
            }
            var sign = Box("Cafe name plaque", terrace, new Vector3(0, 2.65f, -2.7f), new Vector3(2.1f, .38f, .06f), iron);
            var text = new GameObject("Cafe name lettering", typeof(TextMeshPro)).GetComponent<TextMeshPro>();
            text.transform.SetParent(root, false); text.transform.position = sign.transform.position + Vector3.forward * .04f;
            text.transform.rotation = Quaternion.Euler(0, 180, 0); text.fontSize = .6f; text.text = "CAFÉ DES JARDINS";
            text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/VRTemplateAssets/Fonts/Inter/Inter-Regular SDF.asset");
            text.color = canvas.color; text.alignment = TextAlignmentOptions.Center; text.rectTransform.sizeDelta = new Vector2(2, .35f);
        }
        static void Lantern(Vector3 p)
        {
            var l = Group("Warm cafe lantern", terrace, p);
            Box("Lantern golden pane", l, Vector3.zero, new Vector3(.19f, .28f, .16f), canvas);
            Box("Lantern roof", l, new Vector3(0, .18f, 0), new Vector3(.3f, .08f, .24f), iron);
            Box("Lantern base", l, new Vector3(0, -.18f, 0), new Vector3(.26f, .05f, .21f), iron);
            for (int s = -1; s <= 1; s += 2) Box("Lantern frame", l, new Vector3(s * .105f, 0, .095f), new Vector3(.025f, .32f, .025f), iron);
        }
        static void LampPost(Vector3 p)
        {
            Shape(PrimitiveType.Cylinder, "Boulevard lamp column", city, p + Vector3.up * 1.6f, new Vector3(.11f, 1.6f, .11f), iron);
            Box("Street lamp glass", city, p + Vector3.up * 3.35f, new Vector3(.32f, .4f, .32f), canvas);
            Box("Street lamp cap", city, p + Vector3.up * 3.6f, new Vector3(.46f, .10f, .46f), iron);
        }
        static void Pot(Vector3 p, float height)
        {
            Shape(PrimitiveType.Cylinder, "Large terracotta planter", terrace, p + Vector3.up * .24f, new Vector3(.50f, .24f, .50f), terracotta);
            Shape(PrimitiveType.Cylinder, "Planter rim", terrace, p + Vector3.up * .47f, new Vector3(.56f, .035f, .56f), terracotta);
            Plant("LPW_Tree_A1_6.5m_01", terrace, p + Vector3.up * .43f, height);
        }
        static void Tree(Vector3 p, float height, int variant)
        {
            if (p.z > 85 && variant == 2) Plant("LPW_Tree_C1_9m_01", city, p, height);
            else Broadleaf(city, p, height, variant);
        }
        static void Broadleaf(Transform parent, Vector3 p, float height, int variant)
        {
            var t = Group("Paris park broadleaf tree", parent, p);
            var bark = Mat("Plane tree bark", new Color(.34f, .25f, .15f));
            Shape(PrimitiveType.Cylinder, "Tree trunk", t, Vector3.up * height * .32f, new Vector3(height * .055f, height * .32f, height * .055f), bark);
            if (!canopy)
            {
                canopy = AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "/Faceted canopy.asset");
                if (!canopy)
                {
                    var points = new List<Vector3>(); var triangles = new List<int>();
                    const int rings = 6, segments = 10;
                    Func<int,int,Vector3> point = (r,s) => new Vector3(Mathf.Sin(r * Mathf.PI / rings) * Mathf.Cos(s * Mathf.PI * 2 / segments), Mathf.Cos(r * Mathf.PI / rings), Mathf.Sin(r * Mathf.PI / rings) * Mathf.Sin(s * Mathf.PI * 2 / segments)) * .5f;
                    for (int r = 0; r < rings; r++) for (int s = 0; s < segments; s++)
                    {
                        var a = point(r,s); var b = point(r+1,s); var c = point(r+1,s+1); var d = point(r,s+1);
                        int n = points.Count; points.AddRange(new[] {a,c,b,a,d,c}); triangles.AddRange(Enumerable.Range(n,6));
                    }
                    canopy = new Mesh { vertices = points.ToArray(), triangles = triangles.ToArray() }; canopy.RecalculateNormals();
                    AssetDatabase.CreateAsset(canopy, Folder + "/Faceted canopy.asset");
                }
            }
            for (int i = 0; i < 5; i++)
            {
                float angle = i * 1.9f;
                var c = new GameObject("Faceted leafy crown", typeof(MeshFilter), typeof(MeshRenderer)); c.transform.SetParent(t,false);
                c.transform.localPosition = new Vector3(Mathf.Cos(angle) * height * .15f, height * (.65f + i % 2 * .12f), Mathf.Sin(angle) * height * .15f);
                c.transform.localScale = new Vector3(height * .58f, height * .50f, height * .56f);
                c.GetComponent<MeshFilter>().sharedMesh = canopy;
                int tone = (variant + i) % 3;
                c.GetComponent<Renderer>().sharedMaterial = Mat("Summer leaves " + tone, new Color(.28f + tone * .055f, .41f + tone * .035f, .12f + tone * .025f));
                Beam("Tree fork", t, new Vector3(0,height * .4f,0), c.transform.localPosition, height * .028f, bark);
            }
        }
        static void Plant(string name, Transform parent, Vector3 p, float height)
        {
            string path = "Assets/Nicrom/Shaders/Wind/Prefabs/" + name + ".prefab";
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!source) throw new InvalidOperationException("Missing supplied vegetation: " + path);
            var wrap = Group("Nicrom " + name, parent, p);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(source); go.transform.SetParent(wrap, false);
            foreach (var lod in go.GetComponentsInChildren<LODGroup>()) Object.DestroyImmediate(lod);
            var renderers = go.GetComponentsInChildren<MeshRenderer>();
            var b = renderers[0].bounds; foreach (var r in renderers.Skip(1)) b.Encapsulate(r.bounds);
            wrap.localScale = Vector3.one * (height / b.size.y); wrap.localRotation = Quaternion.Euler(0, random.Next(360), 0);
            foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            foreach (var r in renderers)
            {
                r.sharedMaterials = r.sharedMaterials.Select(m =>
                {
                    if (vegetationMaterials.TryGetValue(m, out var existing)) return existing;
                    var copy = Mat("Nicrom " + m.name, Color.white);
                    if (m.HasProperty("_MainTex")) copy.mainTexture = m.GetTexture("_MainTex");
                    else if (m.HasProperty("_BaseMap")) copy.mainTexture = m.GetTexture("_BaseMap");
                    copy.SetFloat("_Cull", 0); copy.SetFloat("_Smoothness", 0);
                    vegetationMaterials.Add(m, copy); return copy;
                }).ToArray();
            }
        }
        static Material Mat(string name, Color c)
        {
            string path = Folder + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.color = c; m.enableInstancing = true; m.SetFloat("_Smoothness", .18f); return m;
        }
        static Transform Group(string name, Transform parent, Vector3 p)
        { var t = new GameObject(name).transform; t.SetParent(parent, false); t.localPosition = p; return t; }
        static GameObject Box(string name, Transform parent, Vector3 p, Vector3 s, Material mat) => Shape(PrimitiveType.Cube, name, parent, p, s, mat);
        static GameObject Shape(PrimitiveType type, string name, Transform parent, Vector3 p, Vector3 s, Material mat)
        {
            var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.SetParent(parent, false); go.transform.localPosition = p; go.transform.localScale = s;
            go.GetComponent<Renderer>().sharedMaterial = mat; Object.DestroyImmediate(go.GetComponent<Collider>()); return go;
        }
        static void Beam(string name, Transform parent, Vector3 a, Vector3 b, float width, Material mat)
        { var go = Box(name, parent, (a + b) * .5f, new Vector3(width, (b - a).magnitude, width), mat); go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, b - a); }
        static void AddWoodGrain()
        {
            const int size = 128; var tex = new Texture2D(size, size);
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float grain = .82f + .12f * Mathf.Sin(y * .57f + Mathf.Sin(x * .08f) * 1.2f) + (float)random.NextDouble() * .06f;
                if (y % 32 < 2) grain *= .65f;
                tex.SetPixel(x, y, new Color(grain, grain, grain));
            }
            tex.Apply(); File.WriteAllBytes(Folder + "/Oak grain.png", tex.EncodeToPNG()); Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(Folder + "/Oak grain.png"); wood.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/Oak grain.png");
        }
        static void Combine(Transform parent, string label)
        {
            // Merge static decoration per material. Keep authoring transforms/colliders, disable only source renderers.
            var groups = new Dictionary<Material, List<CombineInstance>>();
            foreach (var filter in parent.GetComponentsInChildren<MeshFilter>())
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (!renderer || !renderer.enabled || !filter.sharedMesh) continue;
                for (int i = 0; i < filter.sharedMesh.subMeshCount; i++)
                {
                    var mat = renderer.sharedMaterials[Mathf.Min(i, renderer.sharedMaterials.Length - 1)];
                    if (!groups.TryGetValue(mat, out var list)) groups.Add(mat, list = new List<CombineInstance>());
                    list.Add(new CombineInstance { mesh = filter.sharedMesh, subMeshIndex = i, transform = parent.worldToLocalMatrix * filter.transform.localToWorldMatrix });
                }
                renderer.enabled = false;
            }
            int index = 0;
            foreach (var pair in groups)
            {
                var mesh = new Mesh { name = label + " static " + index, indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(pair.Value.ToArray(), true, true); mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh, Folder + "/" + label + " mesh " + index++ + ".asset");
                var go = new GameObject(label + " combined " + pair.Key.name, typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(parent, false);
                go.GetComponent<MeshFilter>().sharedMesh = mesh; go.GetComponent<Renderer>().sharedMaterial = pair.Key;
                go.GetComponent<Renderer>().shadowCastingMode = label == "Terrace" ? ShadowCastingMode.On : ShadowCastingMode.Off;
            }
            PruneBatchedSources(parent);
        }
        static void PruneBatchedSources(Transform parent)
        {
            foreach (var child in parent.Cast<Transform>().ToArray())
            {
                if (child.name.Contains(" combined ")) continue;
                // Unpack imported foliage before removing its now-batched source meshes.
                if (PrefabUtility.IsAnyPrefabInstanceRoot(child.gameObject)) PrefabUtility.UnpackPrefabInstance(child.gameObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                PruneBatchedSources(child);
                if (child.GetComponent<MeshFilter>() && !child.GetComponent<Collider>()) Object.DestroyImmediate(child.gameObject);
                else if (child.childCount == 0 && child.GetComponents<Component>().Length == 1) Object.DestroyImmediate(child.gameObject);
            }
        }
    }
}
