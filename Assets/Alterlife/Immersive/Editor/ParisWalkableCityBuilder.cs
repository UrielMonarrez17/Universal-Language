using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;
using Object = UnityEngine.Object;

namespace Alterlife.Immersive.Editor
{
    /// <summary>
    /// Upgrades Scenes/Paris.unity into an authentic, walkable ~130x125m street-level Parisian neighborhood:
    /// - Brings the entire city up to y=0 (sidewalks & café terrace flush at y=0, cobblestone streets at y=-0.14m).
    /// - Replaces flat placeholder boxes with custom Blender Haussmann buildings (Boulangerie, Librairie, Apartment,
    ///   domed Corner rotundas, and 5-story Le Café de Paris home building) + Parisian street furniture
    ///   (Réverbère lamps, Banc Davioud benches, Wallace Fountain square, Morris Columns, Art Nouveau Métro entrance,
    ///   bollards, and terrace geranium planters).
    /// - Populates the 360° distant skyline with Bricktown buildings re-mapped to warm Parisian limestone/slate URP materials.
    /// - Scales the Eiffel Tower to a monumental ~285m along the grand boulevard visual axis with updated farClipPlane & fog.
    /// - Configures full street-level locomotion: Smooth Move + Snap Turn default, ClearTeleportArea across all walkable
    ///   surfaces, step-friendly CharacterController, and solid building/perimeter colliders.
    /// - Preserves 100% of existing gameplay objects, waiter, menu, fox companion, and UI references.
    /// </summary>
    public static class ParisWalkableCityBuilder
    {
        const string ParisScenePath = "Assets/Scenes/Paris.unity";
        const string BlenderDir = "Assets/Assets-paris/BlenderParis";
        const string BricktownDir = "Assets/Assets-paris/LoafbrrAssets/Bricktown/prefabs/Buildings";
        const string NicromDir = "Assets/Nicrom/Shaders/Wind/Prefabs";
        const string MaterialsDir = "Assets/Alterlife/Immersive/Paris/CityWalkable";
        const string FontAssetPath = "Assets/VRTemplateAssets/Fonts/Inter/Inter-Regular SDF.asset";

        static Transform root;
        static Transform cityRoot;
        static Transform terraceRoot;
        static TeleportationProvider teleportProvider;
        static TMP_FontAsset fontAsset;
        static readonly Dictionary<string, Material> urpPalette = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<Material, Material> nicromMatCache = new Dictionary<Material, Material>();
        static System.Random rng;
        static Mesh canopyMesh;

        [MenuItem("Alterlife/Restaurant/Build Walkable Paris City (Blender + Hybrid)")]
        public static void BuildWalkableParis()
        {
            if (!Application.isBatchMode && EditorSceneManager.GetActiveScene().isDirty)
                EditorSceneManager.SaveOpenScenes();

            Directory.CreateDirectory(MaterialsDir);
            AssetDatabase.Refresh();

            var scene = EditorSceneManager.OpenScene(ParisScenePath, OpenSceneMode.Single);
            var rootGo = GameObject.Find("Paris Restaurant Prototype");
            if (rootGo == null)
            {
                Debug.LogError("[ParisWalkableCityBuilder] 'Paris Restaurant Prototype' root not found in Paris.unity.");
                return;
            }

            root = rootGo.transform;
            rng = new System.Random(2026);
            urpPalette.Clear();
            nicromMatCache.Clear();
            fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);

            BuildSharedUrpPalette();

            var xrOrigin = Object.FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>(FindObjectsInactive.Include);
            teleportProvider = xrOrigin != null ? xrOrigin.GetComponentInChildren<TeleportationProvider>(true) : null;

            // 1. Clean old generated city/terrace/clouds containers while keeping all gameplay objects untouched
            DestroyChildIfExists(root, "Paris cityscape");
            DestroyChildIfExists(root, "Cafe terrace detailing");
            DestroyChildIfExists(root, "Cafe name lettering");
            DestroyChildIfExists(root, "Paris sky clouds");
            DestroyChildIfExists(root, "Walkable Paris Colliders");

            // Archive/hide legacy flat cafe wall & floating mismatched shop so the 5-story Blender Haussmann_CafeHome takes over cleanly
            HideLegacyCafeShell();

            cityRoot = CreateGroup("Paris cityscape", root, Vector3.zero);
            terraceRoot = CreateGroup("Cafe terrace detailing", root, Vector3.zero);

            // 2. Configure Lighting, Sky, Fog, and Far Clip Plane for monumental Eiffel Tower vista
            ConfigureAtmosphereAndCamera(xrOrigin);

            // 3. Configure XR Locomotion (Smooth Move + Snap Turn default, step over curbs, wrist menu toggle intact)
            ConfigureLocomotionAndFox(xrOrigin);

            // 4. Build Walkable Street-Level Ground, Boulevards, Sidewalks, Crosswalks, & Square de la Tour
            BuildWalkableGroundAndStreets();

            // 5. Build Hero Café Terrace Detail (street-level open bistro terrace + planters + tables/chairs)
            BuildStreetLevelTerrace();

            // 6. Place Blender Hero Haussmann Buildings (Café Home, Boulangeries, Librairies, Apartments, Domed Corners)
            BuildHeroHaussmannBlocks();

            // 7. Place Iconic Blender Parisian Street Furniture (Lamps, Benches, Wallace Fountain, Morris Columns, Métro, Bollards) + Trees
            BuildParisianStreetLife();

            // 8. Populate Hybrid 360° Background Skyline with Limestone-Retextured Bricktown Buildings
            BuildHybridBricktownSkyline();

            // 9. Rescale & Position Eiffel Tower Monumentally at the end of the Grand Boulevard (~285m tall)
            ConfigureMonumentalEiffelTower();

            // 10. Stylized Parisian Sky Clouds
            BuildSkyClouds();

            // 11. Perimeter Safety Boundaries (prevent walking off map edges)
            BuildPerimeterBoundaries();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("ALTERLIFE_WALKABLE_PARIS_BUILT_SUCCESSFULLY");
        }

        static void DestroyChildIfExists(Transform parent, string childName)
        {
            var c = parent.Find(childName);
            if (c != null) Object.DestroyImmediate(c.gameObject);
        }

        static void HideLegacyCafeShell()
        {
            string[] legacyNames =
            {
                "Restaurant facade", "Cafe window frame", "Cafe window glass", "Cafe mullion",
                "Existing cafe", "Terrace railing", "Striped terrace awning", "Tile grout", "City ground"
            };

            Transform archive = root.Find("Previous terrace styling — preserved");
            if (archive == null)
                archive = CreateGroup("Previous terrace styling — preserved", root, Vector3.zero);

            foreach (Transform child in root.Cast<Transform>().ToArray())
            {
                if (legacyNames.Contains(child.name) || child.name.StartsWith("Paris city building"))
                {
                    child.SetParent(archive, true);
                }
            }
            archive.gameObject.SetActive(false);

            // Keep Terrace floor collider & ClearTeleportArea active, flush at y=0, styled with warm Parisian pavers
            Transform terraceFloor = root.Find("Terrace floor");
            if (terraceFloor != null)
            {
                terraceFloor.localPosition = new Vector3(0f, -0.15f, 0.5f);
                terraceFloor.localScale = new Vector3(8.0f, 0.30f, 7.0f);
                var r = terraceFloor.GetComponent<Renderer>();
                if (r != null) r.sharedMaterial = urpPalette["Paris_Terrace_Paving"];
                EnsureTeleportArea(terraceFloor.gameObject);
            }
        }

