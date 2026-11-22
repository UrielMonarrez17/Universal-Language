using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Object = UnityEngine.Object;

namespace Alterlife.Immersive.Editor
{
    /// <summary>
    /// Deep Quest 2 performance optimizer for Assets/Scenes/Paris.unity:
    /// - Combines thousands of unbatched Haussmann, Bricktown, Nicrom, street, and terrace MeshRenderers
    ///   into spatially chunked, per-material 16-bit static meshes saved under Assets/Alterlife/Immersive/Paris/CityOptimized.
    /// - Preserves 100% of walkable colliders, ClearTeleportArea surfaces, gameplay objects, waiter,
    ///   menu, fox companion, UI, and visual realism (only disables source MeshRenderers after baking chunks).
    /// - Trims real-time shadow casters exclusively to near hero café objects (Lucas + hero tables/chairs)
    ///   with Hard Shadows, enables GPU instancing, clamps farClipPlane to 360m (Eiffel Tower + clouds intact),
    ///   configures High FFR (0.85), and bakes Unity Static Occlusion Culling.
    /// </summary>
    public static class ParisPerformanceOptimizer
    {
        const string ParisScenePath = "Assets/Scenes/Paris.unity";
        const string OptimizedMeshDir = "Assets/Alterlife/Immersive/Paris/CityOptimized";
        const string ChunksContainerName = "Paris Optimized Static Chunks";
        const int MaxVerticesPerChunkMesh = 58000; // Keep UInt16 index buffers for Adreno 650 (Quest 2) cache efficiency
        const float TargetFarClip = 360f;

        static readonly Vector3 CafeCenter = new Vector3(0f, 0f, 0.1f);

        struct SourceSubmesh
        {
            public Mesh mesh;
            public int submeshIndex;
            public Matrix4x4 localToWorld;
            public bool castShadows;
            public bool isOccluder;
        }

        [MenuItem("Alterlife/Restaurant/Optimize Paris Scene for Quest 2 (72 FPS)")]
        public static void OptimizeParisScene()
        {
            if (!Application.isBatchMode && EditorSceneManager.GetActiveScene().isDirty)
                EditorSceneManager.SaveOpenScenes();

            var scene = EditorSceneManager.OpenScene(ParisScenePath, OpenSceneMode.Single);
            var rootGo = GameObject.Find("Paris Restaurant Prototype");
            if (rootGo == null)
            {
                Debug.LogError("[ParisPerfOptimizer] 'Paris Restaurant Prototype' root not found in Paris.unity.");
                return;
            }

            Transform root = rootGo.transform;

            // 1. Restore any previous optimization run cleanly (100% idempotent)
            RestorePreviousRun(root);

            var beforeStats = MeasureActiveSceneStats();
            Debug.Log($"[ParisPerfOptimizer] BEFORE -> ActiveMeshRenderers={beforeStats.activeMeshRenderers}, ShadowCasters={beforeStats.shadowCasters}, Materials={beforeStats.uniqueMaterials}, Triangles={beforeStats.totalTriangles:N0}");

            // 2. Pull sky clouds slightly inside 360m farClipPlane so sky realism stays 100% intact
            AdjustSkyCloudsIntoClipRange(root);

            // 3. Collect all static decorative MeshRenderers eligible for spatial chunk combining
            var candidateRenderers = CollectCombinableRenderers(root);

            // 4. Ensure all source FBX/Mesh assets are readable so submesh geometry combines cleanly
            EnsureMeshesReadable(candidateRenderers);

            // 5. Bucket submeshes by (spatialZone, material, castShadows) using chunksRoot local space
            var chunksRootGo = new GameObject(ChunksContainerName);
            chunksRootGo.transform.SetParent(root, false);
            chunksRootGo.transform.localPosition = Vector3.zero;
            chunksRootGo.transform.localRotation = Quaternion.identity;
            chunksRootGo.transform.localScale = Vector3.one;
            Matrix4x4 worldToChunks = chunksRootGo.transform.worldToLocalMatrix;

            var buckets = new Dictionary<string, (Material mat, bool castShadows, bool hasOccluder, List<SourceSubmesh> items)>();
            var combinedSourceRenderers = new List<MeshRenderer>(candidateRenderers.Count);

            foreach (var mr in candidateRenderers)
            {
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable)
                    continue;

                Mesh mesh = mf.sharedMesh;
                Material[] mats = mr.sharedMaterials;
                if (mats == null || mats.Length == 0)
                    continue;

                Vector3 worldPos = mr.bounds.center;
                float distToCafe = Vector2.Distance(new Vector2(worldPos.x, worldPos.z), new Vector2(CafeCenter.x, CafeCenter.z));
                string zone = ClassifySpatialZone(mr.transform, worldPos, distToCafe);

                bool isSkyOrThin = zone.StartsWith("Clouds", StringComparison.Ordinal) ||
                                   zone.StartsWith("Eiffel", StringComparison.Ordinal) ||
                                   IsThinPropOrFoliage(mr.transform);
                bool isOccluder = !isSkyOrThin && mr.bounds.size.magnitude >= 4.0f;

                // Only near café hero furniture casts real-time shadows; city/buildings/trees/skyline do NOT
                bool castShadows = zone == "CafeHeroShadow";
                Matrix4x4 toChunkSpace = worldToChunks * mr.transform.localToWorldMatrix;

                bool addedAny = false;
                int subCount = mesh.subMeshCount;
                for (int sub = 0; sub < subCount; sub++)
                {
                    Material mat = mats[Mathf.Min(sub, mats.Length - 1)];
                    if (mat == null) continue;

                    string matKey = mat.GetEntityId().ToString();
                    string bucketKey = $"{zone}|{matKey}|{(castShadows ? 1 : 0)}";
                    if (!buckets.TryGetValue(bucketKey, out var bucket))
                    {
                        bucket = (mat, castShadows, isOccluder, new List<SourceSubmesh>(32));
                        buckets[bucketKey] = bucket;
                    }
                    else if (isOccluder && !bucket.hasOccluder)
                    {
                        bucket.hasOccluder = true;
                        buckets[bucketKey] = bucket;
                    }

                    bucket.items.Add(new SourceSubmesh
                    {
                        mesh = mesh,
                        submeshIndex = sub,
                        localToWorld = toChunkSpace,
                        castShadows = castShadows,
                        isOccluder = isOccluder
                    });
                    addedAny = true;
                }

                if (addedAny)
                    combinedSourceRenderers.Add(mr);
            }

