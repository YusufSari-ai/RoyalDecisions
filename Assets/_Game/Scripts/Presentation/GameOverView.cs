using System;
using RoyalDecisions.Domain;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RoyalDecisions.Presentation
{
    /// <summary>
    /// Renders the ending screen and reports that the player asked to restart.
    /// </summary>
    /// <remarks>
    /// It never restarts anything. <see cref="RestartRequested"/> is a presentation event; Phase 7
    /// subscribes and decides what a restart means.
    ///
    /// The restart button is wired through the Inspector to <see cref="HandleRestartButton"/> rather
    /// than in <c>Awake</c>, so the view has no lifecycle requirements and a test can raise the same
    /// path the button does.
    /// </remarks>
    public sealed class GameOverView : MonoBehaviour
    {
        [Header("Layout")]
        [Tooltip("Toggled by Show and Hide. Defaults to this object.")]
        [SerializeField] private GameObject panelRoot;

        [Header("Content")]
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text bodyText;
        [SerializeField] private Image illustrationImage;
        [SerializeField] private GraphicFallbackSettings illustrationFallback = new GraphicFallbackSettings();

        [Header("Missing-ending fallback")]
        [Tooltip("Shown when a boundary is reached that no ending asset covers.")]
        [SerializeField] private string genericTitle = "The Reign Ends";

        [TextArea(2, 5)]
        [SerializeField] private string genericBody =
            "Your rule is over. The chronicles do not record how.";

        [Header("Optional")]
        [SerializeField] private Button restartButton;

        /// <summary>Raised when the player presses restart. The view takes no other action.</summary>
        public event Action RestartRequested;

        public bool IsVisible { get; private set; }

        public bool IsShowingGenericFallback { get; private set; }

        public GraphicFallbackMode IllustrationMode { get; private set; } = GraphicFallbackMode.HideGraphic;

        public void Show(GameOverResult result)
        {
            Show(GameOverPresenter.Create(result, genericTitle, genericBody));
        }

        public void Show(GameOverPresentation presentation)
        {
            if (!presentation.HasEnding)
            {
                Hide();
                return;
            }

            SetText(titleText, presentation.Title);
            SetText(bodyText, presentation.BodyText);

            IllustrationMode = GraphicFallback.Apply(
                illustrationImage, presentation.Illustration, illustrationFallback);

            IsShowingGenericFallback = presentation.IsGenericFallback;
            SetVisible(true);
        }

        public void Hide()
        {
            SetVisible(false);
        }

        /// <summary>Wire the restart Button's OnClick to this method in the Inspector.</summary>
        public void HandleRestartButton()
        {
            RestartRequested?.Invoke();
        }

        public void SetRestartInteractable(bool interactable)
        {
            if (restartButton != null)
            {
                restartButton.interactable = interactable;
            }
        }

        private void SetVisible(bool visible)
        {
            IsVisible = visible;

            GameObject root = panelRoot != null ? panelRoot : gameObject;
            root.SetActive(visible);
        }

        private static void SetText(TMP_Text target, string value)
        {
            if (target != null)
            {
                target.text = value ?? string.Empty;
            }
        }

#if UNITY_EDITOR
        /// <summary>Editor-only wiring hook shared by prefab setup and tests.</summary>
        public void SetAuthoringReferences(
            TMP_Text title,
            TMP_Text body,
            Image illustration,
            GameObject root = null,
            GraphicFallbackSettings fallback = null,
            string fallbackTitle = null,
            string fallbackBody = null)
        {
            titleText = title;
            bodyText = body;
            illustrationImage = illustration;
            panelRoot = root;

            if (fallback != null)
            {
                illustrationFallback = fallback;
            }

            if (fallbackTitle != null)
            {
                genericTitle = fallbackTitle;
            }

            if (fallbackBody != null)
            {
                genericBody = fallbackBody;
            }
        }
#endif
    }
}
