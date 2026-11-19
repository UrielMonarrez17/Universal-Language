using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Alterlife.Immersive.Editor
{
    /// <summary>
    /// Configures the animated Blender FBX (materials, Generic rig, 8 animation clips),
    /// builds <c>LucasBaristaAnimator.controller</c>, and wires <see cref="WaiterAnimationController"/>
    /// onto <c>Paris waiter</c> in <c>Assets/Scenes/Paris.unity</c>.
    /// </summary>
    public static class WireAnimatedBarista
    {
        const string FbxPath = "Assets/Assets-paris/fluxcoffee_barista_animated.fbx";
        const string AlbedoTexPath = "Assets/Assets-paris/Textures/barista_albedo.png";
        const string NormalTexPath = "Assets/Assets-paris/Textures/barista_normal.png";
        const string MaterialsDir = "Assets/Assets-paris/Materials";
        const string BodyMatPath = MaterialsDir + "/Barista_Body.mat";
        const string MouthMatPath = MaterialsDir + "/Barista_MouthInterior.mat";
        const string ControllerPath = "Assets/Alterlife/Immersive/Paris/LucasBaristaAnimator.controller";

        static readonly string[] RequiredClips =
        {
            "Idle",
            "Wave",
            "Talking_01",
            "Talking_02",
            "Hand_Gesture",
            "Listening_Nod",
            "Thinking",
            "Serve_Present",
        };

        static readonly HashSet<string> LoopingClips = new HashSet<string>(StringComparer.Ordinal)
        {
            "Idle",
            "Talking_01",
            "Talking_02",
            "Listening_Nod",
            "Thinking",
        };

        [MenuItem("Alterlife/Restaurant/Wire Animated Barista")]
        public static void Run()
        {
            AssetDatabase.Refresh();

            EnsureNormalMapImporter();
            Material bodyMat = EnsureBodyMaterial();
            Material mouthMat = EnsureMouthMaterial();
            ConfigureFbxImporter(bodyMat, mouthMat);
            AnimatorController controller = BuildAnimatorController();
            WireParisScene(controller);

            AssetDatabase.SaveAssets();
            Debug.Log("ALTERLIFE_ANIMATED_BARISTA_WIRED");
        }

        static void EnsureNormalMapImporter()
        {
            var texImporter = AssetImporter.GetAtPath(NormalTexPath) as TextureImporter;
            if (texImporter == null)
                throw new InvalidOperationException("Missing normal map texture at " + NormalTexPath);

            if (texImporter.textureType != TextureImporterType.NormalMap)
            {
                texImporter.textureType = TextureImporterType.NormalMap;
                texImporter.SaveAndReimport();
            }
        }

        static Material EnsureBodyMaterial()
        {
            Directory.CreateDirectory(MaterialsDir);
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (litShader == null)
                throw new InvalidOperationException("Could not locate shader 'Universal Render Pipeline/Lit'.");

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(BodyMatPath);
            if (mat == null)
            {
                mat = new Material(litShader) { name = "Barista_Body" };
                AssetDatabase.CreateAsset(mat, BodyMatPath);
            }
            else if (mat.shader != litShader)
            {
                mat.shader = litShader;
            }

            Texture2D albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(AlbedoTexPath);
            Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(NormalTexPath);
            if (albedo == null)
                throw new InvalidOperationException("Missing albedo texture at " + AlbedoTexPath);

            mat.SetColor("_BaseColor", Color.white);
            mat.SetTexture("_BaseMap", albedo);
            mat.SetTexture("_BumpMap", normal);
            if (normal != null)
                mat.EnableKeyword("_NORMALMAP");
            mat.SetFloat("_Smoothness", 0.35f);
            mat.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material EnsureMouthMaterial()
        {
            Directory.CreateDirectory(MaterialsDir);
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (litShader == null)
                throw new InvalidOperationException("Could not locate shader 'Universal Render Pipeline/Lit'.");

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(MouthMatPath);
            if (mat == null)
            {
                mat = new Material(litShader) { name = "Barista_MouthInterior" };
                AssetDatabase.CreateAsset(mat, MouthMatPath);
            }
            else if (mat.shader != litShader)
            {
                mat.shader = litShader;
            }

            mat.SetColor("_BaseColor", new Color(0.11f, 0.025f, 0.025f, 1f));
            mat.SetFloat("_Smoothness", 0.15f);
            mat.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void ConfigureFbxImporter(Material bodyMat, Material mouthMat)
        {
            var importer = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
            if (importer == null)
                throw new InvalidOperationException("Missing animated barista FBX at " + FbxPath);

            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importAnimation = true;
            importer.importBlendShapes = true;
            importer.importNormals = ModelImporterNormals.Import;

            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "BakedMaterial"), bodyMat);
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "MouthInterior"), mouthMat);
            importer.SaveAndReimport();

            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            if (clips == null || clips.Length == 0)
                throw new InvalidOperationException("No clip animations found in FBX: " + FbxPath);

            for (int i = 0; i < clips.Length; i++)
                clips[i].loopTime = LoopingClips.Contains(clips[i].name);

            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }

        static AnimatorController BuildAnimatorController()
        {
            string dir = Path.GetDirectoryName(ControllerPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
                AssetDatabase.DeleteAsset(ControllerPath);

            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("IsTalking", AnimatorControllerParameterType.Bool);
            controller.AddParameter("TalkVariant", AnimatorControllerParameterType.Int);
            controller.AddParameter("IsListening", AnimatorControllerParameterType.Bool);
            controller.AddParameter("IsThinking", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Wave", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("HandGesture", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("ServePresent", AnimatorControllerParameterType.Trigger);

            Dictionary<string, AnimationClip> clipMap = AssetDatabase.LoadAllAssetsAtPath(FbxPath)
                .OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal))
                .ToDictionary(c => c.name, c => c, StringComparer.Ordinal);

            foreach (string required in RequiredClips)
            {
                if (!clipMap.ContainsKey(required))
                    throw new InvalidOperationException($"Missing required AnimationClip '{required}' in {FbxPath}");
            }

            AnimatorStateMachine sm = controller.layers[0].stateMachine;
            var states = new Dictionary<string, AnimatorState>(StringComparer.Ordinal);
            Vector3[] positions =
            {
                new Vector3(300, 120, 0),  // Idle
                new Vector3(40,  -40, 0),  // Wave
                new Vector3(580, 40,  0),  // Talking_01
                new Vector3(580, 190, 0),  // Talking_02
                new Vector3(40,  100, 0),  // Hand_Gesture
                new Vector3(300, -40, 0),  // Listening_Nod
                new Vector3(300, 270, 0),  // Thinking
                new Vector3(40,  220, 0),  // Serve_Present
            };

            for (int i = 0; i < RequiredClips.Length; i++)
            {
                string clipName = RequiredClips[i];
                AnimatorState st = sm.AddState(clipName, positions[i]);
                st.motion = clipMap[clipName];
                states[clipName] = st;
            }
            sm.defaultState = states["Idle"];

            // Helper for standard instant-blend transitions
            static AnimatorStateTransition AddStateTransition(AnimatorState from, AnimatorState to, bool hasExitTime = false, float exitTime = 0.9f)
            {
                AnimatorStateTransition tr = from.AddTransition(to);
                tr.hasExitTime = hasExitTime;
                tr.exitTime = exitTime;
                tr.hasFixedDuration = true;
                tr.duration = 0.2f;
                return tr;
            }

            // Idle <-> Talking_01 / Talking_02
            var t = AddStateTransition(states["Idle"], states["Talking_01"]);
            t.AddCondition(AnimatorConditionMode.If, 0, "IsTalking");
            t.AddCondition(AnimatorConditionMode.Equals, 0, "TalkVariant");

            t = AddStateTransition(states["Idle"], states["Talking_02"]);
            t.AddCondition(AnimatorConditionMode.If, 0, "IsTalking");
            t.AddCondition(AnimatorConditionMode.Equals, 1, "TalkVariant");

            foreach (string talkState in new[] { "Talking_01", "Talking_02" })
            {
                AnimatorState src = states[talkState];

                t = AddStateTransition(src, states["Idle"]);
                t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsTalking");
                t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsListening");
                t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsThinking");

                t = AddStateTransition(src, states["Listening_Nod"]);
                t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsTalking");
                t.AddCondition(AnimatorConditionMode.If, 0, "IsListening");

                t = AddStateTransition(src, states["Thinking"]);
                t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsTalking");
                t.AddCondition(AnimatorConditionMode.If, 0, "IsThinking");
            }

            // Idle <-> Listening_Nod
            t = AddStateTransition(states["Idle"], states["Listening_Nod"]);
            t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsTalking");
            t.AddCondition(AnimatorConditionMode.If, 0, "IsListening");
            t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsThinking");

            t = AddStateTransition(states["Listening_Nod"], states["Idle"]);
            t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsTalking");
            t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsListening");
            t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsThinking");

            t = AddStateTransition(states["Listening_Nod"], states["Thinking"]);
            t.AddCondition(AnimatorConditionMode.If, 0, "IsThinking");

            t = AddStateTransition(states["Listening_Nod"], states["Talking_01"]);
            t.AddCondition(AnimatorConditionMode.If, 0, "IsTalking");
            t.AddCondition(AnimatorConditionMode.Equals, 0, "TalkVariant");

            t = AddStateTransition(states["Listening_Nod"], states["Talking_02"]);
            t.AddCondition(AnimatorConditionMode.If, 0, "IsTalking");
            t.AddCondition(AnimatorConditionMode.Equals, 1, "TalkVariant");

            // Idle <-> Thinking
            t = AddStateTransition(states["Idle"], states["Thinking"]);
            t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsTalking");
            t.AddCondition(AnimatorConditionMode.If, 0, "IsThinking");

            t = AddStateTransition(states["Thinking"], states["Idle"]);
            t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsTalking");
            t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsThinking");

            t = AddStateTransition(states["Thinking"], states["Talking_01"]);
            t.AddCondition(AnimatorConditionMode.If, 0, "IsTalking");
            t.AddCondition(AnimatorConditionMode.Equals, 0, "TalkVariant");

            t = AddStateTransition(states["Thinking"], states["Talking_02"]);
            t.AddCondition(AnimatorConditionMode.If, 0, "IsTalking");
            t.AddCondition(AnimatorConditionMode.Equals, 1, "TalkVariant");

            // AnyState -> One-shot gesture triggers & exit-time returns
            foreach (var (stateName, triggerName) in new[]
            {
                ("Wave", "Wave"),
                ("Hand_Gesture", "HandGesture"),
                ("Serve_Present", "ServePresent"),
            })
            {
                AnimatorState st = states[stateName];
                AnimatorStateTransition anyTr = sm.AddAnyStateTransition(st);
                anyTr.canTransitionToSelf = false;
                anyTr.hasExitTime = false;
                anyTr.hasFixedDuration = true;
                anyTr.duration = 0.2f;
                anyTr.AddCondition(AnimatorConditionMode.If, 0, triggerName);

                var retTalk1 = AddStateTransition(st, states["Talking_01"], hasExitTime: true, exitTime: 0.9f);
                retTalk1.AddCondition(AnimatorConditionMode.If, 0, "IsTalking");
                retTalk1.AddCondition(AnimatorConditionMode.Equals, 0, "TalkVariant");

                var retTalk2 = AddStateTransition(st, states["Talking_02"], hasExitTime: true, exitTime: 0.9f);
                retTalk2.AddCondition(AnimatorConditionMode.If, 0, "IsTalking");
                retTalk2.AddCondition(AnimatorConditionMode.Equals, 1, "TalkVariant");

                var retIdle = AddStateTransition(st, states["Idle"], hasExitTime: true, exitTime: 0.9f);
                retIdle.AddCondition(AnimatorConditionMode.IfNot, 0, "IsTalking");
            }

            EditorUtility.SetDirty(controller);
            return controller;
        }

        static void WireParisScene(AnimatorController controller)
        {
            var scene = EditorSceneManager.OpenScene(ParisPrototypeBuilder.ScenePath);
            GameObject rootGo = GameObject.Find("Paris Restaurant Prototype");
            Transform waiter = rootGo != null ? rootGo.transform.Find("Paris waiter") : null;
            if (waiter == null)
            {
                GameObject fallback = GameObject.Find("Paris waiter");
                waiter = fallback != null ? fallback.transform : null;
            }
            if (waiter == null)
                throw new InvalidOperationException("Could not find 'Paris waiter' in " + ParisPrototypeBuilder.ScenePath);

            // Remove legacy static GLB or older animated child instances
            for (int i = waiter.childCount - 1; i >= 0; i--)
            {
                Transform child = waiter.GetChild(i);
                if (child.name.Contains("fluxcoffee", StringComparison.OrdinalIgnoreCase))
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
            }

            GameObject fbxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            if (fbxPrefab == null)
                throw new InvalidOperationException("Could not load FBX prefab at " + FbxPath);

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(fbxPrefab, waiter);
            instance.name = "fluxcoffee_barista_animated";
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            waiter.rotation = Quaternion.Euler(0f, 163f, 0f);
            EditorUtility.SetDirty(waiter);

            Animator anim = instance.GetComponent<Animator>();
            if (anim == null)
                anim = instance.AddComponent<Animator>();
            anim.runtimeAnimatorController = controller;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            EditorUtility.SetDirty(anim);

            WaiterAnimationController animCtrl = waiter.GetComponent<WaiterAnimationController>();
            if (animCtrl == null)
                animCtrl = waiter.gameObject.AddComponent<WaiterAnimationController>();
            animCtrl.animator = anim;
            animCtrl.faceRenderer = instance.GetComponentInChildren<SkinnedMeshRenderer>();
            animCtrl.voice = UnityEngine.Object.FindAnyObjectByType<RestaurantVoiceBridge>(FindObjectsInactive.Include);
            animCtrl.evaluation = UnityEngine.Object.FindAnyObjectByType<RestaurantEvaluation>(FindObjectsInactive.Include);
            animCtrl.waiterAudio = waiter.GetComponent<AudioSource>();
            EditorUtility.SetDirty(animCtrl);

            WaiterPresentation presentation = UnityEngine.Object.FindAnyObjectByType<WaiterPresentation>(FindObjectsInactive.Include);
            if (presentation != null)
            {
                presentation.invertFacing = false;
                EditorUtility.SetDirty(presentation);
            }

            BoxCollider box = waiter.GetComponent<BoxCollider>();
            if (box != null)
            {
                box.center = new Vector3(0f, 0.86f, 0f);
                box.size = new Vector3(0.65f, 1.72f, 0.45f);
                EditorUtility.SetDirty(box);
            }

            EditorSceneManager.SaveScene(scene);
        }
    }
}
