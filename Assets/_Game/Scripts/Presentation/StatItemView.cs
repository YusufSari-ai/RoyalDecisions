using RoyalDecisions.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RoyalDecisions.Presentation
{
    /// <summary>
    /// A single statistic display: icon, optional label, and a fill bar.
    /// </summary>
    /// <remarks>
    /// Reusable by design — the HUD holds four of these, each carrying its own
    /// <see cref="StatType"/>, so adding or restyling a statistic is prefab work rather than code.
    ///
    /// Nothing here can write to a run: it receives a normalised float and shows it.
    /// </remarks>
    public sealed class StatItemView : MonoBehaviour
    {
        [SerializeField] private StatType stat = StatType.Authority;

        [Tooltip("Image with Type = Filled. Its fillAmount is what moves.")]
        [SerializeField] private Image fillImage;

        [SerializeField] private Image iconImage;
        [SerializeField] private Sprite iconSprite;
        [SerializeField] private GraphicFallbackSettings iconFallback = new GraphicFallbackSettings();

        [SerializeField] private TMP_Text label;

        [Tooltip("Fill units per second when animating. Zero snaps instantly.")]
        [Min(0f)]
        [SerializeField] private float animationSpeed = 2.5f;

        private float targetFill;
        private bool animating;

        public StatType Stat => stat;

        /// <summary>What is actually on screen right now, not the requested target.</summary>
        public float DisplayedFill => fillImage != null ? fillImage.fillAmount : 0f;

        public float TargetFill => targetFill;

        public bool IsAnimating => animating;

        public GraphicFallbackMode IconMode { get; private set; } = GraphicFallbackMode.HideGraphic;

        private void Awake()
        {
            RefreshIcon();
        }

        /// <summary>Sets the bar immediately, cancelling any animation in flight.</summary>
        public void SetFill(float normalized)
        {
            targetFill = Mathf.Clamp01(normalized);
            animating = false;
            ApplyFill(targetFill);
        }

        /// <summary>
        /// Eases the bar towards a value. Purely presentational — the domain value changed the
        /// instant the choice resolved, whatever the bar is doing.
        /// </summary>
        public void SetFillAnimated(float normalized)
        {
            targetFill = Mathf.Clamp01(normalized);

            if (animationSpeed <= 0f)
            {
                SetFill(targetFill);
                return;
            }

            animating = !Mathf.Approximately(DisplayedFill, targetFill);
        }

        public void SetLabel(string text)
        {
            if (label != null)
            {
                label.text = text ?? string.Empty;
            }
        }

        public void RefreshIcon()
        {
            IconMode = GraphicFallback.Apply(iconImage, iconSprite, iconFallback);
        }

        private void Update()
        {
            // Returns on the first line for every frame the bar is at rest, which is nearly all of
            // them. No allocation on either path.
            if (!animating)
            {
                return;
            }

            float next = Mathf.MoveTowards(DisplayedFill, targetFill, animationSpeed * Time.deltaTime);
            ApplyFill(next);

            if (Mathf.Approximately(next, targetFill))
            {
                ApplyFill(targetFill);
                animating = false;
            }
        }

        private void ApplyFill(float value)
        {
            if (fillImage != null)
            {
                fillImage.fillAmount = value;
            }
        }

#if UNITY_EDITOR
        /// <summary>Editor-only wiring hook shared by prefab setup and tests.</summary>
        public void SetAuthoringReferences(
            StatType statType,
            Image fill,
            Image icon = null,
            TMP_Text statLabel = null,
            Sprite sprite = null,
            GraphicFallbackSettings fallback = null,
            float speed = 2.5f)
        {
            stat = statType;
            fillImage = fill;
            iconImage = icon;
            label = statLabel;
            iconSprite = sprite;
            animationSpeed = speed;

            if (fallback != null)
            {
                iconFallback = fallback;
            }
        }
#endif
    }
}
