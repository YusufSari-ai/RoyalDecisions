using RoyalDecisions.Application;
using RoyalDecisions.Domain;
using RoyalDecisions.Presentation;
using RoyalDecisions.Infrastructure;
using UnityEngine;

namespace RoyalDecisions.Composition
{
    /// <summary>Owns staged settings edits and applies them only on explicit confirmation.</summary>
    public sealed class SettingsController : MonoBehaviour
    {
        [SerializeField] private SettingsPanelView view;
        [SerializeField] private AudioService audioService;
        [SerializeField] private AccessibilityPresentationController accessibility;

        private ISettingsStore store;
        private IHapticService haptics;
        private GameSettings current;

        public GameSettings Current => current;

        private void Awake()
        {
            if (store == null)
            {
                SavePaths paths = SavePaths.ForPersistentData();
                store = new SettingsServiceStore(
                    new SettingsSaveService(new SystemFileSystem(), paths));
            }
            haptics ??= new UnityHapticService();
            LoadAndApply();
        }

        private void OnEnable()
        {
            if (view == null)
            {
                return;
            }
            view.ApplyRequested += ApplyFromView;
            view.CancelRequested += Cancel;
            view.ResetRequested += ResetToDefaults;
        }

        private void OnDisable()
        {
            if (view == null)
            {
                return;
            }
            view.ApplyRequested -= ApplyFromView;
            view.CancelRequested -= Cancel;
            view.ResetRequested -= ResetToDefaults;
        }

        public void Configure(ISettingsStore settingsStore, IHapticService hapticService = null)
        {
            store = settingsStore;
            haptics = hapticService ?? new NoOpHapticService();
            LoadAndApply();
        }

        public void Open()
        {
            EnsureLoaded();
            view?.Show(current);
        }

        public bool CloseIfOpen()
        {
            if (view == null || !view.IsOpen)
            {
                return false;
            }
            Cancel();
            return true;
        }

        public void LoadAndApply()
        {
            current = store != null ? store.Load() : GameSettings.CreateDefault();
            ApplyRuntime(current);
            view?.Render(current);
        }

        public void ApplyFromView()
        {
            EnsureLoaded();
            current.SetMusicVolume(view != null ? view.MusicVolume : current.MusicVolume);
            current.SetSfxVolume(view != null ? view.SfxVolume : current.SfxVolume);
            current.SetMasterMuted(view != null && view.MasterMuted);
            current.SetHapticsEnabled(view == null || view.HapticsEnabled);
            current.SetReducedMotion(view != null && view.ReducedMotion);
            current.SetLargerText(view != null && view.LargerText);
            current.SetHighContrast(view != null && view.HighContrast);
            SaveOutcome outcome = store != null ? store.Save(current) : SaveOutcome.Ok();
            if (!outcome.Succeeded)
            {
                Debug.LogWarning("[Settings] Could not save preferences: " + outcome.Message, this);
                return;
            }
            ApplyRuntime(current);
            view?.Hide();
        }

        public void Cancel()
        {
            view?.Render(current ?? GameSettings.CreateDefault());
            view?.Hide();
        }

        public void ResetToDefaults()
        {
            current = GameSettings.CreateDefault();
            if (store != null)
            {
                SaveOutcome outcome = store.Save(current);
                if (!outcome.Succeeded)
                {
                    Debug.LogWarning("[Settings] Could not reset preferences: " + outcome.Message, this);
                }
            }
            ApplyRuntime(current);
            view?.Render(current);
        }

        private void ApplyRuntime(GameSettings settings)
        {
            if (audioService != null)
            {
                audioService.SetMusicVolume(settings.MusicVolume);
                audioService.SetSfxVolume(settings.SfxVolume);
                audioService.SetMasterMuted(settings.MasterMuted);
            }
            haptics?.SetEnabled(settings.HapticsEnabled);
            accessibility?.Apply(settings);
        }

        private void EnsureLoaded()
        {
            if (current == null)
            {
                LoadAndApply();
            }
        }

#if UNITY_EDITOR
        public void SetAuthoringReferences(
            SettingsPanelView settingsView,
            AudioService audio,
            AccessibilityPresentationController accessibilityController)
        {
            view = settingsView;
            audioService = audio;
            accessibility = accessibilityController;
        }
#endif
    }
}
