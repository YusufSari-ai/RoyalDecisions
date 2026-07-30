using RoyalDecisions.Application;
using RoyalDecisions.Data;
using RoyalDecisions.Domain;
using RoyalDecisions.Presentation;

namespace RoyalDecisions.Composition
{
    /// <summary>
    /// Drives the Phase 5 views and the Phase 6 swipe controller on the session's behalf.
    /// </summary>
    /// <remarks>
    /// Pure translation: every method forwards to a view and calculates nothing. Each reference is
    /// optional at runtime — a scene missing its HUD should render an incomplete screen, not throw
    /// mid-decision.
    /// </remarks>
    public sealed class UnityGamePresenter : IGamePresenter
    {
        private readonly CardView cardView;
        private readonly HUDView hudView;
        private readonly GameOverView gameOverView;
        private readonly CardSwipeController swipeController;

        public UnityGamePresenter(
            CardView cardView,
            HUDView hudView,
            GameOverView gameOverView,
            CardSwipeController swipeController)
        {
            this.cardView = cardView;
            this.hudView = hudView;
            this.gameOverView = gameOverView;
            this.swipeController = swipeController;
        }

        public void ShowCard(CardDefinition card)
        {
            if (cardView != null)
            {
                cardView.Show(card);
            }
        }

        public void ClearCard()
        {
            if (cardView != null)
            {
                cardView.Clear();
            }
        }

        public void PrepareForInput()
        {
            if (swipeController != null)
            {
                swipeController.ResetForNextCard();
            }
        }

        public void CancelInput()
        {
            if (swipeController != null)
            {
                swipeController.CancelInteraction();
            }
        }

        public void BindStats(StatSystem statSystem)
        {
            if (hudView != null)
            {
                hudView.Bind(statSystem);
            }
        }

        public void UnbindStats()
        {
            if (hudView != null)
            {
                hudView.Unbind();
            }
        }

        public void RefreshStats(StatValues values)
        {
            if (hudView != null)
            {
                hudView.Render(values, true);
            }
        }

        public void ShowGameOver(GameOverResult result)
        {
            if (gameOverView != null)
            {
                gameOverView.Show(result);
            }
        }

        public void HideGameOver()
        {
            if (gameOverView != null)
            {
                gameOverView.Hide();
            }
        }
    }
}
