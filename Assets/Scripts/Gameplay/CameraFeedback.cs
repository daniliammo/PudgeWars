using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// What the local player feels through the camera (<see cref="TopDownCamera"/>, which adds this if the scene has
    /// none): a shake when the own hero is caught by a hook, when the own hook catches someone and on death; on death
    /// red floods in from the screen's sides and the picture loses most of its colour, until the hero is back, which
    /// sounds the revive clip (Resources/Audio/revive) — only here, for the own hero: every player hears their own.
    /// The red is a screen overlay; the colour goes with URP's Color Adjustments, and the camera's post-processing is
    /// on only while that runs (it costs a full-screen pass on phones). Works on a network client too: the state is
    /// read from the hero, and the own hook's catches come from the server (<see cref="HookAbility.ApplyRemoteCatch"/>).
    /// </summary>
    public class CameraFeedback : MonoBehaviour
    {
        [Header("Shake")]
        [Tooltip("Jolt when the own hero is caught by a hook (0..1).")] public float caughtShake = 0.7f;
        [Tooltip("Jolt when the own hook catches an enemy.")] public float hitShake = 0.6f;
        [Tooltip("Jolt when the own hook catches an ally (a pull or a rescue).")] public float allyHitShake = 0.3f;
        [Tooltip("Jolt when the own hero dies.")] public float deathShake = 0.8f;

        [Header("Death")]
        public Color bloodColor = new(0.7f, 0.02f, 0.02f);
        [Tooltip("Red at its strongest, right after death, then settling to bloodHold.")] [Range(0f, 1f)] public float bloodPeak = 0.9f;
        [Range(0f, 1f)] public float bloodHold = 0.55f;
        [Tooltip("Seconds the red takes to flood in from the sides.")] public float bloodIn = 0.3f;
        [Tooltip("Seconds it takes to settle from the peak.")] public float bloodSettle = 1.2f;
        [Tooltip("Colour saturation while dead: -100 is black and white.")] [Range(-100f, 0f)] public float deadSaturation = -75f;
        [Tooltip("Seconds the colour takes to drain.")] public float greyIn = 0.8f;
        [Tooltip("Seconds everything takes to come back after the respawn.")] public float recover = 0.5f;
        [Tooltip("Played when the own hero comes back; Resources/Audio/revive if empty.")] public AudioClip reviveSound;
        [Range(0f, 1f)] public float reviveVolume = 0.8f;

        TopDownCamera follow;
        Camera cam;
        Hookable hero;
        HookAbility heroHook;
        bool wasHooked, wasAlive = true;
        float deadFor = -1f;  // seconds since death, -1 while alive
        float fade;           // 1 = the death look in full, eased back to 0 after the respawn
        Image blood;
        RectTransform bloodRect;
        Volume grey;
        AudioSource audioSource;

        /// <summary>The red overlay's opacity now (0 = none).</summary>
        public float BloodAlpha => blood != null && blood.enabled ? blood.color.a : 0f;
        /// <summary>How much of the colour is gone now, 0..1 (the Volume's weight).</summary>
        public float GreyWeight => grey != null ? grey.weight : 0f;
        /// <summary>The camera's post-processing is on (only while the picture is greyed).</summary>
        public bool PostProcessing => cam != null && cam.GetUniversalAdditionalCameraData().renderPostProcessing;

        void Awake()
        {
            follow = GetComponent<TopDownCamera>();
            cam = GetComponent<Camera>();
            // the camera adds this at runtime, so the clip cannot wait for the scene to assign it
            if (reviveSound == null) reviveSound = Resources.Load<AudioClip>("Audio/revive");
        }

        void OnEnable()
        {
            LocalPlayer.Changed += Bind;
            Bind();
        }

        void OnDisable()
        {
            LocalPlayer.Changed -= Bind;
            Bind(null);
        }

        void OnDestroy()
        {
            if (grey != null) Destroy(grey.profile);
        }

        void Bind() => Bind(LocalPlayer.Hero);

        void Bind(Hookable next)
        {
            if (heroHook != null) heroHook.TargetCaught -= OnOwnHookCaught;
            hero = next;
            heroHook = hero != null ? hero.GetComponent<HookAbility>() : null;
            if (heroHook != null) heroHook.TargetCaught += OnOwnHookCaught;
            wasHooked = hero != null && hero.IsHooked;
            wasAlive = hero == null || hero.IsAlive;
            deadFor = wasAlive ? -1f : 0f;
        }

        void OnOwnHookCaught(Hookable target)
        {
            if (follow == null || target == null) return;
            follow.Shake(hero != null && target.team == hero.team ? allyHitShake : hitShake);
        }

        void Update()
        {
            if (hero != null)
            {
                if (hero.IsHooked && !wasHooked && follow != null) follow.Shake(caughtShake);
                wasHooked = hero.IsHooked;
                if (wasAlive && !hero.IsAlive) Died();
                else if (!wasAlive && hero.IsAlive) Revived();
                wasAlive = hero.IsAlive;
            }
            else deadFor = -1f;

            bool dead = deadFor >= 0f;
            if (dead) deadFor += Time.deltaTime;
            fade = dead ? 1f : Mathf.MoveTowards(fade, 0f, Time.deltaTime / Mathf.Max(recover, 0.01f));
            if (!dead && fade <= 0f)
            {
                if (blood != null) blood.enabled = false;
                SetGrey(0f);
                return;
            }
            float t = dead ? deadFor : float.MaxValue; // after the respawn: the settled look, fading out
            ShowBlood(t, fade);
            SetGrey(Mathf.SmoothStep(0f, 1f, t / Mathf.Max(greyIn, 0.01f)) * fade);
        }

        void Died()
        {
            deadFor = 0f;
            if (follow != null) follow.Shake(deathShake);
        }

        void Revived()
        {
            deadFor = -1f;
            if (reviveSound == null) return;
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 0f; // the own hero's: heard the same wherever the camera is
            }
            audioSource.PlayOneShot(reviveSound, reviveVolume);
        }

        /// <summary>
        /// The red comes in from the sides (the overlay shrinks from beyond the screen onto it) and flares, then
        /// settles to a steady glow.
        /// </summary>
        void ShowBlood(float t, float fade)
        {
            if (blood == null) BuildBlood();
            float flood = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / Mathf.Max(bloodIn, 0.01f)), 3f); // ease-out
            float settle = Mathf.SmoothStep(0f, 1f, (t - bloodIn) / Mathf.Max(bloodSettle, 0.01f));
            var c = bloodColor;
            c.a = Mathf.Lerp(bloodPeak, bloodHold, settle) * flood * fade;
            blood.color = c;
            blood.enabled = true;
            bloodRect.localScale = Vector3.one * Mathf.Lerp(1.8f, 1f, flood);
        }

        void SetGrey(float weight)
        {
            if (weight <= 0f && grey == null) return;
            if (grey == null) BuildGrey();
            grey.weight = weight;
            // post-processing only while the picture is greyed: it is a full-screen pass
            if (cam != null) cam.GetUniversalAdditionalCameraData().renderPostProcessing = weight > 0f;
        }

        void BuildGrey()
        {
            var go = new GameObject("DeathGrey");
            go.transform.SetParent(transform, false);
            grey = go.AddComponent<Volume>();
            grey.isGlobal = true;
            grey.priority = 100f;
            grey.weight = 0f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var colour = profile.Add<ColorAdjustments>(true);
            colour.saturation.Override(deadSaturation);
            colour.contrast.Override(10f);
            colour.postExposure.Override(-0.15f);
            grey.profile = profile;
        }

        /// <summary>A full-screen overlay under the HUD: clear in the middle, red toward the edges (more at the sides).</summary>
        void BuildBlood()
        {
            var canvasGo = new GameObject("DeathOverlay", typeof(Canvas));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -100; // drawn before (under) the HUD
            var go = new GameObject("Blood", typeof(RectTransform));
            bloodRect = (RectTransform)go.transform;
            bloodRect.SetParent(canvasGo.transform, false);
            bloodRect.anchorMin = Vector2.zero;
            bloodRect.anchorMax = Vector2.one;
            bloodRect.offsetMin = bloodRect.offsetMax = Vector2.zero;
            blood = go.AddComponent<Image>();
            blood.raycastTarget = false;
            blood.sprite = EdgeSprite();
        }

        static Sprite EdgeSprite()
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "DeathEdges" };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                // an ellipse squeezed vertically: the screen's sides redden first and most
                float d = Mathf.Sqrt(u * u * 1.15f + v * v * 0.7f);
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1.15f, d));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }
    }
}