        static void BuildSharedUrpPalette()
        {
            // Shared URP Lit materials matching exact Blender material names -> low draw calls & guaranteed URP support!
            RegMat("Paris_Limestone_Cream",  new Color(0.86f, 0.78f, 0.65f), 0.16f, 0f);
            RegMat("Paris_Limestone_Honey",  new Color(0.80f, 0.70f, 0.54f), 0.16f, 0f);
            RegMat("Paris_Limestone_Ivory",  new Color(0.91f, 0.85f, 0.74f), 0.18f, 0f);
            RegMat("Paris_CarvedStone_Trim", new Color(0.95f, 0.90f, 0.80f), 0.22f, 0f);
            RegMat("Paris_Mansard_Slate",    new Color(0.18f, 0.23f, 0.30f), 0.35f, 0.12f);
            RegMat("Paris_Roof_Zinc",        new Color(0.46f, 0.51f, 0.55f), 0.52f, 0.50f);
            RegMat("Paris_WroughtIron",      new Color(0.07f, 0.10f, 0.10f), 0.55f, 0.70f);
            RegMat("Paris_CastIron_Green",   new Color(0.08f, 0.24f, 0.16f), 0.52f, 0.55f);
            RegMat("Paris_Window_Glass",     new Color(0.11f, 0.20f, 0.27f), 0.82f, 0.18f);
            RegMat("Paris_Shop_WarmGlass",   new Color(0.96f, 0.82f, 0.52f), 0.60f, 0f, new Color(0.65f, 0.48f, 0.22f));
            RegMat("Paris_Window_Wood",      new Color(0.94f, 0.91f, 0.85f), 0.32f, 0f);
            RegMat("Paris_Shutter_Sage",     new Color(0.48f, 0.55f, 0.51f), 0.25f, 0f);
            RegMat("Paris_Shop_Green",       new Color(0.09f, 0.24f, 0.17f), 0.40f, 0f);
            RegMat("Paris_Shop_Burgundy",    new Color(0.37f, 0.09f, 0.12f), 0.40f, 0f);
            RegMat("Paris_Shop_Navy",        new Color(0.10f, 0.17f, 0.29f), 0.40f, 0f);
            RegMat("Paris_Awning_Cream",     new Color(0.96f, 0.91f, 0.79f), 0.10f, 0f);
            RegMat("Paris_Awning_Burgundy",  new Color(0.55f, 0.13f, 0.16f), 0.10f, 0f);
            RegMat("Paris_Awning_Green",     new Color(0.14f, 0.34f, 0.23f), 0.10f, 0f);
            RegMat("Paris_Brass_Gold",       new Color(0.79f, 0.60f, 0.22f), 0.72f, 0.80f);
            RegMat("Paris_Terracotta",       new Color(0.66f, 0.31f, 0.18f), 0.15f, 0f);
            RegMat("Paris_Lantern_Glow",     new Color(1.00f, 0.90f, 0.65f), 0.40f, 0f, new Color(1.1f, 0.88f, 0.48f));
            RegMat("Paris_Metro_Amber",      new Color(1.00f, 0.54f, 0.18f), 0.50f, 0f, new Color(1.0f, 0.45f, 0.12f));
            RegMat("Paris_Metro_SignCream",  new Color(0.95f, 0.90f, 0.76f), 0.30f, 0f);
            RegMat("Paris_Bench_Oak",        new Color(0.50f, 0.28f, 0.14f), 0.35f, 0f);
            RegMat("Paris_Fountain_Water",   new Color(0.28f, 0.66f, 0.78f), 0.88f, 0.10f);
            RegMat("Paris_Poster_Coral",     new Color(0.82f, 0.28f, 0.22f), 0.22f, 0f);
            RegMat("Paris_Poster_Cobalt",    new Color(0.18f, 0.36f, 0.66f), 0.22f, 0f);
            RegMat("Paris_Poster_Ochre",     new Color(0.88f, 0.70f, 0.22f), 0.22f, 0f);
            RegMat("Paris_Planter_Soil",     new Color(0.22f, 0.15f, 0.10f), 0.05f, 0f);
            RegMat("Paris_Foliage_Green",    new Color(0.22f, 0.44f, 0.16f), 0.15f, 0f);
            RegMat("Paris_Geranium_Red",     new Color(0.86f, 0.16f, 0.20f), 0.20f, 0f);
            RegMat("Paris_Geranium_Pink",    new Color(0.93f, 0.44f, 0.56f), 0.20f, 0f);

            // Ground, street, lawn, Eiffel Tower, and Bricktown re-palette materials
            RegMat("Paris_Road_Cobble",      new Color(0.27f, 0.28f, 0.30f), 0.15f, 0f);
            RegMat("Paris_Sidewalk_Stone",   new Color(0.75f, 0.71f, 0.63f), 0.14f, 0f);
            RegMat("Paris_Terrace_Paving",   new Color(0.68f, 0.59f, 0.47f), 0.18f, 0f);
            RegMat("Paris_Square_Gravel",    new Color(0.82f, 0.77f, 0.66f), 0.10f, 0f);
            RegMat("Paris_Park_Lawn",        new Color(0.28f, 0.45f, 0.20f), 0.08f, 0f);
            RegMat("Paris_Crosswalk_White",  new Color(0.92f, 0.90f, 0.85f), 0.12f, 0f);
            RegMat("Paris_Eiffel_Iron",      new Color(0.36f, 0.30f, 0.24f), 0.38f, 0.48f);
            RegMat("Paris_Tree_Bark",        new Color(0.33f, 0.25f, 0.17f), 0.08f, 0f);
            RegMat("Paris_Leaves_0",         new Color(0.25f, 0.43f, 0.15f), 0.12f, 0f);
            RegMat("Paris_Leaves_1",         new Color(0.31f, 0.48f, 0.18f), 0.12f, 0f);
            RegMat("Paris_Leaves_2",         new Color(0.21f, 0.38f, 0.13f), 0.12f, 0f);
            RegMat("Paris_Bricktown_Cream",  new Color(0.83f, 0.76f, 0.64f), 0.15f, 0f);
            RegMat("Paris_Bricktown_Warm",   new Color(0.78f, 0.69f, 0.56f), 0.15f, 0f);
            RegMat("Paris_Bricktown_Roof",   new Color(0.22f, 0.27f, 0.33f), 0.30f, 0.10f);
        }

        static Material RegMat(string name, Color baseColor, float smoothness, float metallic, Color? emission = null)
        {
            string path = $"{MaterialsDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
            if (mat == null)
            {
                mat = new Material(urpLit) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != urpLit)
            {
                mat.shader = urpLit;
            }

            mat.SetColor("_BaseColor", baseColor);
            mat.color = baseColor;
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", metallic);
            mat.enableInstancing = true;

            if (emission.HasValue)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", emission.Value);
            }
            else
            {
                mat.DisableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", Color.black);
            }

