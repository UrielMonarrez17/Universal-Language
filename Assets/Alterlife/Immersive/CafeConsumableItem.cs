using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Alterlife.Immersive
{
    public enum CafeItemKind
    {
        Drink,
        MainDish,
        Dessert
    }

    /// <summary>
    /// Makes a served café drink, dish, or dessert physically grabbable and consumable in VR:
    /// - Bring near the player's mouth (Camera.main) while holding to take a sip or bite.
    /// - Drinks shrink their 'Liquid' child progressively; dishes hide 'Bite_1..Bite_N' segments.
    /// - Plays lightweight procedurally synthesized sip / bite / clink SFX with zero external audio files.
    /// - Gently settles back onto its table slot when released so cups and plates never fall into the void.
    /// </summary>
    public sealed class CafeConsumableItem : MonoBehaviour
    {
        [Header("Consumable Configuration")]
        public string itemDisplayName = "Café";
        public CafeItemKind kind = CafeItemKind.Drink;
        public bool isHotItem;
        public int totalSteps = 3;
        public float mouthTriggerRadius = 0.27f;
        public float consumeCooldown = 1.05f;

        [Header("Optional Visual References (Auto-Resolved if Blank)")]
        public Transform liquidTransform;
        public List<GameObject> biteSegments = new List<GameObject>();
        public ParticleSystem steamParticles;

        public int RemainingSteps { get; private set; }
        public bool IsConsumed => RemainingSteps <= 0;
        public bool IsHeld { get; private set; }
        public bool IsPlacedOnTable { get; private set; }

        public event Action<CafeConsumableItem> ConsumedStep;
        public event Action<CafeConsumableItem> FullyConsumed;

        Vector3 tableSlotWorldPos;
        Quaternion tableSlotWorldRot = Quaternion.identity;
        Vector3 initialLiquidScale = Vector3.one;
        bool hasInitialLiquidScale;
        float lastConsumeTime = -10f;
        bool returningToTable;

        XRGrabInteractable grabInteractable;
        Rigidbody rb;
        AudioSource sfxSource;

        static AudioClip cachedSipClip;
        static AudioClip cachedBiteClip;
        static AudioClip cachedClinkClip;
        static Material cachedSteamMaterial;

        void Awake()
        {
            ResolveVisualParts();
            SetupGrabAndPhysics();
            RemainingSteps = Mathf.Max(1, totalSteps);
        }

        void OnEnable()
        {
            if (grabInteractable != null)
            {
                grabInteractable.selectEntered.AddListener(OnSelectEntered);
                grabInteractable.selectExited.AddListener(OnSelectExited);
            }
        }

        void OnDisable()
        {
            if (grabInteractable != null)
            {
                grabInteractable.selectEntered.RemoveListener(OnSelectEntered);
                grabInteractable.selectExited.RemoveListener(OnSelectExited);
            }
        }

        public void ConfigureForTableSlot(Vector3 worldPos, Quaternion worldRot)
        {
            tableSlotWorldPos = worldPos;
            tableSlotWorldRot = worldRot;
            IsPlacedOnTable = true;
            returningToTable = false;
            transform.SetPositionAndRotation(worldPos, worldRot);
            if (grabInteractable != null)
                grabInteractable.enabled = true;
            if (isHotItem && steamParticles == null)
                CreateLightweightSteam();
            PlayClinkSfx();
        }

        public void SetHeldByWaiterTray()
        {
            IsPlacedOnTable = false;
            returningToTable = false;
            if (grabInteractable != null)
                grabInteractable.enabled = false;
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
            }
            if (isHotItem && steamParticles == null)
                CreateLightweightSteam();
        }

        void ResolveVisualParts()
        {
            if (liquidTransform == null)
            {
                foreach (Transform t in GetComponentsInChildren<Transform>(true))
                {
                    if (t != transform && t.name.Equals("Liquid", StringComparison.OrdinalIgnoreCase))
                    {
                        liquidTransform = t;
                        break;
                    }
                }
            }

            if (liquidTransform != null && !hasInitialLiquidScale)
            {
                initialLiquidScale = liquidTransform.localScale;
                hasInitialLiquidScale = true;
            }

            if (biteSegments.Count == 0)
            {
                var found = new List<Transform>();
                foreach (Transform t in GetComponentsInChildren<Transform>(true))
                {
                    if (t != transform && t.name.StartsWith("Bite_", StringComparison.OrdinalIgnoreCase))
                        found.Add(t);
                }
                found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                for (int i = 0; i < found.Count; i++)
                    biteSegments.Add(found[i].gameObject);
            }

            if (biteSegments.Count > 0)
                totalSteps = biteSegments.Count;
            else
                totalSteps = Mathf.Max(3, totalSteps);
        }

        void SetupGrabAndPhysics()
        {
            rb = GetComponent<Rigidbody>();
            if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.isKinematic = true;
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;

            if (GetComponentInChildren<Collider>() == null)
            {
                var box = gameObject.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 0.055f, 0f);
                box.size = kind == CafeItemKind.Drink
                    ? new Vector3(0.11f, 0.12f, 0.11f)
                    : new Vector3(0.22f, 0.08f, 0.22f);
            }

            grabInteractable = GetComponent<XRGrabInteractable>();
            if (grabInteractable == null)
                grabInteractable = gameObject.AddComponent<XRGrabInteractable>();

            grabInteractable.movementType = XRBaseInteractable.MovementType.VelocityTracking;
            grabInteractable.throwOnDetach = false;
            grabInteractable.trackPosition = true;
            grabInteractable.trackRotation = true;
            grabInteractable.useDynamicAttach = true;

            sfxSource = GetComponent<AudioSource>();
            if (sfxSource == null)
                sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
            sfxSource.spatialBlend = 1f;
            sfxSource.minDistance = 0.25f;
            sfxSource.maxDistance = 4f;
            sfxSource.volume = 0.55f;
        }

        void CreateLightweightSteam()
        {
            var steamGo = new GameObject("SteamFX");
            steamGo.transform.SetParent(transform, false);
            steamGo.transform.localPosition = new Vector3(0f, kind == CafeItemKind.Drink ? 0.085f : 0.095f, 0f);

            steamParticles = steamGo.AddComponent<ParticleSystem>();
            var main = steamParticles.main;
            main.maxParticles = 6;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.0f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.035f, 0.06f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.045f);
            main.startColor = new Color(0.95f, 0.96f, 0.98f, 0.22f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = steamParticles.emission;
            emission.rateOverTime = 2.6f;

            var shape = steamParticles.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.024f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var colOverLife = steamParticles.colorOverLifetime;
            colOverLife.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.24f, 0.25f), new GradientAlphaKey(0f, 1f) });
            colOverLife.color = grad;

            var renderer = steamGo.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = GetSoftSteamMaterial();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        static Material GetSoftSteamMaterial()
        {
            if (cachedSteamMaterial != null) return cachedSteamMaterial;
            const int sz = 32;
            var tex = new Texture2D(sz, sz, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            float half = (sz - 1) * 0.5f;
            for (int y = 0; y < sz; y++)
            {
                for (int x = 0; x < sz; x++)
                {
                    float dx = (x - half) / half;
                    float dy = (y - half) / half;
                    float r2 = dx * dx + dy * dy;
                    float a = r2 >= 1f ? 0f : Mathf.Pow(1f - r2, 2.2f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply(false, true);

            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            cachedSteamMaterial = new Material(sh) { mainTexture = tex };
            cachedSteamMaterial.SetColor("_Color", new Color(1f, 1f, 1f, 0.35f));
            return cachedSteamMaterial;
        }

        void OnSelectEntered(SelectEnterEventArgs _)
        {
            IsHeld = true;
            returningToTable = false;
        }

        void OnSelectExited(SelectExitEventArgs _)
        {
            IsHeld = false;
            if (IsPlacedOnTable)
            {
                if (rb != null)
                {
                    rb.isKinematic = true;
                    rb.useGravity = false;
                }
                returningToTable = true;
            }
        }

        void Update()
        {
            if (IsHeld && !IsConsumed)
            {
                Camera cam = Camera.main;
                if (cam != null && Time.time - lastConsumeTime >= consumeCooldown)
                {
                    Vector3 mouthPos = cam.transform.position - cam.transform.up * 0.10f + cam.transform.forward * 0.07f;
                    if (Vector3.Distance(transform.position, mouthPos) <= mouthTriggerRadius)
                        TryConsumeOneStep();
                }
                return;
            }

            if (returningToTable && IsPlacedOnTable)
            {
                float t = 1f - Mathf.Exp(-9f * Time.deltaTime);
                transform.position = Vector3.Lerp(transform.position, tableSlotWorldPos, t);
                transform.rotation = Quaternion.Slerp(transform.rotation, tableSlotWorldRot, t);
                if (Vector3.Distance(transform.position, tableSlotWorldPos) < 0.004f)
                {
                    transform.SetPositionAndRotation(tableSlotWorldPos, tableSlotWorldRot);
                    returningToTable = false;
                    PlayClinkSfx();
                }
            }
        }

        /// <summary>
        /// Consumes one sip or bite step, updating visuals and playing subtle audio feedback.
        /// </summary>
        public bool TryConsumeOneStep()
        {
            if (IsConsumed)
                return false;

            lastConsumeTime = Time.time;
            RemainingSteps = Mathf.Max(0, RemainingSteps - 1);

            if (biteSegments.Count > 0)
            {
                int eatenIndex = biteSegments.Count - 1 - RemainingSteps;
                if (eatenIndex >= 0 && eatenIndex < biteSegments.Count && biteSegments[eatenIndex] != null)
                    biteSegments[eatenIndex].SetActive(false);
            }
            else if (liquidTransform != null)
            {
                if (RemainingSteps <= 0)
                {
                    liquidTransform.gameObject.SetActive(false);
                }
                else
                {
                    float frac = (float)RemainingSteps / Mathf.Max(1, totalSteps);
                    float hScale = Mathf.Lerp(0.18f, 1f, frac);
                    if (Mathf.Abs(Vector3.Dot(liquidTransform.forward, transform.up)) > 0.7f)
                        liquidTransform.localScale = new Vector3(initialLiquidScale.x, initialLiquidScale.y, initialLiquidScale.z * hScale);
                    else
                        liquidTransform.localScale = new Vector3(initialLiquidScale.x, initialLiquidScale.y * hScale, initialLiquidScale.z);
                }
            }

            PlayConsumeSfx();
            ConsumedStep?.Invoke(this);

            if (IsConsumed)
            {
                if (steamParticles != null)
                    steamParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                FullyConsumed?.Invoke(this);
            }

            return true;
        }

        void PlayConsumeSfx()
        {
            if (sfxSource == null || Application.isBatchMode) return;
            AudioClip clip = kind == CafeItemKind.Drink ? GetSipClip() : GetBiteClip();
            if (clip != null) sfxSource.PlayOneShot(clip, 0.55f);
        }

        void PlayClinkSfx()
        {
            if (sfxSource == null || Application.isBatchMode) return;
            AudioClip clip = GetClinkClip();
            if (clip != null) sfxSource.PlayOneShot(clip, 0.35f);
        }

        static AudioClip GetSipClip()
        {
            if (cachedSipClip != null) return cachedSipClip;
            const int sr = 16000;
            int len = sr / 3; // 0.33s gentle sip
            float[] data = new float[len];
            var rng = new System.Random(101);
            float lp = 0f;
            for (int i = 0; i < len; i++)
            {
                float t = (float)i / len;
                float env = Mathf.Sin(t * Mathf.PI) * Mathf.Exp(-t * 1.4f);
                float n = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp = lp * 0.78f + n * 0.22f;
                float tone = Mathf.Sin(2f * Mathf.PI * (340f + 220f * t) * (i / (float)sr)) * 0.16f;
                data[i] = (lp * 0.5f + tone) * env * 0.55f;
            }
            cachedSipClip = AudioClip.Create("CafeSipSfx", len, 1, sr, false);
            cachedSipClip.SetData(data, 0);
            return cachedSipClip;
        }

        static AudioClip GetBiteClip()
        {
            if (cachedBiteClip != null) return cachedBiteClip;
            const int sr = 16000;
            int len = sr / 4; // 0.25s crisp pastry/food bite
            float[] data = new float[len];
            var rng = new System.Random(202);
            float prev = 0f;
            for (int i = 0; i < len; i++)
            {
                float t = (float)i / len;
                float burst = Mathf.Exp(-Mathf.Abs(t - 0.22f) * 18f) + 0.7f * Mathf.Exp(-Mathf.Abs(t - 0.52f) * 20f);
                float n = (float)(rng.NextDouble() * 2.0 - 1.0);
                float hp = n - prev * 0.45f;
                prev = n;
                data[i] = hp * burst * 0.42f;
            }
            cachedBiteClip = AudioClip.Create("CafeBiteSfx", len, 1, sr, false);
            cachedBiteClip.SetData(data, 0);
            return cachedBiteClip;
        }

        static AudioClip GetClinkClip()
        {
            if (cachedClinkClip != null) return cachedClinkClip;
            const int sr = 16000;
            int len = sr / 5; // 0.20s soft porcelain clink
            float[] data = new float[len];
            for (int i = 0; i < len; i++)
            {
                float sec = i / (float)sr;
                float env = Mathf.Exp(-sec * 26f);
                float s = Mathf.Sin(2f * Mathf.PI * 1950f * sec) * 0.45f +
                          Mathf.Sin(2f * Mathf.PI * 3120f * sec) * 0.30f +
                          Mathf.Sin(2f * Mathf.PI * 4680f * sec) * 0.15f;
                data[i] = s * env * 0.35f;
            }
            cachedClinkClip = AudioClip.Create("CafeClinkSfx", len, 1, sr, false);
            cachedClinkClip.SetData(data, 0);
            return cachedClinkClip;
        }
    }
}