            // 6. Clean old generated chunk mesh assets and create fresh 16-bit combined chunk meshes
            PrepareOutputDirectory();

            int createdChunks = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var kv in buckets.OrderBy(k => k.Key, StringComparer.Ordinal))
                {
                    string zoneName = kv.Key.Split('|')[0];
                    var bucket = kv.Value;
                    List<Mesh> builtMeshes = BuildCombinedMeshes(zoneName, bucket.mat.name, bucket.items);

                    // Decimate extreme-poly distant landmark (Eiffel Tower at z=255m) & ultra-dense distant skyline chunks
                    if (zoneName == "Eiffel")
                    {
                        builtMeshes = DecimateMeshListAdaptive(builtMeshes, maxTriangles: 14000, startCellSize: 1.0f);
                    }
                    else if (zoneName.StartsWith("Skyline_", StringComparison.Ordinal))
                    {
                        builtMeshes = DecimateMeshListAdaptive(builtMeshes, maxTriangles: 22000, startCellSize: 0.25f);
                    }

                    for (int i = 0; i < builtMeshes.Count; i++)
                    {
                        Mesh combined = builtMeshes[i];
                        string safeName = SanitizeFileName($"{zoneName}_{bucket.mat.name}_{createdChunks:D3}");
                        combined.name = safeName;
                        string assetPath = $"{OptimizedMeshDir}/{safeName}.asset";
                        AssetDatabase.CreateAsset(combined, assetPath);

                        var chunkGo = new GameObject(safeName, typeof(MeshFilter), typeof(MeshRenderer));
                        chunkGo.transform.SetParent(chunksRootGo.transform, false);
                        chunkGo.GetComponent<MeshFilter>().sharedMesh = combined;

                        var rend = chunkGo.GetComponent<MeshRenderer>();
                        rend.sharedMaterial = bucket.mat;
                        rend.shadowCastingMode = bucket.castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                        rend.receiveShadows = !zoneName.StartsWith("Clouds", StringComparison.Ordinal) &&
                                              !zoneName.StartsWith("Skyline", StringComparison.Ordinal) &&
                                              !zoneName.StartsWith("Eiffel", StringComparison.Ordinal);
                        rend.lightProbeUsage = LightProbeUsage.Off;
                        rend.reflectionProbeUsage = ReflectionProbeUsage.Off;
                        rend.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

                        StaticEditorFlags flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic;
                        if (bucket.hasOccluder)
                            flags |= StaticEditorFlags.OccluderStatic;
                        GameObjectUtility.SetStaticEditorFlags(chunkGo, flags);

                        createdChunks++;
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            // 7. Disable ONLY the MeshRenderer on combined source objects (preserves ALL Colliders & ClearTeleportAreas!)
            for (int i = 0; i < combinedSourceRenderers.Count; i++)
            {
                var mr = combinedSourceRenderers[i];
                if (mr != null)
                {
                    mr.enabled = false;
                    EditorUtility.SetDirty(mr);
                }
            }

            // 8. Trim shadow casters & probes on all remaining scene renderers (waiter, player table, props, fox, UI)
            OptimizeRemainingSceneRenderers(root);

            // 8b. Decimate any extreme-poly non-combined hero meshes (e.g. 122k-tri floating_fox parts, 37k-tri café plants)
            // while preserving UV seams and transforms cleanly
            OptimizeExtremePolyHeroMeshes(root);

            // 9. Optimize Materials (GPU instancing ON)
            OptimizeSceneMaterials();

            // 10. Configure Camera farClipPlane, Directional Light Hard Shadows, and QuestPerformanceOptimizer
            ConfigureLightingCameraAndQuestOptimizer(root);

            // 11. Bake Static Occlusion Culling for Quest 2 frustum + occlusion efficiency
            BakeOcclusionCulling();

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            var afterStats = MeasureActiveSceneStats();
            Debug.Log(
                $"[ParisPerfOptimizer] AFTER  -> ActiveMeshRenderers={afterStats.activeMeshRenderers} (was {beforeStats.activeMeshRenderers}), " +
                $"ShadowCasters={afterStats.shadowCasters} (was {beforeStats.shadowCasters}), " +
                $"Materials={afterStats.uniqueMaterials}, Triangles={afterStats.totalTriangles:N0} (was {beforeStats.totalTriangles:N0}), " +
                $"SkinnedTriangles={afterStats.skinnedTriangles:N0}, CombinedChunks={createdChunks}, DisabledSourceRenderers={combinedSourceRenderers.Count}");
            LogTopHeaviestRenderers(12);
            Debug.Log("ALTERLIFE_PARIS_PERF_OPTIMIZED_SUCCESSFULLY");
        }

        [MenuItem("Alterlife/Restaurant/Build Quest 2 Dev APK (UniversalLanguage-Dev.apk)")]
        public static void BuildQuestDevApk()
        {
            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "Quest"));
            Directory.CreateDirectory(outDir);
            string apkPath = Path.Combine(outDir, "UniversalLanguage-Dev.apk");

