using UnityEngine;

namespace RoyalDecisions.Presentation
{
    /// <summary>
    /// An audio service that accepts everything and plays nothing.
    /// </summary>
    /// <remarks>
    /// Makes "no audio wired yet" a supported configuration rather than a null reference — which is
    /// the state the project is in today, with no clips authored at all.
    /// </remarks>
    public sealed class SilentAudioService : IAudioService
    {
        private float volume = 1f;

        public float Volume => volume;

        public bool IsMuted { get; private set; }

        public AudioPlayResult Play(string audioEventId)
        {
            return string.IsNullOrEmpty(audioEventId)
                ? AudioPlayResult.NoCueId
                : AudioPlayResult.NoLibrary;
        }

        public void SetVolume(float value)
        {
            volume = Mathf.Clamp01(value);
        }

        public void SetMuted(bool muted)
        {
            IsMuted = muted;
        }
    }
}
