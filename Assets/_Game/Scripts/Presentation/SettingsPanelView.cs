using System;
using RoyalDecisions.Domain;
using UnityEngine;
using UnityEngine.UI;

namespace RoyalDecisions.Presentation
{
    public sealed class SettingsPanelView : MonoBehaviour
    {
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Slider musicVolume;
        [SerializeField] private Slider sfxVolume;
        [SerializeField] private Toggle masterMute;
        [SerializeField] private Toggle haptics;
        [SerializeField] private Toggle reducedMotion;
        [SerializeField] private Toggle largerText;
        [SerializeField] private Toggle highContrast;
        [SerializeField] private Button applyButton;
        [SerializeField] private Button cancelButton;
        [SerializeField] private Button resetButton;

        public event Action ApplyRequested;
        public event Action CancelRequested;
        public event Action ResetRequested;

        public bool IsOpen => panelRoot != null && panelRoot.activeSelf;
        public float MusicVolume => musicVolume != null ? musicVolume.value : GameSettings.DefaultVolume;
        public float SfxVolume => sfxVolume != null ? sfxVolume.value : GameSettings.DefaultVolume;
        public bool MasterMuted => masterMute != null && masterMute.isOn;
        public bool HapticsEnabled => haptics == null || haptics.isOn;
        public bool ReducedMotion => reducedMotion != null && reducedMotion.isOn;
        public bool LargerText => largerText != null && largerText.isOn;
        public bool HighContrast => highContrast != null && highContrast.isOn;

        private void OnEnable()
        {
            if (applyButton != null) applyButton.onClick.AddListener(HandleApply);
            if (cancelButton != null) cancelButton.onClick.AddListener(HandleCancel);
            if (resetButton != null) resetButton.onClick.AddListener(HandleReset);
        }

        private void OnDisable()
        {
            if (applyButton != null) applyButton.onClick.RemoveListener(HandleApply);
            if (cancelButton != null) cancelButton.onClick.RemoveListener(HandleCancel);
            if (resetButton != null) resetButton.onClick.RemoveListener(HandleReset);
        }

        public void Show(GameSettings settings)
        {
            Render(settings);
            panelRoot?.SetActive(true);
        }

        public void Hide() => panelRoot?.SetActive(false);

        public void Render(GameSettings settings)
        {
            settings ??= GameSettings.CreateDefault();
            if (musicVolume != null) musicVolume.SetValueWithoutNotify(settings.MusicVolume);
            if (sfxVolume != null) sfxVolume.SetValueWithoutNotify(settings.SfxVolume);
            if (masterMute != null) masterMute.SetIsOnWithoutNotify(settings.MasterMuted);
            if (haptics != null) haptics.SetIsOnWithoutNotify(settings.HapticsEnabled);
            if (reducedMotion != null) reducedMotion.SetIsOnWithoutNotify(settings.ReducedMotion);
            if (largerText != null) largerText.SetIsOnWithoutNotify(settings.LargerText);
            if (highContrast != null) highContrast.SetIsOnWithoutNotify(settings.HighContrast);
        }

        private void HandleApply() => ApplyRequested?.Invoke();
        private void HandleCancel() => CancelRequested?.Invoke();
        private void HandleReset() => ResetRequested?.Invoke();

#if UNITY_EDITOR
        public void SetAuthoringReferences(
            GameObject root,
            Slider music,
            Slider sfx,
            Toggle mute,
            Toggle hapticToggle,
            Toggle motion,
            Toggle text,
            Toggle contrast,
            Button apply,
            Button cancel,
            Button reset)
        {
            panelRoot = root;
            musicVolume = music;
            sfxVolume = sfx;
            masterMute = mute;
            haptics = hapticToggle;
            reducedMotion = motion;
            largerText = text;
            highContrast = contrast;
            applyButton = apply;
            cancelButton = cancel;
            resetButton = reset;
        }
#endif
    }
}