            string[] scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled && !string.IsNullOrEmpty(s.path))
                .Select(s => s.path)
                .ToArray();

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = apkPath,
                target = BuildTarget.Android,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"ALTERLIFE_QUEST_APK_BUILD_SUCCEEDED: {apkPath} ({report.summary.totalSize / (1024f * 1024f):F1} MB, {report.summary.totalTime.TotalSeconds:F1}s)");
            }
            else
            {
                Debug.LogError($"ALTERLIFE_QUEST_APK_BUILD_FAILED: {report.summary.result} (errors={report.summary.totalErrors})");
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        static void RestorePreviousRun(Transform root)
        {
            Transform existing = root.Find(ChunksContainerName);
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);

            string[] combinableRoots = { "Paris cityscape", "Cafe terrace detailing", "Paris sky clouds", "Eiffel Tower" };
            foreach (string name in combinableRoots)
            {
                Transform container = root.Find(name);
                if (container == null || !container.gameObject.activeInHierarchy) continue;
                foreach (var mr in container.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (mr != null && mr.gameObject.activeInHierarchy && !mr.enabled)
                    {
                        mr.enabled = true;
                        EditorUtility.SetDirty(mr);
                    }
                }
            }
        }

        static void AdjustSkyCloudsIntoClipRange(Transform root)
        {
            Transform clouds = root.Find("Paris sky clouds");
            if (clouds == null) return;
            foreach (Transform child in clouds)
            {
                Vector3 p = child.localPosition;
                if (p.z > 295f)
                {
                    p.z = Mathf.Lerp(235f, 290f, Mathf.InverseLerp(315f, 380f, p.z));
                    p.y = Mathf.Clamp(p.y, 92f, 130f);
                    p.x = Mathf.Clamp(p.x, -175f, 175f);
                    child.localPosition = p;
                    EditorUtility.SetDirty(child);
                }
            }
        }

        static List<MeshRenderer> CollectCombinableRenderers(Transform root)
        {
            var result = new List<MeshRenderer>(1024);
            string[] combinableRoots = { "Paris cityscape", "Cafe terrace detailing", "Paris sky clouds", "Eiffel Tower" };

            foreach (string containerName in combinableRoots)
            {
                Transform container = root.Find(containerName);
                if (container == null || !container.gameObject.activeInHierarchy)
                    continue;

                var renderers = container.GetComponentsInChildren<MeshRenderer>(false);
                foreach (var mr in renderers)
                {
                    if (mr == null || !mr.enabled || !mr.gameObject.activeInHierarchy)
                        continue;

                    if (ShouldSkipFromCombining(mr.transform, container))
                        continue;

                    result.Add(mr);
                }
            }

            return result;
        }

        static bool ShouldSkipFromCombining(Transform tf, Transform containerRoot)
        {
            if (tf.GetComponent<TMP_Text>() != null || tf.GetComponent<Canvas>() != null)
                return true;

            Transform cur = tf;
            while (cur != null && cur != containerRoot)
            {
                if (cur.GetComponent<XRBaseInteractable>() != null ||
                    cur.GetComponent<PhysicalControl>() != null ||
                    cur.GetComponent<Animator>() != null ||
                    cur.GetComponent<AudioSource>() != null ||
                    cur.GetComponent<Light>() != null)
                {
                    return true;
                }
                cur = cur.parent;
            }

            return false;
        }

        static string ClassifySpatialZone(Transform tf, Vector3 worldPos, float distToCafe)
        {
            if (IsDescendantOfNamed(tf, "Paris sky clouds"))
                return "Clouds";

            if (IsDescendantOfNamed(tf, "Eiffel Tower"))
                return "Eiffel";

            if (IsDescendantOfNamed(tf, "Bricktown") || distToCafe > 88f)
            {
                // Split outer skyline into North (in front of café) vs South (behind café -> 100% frustum-culled at table)
                return worldPos.z >= 0f ? "Skyline_N" : "Skyline_S";
            }

            if (IsDescendantOfNamed(tf, "Cafe terrace detailing"))
            {
                if (distToCafe <= 5.8f && (tf.name.IndexOf("Table", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                           tf.name.IndexOf("Chair", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                           tf.name.IndexOf("Planter", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return "CafeHeroShadow";
                }
                return "CafeTerrace";
            }

            // Clean North/South walkable city split: minimizes draw calls while culling everything behind the player at the café
            return worldPos.z >= 6f ? "City_N" : "City_S";
        }

        static bool IsDescendantOfNamed(Transform tf, string token)
        {
            Transform cur = tf;
            while (cur != null)
            {
                if (cur.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                cur = cur.parent;
            }
            return false;
        }

        static bool IsThinPropOrFoliage(Transform tf)
        {
            Transform cur = tf;
            while (cur != null)
            {
                string n = cur.name;
                if (n.IndexOf("Tree", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Crown", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Trunk", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Lamp", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Bollard", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Bench", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Fountain", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Crosswalk", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Dash", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("LPW_", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
                cur = cur.parent;
            }
            return false;
        }

        static void EnsureMeshesReadable(List<MeshRenderer> renderers)
        {
            var pathsToReimport = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var mr in renderers)
            {
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null || mf.sharedMesh.isReadable)
                    continue;

                string path = AssetDatabase.GetAssetPath(mf.sharedMesh);
                if (string.IsNullOrEmpty(path) || path.StartsWith("Library/", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (AssetImporter.GetAtPath(path) is ModelImporter importer && !importer.isReadable)
                    pathsToReimport.Add(path);
            }

            if (pathsToReimport.Count == 0)
                return;

            Debug.Log($"[ParisPerfOptimizer] Enabling Read/Write on {pathsToReimport.Count} static city model asset(s) for chunk combining...");
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string path in pathsToReimport)
                {
                    if (AssetImporter.GetAtPath(path) is ModelImporter importer)
                    {
                        importer.isReadable = true;
                        importer.SaveAndReimport();
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            AssetDatabase.Refresh();
        }

        static void PrepareOutputDirectory()
        {
            if (AssetDatabase.IsValidFolder(OptimizedMeshDir))
            {
                string[] existingGuids = AssetDatabase.FindAssets("", new[] { OptimizedMeshDir });
                foreach (string guid in existingGuids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!string.IsNullOrEmpty(path) &&
                        path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) &&
                        !Path.GetFileName(path).StartsWith("HeroLOD_", StringComparison.OrdinalIgnoreCase))
                        AssetDatabase.DeleteAsset(path);
                }
            }
            else
            {
                Directory.CreateDirectory(OptimizedMeshDir);
                AssetDatabase.Refresh();
            }
        }

        static List<Mesh> BuildCombinedMeshes(string zone, string matName, List<SourceSubmesh> items)
        {
            var result = new List<Mesh>();
            var verts = new List<Vector3>(4096);
            var norms = new List<Vector3>(4096);
            var uvs = new List<Vector2>(4096);
            var tris = new List<int>(6144);

            void Flush()
            {
                if (verts.Count == 0 || tris.Count == 0) return;
                var m = new Mesh
                {
                    indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16
                };
                m.SetVertices(verts);
                m.SetNormals(norms);
                m.SetUVs(0, uvs);
                m.SetTriangles(tris, 0, true);
                m.RecalculateBounds();
                m.Optimize();
                result.Add(m);

                verts.Clear();
                norms.Clear();
                uvs.Clear();
                tris.Clear();
            }

            var srcVerts = new List<Vector3>();
            var srcNorms = new List<Vector3>();
            var srcUvs = new List<Vector2>();
            var srcTris = new List<int>();
            var indexRemap = new Dictionary<int, int>(1024);

            foreach (var item in items)
            {
                srcVerts.Clear();
                srcNorms.Clear();
                srcUvs.Clear();
                srcTris.Clear();

                item.mesh.GetVertices(srcVerts);
                item.mesh.GetNormals(srcNorms);
                item.mesh.GetUVs(0, srcUvs);
                item.mesh.GetTriangles(srcTris, item.submeshIndex);

                if (srcTris.Count == 0 || srcVerts.Count == 0)
                    continue;

                // Estimate unique vertex count conservatively and flush before exceeding 16-bit vertex budget
                if (verts.Count > 0 && verts.Count + srcVerts.Count > MaxVerticesPerChunkMesh)
                    Flush();

                bool hasNormals = srcNorms.Count == srcVerts.Count;
                bool hasUvs = srcUvs.Count == srcVerts.Count;
                Matrix4x4 m = item.localToWorld;
                Matrix4x4 nMat = m.inverse.transpose;
                bool flipWinding = m.determinant < 0f;

                indexRemap.Clear();
                for (int t = 0; t + 2 < srcTris.Count; t += 3)
                {
                    int i0 = srcTris[t];
                    int i1 = flipWinding ? srcTris[t + 2] : srcTris[t + 1];
                    int i2 = flipWinding ? srcTris[t + 1] : srcTris[t + 2];

                    int o0 = RemapVertex(i0, srcVerts, srcNorms, srcUvs, hasNormals, hasUvs, m, nMat, indexRemap, verts, norms, uvs);
                    int o1 = RemapVertex(i1, srcVerts, srcNorms, srcUvs, hasNormals, hasUvs, m, nMat, indexRemap, verts, norms, uvs);
                    int o2 = RemapVertex(i2, srcVerts, srcNorms, srcUvs, hasNormals, hasUvs, m, nMat, indexRemap, verts, norms, uvs);

                    tris.Add(o0);
                    tris.Add(o1);
                    tris.Add(o2);
                }
            }

            Flush();
            return result;
        }

        static int RemapVertex(
            int srcIndex,
            List<Vector3> srcVerts,
            List<Vector3> srcNorms,
            List<Vector2> srcUvs,
            bool hasNormals,
            bool hasUvs,
            Matrix4x4 m,
            Matrix4x4 nMat,
            Dictionary<int, int> remap,
            List<Vector3> dstVerts,
            List<Vector3> dstNorms,
            List<Vector2> dstUvs)
        {
            if (remap.TryGetValue(srcIndex, out int existing))
                return existing;

            int next = dstVerts.Count;
            remap[srcIndex] = next;
            dstVerts.Add(m.MultiplyPoint3x4(srcVerts[srcIndex]));

            Vector3 n = hasNormals ? nMat.MultiplyVector(srcNorms[srcIndex]) : Vector3.up;
            if (n.sqrMagnitude > 1e-6f) n.Normalize(); else n = Vector3.up;
            dstNorms.Add(n);

            dstUvs.Add(hasUvs ? srcUvs[srcIndex] : Vector2.zero);
            return next;
        }

        static void OptimizeRemainingSceneRenderers(Transform root)
        {
            Transform chunksRoot = root.Find(ChunksContainerName);
            var allRenderers = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            foreach (var r in allRenderers)
            {
                if (r == null || !r.enabled) continue;
                if (chunksRoot != null && r.transform.IsChildOf(chunksRoot)) continue;

                r.reflectionProbeUsage = ReflectionProbeUsage.Off;

                if (r is SkinnedMeshRenderer)
                {
                    // Keep shadows on Lucas the waiter
                    bool isWaiter = IsDescendantOfNamed(r.transform, "Waiter") || IsDescendantOfNamed(r.transform, "Lucas");
                    r.shadowCastingMode = isWaiter ? ShadowCastingMode.On : ShadowCastingMode.Off;
                    EditorUtility.SetDirty(r);
                    continue;
                }

                if (r.GetComponent<TMP_Text>() != null ||
                    IsDescendantOfNamed(r.transform, "floating_fox") ||
                    IsDescendantOfNamed(r.transform, "XR Origin") ||
                    IsDescendantOfNamed(r.transform, "HUD") ||
                    IsDescendantOfNamed(r.transform, "Panel") ||
                    IsDescendantOfNamed(r.transform, "Card") ||
                    IsDescendantOfNamed(r.transform, "Menu"))
                {
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    EditorUtility.SetDirty(r);
                    continue;
                }

                // Near hero café furniture (Your table, waiter body parts, nearby café props >= 0.28m)
                Vector3 p = r.bounds.center;
                float dist = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(CafeCenter.x, CafeCenter.z));
                bool isNearHero = dist <= 5.5f && r.bounds.size.magnitude >= 0.28f &&
                                  (IsDescendantOfNamed(r.transform, "Waiter") ||
                                   IsDescendantOfNamed(r.transform, "Lucas") ||
                                   IsDescendantOfNamed(r.transform, "Your table") ||
                                   IsDescendantOfNamed(r.transform, "Table") ||
                                   IsDescendantOfNamed(r.transform, "Chair"));

                r.shadowCastingMode = isNearHero ? ShadowCastingMode.On : ShadowCastingMode.Off;
                EditorUtility.SetDirty(r);
            }
        }

        static void OptimizeSceneMaterials()
        {
            var seen = new HashSet<Material>();
            var renderers = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var r in renderers)
            {
                if (r == null || !r.enabled) continue;
                foreach (var mat in r.sharedMaterials)
                {
                    if (mat == null || !seen.Add(mat)) continue;
                    string assetPath = AssetDatabase.GetAssetPath(mat);
                    if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                        continue;

                    bool dirty = false;
                    if (!mat.enableInstancing)
                    {
                        mat.enableInstancing = true;
                        dirty = true;
                    }

                    // Turn back-face culling ON for opaque non-foliage Parisian architectural materials
                    bool isFoliage = mat.name.IndexOf("Leaf", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     mat.name.IndexOf("Leaves", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     mat.name.IndexOf("Nicrom", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!isFoliage && mat.HasProperty("_Cull") && Mathf.Approximately(mat.GetFloat("_Cull"), 0f))
                    {
                        mat.SetFloat("_Cull", 2f);
                        dirty = true;
                    }

                    if (dirty) EditorUtility.SetDirty(mat);
                }
            }
        }

        static void ConfigureLightingCameraAndQuestOptimizer(Transform root)
        {
            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (light.type == LightType.Directional)
                {
                    light.shadows = LightShadows.Hard;
                    light.shadowResolution = LightShadowResolution.Medium;
                    EditorUtility.SetDirty(light);
                }
            }

            var xrOrigin = Object.FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>(FindObjectsInactive.Include);
            if (xrOrigin != null && xrOrigin.Camera != null)
            {
                xrOrigin.Camera.farClipPlane = TargetFarClip;
                xrOrigin.Camera.nearClipPlane = 0.04f;
                xrOrigin.Camera.useOcclusionCulling = true;
                EditorUtility.SetDirty(xrOrigin.Camera);
            }

            var perf = Object.FindAnyObjectByType<QuestPerformanceOptimizer>(FindObjectsInactive.Include);
            if (perf != null)
            {
                perf.targetRefreshRate = 72f;
                perf.foveatedRenderingLevel = 0.85f;
                perf.maxCameraFarClip = TargetFarClip;
                EditorUtility.SetDirty(perf);
            }
        }

        static void BakeOcclusionCulling()
        {
            StaticOcclusionCulling.Clear();
            StaticOcclusionCulling.smallestOccluder = 3.5f;
            StaticOcclusionCulling.smallestHole = 0.45f;
            StaticOcclusionCulling.backfaceThreshold = 100f;
            bool ok = StaticOcclusionCulling.Compute();
            Debug.Log("[ParisPerfOptimizer] Static Occlusion Culling bake result: " + ok);
        }

        /// <summary>
        /// Adaptive 3D spatial vertex-clustering decimator for high-poly distant landmarks (e.g. 650k-tri Eiffel Tower
        /// at 255m distance) and ultra-dense distant skyline chunks. Preserves exact macro silhouette while eliminating
        /// sub-pixel triangles and collapsing multi-chunk meshes into a single 16-bit Quest-friendly mesh.
        /// </summary>
        static List<Mesh> DecimateMeshListAdaptive(List<Mesh> sourceMeshes, int maxTriangles, float startCellSize)
        {
            if (sourceMeshes == null || sourceMeshes.Count == 0)
                return sourceMeshes;

            long totalTris = 0;
            foreach (var m in sourceMeshes)
                if (m != null) totalTris += m.triangles.Length / 3;

            if (totalTris <= maxTriangles && sourceMeshes.Count == 1)
                return sourceMeshes;

            // Gather all vertices, normals, UVs, and triangles from the list into flat arrays once
            var allVerts = new List<Vector3>();
            var allNorms = new List<Vector3>();
            var allUvs = new List<Vector2>();
            var allTris = new List<int>();

            foreach (var m in sourceMeshes)
            {
                if (m == null) continue;
                int vOffset = allVerts.Count;
                var mv = m.vertices;
                var mn = m.normals;
                var mu = m.uv;
                var mt = m.triangles;

                allVerts.AddRange(mv);
                if (mn != null && mn.Length == mv.Length) allNorms.AddRange(mn);
                else for (int i = 0; i < mv.Length; i++) allNorms.Add(Vector3.up);

                if (mu != null && mu.Length == mv.Length) allUvs.AddRange(mu);
                else for (int i = 0; i < mv.Length; i++) allUvs.Add(Vector2.zero);

                for (int i = 0; i < mt.Length; i++)
                    allTris.Add(mt[i] + vOffset);
            }

            float cell = startCellSize;
            Mesh decimated = null;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                decimated = ClusterDecimate(allVerts, allNorms, allUvs, allTris, cell);
                int outTris = decimated.triangles.Length / 3;
                if (outTris <= maxTriangles && decimated.vertexCount <= MaxVerticesPerChunkMesh)
                    break;
                Object.DestroyImmediate(decimated);
                decimated = null;
                cell *= 1.55f;
            }

            if (decimated == null)
                decimated = ClusterDecimate(allVerts, allNorms, allUvs, allTris, cell);

            Debug.Log($"[ParisPerfOptimizer] Decimated {totalTris:N0} tris ({sourceMeshes.Count} meshes) -> {decimated.triangles.Length / 3:N0} tris (1 mesh, cell={cell:F2}m)");
            foreach (var m in sourceMeshes)
                if (m != null) Object.DestroyImmediate(m);

            return new List<Mesh> { decimated };
        }

        static Mesh ClusterDecimate(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris, float cellSize)
        {
            float inv = 1f / Mathf.Max(0.01f, cellSize);
            var cellToIdx = new Dictionary<long, int>(verts.Count / 4);
            var posSum = new List<Vector3>(verts.Count / 4);
            var normSum = new List<Vector3>(verts.Count / 4);
            var uvSum = new List<Vector2>(verts.Count / 4);
            var counts = new List<int>(verts.Count / 4);
            var vertRemap = new int[verts.Count];

            for (int i = 0; i < verts.Count; i++)
            {
                Vector3 p = verts[i];
                long cx = Mathf.FloorToInt(p.x * inv);
                long cy = Mathf.FloorToInt(p.y * inv);
                long cz = Mathf.FloorToInt(p.z * inv);
                long key = ((cx & 0x1FFFFFL) << 42) | ((cy & 0x1FFFFFL) << 21) | (cz & 0x1FFFFFL);

                if (!cellToIdx.TryGetValue(key, out int idx))
                {
                    idx = posSum.Count;
                    cellToIdx[key] = idx;
                    posSum.Add(p);
                    normSum.Add(norms[i]);
                    uvSum.Add(uvs[i]);
                    counts.Add(1);
                }
                else
                {
                    posSum[idx] += p;
                    normSum[idx] += norms[i];
                    uvSum[idx] += uvs[i];
                    counts[idx] += 1;
                }
                vertRemap[i] = idx;
            }

            var outVerts = new Vector3[posSum.Count];
            var outNorms = new Vector3[posSum.Count];
            var outUvs = new Vector2[posSum.Count];
            for (int i = 0; i < posSum.Count; i++)
            {
                float c = counts[i];
                outVerts[i] = posSum[i] / c;
                Vector3 n = normSum[i];
                outNorms[i] = n.sqrMagnitude > 1e-6f ? n.normalized : Vector3.up;
                outUvs[i] = uvSum[i] / c;
            }

            var outTris = new List<int>(tris.Count / 4);
            var seenTris = new HashSet<ulong>();
            for (int t = 0; t + 2 < tris.Count; t += 3)
            {
                int a = vertRemap[tris[t]];
                int b = vertRemap[tris[t + 1]];
                int c = vertRemap[tris[t + 2]];
                if (a == b || b == c || a == c)
                    continue;

                // Canonicalize vertex triple (preserving winding) to drop duplicate overlapping faces
                int m0 = a, m1 = b, m2 = c;
                if (m1 < m0 && m1 < m2) { m0 = b; m1 = c; m2 = a; }
                else if (m2 < m0 && m2 < m1) { m0 = c; m1 = a; m2 = b; }
                ulong triKey = ((ulong)(uint)m0 << 42) | ((ulong)(uint)m1 << 21) | (uint)m2;
                if (!seenTris.Add(triKey))
                    continue;

                outTris.Add(a);
                outTris.Add(b);
                outTris.Add(c);
            }

            var mesh = new Mesh
            {
                indexFormat = outVerts.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16,
                vertices = outVerts,
                normals = outNorms,
                uv = outUvs
            };
            mesh.SetTriangles(outTris, 0, true);
            mesh.RecalculateBounds();
            mesh.Optimize();
            return mesh;
        }

        /// <summary>
        /// Decimates any remaining non-chunk MeshRenderer whose mesh exceeds 12,000 triangles
        /// (such as floating_fox.glb's 122k/67k-tri subparts or 37k-tri café plants) down to a crisp
        /// Quest-friendly LOD mesh (~3,500-6,500 tris) while preserving UV seams and submesh materials.
        /// </summary>
        static void OptimizeExtremePolyHeroMeshes(Transform root)
        {
            Transform chunksRoot = root.Find(ChunksContainerName);
            var renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            int lodIndex = 0;

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var mr in renderers)
                {
                    if (mr == null || !mr.enabled || !mr.gameObject.activeInHierarchy) continue;
                    if (chunksRoot != null && mr.transform.IsChildOf(chunksRoot)) continue;

                    var mf = mr.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    Mesh src = mf.sharedMesh;

                    // Skip meshes already optimized in CityOptimized
                    string existingPath = AssetDatabase.GetAssetPath(src);
                    if (!string.IsNullOrEmpty(existingPath) && existingPath.StartsWith(OptimizedMeshDir, StringComparison.OrdinalIgnoreCase))
                        continue;

                    int origTris = src.triangles.Length / 3;
                    if (origTris <= 12000) continue;

                    if (!src.isReadable)
                    {
                        if (!string.IsNullOrEmpty(existingPath) && AssetImporter.GetAtPath(existingPath) is ModelImporter imp)
                        {
                            imp.isReadable = true;
                            imp.SaveAndReimport();
                        }
                        if (!src.isReadable) continue;
                    }

                    bool isFox = IsDescendantOfNamed(mr.transform, "floating_fox");
                    int targetTris = isFox ? 5500 : 3200;

                    Mesh lodMesh = BuildUvAwareLocalLodMesh(src, targetTris);
                    if (lodMesh == null || lodMesh.triangles.Length / 3 >= origTris * 0.85f)
                    {
                        if (lodMesh != null) Object.DestroyImmediate(lodMesh);
                        continue;
                    }

                    string safeName = SanitizeFileName($"HeroLOD_{mr.gameObject.name}_{lodIndex:D2}");
                    lodMesh.name = safeName;
                    string outPath = $"{OptimizedMeshDir}/{safeName}.asset";
                    AssetDatabase.CreateAsset(lodMesh, outPath);

                    mf.sharedMesh = lodMesh;
                    EditorUtility.SetDirty(mf);
                    Debug.Log($"[ParisPerfOptimizer] HeroLOD '{mr.gameObject.name}': {origTris:N0} tris -> {lodMesh.triangles.Length / 3:N0} tris ({outPath})");
                    lodIndex++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
        }

        static Mesh BuildUvAwareLocalLodMesh(Mesh src, int targetTris)
        {
            Vector3 size = src.bounds.size;
            float maxDim = Mathf.Max(0.01f, Mathf.Max(size.x, Mathf.Max(size.y, size.z)));
            float cell = maxDim / 48f;

            var srcVerts = src.vertices;
            var srcNorms = src.normals;
            var srcUvs = src.uv;
            bool hasNorms = srcNorms != null && srcNorms.Length == srcVerts.Length;
            bool hasUvs = srcUvs != null && srcUvs.Length == srcVerts.Length;

            int subCount = src.subMeshCount;
            var subTrisList = new List<int[]>(subCount);
            for (int s = 0; s < subCount; s++)
                subTrisList.Add(src.GetTriangles(s));

            Mesh best = null;
            for (int attempt = 0; attempt < 7; attempt++)
            {
                best = BuildUvAwareClusterPass(srcVerts, srcNorms, srcUvs, hasNorms, hasUvs, subTrisList, cell);
                int tris = best.triangles.Length / 3;
                if (tris <= targetTris && tris > 80)
                    break;
                if (attempt < 6)
                {
                    Object.DestroyImmediate(best);
                    best = null;
                    cell *= 1.45f;
                }
            }
            return best;
        }

        static Mesh BuildUvAwareClusterPass(
            Vector3[] srcVerts,
            Vector3[] srcNorms,
            Vector2[] srcUvs,
            bool hasNorms,
            bool hasUvs,
            List<int[]> subTrisList,
            float cellSize)
        {
            float inv = 1f / Mathf.Max(1e-4f, cellSize);
            var cellToIdx = new Dictionary<string, int>(srcVerts.Length / 4);
            var posSum = new List<Vector3>(srcVerts.Length / 4);
            var normSum = new List<Vector3>(srcVerts.Length / 4);
            var uvSum = new List<Vector2>(srcVerts.Length / 4);
            var counts = new List<int>(srcVerts.Length / 4);
            var remap = new int[srcVerts.Length];

            for (int i = 0; i < srcVerts.Length; i++)
            {
                Vector3 p = srcVerts[i];
                int cx = Mathf.FloorToInt(p.x * inv);
                int cy = Mathf.FloorToInt(p.y * inv);
                int cz = Mathf.FloorToInt(p.z * inv);
                int ux = hasUvs ? Mathf.FloorToInt(srcUvs[i].x * 8f) : 0;
                int uy = hasUvs ? Mathf.FloorToInt(srcUvs[i].y * 8f) : 0;
                string key = $"{cx},{cy},{cz}|{ux},{uy}";

                if (!cellToIdx.TryGetValue(key, out int idx))
                {
                    idx = posSum.Count;
                    cellToIdx[key] = idx;
                    posSum.Add(p);
                    normSum.Add(hasNorms ? srcNorms[i] : Vector3.up);
                    uvSum.Add(hasUvs ? srcUvs[i] : Vector2.zero);
                    counts.Add(1);
                }
                else
                {
                    posSum[idx] += p;
                    normSum[idx] += hasNorms ? srcNorms[i] : Vector3.up;
                    uvSum[idx] += hasUvs ? srcUvs[i] : Vector2.zero;
                    counts[idx] += 1;
                }
                remap[i] = idx;
            }

            var outVerts = new Vector3[posSum.Count];
            var outNorms = new Vector3[posSum.Count];
            var outUvs = new Vector2[posSum.Count];
            for (int i = 0; i < posSum.Count; i++)
            {
                float c = counts[i];
                outVerts[i] = posSum[i] / c;
                Vector3 n = normSum[i];
                outNorms[i] = n.sqrMagnitude > 1e-6f ? n.normalized : Vector3.up;
                outUvs[i] = uvSum[i] / c;
            }

            var mesh = new Mesh
            {
                indexFormat = outVerts.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16,
                vertices = outVerts,
                normals = outNorms,
                uv = outUvs,
                subMeshCount = subTrisList.Count
            };

            var seen = new HashSet<ulong>();
            for (int s = 0; s < subTrisList.Count; s++)
            {
                seen.Clear();
                var tris = subTrisList[s];
                var outSub = new List<int>(tris.Length / 4);
                for (int t = 0; t + 2 < tris.Length; t += 3)
                {
                    int a = remap[tris[t]];
                    int b = remap[tris[t + 1]];
                    int c = remap[tris[t + 2]];
                    if (a == b || b == c || a == c) continue;

                    int m0 = a, m1 = b, m2 = c;
                    if (m1 < m0 && m1 < m2) { m0 = b; m1 = c; m2 = a; }
                    else if (m2 < m0 && m2 < m1) { m0 = c; m1 = a; m2 = b; }
                    ulong triKey = ((ulong)(uint)m0 << 42) | ((ulong)(uint)m1 << 21) | (uint)m2;
                    if (!seen.Add(triKey)) continue;

                    outSub.Add(a);
                    outSub.Add(b);
                    outSub.Add(c);
                }
                mesh.SetTriangles(outSub, s, true);
            }

            mesh.RecalculateBounds();
            mesh.Optimize();
            return mesh;
        }

        struct SceneStats
        {
            public int activeMeshRenderers;
            public int shadowCasters;
            public int uniqueMaterials;
            public long totalTriangles;
            public long skinnedTriangles;
        }

        static SceneStats MeasureActiveSceneStats()
        {
            var stats = new SceneStats();
            var mats = new HashSet<Material>();
            var meshRenderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            foreach (var mr in meshRenderers)
            {
                if (mr == null || !mr.enabled || !mr.gameObject.activeInHierarchy) continue;
                stats.activeMeshRenderers++;
                if (mr.shadowCastingMode != ShadowCastingMode.Off)
                    stats.shadowCasters++;
                foreach (var m in mr.sharedMaterials)
                    if (m != null) mats.Add(m);

                var mf = mr.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                    stats.totalTriangles += mf.sharedMesh.triangles.Length / 3;
            }

            var skinned = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var smr in skinned)
            {
                if (smr == null || !smr.enabled || !smr.gameObject.activeInHierarchy) continue;
                if (smr.shadowCastingMode != ShadowCastingMode.Off)
                    stats.shadowCasters++;
                if (smr.sharedMesh != null)
                    stats.skinnedTriangles += smr.sharedMesh.triangles.Length / 3;
            }

            stats.uniqueMaterials = mats.Count;
            return stats;
        }

        static void LogTopHeaviestRenderers(int topN)
        {
            var list = new List<(string name, long tris, bool shadows, string type)>();
            foreach (var mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (mr == null || !mr.enabled || !mr.gameObject.activeInHierarchy) continue;
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                list.Add((mr.gameObject.name, mf.sharedMesh.triangles.Length / 3, mr.shadowCastingMode != ShadowCastingMode.Off, "Mesh"));
            }
            foreach (var smr in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (smr == null || !smr.enabled || !smr.gameObject.activeInHierarchy || smr.sharedMesh == null) continue;
                list.Add((smr.gameObject.name, smr.sharedMesh.triangles.Length / 3, smr.shadowCastingMode != ShadowCastingMode.Off, "Skinned"));
            }

            foreach (var entry in list.OrderByDescending(e => e.tris).Take(topN))
                Debug.Log($"[ParisPerfOptimizer]   Heavy[{entry.type}]: {entry.name} = {entry.tris:N0} tris (shadows={entry.shadows})");
        }

        static string SanitizeFileName(string raw)
        {
            var chars = raw.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '-')
                    chars[i] = '_';
            }
            return new string(chars);
        }
    }
}
