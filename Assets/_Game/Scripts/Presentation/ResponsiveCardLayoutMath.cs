using UnityEngine;

namespace RoyalDecisions.Presentation
{
    /// <summary>Pure responsive sizing for the active and queued card surfaces.</summary>
    public static class ResponsiveCardLayoutMath
    {
        public static Vector2 Calculate(
            Vector2 availableSize,
            float preferredWidthRatio,
            float widthToHeightRatio,
            float maximumHeightRatio)
        {
            float width = Mathf.Max(0f, availableSize.x) * Mathf.Clamp(preferredWidthRatio, 0.1f, 1f);
            float aspect = Mathf.Max(0.01f, widthToHeightRatio);
            float maximumHeight = Mathf.Max(0f, availableSize.y) * Mathf.Clamp01(maximumHeightRatio);
            float height = width / aspect;

            if (height > maximumHeight)
            {
                height = maximumHeight;
                width = height * aspect;
            }

            return new Vector2(width, height);
        }
    }
}