            EditorUtility.SetDirty(mat);
            urpPalette[name] = mat;
            return mat;
        }

        static void ConfigureAtmosphereAndCamera(Unity.XR.CoreUtils.XROrigin xrOrigin)
        {
            string skyPath = $"{MaterialsDir}/Paris_GoldenAfternoon_Sky.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            if (sky == null)
            {
                sky = new Material(Shader.Find("Skybox/Procedural"));
                AssetDatabase.CreateAsset(sky, skyPath);
            }
            sky.SetColor("_SkyTint", new Color(0.52f, 0.66f, 0.86f));
            sky.SetColor("_GroundColor", new Color(0.42f, 0.39f, 0.34f));
            sky.SetFloat("_AtmosphereThickness", 0.85f);
            sky.SetFloat("_Exposure", 1.15f);
            sky.SetFloat("_SunSize", 0.03f);
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;

            // Gentle aerial perspective so the 285m Eiffel Tower stands tall and dramatic at z=255m
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.00145f;
            RenderSettings.fogColor = new Color(0.73f, 0.80f, 0.88f);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.66f, 0.75f, 0.86f);
            RenderSettings.ambientEquatorColor = new Color(0.60f, 0.55f, 0.46f);
            RenderSettings.ambientGroundColor = new Color(0.30f, 0.27f, 0.22f);

            Transform sunTf = root.Find("Paris late afternoon sun");
            if (sunTf != null && sunTf.TryGetComponent<Light>(out var sun))
            {
                sun.color = new Color(1.0f, 0.89f, 0.72f);
                sun.intensity = 1.30f;
                sun.shadowStrength = 0.68f;
                sun.transform.rotation = Quaternion.Euler(44f, -38f, 0f);
                EditorUtility.SetDirty(sun);
            }

            if (xrOrigin != null && xrOrigin.Camera != null)
            {
                xrOrigin.Camera.farClipPlane = 850f;
                xrOrigin.Camera.nearClipPlane = 0.04f;
                EditorUtility.SetDirty(xrOrigin.Camera);
            }
        }

        static void ConfigureLocomotionAndFox(Unity.XR.CoreUtils.XROrigin xrOrigin)
        {
            if (xrOrigin != null)
            {
                // Step smoothly over 14cm Parisian curbs
                var cc = xrOrigin.GetComponent<CharacterController>();
                if (cc != null)
                {
                    cc.radius = 0.24f;
                    cc.stepOffset = 0.26f;
                    EditorUtility.SetDirty(cc);
                }

                // Enable Smooth Move + Snap Turn by default (Wrist Menu still switches smoothly between Smooth & Teleport!)
                foreach (var ciam in xrOrigin.GetComponentsInChildren<ControllerInputActionManager>(true))
                {
                    bool isLeft = ciam.name.IndexOf("Left", StringComparison.OrdinalIgnoreCase) >= 0;
                    var so = new SerializedObject(ciam);
                    var smoothMotionProp = so.FindProperty("m_SmoothMotionEnabled");
                    var smoothTurnProp = so.FindProperty("m_SmoothTurnEnabled");
                    if (smoothMotionProp != null) smoothMotionProp.boolValue = isLeft;
                    if (smoothTurnProp != null) smoothTurnProp.boolValue = false;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(ciam);
                }
            }

            var comfort = Object.FindAnyObjectByType<ComfortOptions>(FindObjectsInactive.Include);
            if (comfort != null)
            {
                comfort.defaultSmoothMovement = true;
                EditorUtility.SetDirty(comfort);
            }

            // Ensure Fox spatial AudioSource is clearly audible + cafeAnchor wired for >6m Paris Guide proximity mode
            var voice = Object.FindAnyObjectByType<RestaurantVoiceBridge>(FindObjectsInactive.Include);
            if (voice != null)
            {
                Transform table = root.Find("Your table");
                if (table != null) voice.cafeAnchor = table;
                if (voice.foxSpeaker != null)
                {
                    voice.foxSpeaker.mute = false;
                    voice.foxSpeaker.volume = 1f;
                    voice.foxSpeaker.spatialBlend = 0.65f;
                    voice.foxSpeaker.minDistance = 1.2f;
                    voice.foxSpeaker.maxDistance = 12f;
                    EditorUtility.SetDirty(voice.foxSpeaker);
                }
                EditorUtility.SetDirty(voice);
            }
        }

        static void BuildWalkableGroundAndStreets()
        {
            var groundGroup = CreateGroup("Streets and Sidewalks", cityRoot, Vector3.zero);

            // 1. Main walkable street-level asphalt/cobble plane (top surface y = -0.14)
            var roadBase = CreateBox("Paris Road Network", groundGroup,
                new Vector3(0f, -0.34f, 52f), new Vector3(160f, 0.40f, 165f),
                urpPalette["Paris_Road_Cobble"], withCollider: true, castShadows: false);
            EnsureTeleportArea(roadBase);

            // Distant Champ-de-Mars Esplanade green park ground stretching beneath the Eiffel Tower (z=120..360)
            CreateBox("Champ de Mars Lawn Base", groundGroup,
                new Vector3(0f, -0.36f, 245f), new Vector3(360f, 0.40f, 240f),
                urpPalette["Paris_Park_Lawn"], withCollider: false, castShadows: false);
            CreateBox("Champ de Mars Central Path", groundGroup,
                new Vector3(0f, -0.145f, 200f), new Vector3(18f, 0.04f, 160f),
                urpPalette["Paris_Square_Gravel"], withCollider: false, castShadows: false);

            // 2. Raised Parisian Limestone Sidewalks (top surface y = 0.0, flush with café Terrace floor!)
            // South Café Promenade Sidewalk (z = -3 to +6, x = -66 to +66) — left & right of Terrace floor (x in [-4,+4])
            AddWalkableSidewalk("Sidewalk South West", groundGroup, new Vector3(-35f, -0.14f, 1.5f), new Vector3(62f, 0.28f, 9f));
            AddWalkableSidewalk("Sidewalk South East", groundGroup, new Vector3(35f, -0.14f, 1.5f), new Vector3(62f, 0.28f, 9f));
            AddWalkableSidewalk("Sidewalk Cafe Front Curb", groundGroup, new Vector3(0f, -0.14f, 5.0f), new Vector3(8.2f, 0.28f, 2.0f));

            // Grand Boulevard West Sidewalk (x = -13.5 to -7.5, z = 16 to 68 and z = 78 to 118)
            AddWalkableSidewalk("Boulevard Sidewalk West Block 1", groundGroup, new Vector3(-10.5f, -0.14f, 42f), new Vector3(6.0f, 0.28f, 52f));
            AddWalkableSidewalk("Boulevard Sidewalk West Block 2", groundGroup, new Vector3(-10.5f, -0.14f, 98f), new Vector3(6.0f, 0.28f, 40f));

            // Grand Boulevard East Sidewalk (x = +7.5 to +13.5, z = 16 to 68 and z = 78 to 118)
            AddWalkableSidewalk("Boulevard Sidewalk East Block 1", groundGroup, new Vector3(10.5f, -0.14f, 42f), new Vector3(6.0f, 0.28f, 52f));
            AddWalkableSidewalk("Boulevard Sidewalk East Block 2", groundGroup, new Vector3(10.5f, -0.14f, 98f), new Vector3(6.0f, 0.28f, 40f));

            // Cross-street Sidewalks (Rue du Café north side z = 16..20, Rue Haussmann south z = 64..68 & north z = 78..82)
            AddWalkableSidewalk("Rue du Cafe North West", groundGroup, new Vector3(-40f, -0.14f, 18.0f), new Vector3(54f, 0.28f, 4.0f));
            AddWalkableSidewalk("Rue Haussmann South West", groundGroup, new Vector3(-40f, -0.14f, 66.0f), new Vector3(54f, 0.28f, 4.0f));
            AddWalkableSidewalk("Rue Haussmann North West", groundGroup, new Vector3(-40f, -0.14f, 80.0f), new Vector3(54f, 0.28f, 4.0f));
            AddWalkableSidewalk("Rue Haussmann North East", groundGroup, new Vector3(40f, -0.14f, 80.0f), new Vector3(54f, 0.28f, 4.0f));

            // Northern Esplanade Overlook facing Eiffel Tower (z = 112..118, x = -64..+64)
            AddWalkableSidewalk("Eiffel Overlook Promenade West", groundGroup, new Vector3(-39f, -0.14f, 115f), new Vector3(51f, 0.28f, 6.0f));
            AddWalkableSidewalk("Eiffel Overlook Promenade East", groundGroup, new Vector3(39f, -0.14f, 115f), new Vector3(51f, 0.28f, 6.0f));

            // 3. Square de la Tour (Walkable Parisian Pocket Park & Wallace Fountain Plaza: x = +13.5..+47.5, z = +16..+68)
            var squareBase = CreateBox("Square de la Tour Plaza", groundGroup,
                new Vector3(30.5f, -0.14f, 42.0f), new Vector3(34.0f, 0.28f, 52.0f),
                urpPalette["Paris_Square_Gravel"], withCollider: true, castShadows: false);
            EnsureTeleportArea(squareBase);

            // Ornamental Lawn Beds inside the Square (decorative flush grass patches with stone curb trim, still easy to walk around)
            Vector3[] lawnCenters =
            {
                new Vector3(22.5f, 0.005f, 30.5f),
                new Vector3(38.5f, 0.005f, 30.5f),
                new Vector3(22.5f, 0.005f, 51.5f),
                new Vector3(38.5f, 0.005f, 51.5f)
            };
            foreach (var lc in lawnCenters)
            {
                CreateBox("Square Lawn Curbing", groundGroup, lc - Vector3.up * 0.01f, new Vector3(9.4f, 0.05f, 9.4f), urpPalette["Paris_CarvedStone_Trim"], false, false);
                CreateBox("Square Lawn Turf", groundGroup, lc + Vector3.up * 0.015f, new Vector3(8.8f, 0.04f, 8.8f), urpPalette["Paris_Park_Lawn"], false, false);
            }

            // Central stone medallion around Wallace Fountain at (30.5, 0, 41.0)
            var plazaDisc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            plazaDisc.name = "Wallace Plaza Medallion";
            plazaDisc.transform.SetParent(groundGroup, false);
            plazaDisc.transform.localPosition = new Vector3(30.5f, 0.005f, 41.0f);
            plazaDisc.transform.localScale = new Vector3(7.6f, 0.015f, 7.6f);
            plazaDisc.GetComponent<Renderer>().sharedMaterial = urpPalette["Paris_Sidewalk_Stone"];
            plazaDisc.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            Object.DestroyImmediate(plazaDisc.GetComponent<Collider>());

            // 4. Boulevard Lane Markings & Parisian Pedestrian Crosswalks (Passage piéton)
            for (float z = 19f; z < 114f; z += 5.5f)
            {
                if (z > 65f && z < 80f) continue;
                CreateBox("Boulevard Center Dash", groundGroup, new Vector3(0f, -0.132f, z), new Vector3(0.14f, 0.012f, 2.4f), urpPalette["Paris_Crosswalk_White"], false, false);
            }
            AddCrosswalk(groundGroup, new Vector3(0f, -0.132f, 15.0f), horizontal: true);
            AddCrosswalk(groundGroup, new Vector3(0f, -0.132f, 7.2f), horizontal: true);
            AddCrosswalk(groundGroup, new Vector3(0f, -0.132f, 67.2f), horizontal: true);
            AddCrosswalk(groundGroup, new Vector3(0f, -0.132f, 78.8f), horizontal: true);
            AddCrosswalk(groundGroup, new Vector3(-8.8f, -0.132f, 11.0f), horizontal: false);
            AddCrosswalk(groundGroup, new Vector3(8.8f, -0.132f, 11.0f), horizontal: false);
            AddCrosswalk(groundGroup, new Vector3(-8.8f, -0.132f, 73.0f), horizontal: false);
            AddCrosswalk(groundGroup, new Vector3(8.8f, -0.132f, 73.0f), horizontal: false);
        }

        static void AddWalkableSidewalk(string name, Transform parent, Vector3 pos, Vector3 scale)
        {
            var go = CreateBox(name, parent, pos, scale, urpPalette["Paris_Sidewalk_Stone"], withCollider: true, castShadows: false);
            EnsureTeleportArea(go);
        }

        static void AddCrosswalk(Transform parent, Vector3 center, bool horizontal)
        {
            for (int i = -4; i <= 4; i++)
            {
                Vector3 offset = horizontal ? new Vector3(i * 1.35f, 0f, 0f) : new Vector3(0f, 0f, i * 0.95f);
                Vector3 size = horizontal ? new Vector3(0.65f, 0.014f, 2.8f) : new Vector3(2.8f, 0.014f, 0.50f);
                CreateBox("Crosswalk Stripe", parent, center + offset, size, urpPalette["Paris_Crosswalk_White"], false, false);
            }
        }

        static void BuildStreetLevelTerrace()
        {
            // Keep existing tables, chairs, waiter, menu, panels intact; add crisp Parisian bistro chairs,
            // flower pots on tables, and OPEN low geranium planter boxes along the sides/corners (leaving wide exits!)
            var wood = urpPalette["Paris_Bench_Oak"];
            var trim = urpPalette["Paris_CarvedStone_Trim"];
            var iron = urpPalette["Paris_WroughtIron"];
            var brass = urpPalette["Paris_Brass_Gold"];

            foreach (var t in root.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Round wooden tabletop"))
            {
                var r = t.GetComponent<Renderer>();
                if (r != null) r.sharedMaterial = wood;
            }

            // Parisian café terrace folding chairs at the two flanking tables
            BuildBistroChair(new Vector3(-3.15f, 0f, 1.75f), 35f, wood, iron);
            BuildBistroChair(new Vector3(-2.10f, 0f, 2.95f), 205f, wood, iron);
            BuildBistroChair(new Vector3( 2.10f, 0f, 1.75f), -35f, wood, iron);
            BuildBistroChair(new Vector3( 3.15f, 0f, 2.95f), 145f, wood, iron);

            // Low Parisian terrace geranium planter boxes framing left/right corners ONLY — completely open to walk out!
            PlaceBlenderAsset("Paris_PlanterBox", "Cafe Planter Front Left",  terraceRoot, new Vector3(-3.1f, 0f, 3.85f), 0f, addCollider: true, castShadows: true);
            PlaceBlenderAsset("Paris_PlanterBox", "Cafe Planter Front Right", terraceRoot, new Vector3( 3.1f, 0f, 3.85f), 0f, addCollider: true, castShadows: true);
            PlaceBlenderAsset("Paris_PlanterBox", "Cafe Planter Side Left",   terraceRoot, new Vector3(-4.0f, 0f, 2.30f), 90f, addCollider: true, castShadows: true);
            PlaceBlenderAsset("Paris_PlanterBox", "Cafe Planter Side Right",  terraceRoot, new Vector3( 4.0f, 0f, 2.30f), 90f, addCollider: true, castShadows: true);

            // Table flower centerpieces
            foreach (var tablePos in new[] { new Vector3(-2.6f, 0.82f, 2.3f), new Vector3(2.6f, 0.82f, 2.3f) })
            {
                var pot = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pot.name = "Table Flower Pot";
                pot.transform.SetParent(terraceRoot, false);
                pot.transform.localPosition = tablePos + Vector3.up * 0.065f;
                pot.transform.localScale = new Vector3(0.16f, 0.065f, 0.16f);
                pot.GetComponent<Renderer>().sharedMaterial = trim;
                Object.DestroyImmediate(pot.GetComponent<Collider>());
                PlaceNicromPlant("LPW_Flower_A1_H70cm_01", terraceRoot, tablePos + Vector3.up * 0.13f, 0.25f);
            }

            // Crisp gold sign "LE CAFÉ DE PARIS" mounted on the fascia of Haussmann_CafeHome above the terrace
            CreateShopSign("LE CAFÉ DE PARIS", terraceRoot, new Vector3(0f, 3.62f, -2.72f), 180f, 4.8f, 0.50f, 2.35f, new Color(0.11f, 0.19f, 0.14f));

            // Keep existing table plate polish
            var plate = root.Find("Dinner plate");
            if (plate != null)
            {
                plate.localScale = new Vector3(0.30f, 0.012f, 0.30f);
                var pr = plate.GetComponent<Renderer>();
                if (pr != null) pr.sharedMaterial = trim;
            }
            for (int i = 0; i < 4; i++)
                CreateBox("Fork tine", terraceRoot, new Vector3(0.5f - 0.009f + i * 0.006f, 0.845f, 0.05f), new Vector3(0.003f, 0.003f, 0.035f), brass, false, false);
        }

        static void BuildBistroChair(Vector3 pos, float yaw, Material wood, Material iron)
        {
            var c = CreateGroup("Paris folding bistro chair", terraceRoot, pos);
            c.localRotation = Quaternion.Euler(0f, yaw, 0f);
            for (int i = 0; i < 4; i++)
                CreateBox("Seat slat", c, new Vector3(0f, 0.45f, -0.18f + i * 0.115f), new Vector3(0.43f, 0.028f, 0.095f), wood, false, true);
            for (int side = -1; side <= 1; side += 2)
            {
                CreateBox("Leg", c, new Vector3(side * 0.20f, 0.24f, -0.18f), new Vector3(0.026f, 0.48f, 0.026f), iron, false, true);
                CreateBox("Leg", c, new Vector3(side * 0.20f, 0.24f,  0.18f), new Vector3(0.026f, 0.48f, 0.026f), iron, false, true);
                CreateBox("Back upright", c, new Vector3(side * 0.20f, 0.68f, 0.22f), new Vector3(0.025f, 0.52f, 0.025f), iron, false, true);
            }
            for (int i = 0; i < 3; i++)
                CreateBox("Backrest slat", c, new Vector3(0f, 0.69f + i * 0.10f, 0.23f), new Vector3(0.46f, 0.075f, 0.024f), wood, false, true);
            var col = c.gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.46f, 0.02f);
            col.size = new Vector3(0.49f, 0.92f, 0.53f);
        }

        static void BuildHeroHaussmannBlocks()
        {
            var buildings = CreateGroup("Haussmann Hero Blocks", cityRoot, Vector3.zero);

            // 1. SOUTH ROW — Café Promenade (facing +Z toward Rue du Café & Eiffel Tower, front wall flush at z = -3.0!)
            PlaceBlenderAsset("Haussmann_CafeHome",    "Le Cafe de Paris Building",   buildings, new Vector3(  0.0f, 0f, -8.0f), 0f, true, true);
            PlaceBlenderAsset("Haussmann_Boulangerie", "Boulangerie South West 1",    buildings, new Vector3(-14.2f, 0f, -8.5f), 0f, true, true);
            PlaceBlenderAsset("Haussmann_Librairie",   "Librairie South East 1",      buildings, new Vector3( 14.2f, 0f, -8.5f), 0f, true, true);
            PlaceBlenderAsset("Haussmann_Apartment",   "Haussmann Apt South West 2",  buildings, new Vector3(-28.4f, 0f, -8.5f), 0f, true, true);
            PlaceBlenderAsset("Haussmann_Boulangerie", "Patisserie South East 2",     buildings, new Vector3( 28.4f, 0f, -8.5f), 0f, true, true);
            PlaceBlenderAsset("Haussmann_Librairie",   "Librairie South West 3",      buildings, new Vector3(-42.6f, 0f, -8.5f), 0f, true, false);
            PlaceBlenderAsset("Haussmann_Apartment",   "Haussmann Apt South East 3",  buildings, new Vector3( 42.6f, 0f, -8.5f), 0f, true, false);
            PlaceBlenderAsset("Haussmann_Apartment",   "Haussmann Apt South West 4",  buildings, new Vector3(-56.8f, 0f, -8.5f), 0f, true, false);
            PlaceBlenderAsset("Haussmann_Boulangerie", "Shop South East 4",           buildings, new Vector3( 56.8f, 0f, -8.5f), 0f, true, false);

            // Storefront signage on neighboring shops
            CreateShopSign("BOULANGERIE · PÂTISSERIE", buildings, new Vector3(-14.2f, 3.62f, -2.72f), 180f, 5.4f, 0.45f, 1.95f, new Color(0.11f, 0.19f, 0.14f));
            CreateShopSign("LIBRAIRIE DE PARIS",       buildings, new Vector3( 14.2f, 3.62f, -2.72f), 180f, 5.4f, 0.45f, 1.95f, new Color(0.28f, 0.08f, 0.10f));
            CreateShopSign("PÂTISSERIE FINE",          buildings, new Vector3( 28.4f, 3.62f, -2.72f), 180f, 5.0f, 0.45f, 1.95f, new Color(0.11f, 0.19f, 0.14f));

            // 2. GRAND BOULEVARD — WEST SIDE BLOCK 1 (z = 20..64, fronting +X onto boulevard at x = -13.5!)
            // Haussmann_Corner at yaw=90 places turret at (+X, -Z) = SE corner facing the intersection & Café!
            PlaceBlenderAsset("Haussmann_Corner",      "Corner Boulevard SW1",       buildings, new Vector3(-21.0f, 0f, 27.5f),  90f, true, true);
            PlaceBlenderAsset("Haussmann_Boulangerie", "Boulevard West Shop 1",      buildings, new Vector3(-19.0f, 0f, 42.1f),  90f, true, true);
            PlaceBlenderAsset("Haussmann_Corner",      "Corner Rue Haussmann NW1",   buildings, new Vector3(-21.0f, 0f, 56.7f),   0f, true, true);
            CreateShopSign("BOULANGERIE DU BOULEVARD", buildings, new Vector3(-13.22f, 3.62f, 42.1f), -90f, 5.4f, 0.45f, 1.85f, new Color(0.11f, 0.19f, 0.14f));

            // Rue du Café North-West side (facing -Z toward the café across the cross street!)
            PlaceBlenderAsset("Haussmann_Librairie",   "Rue du Cafe NW Shop 1",      buildings, new Vector3(-35.6f, 0f, 25.5f), 180f, true, false);
            PlaceBlenderAsset("Haussmann_Apartment",   "Rue du Cafe NW Apt 2",       buildings, new Vector3(-49.8f, 0f, 25.5f), 180f, true, false);
            PlaceBlenderAsset("Haussmann_Boulangerie", "Rue du Cafe NW Shop 3",      buildings, new Vector3(-64.0f, 0f, 25.5f), 180f, true, false);

            // 3. GRAND BOULEVARD — WEST SIDE BLOCK 2 (z = 82..111, after Rue Haussmann)
            PlaceBlenderAsset("Haussmann_Corner",      "Corner Boulevard West 2",    buildings, new Vector3(-21.0f, 0f, 89.5f),  90f, true, false);
            PlaceBlenderAsset("Haussmann_Librairie",   "Boulevard West Shop 2",      buildings, new Vector3(-19.0f, 0f, 104.1f), 90f, true, false);
            PlaceBlenderAsset("Haussmann_Apartment",   "Rue Haussmann NW Block 2",   buildings, new Vector3(-35.6f, 0f, 87.5f), 180f, true, false);
            PlaceBlenderAsset("Haussmann_Boulangerie", "Rue Haussmann NW Block 3",   buildings, new Vector3(-49.8f, 0f, 87.5f), 180f, true, false);

            // 4. EAST SIDE — SQUARE DE LA TOUR BACKDROP FACADES (framing our park at x = +47.5 -> center x = +53.0, facing -X!)
            PlaceBlenderAsset("Haussmann_Librairie",   "Square East Librairie",      buildings, new Vector3( 53.0f, 0f, 27.5f), 270f, true, true);
            PlaceBlenderAsset("Haussmann_Boulangerie", "Square East Patisserie",     buildings, new Vector3( 53.0f, 0f, 41.7f), 270f, true, true);
            PlaceBlenderAsset("Haussmann_Apartment",   "Square East Apartment",      buildings, new Vector3( 53.0f, 0f, 55.9f), 270f, true, false);
            CreateShopSign("SALON DE THÉ",             buildings, new Vector3( 47.22f, 3.62f, 41.7f), 90f, 4.4f, 0.45f, 1.95f, new Color(0.11f, 0.19f, 0.14f));

            // 5. GRAND BOULEVARD — EAST SIDE BLOCK 2 (z = 82..111, facing -X onto the boulevard!)
            // Haussmann_Corner at yaw=180 places turret at (-X, -Z) = SW corner facing the intersection!
            PlaceBlenderAsset("Haussmann_Corner",      "Corner Boulevard East 2",    buildings, new Vector3( 21.0f, 0f, 89.5f), 180f, true, false);
            PlaceBlenderAsset("Haussmann_Boulangerie", "Boulevard East Shop 2",      buildings, new Vector3( 19.0f, 0f, 104.1f), 270f, true, false);
            PlaceBlenderAsset("Haussmann_Apartment",   "Rue Haussmann NE Block 2",   buildings, new Vector3( 35.6f, 0f, 87.5f), 180f, true, false);
            PlaceBlenderAsset("Haussmann_Librairie",   "Rue Haussmann NE Block 3",   buildings, new Vector3( 49.8f, 0f, 87.5f), 180f, true, false);
        }

        static void BuildParisianStreetLife()
        {
            var props = CreateGroup("Parisian Street Props", cityRoot, Vector3.zero);
            var trees = CreateGroup("Parisian Trees", cityRoot, Vector3.zero);

            // 1. Iconic Art Nouveau Paris Métro Entrance right across from the café on the Square corner!
            // At yaw=180, Blender -Y (+Z in Unity) faces South (-Z), placing the overhead arch at z = 20.5 - 1.74 = 18.76
            PlaceBlenderAsset("Paris_MetroEntrance", "Metro Champ de Mars", props, new Vector3(17.5f, 0f, 20.5f), 180f, addCollider: true, castShadows: true);
            CreateShopSign("MÉTRO", props, new Vector3(17.5f, 3.32f, 18.67f),   0f, 2.8f, 0.42f, 2.65f, new Color(0.08f, 0.24f, 0.16f));
            CreateShopSign("MÉTRO", props, new Vector3(17.5f, 3.32f, 18.85f), 180f, 2.8f, 0.42f, 2.65f, new Color(0.08f, 0.24f, 0.16f));

            // 2. Iconic Emerald Wallace Fountain + 4 Banc Davioud Benches in Square de la Tour!
            PlaceBlenderAsset("Paris_WallaceFountain", "Fontaine Wallace", props, new Vector3(30.5f, 0f, 41.0f), 0f, addCollider: true, castShadows: true);
            PlaceBlenderAsset("Paris_Bench", "Square Bench North", props, new Vector3(30.5f, 0f, 45.2f), 180f, true, true);
            PlaceBlenderAsset("Paris_Bench", "Square Bench South", props, new Vector3(30.5f, 0f, 36.8f),   0f, true, true);
            PlaceBlenderAsset("Paris_Bench", "Square Bench West",  props, new Vector3(26.3f, 0f, 41.0f),  90f, true, true);
            PlaceBlenderAsset("Paris_Bench", "Square Bench East",  props, new Vector3(34.7f, 0f, 41.0f), 270f, true, true);

            // 3. Iconic Parisian Morris Columns at major corners
            PlaceBlenderAsset("Paris_MorrisColumn", "Colonne Morris SW", props, new Vector3(-10.8f, 0f, 18.8f),  30f, true, true);
            PlaceBlenderAsset("Paris_MorrisColumn", "Colonne Morris NE", props, new Vector3( 10.8f, 0f, 80.5f), 140f, true, false);
            PlaceBlenderAsset("Paris_MorrisColumn", "Colonne Morris NW", props, new Vector3(-10.8f, 0f, 80.5f), -45f, true, false);
            PlaceBlenderAsset("Paris_MorrisColumn", "Colonne Morris Cafe East", props, new Vector3(10.5f, 0f, 4.6f), 20f, true, true);

            // 4. Street Lamps, Plane Trees, and Benches lining the Grand Boulevard (both sides, z=22..112)
            float[] blvdZ = { 23f, 35f, 47f, 59f, 85f, 97f, 109f };
            for (int i = 0; i < blvdZ.Length; i++)
            {
                float z = blvdZ[i];
                for (int side = -1; side <= 1; side += 2)
                {
                    PlaceBlenderAsset("Paris_StreetLamp", $"Blvd Lamp {side}_{i}", props, new Vector3(side * 8.4f, 0f, z), 0f, true, i < 3);
                    PlaceParisTree(trees, new Vector3(side * 9.8f, 0f, z + 5.6f), 7.2f + (i % 3) * 0.9f, i % 3);
                    if (i % 2 == 0)
                        PlaceBlenderAsset("Paris_Bench", $"Blvd Bench {side}_{i}", props, new Vector3(side * 11.4f, 0f, z), side < 0 ? 90f : 270f, true, false);
                }
            }

            // 5. Lamps & Trees along Rue du Café promenade (South sidewalk, outside the café's central 8m view)
            float[] rueX = { -48f, -34f, -20f, -7.2f, 7.2f, 20f, 34f, 48f };
            for (int i = 0; i < rueX.Length; i++)
            {
                float x = rueX[i];
                PlaceBlenderAsset("Paris_StreetLamp", $"Cafe Promenade Lamp {i}", props, new Vector3(x, 0f, 5.1f), 0f, true, Mathf.Abs(x) < 25f);
                if (Mathf.Abs(x) > 12f)
                    PlaceParisTree(trees, new Vector3(x + (x < 0 ? -5.5f : 5.5f), 0f, 4.2f), 6.8f + (i % 2) * 1.1f, i % 3);
            }

            // 6. Trees & Lamps framing Square de la Tour
            Vector3[] squareTrees =
            {
                new Vector3(22.5f, 0f, 30.5f), new Vector3(38.5f, 0f, 30.5f),
                new Vector3(22.5f, 0f, 51.5f), new Vector3(38.5f, 0f, 51.5f),
                new Vector3(16.0f, 0f, 60.0f), new Vector3(44.0f, 0f, 60.0f),
                new Vector3(44.0f, 0f, 21.0f)
            };
            for (int i = 0; i < squareTrees.Length; i++)
                PlaceParisTree(trees, squareTrees[i], 7.6f + (i % 3) * 0.8f, i % 3);

            PlaceBlenderAsset("Paris_StreetLamp", "Square Lamp 1", props, new Vector3(26.5f, 0f, 35.5f), 0f, true, false);
            PlaceBlenderAsset("Paris_StreetLamp", "Square Lamp 2", props, new Vector3(34.5f, 0f, 46.5f), 0f, true, false);

            // 7. Champ-de-Mars Park Tree Alleys framing the monumental Eiffel Tower (z = 128..230)
            for (int row = 0; row < 7; row++)
            {
                float z = 130f + row * 16f;
                for (int side = -1; side <= 1; side += 2)
                {
                    PlaceStylizedBroadleaf(trees, new Vector3(side * 15f, 0f, z), 9.5f + (row % 3) * 1.4f, row % 3);
                    PlaceStylizedBroadleaf(trees, new Vector3(side * 27f, 0f, z + 7f), 10.5f + (row % 2) * 1.5f, (row + 1) % 3);
                }
            }

            // 8. Parisian Cast-Iron Bollards marking the northern Eiffel overlook & street corners gracefully
            for (float x = -56f; x <= 56f; x += 4.0f)
            {
                if (Mathf.Abs(x) < 7.5f)
                    PlaceBlenderAsset("Paris_Bollard", $"Overlook Bollard {x:0}", props, new Vector3(x, -0.14f, 117.5f), 0f, false, false);
                else
                    PlaceBlenderAsset("Paris_Bollard", $"Overlook Bollard {x:0}", props, new Vector3(x, 0f, 117.5f), 0f, false, false);
            }
        }

        static void BuildHybridBricktownSkyline()
        {
            var skyline = CreateGroup("Hybrid Bricktown Parisian Skyline", cityRoot, Vector3.zero);
            var prefabs = new List<GameObject>();
            for (int i = 1; i <= 20; i++)
            {
                var p = AssetDatabase.LoadAssetAtPath<GameObject>($"{BricktownDir}/Building_{i}.prefab");
                if (p != null) prefabs.Add(p);
            }
            if (prefabs.Count == 0)
            {
                Debug.LogWarning("[ParisWalkableCityBuilder] No Bricktown prefabs found; skipping hybrid skyline layer.");
                return;
            }

            // Place limestone-retextured Bricktown blocks in a cohesive 360° background ring behind the hero Haussmann blocks!
            var placements = new List<(Vector3 pos, float yaw, float targetHeight)>();

            // South backdrop (behind Café promenade row)
            for (float x = -72f; x <= 72f; x += 18f)
                placements.Add((new Vector3(x, 0f, -32f), 0f, 19f + (Mathf.Abs((int)x) % 5)));

            // West outer Parisian blocks
            for (float z = 5f; z <= 135f; z += 18f)
            {
                placements.Add((new Vector3(-78f, 0f, z), 90f, 18f + ((int)z % 5)));
                if (z > 30f && z < 76f)
                    placements.Add((new Vector3(-44f, 0f, z + 4f), 90f, 19f + ((int)z % 4)));
            }

            // East outer Parisian blocks
            for (float z = 5f; z <= 135f; z += 18f)
                placements.Add((new Vector3(76f, 0f, z), 270f, 18f + ((int)z % 5)));

            // Far Champ-de-Mars flanking skyline (left & right of the Eiffel Tower avenue, leaving central 90m park corridor open!)
            for (float z = 145f; z <= 255f; z += 22f)
            {
                placements.Add((new Vector3(-56f, 0f, z), 90f, 20f + ((int)z % 6)));
                placements.Add((new Vector3( 56f, 0f, z), 270f, 20f + ((int)z % 6)));
                placements.Add((new Vector3(-86f, 0f, z + 10f), 90f, 22f + ((int)z % 5)));
                placements.Add((new Vector3( 86f, 0f, z + 10f), 270f, 22f + ((int)z % 5)));
            }

            // Far Parisian horizon beyond the Eiffel Tower (z = 325m)
            for (float x = -140f; x <= 140f; x += 24f)
                placements.Add((new Vector3(x, 0f, 325f), 180f, 21f + (Mathf.Abs((int)x) % 6)));

            var stoneChoices = new[]
            {
                urpPalette["Paris_Bricktown_Cream"],
                urpPalette["Paris_Bricktown_Warm"],
                urpPalette["Paris_Limestone_Cream"],
                urpPalette["Paris_Limestone_Ivory"]
            };

            for (int idx = 0; idx < placements.Count; idx++)
            {
                var (pos, yaw, targetH) = placements[idx];
                var src = prefabs[idx % prefabs.Count];
                var wrap = CreateGroup($"Paris Skyline Block {idx}", skyline, pos);
                wrap.localRotation = Quaternion.Euler(0f, yaw, 0f);

                var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
                inst.transform.SetParent(wrap, false);
                inst.transform.localPosition = Vector3.zero;
                inst.transform.localRotation = Quaternion.identity;
                inst.transform.localScale = Vector3.one;

                foreach (var c in inst.GetComponentsInChildren<Collider>(true))
                    Object.DestroyImmediate(c);

                var renderers = inst.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) continue;

                Bounds b = renderers[0].bounds;
                for (int r = 1; r < renderers.Length; r++) b.Encapsulate(renderers[r].bounds);
                if (b.size.y > 0.01f)
                {
                    float s = targetH / b.size.y;
                    inst.transform.localScale = Vector3.one * s;
                    // Recalculate after scale so building sits flush on y=0
                    b = renderers[0].bounds;
                    for (int r = 1; r < renderers.Length; r++) b.Encapsulate(renderers[r].bounds);
                    inst.transform.position += new Vector3(pos.x - b.center.x, pos.y - b.min.y, pos.z - b.center.z);
                }

                // Remap Bricktown brick/metal materials to cohesive Parisian limestone, slate roofs, and dark French glass
                Material facadeMat = stoneChoices[idx % stoneChoices.Length];
                foreach (var rend in renderers)
                {
                    rend.shadowCastingMode = ShadowCastingMode.Off;
                    var mats = rend.sharedMaterials;
                    var remapped = new Material[mats.Length];
                    for (int mi = 0; mi < mats.Length; mi++)
                    {
                        string mName = mats[mi] != null ? mats[mi].name.ToLowerInvariant() : "";
                        if (mName.Contains("glass") || mName.Contains("window"))
                            remapped[mi] = urpPalette["Paris_Window_Glass"];
                        else if (mName.Contains("roof") || mName.Contains("metal") || mName.Contains("dark"))
                            remapped[mi] = urpPalette["Paris_Bricktown_Roof"];
                        else if (mName.Contains("trim") || mName.Contains("concrete") || mName.Contains("stone"))
                            remapped[mi] = urpPalette["Paris_CarvedStone_Trim"];
                        else
                            remapped[mi] = facadeMat;
                    }
                    rend.sharedMaterials = remapped;
                }
            }
        }

        static void ConfigureMonumentalEiffelTower()
        {
            Transform tower = root.Find("Eiffel Tower");
            if (tower == null)
            {
                Debug.LogWarning("[ParisWalkableCityBuilder] 'Eiffel Tower' not found in Paris.unity.");
                return;
            }

            // Reset wrapper to clean world origin on the Grand Boulevard axis (x=0, y=0, z=255)
            tower.localPosition = new Vector3(0f, 0f, 255f);
            tower.localRotation = Quaternion.identity;
            tower.localScale = Vector3.one;

            var renderers = tower.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
            {
                // Apply authentic metallic Brun Tour Eiffel iron URP material
                var eiffelMat = urpPalette["Paris_Eiffel_Iron"];
                foreach (var r in renderers)
                {
                    r.sharedMaterials = r.sharedMaterials.Select(_ => eiffelMat).ToArray();
                    r.shadowCastingMode = ShadowCastingMode.Off; // Saves huge Quest shadowmap budget at 255m distance
                }

                // Measure real current world bounds & scale accurately to ~285 meters tall!
                Bounds b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);

                if (b.size.y > 0.1f)
                {
                    const float targetHeightMeters = 285f;
                    float scaleFactor = targetHeightMeters / b.size.y;
                    tower.localScale = Vector3.one * scaleFactor;

                    // Re-measure after scaling and seat base pillars flush on y = 0.0!
                    b = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
                    tower.position += new Vector3(0f - b.center.x, 0f - b.min.y, 255f - b.center.z);
                }
            }
            EditorUtility.SetDirty(tower.gameObject);
        }

        static void BuildSkyClouds()
        {
            var clouds = CreateGroup("Paris sky clouds", root, Vector3.zero);
            EnsureCanopyMesh();
            string cloudMatPath = $"{MaterialsDir}/Paris_Summer_Cloud.mat";
            var cloudMat = AssetDatabase.LoadAssetAtPath<Material>(cloudMatPath);
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (cloudMat == null)
            {
                cloudMat = new Material(unlit) { name = "Paris_Summer_Cloud" };
                AssetDatabase.CreateAsset(cloudMat, cloudMatPath);
            }
            cloudMat.SetColor("_BaseColor", new Color(0.97f, 0.96f, 0.93f));
            cloudMat.enableInstancing = true;
            EditorUtility.SetDirty(cloudMat);

            for (int i = 0; i < 6; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    var c = new GameObject("Stylized Cloud", typeof(MeshFilter), typeof(MeshRenderer));
                    c.transform.SetParent(clouds, false);
                    c.transform.localPosition = new Vector3(-210f + i * 85f + j * 14f, 115f + (i % 3) * 18f + (j == 1 ? 4f : 0f), 320f + (i % 2) * 55f);
                    c.transform.localScale = new Vector3(36f, j == 1 ? 11f : 6.5f, 14f);
                    c.GetComponent<MeshFilter>().sharedMesh = canopyMesh;
                    var mr = c.GetComponent<MeshRenderer>();
                    mr.sharedMaterial = cloudMat;
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                }
            }
        }

        static void BuildPerimeterBoundaries()
        {
            // Clean invisible box colliders at the walkable outer perimeter (~126m x 122m) so players cannot walk off the world
            var bounds = CreateGroup("Walkable Paris Colliders", cityRoot, Vector3.zero);
            AddInvisibleWall("Boundary North Overlook", bounds, new Vector3(  0f, 2f, 118.2f), new Vector3(132f, 5f, 1f));
            AddInvisibleWall("Boundary South Back",     bounds, new Vector3(  0f, 2f,  -4.0f), new Vector3(132f, 5f, 1f));
            AddInvisibleWall("Boundary West Edge",      bounds, new Vector3(-63f, 2f,  57.0f), new Vector3(1f, 5f, 126f));
            AddInvisibleWall("Boundary East Edge",      bounds, new Vector3( 63f, 2f,  57.0f), new Vector3(1f, 5f, 126f));
        }

        static void AddInvisibleWall(string name, Transform parent, Vector3 pos, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var bc = go.AddComponent<BoxCollider>();
            bc.size = size;
        }

        static GameObject PlaceBlenderAsset(string fbxName, string instanceName, Transform parent, Vector3 pos, float yaw, bool addCollider, bool castShadows)
        {
            string assetPath = $"{BlenderDir}/{fbxName}.fbx";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null)
            {
                Debug.LogError($"[ParisWalkableCityBuilder] Missing Blender FBX asset at {assetPath}");
                return null;
            }

            var wrap = CreateGroup(instanceName, parent, pos);
            wrap.localRotation = Quaternion.Euler(0f, yaw, 0f);

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            inst.transform.SetParent(wrap, false);
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.identity;
            inst.transform.localScale = Vector3.one;

            var renderers = inst.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                r.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                var srcMats = r.sharedMaterials;
                var mapped = new Material[srcMats.Length];
                for (int i = 0; i < srcMats.Length; i++)
                {
                    string mName = srcMats[i] != null ? srcMats[i].name : "";
                    if (urpPalette.TryGetValue(mName, out var urpMat))
                        mapped[i] = urpMat;
                    else
                        mapped[i] = urpPalette["Paris_Limestone_Cream"];
                }
                r.sharedMaterials = mapped;
            }

            if (renderers.Length > 0)
            {
                Bounds b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);

                // Seat flush on requested Y
                float yOffset = pos.y - b.min.y;
                if (Mathf.Abs(yOffset) > 0.002f)
                {
                    inst.transform.position += Vector3.up * yOffset;
                    b.center += Vector3.up * yOffset;
                }

                if (addCollider)
                {
                    // Clean box collider matching the solid footprint (trimmed slightly on buildings so awnings don't block sidewalks!)
                    bool isBuilding = fbxName.StartsWith("Haussmann_", StringComparison.Ordinal);
                    var col = wrap.gameObject.AddComponent<BoxCollider>();
                    if (isBuilding)
                    {
                        float bw = fbxName == "Haussmann_Corner" ? 15.0f : 14.0f;
                        float bd = fbxName == "Haussmann_CafeHome" ? 10.0f : (fbxName == "Haussmann_Corner" ? 15.0f : 11.0f);
                        col.center = new Vector3(0f, 10f, 0f);
                        col.size = new Vector3(bw, 20f, bd);
                    }
                    else
                    {
                        col.center = wrap.InverseTransformPoint(b.center);
                        Vector3 localSize = new Vector3(
                            Mathf.Max(0.25f, b.size.x),
                            Mathf.Max(0.4f, b.size.y),
                            Mathf.Max(0.25f, b.size.z));
                        // Axis-aligned fallback for rotated props
                        if (Mathf.Abs(Mathf.Sin(yaw * Mathf.Deg2Rad)) > 0.7f)
                            (localSize.x, localSize.z) = (localSize.z, localSize.x);
                        col.size = localSize;
                    }
                }
            }

            return wrap.gameObject;
        }

        static void CreateShopSign(string textValue, Transform parent, Vector3 pos, float yaw, float width, float height, float fontSize, Color color)
        {
            var go = new GameObject("Sign " + textValue, typeof(TextMeshPro));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            var tmp = go.GetComponent<TextMeshPro>();
            if (fontAsset != null) tmp.font = fontAsset;
            tmp.text = textValue;
            tmp.fontSize = fontSize;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.rectTransform.sizeDelta = new Vector2(width, height);
        }

        static void PlaceParisTree(Transform parent, Vector3 pos, float height, int variant)
        {
            string prefabName = variant == 2 ? "LPW_Tree_C1_9m_01" : "LPW_Tree_A1_6.5m_01";
            string path = $"{NicromDir}/{prefabName}.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
                PlaceNicromPlant(prefabName, parent, pos, height, addTrunkCollider: true);
            else
                PlaceStylizedBroadleaf(parent, pos, height, variant);
        }

        static void PlaceNicromPlant(string prefabName, Transform parent, Vector3 pos, float height, bool addTrunkCollider = false)
        {
            string path = $"{NicromDir}/{prefabName}.prefab";
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (src == null) return;

            var wrap = CreateGroup("Tree " + prefabName, parent, pos);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            go.transform.SetParent(wrap, false);
            foreach (var lod in go.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(lod);
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);

            var renderers = go.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length == 0) return;

            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            if (b.size.y > 0.01f)
                wrap.localScale = Vector3.one * (height / b.size.y);
            wrap.localRotation = Quaternion.Euler(0f, rng.Next(360), 0f);

            foreach (var r in renderers)
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.sharedMaterials = r.sharedMaterials.Select(m =>
                {
                    if (m == null) return urpPalette["Paris_Leaves_0"];
                    if (nicromMatCache.TryGetValue(m, out var cached)) return cached;
                    var copy = RegMat("Nicrom_" + m.name.Replace(' ', '_'), Color.white, 0.05f, 0f);
                    if (m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null)
                        copy.SetTexture("_BaseMap", m.GetTexture("_MainTex"));
                    else if (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null)
                        copy.SetTexture("_BaseMap", m.GetTexture("_BaseMap"));
                    copy.SetFloat("_Cull", 0f);
                    EditorUtility.SetDirty(copy);
                    nicromMatCache[m] = copy;
                    return copy;
                }).ToArray();
            }

            if (addTrunkCollider)
            {
                var col = wrap.gameObject.AddComponent<CapsuleCollider>();
                col.radius = 0.25f / Mathf.Max(0.2f, wrap.localScale.x);
                col.height = 2.4f / Mathf.Max(0.2f, wrap.localScale.y);
                col.center = new Vector3(0f, col.height * 0.5f, 0f);
            }
        }

        static void PlaceStylizedBroadleaf(Transform parent, Vector3 pos, float height, int variant)
        {
            EnsureCanopyMesh();
            var t = CreateGroup("Park Broadleaf Tree", parent, pos);
            var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            trunk.name = "Trunk";
            trunk.transform.SetParent(t, false);
            trunk.transform.localPosition = Vector3.up * (height * 0.30f);
            trunk.transform.localScale = new Vector3(height * 0.055f, height * 0.30f, height * 0.055f);
            trunk.GetComponent<Renderer>().sharedMaterial = urpPalette["Paris_Tree_Bark"];
            trunk.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            Object.DestroyImmediate(trunk.GetComponent<Collider>());

            for (int i = 0; i < 4; i++)
            {
                float ang = i * 1.65f + variant;
                var crown = new GameObject("Crown", typeof(MeshFilter), typeof(MeshRenderer));
                crown.transform.SetParent(t, false);
                crown.transform.localPosition = new Vector3(Mathf.Cos(ang) * height * 0.14f, height * (0.64f + (i % 2) * 0.12f), Mathf.Sin(ang) * height * 0.14f);
                crown.transform.localScale = new Vector3(height * 0.54f, height * 0.46f, height * 0.54f);
                crown.GetComponent<MeshFilter>().sharedMesh = canopyMesh;
                var mr = crown.GetComponent<MeshRenderer>();
                mr.sharedMaterial = urpPalette["Paris_Leaves_" + ((variant + i) % 3)];
                mr.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        static void EnsureCanopyMesh()
        {
            if (canopyMesh != null) return;
            string meshPath = $"{MaterialsDir}/Paris_Faceted_Canopy.asset";
            canopyMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (canopyMesh != null) return;

            var points = new List<Vector3>();
            var tris = new List<int>();
            const int rings = 5, segs = 9;
            Vector3 Pt(int r, int s) => new Vector3(
                Mathf.Sin(r * Mathf.PI / rings) * Mathf.Cos(s * Mathf.PI * 2f / segs),
                Mathf.Cos(r * Mathf.PI / rings),
                Mathf.Sin(r * Mathf.PI / rings) * Mathf.Sin(s * Mathf.PI * 2f / segs)) * 0.5f;

            for (int r = 0; r < rings; r++)
            for (int s = 0; s < segs; s++)
            {
                var a = Pt(r, s); var b = Pt(r + 1, s); var c = Pt(r + 1, s + 1); var d = Pt(r, s + 1);
                int n = points.Count;
                points.AddRange(new[] { a, c, b, a, d, c });
                tris.AddRange(Enumerable.Range(n, 6));
            }

            canopyMesh = new Mesh { name = "Paris_Faceted_Canopy", vertices = points.ToArray(), triangles = tris.ToArray() };
            canopyMesh.RecalculateNormals();
            AssetDatabase.CreateAsset(canopyMesh, meshPath);
        }

        static void EnsureTeleportArea(GameObject surface)
        {
            if (surface == null) return;
            var col = surface.GetComponent<Collider>();
            if (col == null) col = surface.AddComponent<BoxCollider>();

            var area = surface.GetComponent<ClearTeleportArea>();
            if (area == null) area = surface.AddComponent<ClearTeleportArea>();
            if (teleportProvider != null) area.teleportationProvider = teleportProvider;
            area.interactionLayers = InteractionLayerMask.GetMask("Teleport");
            area.filterSelectionByHitNormal = true;
            area.colliders.Clear();
            area.colliders.Add(col);
            EditorUtility.SetDirty(area);
        }

        static Transform CreateGroup(string name, Transform parent, Vector3 localPos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            return t;
        }

        static GameObject CreateBox(string name, Transform parent, Vector3 pos, Vector3 scale, Material mat, bool withCollider, bool castShadows)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            if (!withCollider)
                Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }
    }
}
