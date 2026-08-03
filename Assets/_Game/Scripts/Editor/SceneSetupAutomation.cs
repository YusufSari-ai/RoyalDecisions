using System;
using System.Collections.Generic;
using System.IO;
using RoyalDecisions.Composition;
using RoyalDecisions.Data;
using RoyalDecisions.Presentation;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RoyalDecisions.Editor
{
    /// <summary>
    /// Repairs and creates the three MVP scenes without directly editing Unity YAML.
    /// </summary>
    public static class SceneSetupAutomation
    {
        public const string GameScenePath = "Assets/_Game/scenes/Game.unity";
        public const string BootstrapScenePath = "Assets/_Game/scenes/Bootstrap.unity";
        public const string MainMenuScenePath = "Assets/_Game/scenes/MainMenu.unity";
        public const string SessionIntentPath = "Assets/_Game/Content/SessionIntent.asset";
        public const string CataloguePath =
            "Assets/_Game/Content/Placeholder/PlaceholderContentCatalogue.asset";
        public const string InterfaceTextPath = TurkishInterfaceTextGenerator.AssetPath;
        public const string TurkishFontPath = TurkishGlyphValidator.FontAssetPath;
        public const string DefaultThemePath = "Assets/_Game/Content/UI/DefaultGameUITheme.asset";

        private const string ReportPath = "Logs/RoyalDecisionsSceneValidation.json";
        // Unity clears Temp during startup, so rollback data must live in the untracked Library.
        private const string BackupRelativePath = "Library/RoyalDecisionsSceneSetupBackup/Last";
        private const string BackupManifestName = "manifest.json";
        private const string CanvasName = "UICanvas";
        private const string LegacyCanvasName = "U\u0131Canvas";
        private const string BuiltInUiSpritePath = "UI/Skin/UISprite.psd";

        private static readonly Color OverallBackgroundColour = new Color32(0x07, 0x11, 0x1B, 0xFF);
        private static readonly Color SurfaceColour = new Color32(0x12, 0x16, 0x20, 0xFF);
        private static readonly Color CardSurfaceColour = new Color32(0x21, 0x17, 0x1A, 0xFF);
        private static readonly Color BorderGoldColour = new Color32(0xB5, 0x8A, 0x4A, 0xFF);
        private static readonly Color StatBackgroundColour = new Color32(0x2A, 0x2F, 0x3A, 0xFF);
        private static readonly Color ButtonColour = new Color(0.78f, 0.58f, 0.18f, 1f);
        private static readonly Color SpeakerTextColour = new Color32(0xD9, 0xC2, 0x8B, 0xFF);
        private static readonly Color BodyTextColour = new Color32(0xF2, 0xE7, 0xCF, 0xFF);
        private static readonly Color[] StatFillColours =
        {
            new Color32(0x8A, 0x41, 0x4B, 0xFF),
            new Color32(0x68, 0x70, 0x3D, 0xFF),
            new Color32(0x3E, 0x56, 0x7D, 0xFF),
            new Color32(0xB3, 0x8A, 0x3D, 0xFF)
        };

        [MenuItem("Tools/Royal Decisions/Scene Setup/Audit")]
        public static void AuditMenu()
        {
            WriteAndLog(ValidateProject("Audit"));
        }

        [MenuItem("Tools/Royal Decisions/Scene Setup/Apply Remaining Setup")]
        public static void ApplyMenu()
        {
            WriteAndLog(ApplyProject(false));
        }

        [MenuItem("Tools/Royal Decisions/Scene Setup/Validate")]
        public static void ValidateMenu()
        {
            WriteAndLog(ValidateProject("Validate"));
        }

        [MenuItem("Tools/Royal Decisions/Scene Setup/Restore Last Backup")]
        public static void RestoreLastBackupMenu()
        {
            SceneSetupReport report = new SceneSetupReport("Restore Last Backup");

            if (!EditorUtility.DisplayDialog(
                    "Restore Royal Decisions scene setup",
                    "Restore the last scene-setup backup and remove assets created by that run?",
                    "Restore",
                    "Cancel"))
            {
                report.Add(SceneSetupIssueSeverity.Info, "RESTORE_CANCELLED", "Rollback",
                    string.Empty, string.Empty, "Restore was cancelled.");
                WriteAndLog(report);
                return;
            }

            RestoreBackup(report);
            WriteAndLog(report);
        }

        public static void ApplyBatch()
        {
            SceneSetupReport report = ApplyProject(true);
            WriteAndLog(report);

            if (!report.Succeeded)
            {
                throw new InvalidOperationException(
                    "Royal Decisions scene setup failed. See " + ReportPath + ".");
            }
        }

        public static void ValidateBatch()
        {
            SceneSetupReport report = ValidateProject("Validate Batch");
            WriteAndLog(report);

            if (!report.Succeeded)
            {
                throw new InvalidOperationException(
                    "Royal Decisions scene validation failed. See " + ReportPath + ".");
            }
        }

        public static void RestoreLastBackupBatch()
        {
            SceneSetupReport report = new SceneSetupReport("Restore Last Backup Batch");
            RestoreBackup(report);
            WriteAndLog(report);

            if (!report.Succeeded)
            {
                throw new InvalidOperationException(
                    "Royal Decisions scene backup restore failed. See " + ReportPath + ".");
            }
        }

        /// <summary>Test seam: applies Game-scene authoring without saving an asset.</summary>
        public static SceneSetupReport ApplyGameSceneForTests(
            Scene scene,
            ContentCatalogue catalogue,
            SessionIntent sessionIntent)
        {
            SceneSetupReport report = new SceneSetupReport("Apply Game Scene For Tests");
            ApplyGameScene(scene, catalogue, sessionIntent, report);
            return report;
        }

        /// <summary>Test seam: validates a loaded Game scene without changing it.</summary>
        public static SceneSetupReport ValidateGameSceneForTests(
            Scene scene,
            ContentCatalogue catalogue,
            SessionIntent sessionIntent)
        {
            SceneSetupReport report = new SceneSetupReport("Validate Game Scene For Tests");
            ValidateGameScene(scene, catalogue, sessionIntent, report);
            return report;
        }

        private static SceneSetupReport ApplyProject(bool batchMode)
        {
            SceneSetupReport report = new SceneSetupReport("Apply Remaining Setup");

            if (!batchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                report.Add(SceneSetupIssueSeverity.Error, "UNSAVED_SCENES", "Safety",
                    string.Empty, string.Empty,
                    "Scene setup was cancelled because modified scenes were not saved.");
                return report;
            }

            SceneSetup[] originalSetup = EditorSceneManager.GetSceneManagerSetup();
            BackupManifest manifest = CreateBackup(report);

            if (!report.Succeeded)
            {
                return report;
            }

            try
            {
                SessionIntent intent = EnsureSessionIntent(report);
                InterfaceTextDefinition interfaceText =
                    AssetDatabase.LoadAssetAtPath<InterfaceTextDefinition>(InterfaceTextPath);
                TMP_FontAsset turkishFont =
                    AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TurkishFontPath);
                GameUITheme theme = EnsureDefaultTheme(report);
                ContentCatalogue catalogue = AssetDatabase.LoadAssetAtPath<ContentCatalogue>(
                    CataloguePath);

                if (catalogue == null)
                {
                    report.Add(SceneSetupIssueSeverity.Error, "CATALOGUE_MISSING", "Assets",
                        CataloguePath, string.Empty,
                        "The placeholder catalogue is missing or has the wrong type.");
                    throw new InvalidOperationException("Required catalogue is unavailable.");
                }
                if (interfaceText == null || turkishFont == null)
                {
                    report.Add(SceneSetupIssueSeverity.Error, "TURKISH_TEXT_ASSET_MISSING", "Assets",
                        interfaceText == null ? InterfaceTextPath : TurkishFontPath, string.Empty,
                        "The Turkish interface text and project-owned TMP font must be generated first.");
                    throw new InvalidOperationException("Required Turkish text assets are unavailable.");
                }

                AssetDatabase.SaveAssets();

                Scene game = OpenRequiredScene(GameScenePath, report);
                if (!game.IsValid())
                {
                    throw new InvalidOperationException("Game scene could not be opened.");
                }

                catalogue = AssetDatabase.LoadAssetAtPath<ContentCatalogue>(CataloguePath);
                intent = AssetDatabase.LoadAssetAtPath<SessionIntent>(SessionIntentPath);
                interfaceText = AssetDatabase.LoadAssetAtPath<InterfaceTextDefinition>(
                    InterfaceTextPath);
                turkishFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TurkishFontPath);
                theme = AssetDatabase.LoadAssetAtPath<GameUITheme>(DefaultThemePath);

                ApplyGameScene(game, catalogue, intent, interfaceText, turkishFont, theme, report);
                if (!report.Succeeded)
                {
                    throw new InvalidOperationException("Game scene contains blocking ambiguity.");
                }

                EditorSceneManager.MarkSceneDirty(game);
                if (!EditorSceneManager.SaveScene(game, GameScenePath))
                {
                    throw new InvalidOperationException("Game scene could not be saved.");
                }

                AssetDatabase.SaveAssets();

                SceneSetupReport validation = ValidateProjectLoadedState(
                    "Post-apply Validation", catalogue, intent);
                report.Merge(validation);

                if (!validation.Succeeded)
                {
                    throw new InvalidOperationException("Post-apply validation failed.");
                }

                report.Add(SceneSetupIssueSeverity.Info, "APPLY_COMPLETE", "Summary",
                    string.Empty, string.Empty,
                    "Game UI foundation and theme were applied; supporting scenes and build order are valid.");
            }
            catch (Exception exception)
            {
                report.Add(SceneSetupIssueSeverity.Error, "APPLY_EXCEPTION", "Safety",
                    string.Empty, string.Empty, exception.Message);
                RestoreBackup(report, manifest);
            }
            finally
            {
                if (!batchMode && originalSetup != null && originalSetup.Length > 0)
                {
                    EditorSceneManager.RestoreSceneManagerSetup(originalSetup);
                }
            }

            return report;
        }

        private static SceneSetupReport ValidateProject(string operation)
        {
            SceneSetupReport report = new SceneSetupReport(operation);

            if (!Application.isBatchMode
                && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                report.Add(SceneSetupIssueSeverity.Error, "UNSAVED_SCENES", "Safety",
                    string.Empty, string.Empty,
                    "Validation was cancelled because modified scenes were not saved.");
                return report;
            }

            SceneSetup[] originalSetup = EditorSceneManager.GetSceneManagerSetup();

            try
            {
                ContentCatalogue catalogue = AssetDatabase.LoadAssetAtPath<ContentCatalogue>(
                    CataloguePath);
                SessionIntent intent = AssetDatabase.LoadAssetAtPath<SessionIntent>(SessionIntentPath);
                report.Merge(ValidateProjectLoadedState(operation, catalogue, intent));
            }
            finally
            {
                if (!Application.isBatchMode && originalSetup != null && originalSetup.Length > 0)
                {
                    EditorSceneManager.RestoreSceneManagerSetup(originalSetup);
                }
            }

            return report;
        }

        private static SceneSetupReport ValidateProjectLoadedState(
            string operation,
            ContentCatalogue catalogue,
            SessionIntent intent)
        {
            SceneSetupReport report = new SceneSetupReport(operation);

            // Opening scenes with Single mode may unload the managed wrappers supplied by the
            // caller. Reload stable asset references before comparing serialized fields.
            if (catalogue == null)
            {
                catalogue = AssetDatabase.LoadAssetAtPath<ContentCatalogue>(CataloguePath);
            }
            if (intent == null)
            {
                intent = AssetDatabase.LoadAssetAtPath<SessionIntent>(SessionIntentPath);
            }

            GameUITheme theme = AssetDatabase.LoadAssetAtPath<GameUITheme>(DefaultThemePath);
            if (theme == null)
            {
                report.Add(SceneSetupIssueSeverity.Error, "UI_THEME_MISSING", "Assets",
                    DefaultThemePath, string.Empty, "Default GameUITheme is missing or invalid.");
            }
            else
            {
                ValidateTheme(theme, report);
            }

            if (catalogue == null)
            {
                report.Add(SceneSetupIssueSeverity.Error, "CATALOGUE_MISSING", "Assets",
                    CataloguePath, string.Empty, "ContentCatalogue is missing or invalid.");
            }

            if (intent == null)
            {
                report.Add(SceneSetupIssueSeverity.Error, "SESSION_INTENT_MISSING", "Assets",
                    SessionIntentPath, string.Empty, "SessionIntent is missing or invalid.");
            }

            ValidateSceneAsset(GameScenePath, report,
                scene => ValidateGameScene(
                    scene,
                    AssetDatabase.LoadAssetAtPath<ContentCatalogue>(CataloguePath),
                    AssetDatabase.LoadAssetAtPath<SessionIntent>(SessionIntentPath),
                    report));
            ValidateSceneAsset(BootstrapScenePath, report,
                scene => ValidateBootstrapScene(scene, report));
            ValidateSceneAsset(MainMenuScenePath, report,
                scene => ValidateMainMenuScene(
                    scene,
                    AssetDatabase.LoadAssetAtPath<SessionIntent>(SessionIntentPath),
                    report));
            ValidateBuildScenes(report);

            if (report.Succeeded)
            {
                report.Add(SceneSetupIssueSeverity.Info, "VALIDATION_OK", "Summary",
                    string.Empty, string.Empty, "All managed scene setup checks passed.");
            }

            return report;
        }

        private static void ValidateSceneAsset(
            string path,
            SceneSetupReport report,
            Action<Scene> validator)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
            {
                report.Add(SceneSetupIssueSeverity.Error, "SCENE_MISSING", "Scenes",
                    path, string.Empty, "Required scene is missing.");
                return;
            }

            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            validator(scene);
        }

        // Game scene -----------------------------------------------------------------

        private static void ApplyGameScene(
            Scene scene,
            ContentCatalogue catalogue,
            SessionIntent intent,
            SceneSetupReport report)
        {
            ApplyGameScene(
                scene,
                catalogue,
                intent,
                AssetDatabase.LoadAssetAtPath<InterfaceTextDefinition>(InterfaceTextPath),
                AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TurkishFontPath),
                AssetDatabase.LoadAssetAtPath<GameUITheme>(DefaultThemePath),
                report);
        }

        private static void ApplyGameScene(
            Scene scene,
            ContentCatalogue catalogue,
            SessionIntent intent,
            InterfaceTextDefinition interfaceText,
            TMP_FontAsset font,
            GameUITheme theme,
            SceneSetupReport report)
        {
            if (!PreflightGameScene(scene, report))
            {
                return;
            }

            EnsureCamera(scene, report);
            EnsureEventSystem(scene, report);

            GameObject canvasObject = EnsureGameCanvas(scene, report);
            if (canvasObject == null)
            {
                return;
            }

            RectTransform safeArea = EnsureUiChild(canvasObject.transform, "SafeArea", report);
            Stretch(safeArea);
            EnsureSingleComponent<SafeAreaFitter>(safeArea.gameObject, report);

            BackgroundView background = ConfigureBackground(canvasObject.transform, report);
            HUDView hud = ConfigureHud(safeArea, interfaceText, font, report);
            FooterParts footer = ConfigureFooter(safeArea, interfaceText, font, report);
            CardParts card = ConfigureCard(safeArea, font, report);
            GameOverParts gameOver = ConfigureGameOver(
                canvasObject, safeArea, interfaceText, font, report);
            AudioService audio = ConfigureAudio(scene, report);

            GameObject controllerObject = EnsureRoot(scene, "GameSceneController", report);
            GameSceneController controller = EnsureSingleComponent<GameSceneController>(
                controllerObject, report);

            if (controller != null && card.View != null && card.Swipe != null)
            {
                SetObjectProperty(controller, "catalogue", catalogue, report);
                SetObjectProperty(controller, "cardView", card.View, report);
                SetObjectProperty(controller, "hudView", hud, report);
                SetObjectProperty(controller, "gameOverView", gameOver.View, report);
                SetObjectProperty(controller, "swipeController", card.Swipe, report);
                SetObjectProperty(controller, "runStatusView", footer.RunStatus, report);
                SetObjectProperty(controller, "footerView", footer.Footer, report);
                SetObjectProperty(controller, "audioService", audio, report);
                SetObjectProperty(controller, "sessionIntent", intent, report);
                SetEnumProperty(controller, "fallbackStartMode", (int)SessionStartMode.NewGame, report);
            }

            GameUIThemeController themeController = EnsureSingleComponent<GameUIThemeController>(
                canvasObject, report);
            if (themeController != null)
            {
                if (GetObjectProperty(themeController, "theme") == null)
                {
                    SetObjectProperty(themeController, "theme", theme, report);
                }
                SetObjectProperty(themeController, "backgroundView", background, report);
                SetObjectProperty(themeController, "hudView", hud, report);
                SetObjectProperty(themeController, "cardView", card.View, report);
                SetObjectProperty(themeController, "footerView", footer.Footer, report);
                SetObjectProperty(themeController, "gameOverView", gameOver.View, report);
                themeController.ApplyTheme();
            }

            if (background != null)
            {
                SetSiblingIndex(background.transform, 0);
                SetSiblingIndex(safeArea, 1);
            }

            if (hud != null && card.Area != null && footer.Root != null && gameOver.Root != null)
            {
                SetSiblingIndex(hud.transform, 0);
                SetSiblingIndex(card.Area, 1);
                SetSiblingIndex(footer.Root, 2);
                SetSiblingIndex(gameOver.Root, 3);
            }

            // TMP auto-sizing stores both its configured base size and its last calculated size.
            // Force the calculation before every save so a newly-created scene and a repair pass
            // serialize the same value instead of converging only on the second run.
            Canvas.ForceUpdateCanvases();
            TextMeshProUGUI[] managedText = FindComponentsInScene<TextMeshProUGUI>(scene);
            for (int i = 0; i < managedText.Length; i++)
            {
                if (font != null && managedText[i].font != font)
                {
                    Undo.RecordObject(managedText[i], "Assign Turkish TMP font");
                    managedText[i].font = font;
                }
                managedText[i].ForceMeshUpdate(true, true);
            }

            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static bool PreflightGameScene(Scene scene, SceneSetupReport report)
        {
            bool valid = true;
            valid &= CheckRootDuplicates(scene, CanvasName, report);
            valid &= CheckRootDuplicates(scene, "Main Camera", report);
            valid &= CheckRootDuplicates(scene, "EventSystem", report);
            valid &= CheckRootDuplicates(scene, "AudioService", report);
            valid &= CheckRootDuplicates(scene, "GameSceneController", report);

            GameObject canvas = FindUniqueRoot(scene, CanvasName, null);
            if (canvas == null)
            {
                GameObject legacy = FindUniqueRoot(scene, LegacyCanvasName, null);
                Canvas[] canvases = FindComponentsInScene<Canvas>(scene);

                if (legacy == null && canvases.Length > 1)
                {
                    report.Add(SceneSetupIssueSeverity.Error, "AMBIGUOUS_CANVAS", "Hierarchy",
                        scene.path, "/", "Multiple root Canvases exist and none is /UICanvas.");
                    valid = false;
                }
            }

            return valid;
        }

        private static GameObject EnsureGameCanvas(Scene scene, SceneSetupReport report)
        {
            GameObject canvasObject = FindUniqueRoot(scene, CanvasName, report);

            if (canvasObject == null)
            {
                GameObject legacy = FindUniqueRoot(scene, LegacyCanvasName, report);
                if (legacy != null && legacy.GetComponent<Canvas>() != null)
                {
                    Undo.RecordObject(legacy, "Repair UICanvas name");
                    legacy.name = CanvasName;
                    canvasObject = legacy;
                }
            }

            canvasObject ??= EnsureRoot(scene, CanvasName, report, true);
            ConfigureCanvas(canvasObject, report);
            return canvasObject;
        }

        private static BackgroundView ConfigureBackground(
            Transform canvas,
            SceneSetupReport report)
        {
            RectTransform root = EnsureUiChild(canvas, "Background", report);
            Stretch(root);
            Image surface = EnsureSingleComponent<Image>(root.gameObject, report);
            ConfigureSimpleImage(surface, LoadBuiltInUiSprite(report), OverallBackgroundColour, false);

            RectTransform artworkTransform = EnsureUiChild(root, "Artwork", report);
            RectTransform overlayTransform = EnsureUiChild(root, "DarkOverlay", report);
            RectTransform vignetteTransform = EnsureUiChild(root, "Vignette", report);
            Stretch(artworkTransform);
            Stretch(overlayTransform);
            Stretch(vignetteTransform);
            Image artwork = EnsureSingleComponent<Image>(artworkTransform.gameObject, report);
            Image overlay = EnsureSingleComponent<Image>(overlayTransform.gameObject, report);
            Image vignette = EnsureSingleComponent<Image>(vignetteTransform.gameObject, report);
            ConfigureSimpleImage(artwork, null, Color.white, false, false);
            ConfigureSimpleImage(overlay, null, new Color(0f, 0f, 0f, 0.28f), false);
            ConfigureSimpleImage(vignette, null, Color.white, false, false);

            BackgroundView view = EnsureSingleComponent<BackgroundView>(root.gameObject, report);
            SetObjectProperty(view, "fallbackSurface", surface, report);
            SetObjectProperty(view, "artwork", artwork, report);
            SetObjectProperty(view, "darkOverlay", overlay, report);
            SetObjectProperty(view, "vignette", vignette, report);
            return view;
        }

        private static HUDView ConfigureHud(
            RectTransform safeArea,
            InterfaceTextDefinition interfaceText,
            TMP_FontAsset font,
            SceneSetupReport report)
        {
            RectTransform hudTransform = EnsureUiChild(safeArea, "HUD", report);
            SetRect(hudTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                Vector2.zero, new Vector2(0f, 176f), new Vector2(0.5f, 1f));

            HUDView hud = EnsureSingleComponent<HUDView>(hudTransform.gameObject, report);
            Image hudSurface = EnsureSingleComponent<Image>(hudTransform.gameObject, report);
            ConfigureSimpleImage(hudSurface, LoadBuiltInUiSprite(report), SurfaceColour, false);
            HorizontalLayoutGroup layout = EnsureSingleComponent<HorizontalLayoutGroup>(
                hudTransform.gameObject, report);
            if (layout != null)
            {
                Undo.RecordObject(layout, "Configure HUD layout");
                layout.childAlignment = TextAnchor.MiddleCenter;
                layout.padding = new RectOffset(16, 16, 12, 12);
                layout.spacing = 12f;
                layout.childControlWidth = true;
                layout.childControlHeight = true;
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = true;
            }

            string[] statNames =
            {
                "StatItem_People", "StatItem_Security", "StatItem_Authority", "StatItem_Wealth"
            };
            string[] slotNames =
            {
                "StatSlot_People", "StatSlot_Security", "StatSlot_Authority", "StatSlot_Wealth"
            };
            StatType[] stats =
            {
                StatType.People, StatType.Security, StatType.Authority, StatType.Wealth
            };
            Sprite uiSprite = LoadBuiltInUiSprite(report);
            StatItemView[] items = new StatItemView[statNames.Length];

            for (int i = 0; i < statNames.Length; i++)
            {
                RectTransform slot = EnsureUiChild(hudTransform, slotNames[i], report);
                LayoutElement slotLayout = EnsureSingleComponent<LayoutElement>(slot.gameObject, report);
                if (slotLayout != null)
                {
                    Undo.RecordObject(slotLayout, "Configure stat slot layout");
                    slotLayout.flexibleWidth = 1f;
                    slotLayout.minWidth = 0f;
                }

                RectTransform itemTransform = FindDirectChild(slot, statNames[i], report);
                RectTransform legacyItem = FindDirectChild(hudTransform, statNames[i], report);
                if (itemTransform == null && legacyItem != null)
                {
                    Undo.SetTransformParent(legacyItem, slot, "Move stat bar into semantic slot");
                    itemTransform = legacyItem;
                }
                itemTransform ??= EnsureUiChild(slot, statNames[i], report);
                SetRect(itemTransform, new Vector2(0.08f, 0f), new Vector2(0.92f, 0f),
                    new Vector2(0f, 5f), new Vector2(0f, 18f), new Vector2(0.5f, 0f));

                Image background = EnsureSingleComponent<Image>(itemTransform.gameObject, report);
                StatItemView item = EnsureSingleComponent<StatItemView>(
                    itemTransform.gameObject, report);
                RectTransform fillTransform = FindDirectChild(itemTransform, "Fill", report);
                fillTransform ??= EnsureUiChild(itemTransform, "Fill", report);
                Stretch(fillTransform);
                Image fill = EnsureSingleComponent<Image>(fillTransform.gameObject, report);
                RectTransform iconTransform = EnsureUiChild(slot, "Icon", report);
                SetRect(iconTransform, new Vector2(0.06f, 0.24f), new Vector2(0.32f, 0.64f),
                    Vector2.zero, Vector2.zero, Center);
                Image icon = EnsureSingleComponent<Image>(iconTransform.gameObject, report);
                if (icon != null)
                {
                    Undo.RecordObject(icon, "Configure stat icon slot");
                    icon.raycastTarget = false;
                    icon.preserveAspect = true;
                    icon.enabled = false;
                }

                RectTransform fallbackTransform = EnsureUiChild(slot, "IconFallback", report);
                SetRect(fallbackTransform, iconTransform.anchorMin, iconTransform.anchorMax,
                    Vector2.zero, Vector2.zero, Center);
                TextMeshProUGUI fallback = EnsureSingleComponent<TextMeshProUGUI>(
                    fallbackTransform.gameObject, report);
                ConfigureReadableText(fallback, font, 30f, 24f, 34f, true, false, 0f);

                RectTransform labelTransform = FindDirectChild(slot, "Name", report);
                RectTransform legacyLabel = FindDirectChild(itemTransform, "Label", report);
                if (labelTransform == null && legacyLabel != null)
                {
                    Undo.SetTransformParent(legacyLabel, slot, "Move stat name into semantic slot");
                    Undo.RecordObject(legacyLabel.gameObject, "Rename stat label");
                    legacyLabel.gameObject.name = "Name";
                    labelTransform = legacyLabel;
                }
                labelTransform ??= EnsureUiChild(slot, "Name", report);
                SetRect(labelTransform, new Vector2(0.36f, 0.58f), new Vector2(0.78f, 0.92f),
                    Vector2.zero, Vector2.zero, Center);
                TextMeshProUGUI label = EnsureSingleComponent<TextMeshProUGUI>(
                    labelTransform.gameObject, report);
                ConfigureReadableText(label, font, 22f, 18f, 24f, true, false, 0f);

                RectTransform valueTransform = FindDirectChild(slot, "Value", report);
                RectTransform legacyValue = FindDirectChild(itemTransform, "Value", report);
                if (valueTransform == null && legacyValue != null)
                {
                    Undo.SetTransformParent(legacyValue, slot, "Move stat value into semantic slot");
                    valueTransform = legacyValue;
                }
                valueTransform ??= EnsureUiChild(slot, "Value", report);
                SetRect(valueTransform, new Vector2(0.36f, 0.18f), new Vector2(0.66f, 0.58f),
                    Vector2.zero, Vector2.zero, Center);
                TextMeshProUGUI value = EnsureSingleComponent<TextMeshProUGUI>(
                    valueTransform.gameObject, report);
                ConfigureReadableText(value, font, 30f, 24f, 34f, true, false, 0f);

                RectTransform impactTransform = EnsureUiChild(slot, "Impact", report);
                SetRect(impactTransform, new Vector2(0.66f, 0.18f), new Vector2(0.94f, 0.58f),
                    Vector2.zero, Vector2.zero, Center);
                TextMeshProUGUI impact = EnsureSingleComponent<TextMeshProUGUI>(
                    impactTransform.gameObject, report);
                CanvasGroup impactGroup = EnsureSingleComponent<CanvasGroup>(
                    impactTransform.gameObject, report);
                ConfigureReadableText(impact, font, 24f, 18f, 28f, true, false, 0f);
                impact.text = string.Empty;
                impactGroup.alpha = 0f;

                RectTransform criticalTransform = EnsureUiChild(slot, "Critical", report);
                SetRect(criticalTransform, new Vector2(0.78f, 0.60f), new Vector2(0.96f, 0.92f),
                    Vector2.zero, Vector2.zero, Center);
                TextMeshProUGUI critical = EnsureSingleComponent<TextMeshProUGUI>(
                    criticalTransform.gameObject, report);
                ConfigureReadableText(critical, font, 26f, 20f, 30f, true, false, 0f);
                critical.text = "!";
                critical.gameObject.SetActive(false);

                ConfigureStatBackground(background, uiSprite);
                ConfigureStatFill(fill, uiSprite, StatFillColours[i]);

                if (item != null)
                {
                    SetEnumProperty(item, "stat", (int)stats[i], report);
                    SetObjectProperty(item, "fillImage", fill, report);
                    SetObjectProperty(item, "iconImage", icon, report);
                    SetObjectProperty(item, "label", label, report);
                    SetObjectProperty(item, "valueText", value, report);
                    SetObjectProperty(item, "iconFallbackLabel", fallback, report);
                    SetObjectProperty(item, "impactLabel", impact, report);
                    SetObjectProperty(item, "impactGroup", impactGroup, report);
                    SetObjectProperty(item, "criticalLabel", critical, report);
                }

                label.text = interfaceText != null ? interfaceText.GetStatLabel(stats[i]) : stats[i].ToString();
                value.text = StatBounds.Initial.ToString();

                items[i] = item;
                SetSiblingIndex(slot, i);
                SetSiblingIndex(iconTransform, 0);
                SetSiblingIndex(fallbackTransform, 1);
                SetSiblingIndex(labelTransform, 2);
                SetSiblingIndex(valueTransform, 3);
                SetSiblingIndex(impactTransform, 4);
                SetSiblingIndex(criticalTransform, 5);
                SetSiblingIndex(itemTransform, 6);
            }

            if (hud != null)
            {
                SetObjectArrayProperty(hud, "statItems", items, report);
                SetObjectProperty(hud, "interfaceText", interfaceText, report);
            }

            return hud;
        }

        private static FooterParts ConfigureFooter(
            RectTransform safeArea,
            InterfaceTextDefinition interfaceText,
            TMP_FontAsset font,
            SceneSetupReport report)
        {
            RectTransform root = FindDirectChild(safeArea, "Footer", report);
            RectTransform legacyRoot = FindDirectChild(safeArea, "RunStatus", report);
            if (root == null && legacyRoot != null)
            {
                Undo.RecordObject(legacyRoot.gameObject, "Rename run status as footer");
                legacyRoot.gameObject.name = "Footer";
                root = legacyRoot;
            }
            root ??= EnsureUiChild(safeArea, "Footer", report);
            SetRect(root, new Vector2(0f, 0f), new Vector2(1f, 0f),
                Vector2.zero, new Vector2(0f, 80f), new Vector2(0.5f, 0f));

            HorizontalLayoutGroup layout = EnsureSingleComponent<HorizontalLayoutGroup>(
                root.gameObject, report);
            if (layout != null)
            {
                Undo.RecordObject(layout, "Configure footer layout");
                layout.padding = new RectOffset(16, 16, 8, 8);
                layout.spacing = 12f;
                layout.childAlignment = TextAnchor.MiddleCenter;
                layout.childControlWidth = true;
                layout.childControlHeight = true;
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = true;
            }

            RectTransform reignTransform = FindDirectChild(root, "Reign", report);
            RectTransform legacyTurn = FindDirectChild(root, "Turn", report);
            if (reignTransform == null && legacyTurn != null)
            {
                Undo.RecordObject(legacyTurn.gameObject, "Rename footer reign label");
                legacyTurn.gameObject.name = "Reign";
                reignTransform = legacyTurn;
            }
            reignTransform ??= EnsureUiChild(root, "Reign", report);
            RectTransform rulerTransform = EnsureUiChild(root, "Ruler", report);
            RectTransform progressTransform = EnsureUiChild(root, "Progress", report);
            RectTransform sealTransform = EnsureUiChild(root, "Seal", report);

            TextMeshProUGUI reign = EnsureSingleComponent<TextMeshProUGUI>(
                reignTransform.gameObject, report);
            TextMeshProUGUI ruler = EnsureSingleComponent<TextMeshProUGUI>(
                rulerTransform.gameObject, report);
            TextMeshProUGUI progress = EnsureSingleComponent<TextMeshProUGUI>(
                progressTransform.gameObject, report);
            Image seal = EnsureSingleComponent<Image>(sealTransform.gameObject, report);
            ConfigureReadableText(reign, font, 26f, 22f, 28f, true, false, 0f);
            ConfigureReadableText(ruler, font, 26f, 22f, 28f, true, false, 0f);
            ConfigureReadableText(progress, font, 24f, 20f, 26f, true, false, 0f);
            reign.text = string.Format("{0} 1", interfaceText != null ? interfaceText.Turn : "Tur");
            ruler.text = "Royal Decisions";
            progress.text = string.Empty;
            progress.gameObject.SetActive(false);
            if (seal != null)
            {
                Undo.RecordObject(seal, "Configure footer seal slot");
                seal.raycastTarget = false;
                seal.preserveAspect = true;
                seal.enabled = false;
            }

            LayoutElement sealLayout = EnsureSingleComponent<LayoutElement>(sealTransform.gameObject, report);
            if (sealLayout != null)
            {
                Undo.RecordObject(sealLayout, "Configure footer seal layout");
                sealLayout.minWidth = 56f;
                sealLayout.preferredWidth = 56f;
                sealLayout.flexibleWidth = 0f;
            }

            RunStatusView runStatus = EnsureSingleComponent<RunStatusView>(root.gameObject, report);
            SetObjectProperty(runStatus, "interfaceText", interfaceText, report);
            SetObjectProperty(runStatus, "turnText", reign, report);

            FooterView footer = EnsureSingleComponent<FooterView>(root.gameObject, report);
            SetObjectProperty(footer, "interfaceText", interfaceText, report);
            SetObjectProperty(footer, "reignText", reign, report);
            SetObjectProperty(footer, "rulerText", ruler, report);
            SetObjectProperty(footer, "progressText", progress, report);
            SetObjectProperty(footer, "sealImage", seal, report);

            SetSiblingIndex(reignTransform, 0);
            SetSiblingIndex(rulerTransform, 1);
            SetSiblingIndex(progressTransform, 2);
            SetSiblingIndex(sealTransform, 3);
            return new FooterParts(root, runStatus, footer);
        }

        private static Sprite LoadBuiltInUiSprite(SceneSetupReport report)
        {
            Sprite sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>(BuiltInUiSpritePath);
            if (sprite == null)
            {
                report.Add(SceneSetupIssueSeverity.Error, "BUILTIN_UI_SPRITE_MISSING", "Assets",
                    BuiltInUiSpritePath, string.Empty,
                    "Unity's built-in UISprite could not be loaded for serializable UI Images.");
            }
            return sprite;
        }

        private static void ConfigureStatBackground(Image background, Sprite sprite)
        {
            if (background == null || (background.sprite == sprite
                && background.type == Image.Type.Simple
                && !background.raycastTarget
                && ColoursMatch(background.color, StatBackgroundColour)))
            {
                return;
            }

            Undo.RecordObject(background, "Configure stat background");
            background.sprite = sprite;
            background.type = Image.Type.Simple;
            background.raycastTarget = false;
            background.color = StatBackgroundColour;
        }

        private static void ConfigureSimpleImage(
            Image image,
            Sprite sprite,
            Color colour,
            bool raycast,
            bool enabled = true)
        {
            if (image == null)
            {
                return;
            }

            Undo.RecordObject(image, "Configure UI Image");
            image.sprite = sprite;
            image.type = Image.Type.Simple;
            image.preserveAspect = false;
            image.raycastTarget = raycast;
            image.color = colour;
            image.enabled = enabled;
        }

        private static void ConfigureOptionalSlicedImage(Image image, Sprite sprite, Color colour)
        {
            if (image == null)
            {
                return;
            }

            Undo.RecordObject(image, "Configure optional sliced Image");
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.preserveAspect = false;
            image.raycastTarget = false;
            image.color = colour;
            image.enabled = sprite != null;
        }

        private static void ConfigureLayoutElement(
            GameObject target,
            float preferredHeight,
            SceneSetupReport report)
        {
            LayoutElement element = EnsureSingleComponent<LayoutElement>(target, report);
            if (element == null)
            {
                return;
            }

            Undo.RecordObject(element, "Configure layout element");
            element.preferredHeight = preferredHeight;
            element.flexibleHeight = 0f;
        }

        private static void ConfigureStatFill(Image fill, Sprite sprite, Color colour)
        {
            if (fill == null || (fill.sprite == sprite
                && fill.type == Image.Type.Filled
                && fill.fillMethod == Image.FillMethod.Horizontal
                && fill.fillOrigin == (int)Image.OriginHorizontal.Left
                && !fill.preserveAspect
                && !fill.raycastTarget
                && ColoursMatch(fill.color, colour)))
            {
                return;
            }

            Undo.RecordObject(fill, "Configure stat fill");
            fill.sprite = sprite;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.preserveAspect = false;
            fill.raycastTarget = false;
            fill.color = colour;
        }

        private static CardParts ConfigureCard(
            RectTransform safeArea,
            TMP_FontAsset font,
            SceneSetupReport report)
        {
            RectTransform area = EnsureUiChild(safeArea, "CardArea", report);
            SetRect(area, Vector2.zero, Vector2.one, new Vector2(0f, -44f),
                new Vector2(-48f, -312f), Center);

            RectTransform nextCard = EnsureUiChild(area, "NextCard", report);
            Image nextSurface = EnsureSingleComponent<Image>(nextCard.gameObject, report);
            ConfigureSimpleImage(nextSurface, LoadBuiltInUiSprite(report), CardSurfaceColour, false);
            RectTransform nextFrameTransform = EnsureUiChild(nextCard, "Frame", report);
            Stretch(nextFrameTransform);
            Image nextFrame = EnsureSingleComponent<Image>(nextFrameTransform.gameObject, report);
            ConfigureOptionalSlicedImage(nextFrame, null, BorderGoldColour);

            RectTransform card = EnsureUiChild(area, "Card", report);
            SetRect(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(880f, 1300f), new Vector2(0.5f, 0.5f));
            Image cardImage = EnsureSingleComponent<Image>(card.gameObject, report);
            ConfigureSimpleImage(cardImage, LoadBuiltInUiSprite(report), CardSurfaceColour, true);
            Outline outline = EnsureSingleComponent<Outline>(card.gameObject, report);
            if (outline != null)
            {
                Undo.RecordObject(outline, "Configure card outline");
                outline.effectColor = BorderGoldColour;
                outline.effectDistance = new Vector2(3f, -3f);
                outline.useGraphicAlpha = true;
            }

            RectTransform frameTransform = EnsureUiChild(card, "Frame", report);
            Stretch(frameTransform);
            Image frame = EnsureSingleComponent<Image>(frameTransform.gameObject, report);
            ConfigureOptionalSlicedImage(frame, null, BorderGoldColour);

            RectTransform portraitRegion = EnsureUiChild(card, "PortraitRegion", report);
            SetRect(portraitRegion, new Vector2(0.07f, 0.50f), new Vector2(0.93f, 0.93f),
                Vector2.zero, Vector2.zero, Center);
            Image portraitFrame = EnsureSingleComponent<Image>(portraitRegion.gameObject, report);
            ConfigureSimpleImage(portraitFrame, LoadBuiltInUiSprite(report), BorderGoldColour, false);

            RectTransform portraitMask = EnsureUiChild(portraitRegion, "PortraitMask", report);
            SetRect(portraitMask, Vector2.zero, Vector2.one, Vector2.zero,
                new Vector2(-8f, -8f), Center);
            Image maskImage = EnsureSingleComponent<Image>(portraitMask.gameObject, report);
            ConfigureSimpleImage(maskImage, LoadBuiltInUiSprite(report), Color.white, false);
            Mask mask = EnsureSingleComponent<Mask>(portraitMask.gameObject, report);
            if (mask != null)
            {
                Undo.RecordObject(mask, "Configure portrait mask");
                mask.showMaskGraphic = false;
            }

            RectTransform portraitTransform = FindDirectChild(portraitMask, "Portrait", report);
            RectTransform legacyPortrait = FindDirectChild(card, "Portrait", report);
            if (portraitTransform == null && legacyPortrait != null)
            {
                Undo.SetTransformParent(legacyPortrait, portraitMask, "Move portrait into card mask");
                portraitTransform = legacyPortrait;
            }
            portraitTransform ??= EnsureUiChild(portraitMask, "Portrait", report);
            Stretch(portraitTransform);
            Image portrait = EnsureSingleComponent<Image>(portraitTransform.gameObject, report);
            if (portrait != null)
            {
                Undo.RecordObject(portrait, "Configure portrait");
                portrait.raycastTarget = false;
                portrait.preserveAspect = false;
            }

            RectTransform speakerTransform = EnsureUiChild(card, "Speaker", report);
            SetRect(speakerTransform, new Vector2(0.09f, 0.42f), new Vector2(0.91f, 0.50f),
                Vector2.zero, Vector2.zero, Center);
            TextMeshProUGUI speaker = EnsureSingleComponent<TextMeshProUGUI>(
                speakerTransform.gameObject, report);
            RectTransform bodyTransform = EnsureUiChild(card, "Body", report);
            SetRect(bodyTransform, new Vector2(0.09f, 0.19f), new Vector2(0.91f, 0.42f),
                Vector2.zero, Vector2.zero, Center);
            TextMeshProUGUI body = EnsureSingleComponent<TextMeshProUGUI>(
                bodyTransform.gameObject, report);
            ConfigureReadableText(speaker, font, 32f, 26f, 36f, true, false, 3f);
            ConfigureReadableText(body, font, 40f, 32f, 44f, true, true, 4f);
            SetTextColour(speaker, SpeakerTextColour);
            SetTextColour(body, BodyTextColour);

            Image[] corners = new Image[4];
            string[] cornerNames =
            {
                "CornerTopLeft", "CornerTopRight", "CornerBottomLeft", "CornerBottomRight"
            };
            Vector2[] cornerAnchors =
            {
                new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(1f, 0f)
            };
            for (int i = 0; i < cornerNames.Length; i++)
            {
                RectTransform corner = EnsureUiChild(card, cornerNames[i], report);
                SetRect(corner, cornerAnchors[i], cornerAnchors[i], Vector2.zero,
                    new Vector2(96f, 96f), cornerAnchors[i]);
                corners[i] = EnsureSingleComponent<Image>(corner.gameObject, report);
                ConfigureOptionalSlicedImage(corners[i], null, BorderGoldColour);
            }

            ChoicePreviewView left = ConfigurePreview(
                card, "PreviewLeft", ChoiceSide.Left, font, report);
            ChoicePreviewView right = ConfigurePreview(
                card, "PreviewRight", ChoiceSide.Right, font, report);

            CardView view = EnsureSingleComponent<CardView>(card.gameObject, report);
            CardSwipeController swipe = EnsureSingleComponent<CardSwipeController>(
                card.gameObject, report);

            if (view != null)
            {
                SetObjectProperty(view, "cardRoot", card, report);
                SetObjectProperty(view, "speakerText", speaker, report);
                SetObjectProperty(view, "bodyText", body, report);
                SetObjectProperty(view, "portraitImage", portrait, report);
                SetObjectProperty(view, "leftPreview", left, report);
                SetObjectProperty(view, "rightPreview", right, report);
                SetObjectProperty(view, "visualRoot", card.gameObject, report);
                SetObjectProperty(view, "surfaceImage", cardImage, report);
                SetObjectProperty(view, "borderOutline", outline, report);
                SetObjectProperty(view, "frameImage", frame, report);
                SetObjectProperty(view, "portraitFrameImage", portraitFrame, report);
                SetObjectProperty(view, "portraitMaskImage", maskImage, report);
                SetObjectArrayProperty(view, "cornerImages", corners, report);
                SetObjectProperty(view, "nextCardRoot", nextCard.gameObject, report);
                SetObjectProperty(view, "nextCardSurface", nextSurface, report);
                SetObjectProperty(view, "nextCardFrame", nextFrame, report);
            }

            if (swipe != null)
            {
                SetObjectProperty(swipe, "cardView", view, report);
                SetObjectProperty(swipe, "dragParent", area, report);
            }

            ResponsiveCardSizer sizer = EnsureSingleComponent<ResponsiveCardSizer>(
                area.gameObject, report);
            SetObjectProperty(sizer, "card", card, report);
            SetObjectProperty(sizer, "nextCard", nextCard, report);
            sizer?.RecalculateLayout();

            SetSiblingIndex(nextCard, 0);
            SetSiblingIndex(card, 1);
            SetSiblingIndex(frameTransform, 0);
            SetSiblingIndex(portraitRegion, 1);
            SetSiblingIndex(speakerTransform, 2);
            SetSiblingIndex(bodyTransform, 3);

            return new CardParts(area, view, swipe);
        }

        private static ChoicePreviewView ConfigurePreview(
            RectTransform card,
            string name,
            ChoiceSide side,
            TMP_FontAsset font,
            SceneSetupReport report)
        {
            RectTransform preview = EnsureUiChild(card, name, report);
            bool left = side == ChoiceSide.Left;
            Stretch(preview);

            Image image = EnsureSingleComponent<Image>(preview.gameObject, report);
            CanvasGroup group = EnsureSingleComponent<CanvasGroup>(preview.gameObject, report);
            ChoicePreviewView view = EnsureSingleComponent<ChoicePreviewView>(
                preview.gameObject, report);
            if (image != null)
            {
                Undo.RecordObject(image, "Configure choice preview");
                image.raycastTarget = false;
                image.enabled = false;
            }

            if (group != null)
            {
                Undo.RecordObject(group, "Reset choice preview visibility");
                group.alpha = 0f;
                group.blocksRaycasts = false;
                group.interactable = false;
            }

            RectTransform edgeTransform = EnsureUiChild(preview, "EdgeHighlight", report);
            SetRect(edgeTransform,
                new Vector2(left ? 0f : 0.92f, 0f),
                new Vector2(left ? 0.08f : 1f, 1f),
                Vector2.zero, Vector2.zero, Center);
            Image edge = EnsureSingleComponent<Image>(edgeTransform.gameObject, report);
            ConfigureSimpleImage(edge, LoadBuiltInUiSprite(report),
                left ? StatFillColours[0] : StatFillColours[3], false);

            RectTransform markerTransform = EnsureUiChild(preview, "CommitMarker", report);
            SetRect(markerTransform,
                new Vector2(left ? 0.02f : 0.94f, 0.46f),
                new Vector2(left ? 0.06f : 0.98f, 0.54f),
                Vector2.zero, Vector2.zero, Center);
            Image markerImage = EnsureSingleComponent<Image>(markerTransform.gameObject, report);
            ConfigureSimpleImage(markerImage, LoadBuiltInUiSprite(report), BodyTextColour, false);
            CanvasGroup marker = EnsureSingleComponent<CanvasGroup>(markerTransform.gameObject, report);
            if (marker != null)
            {
                Undo.RecordObject(marker, "Reset choice commit marker");
                marker.alpha = 0f;
                marker.blocksRaycasts = false;
                marker.interactable = false;
            }

            RectTransform labelTransform = EnsureUiChild(preview, "Label", report);
            SetRect(labelTransform,
                new Vector2(left ? 0.05f : 0.55f, 0.05f),
                new Vector2(left ? 0.45f : 0.95f, 0.18f),
                Vector2.zero, Vector2.zero, Center);
            TextMeshProUGUI label = EnsureSingleComponent<TextMeshProUGUI>(
                labelTransform.gameObject, report);
            ConfigureReadableText(label, font, 28f, 24f, 32f, true, true, 4f);

            if (view != null)
            {
                SetEnumProperty(view, "side", (int)side, report);
                SetObjectProperty(view, "label", label, report);
                SetObjectProperty(view, "canvasGroup", group, report);
                SetObjectProperty(view, "edgeHighlight", edge, report);
                SetObjectProperty(view, "commitMarker", marker, report);
            }

            SetSiblingIndex(edgeTransform, 0);
            SetSiblingIndex(markerTransform, 1);
            SetSiblingIndex(labelTransform, 2);

            return view;
        }

        private static GameOverParts ConfigureGameOver(
            GameObject canvasObject,
            RectTransform safeArea,
            InterfaceTextDefinition interfaceText,
            TMP_FontAsset font,
            SceneSetupReport report)
        {
            RectTransform panel = EnsureUiChild(safeArea, "GameOverPanel", report);
            Stretch(panel);
            Image panelImage = EnsureSingleComponent<Image>(panel.gameObject, report);
            GameOverView view = EnsureSingleComponent<GameOverView>(panel.gameObject, report);

            if (panelImage != null)
            {
                Undo.RecordObject(panelImage, "Configure game-over panel");
                panelImage.color = OverallBackgroundColour;
            }

            RectTransform content = EnsureUiChild(panel, "Content", report);
            SetRect(content, new Vector2(0.08f, 0.18f), new Vector2(0.92f, 0.82f),
                Vector2.zero, Vector2.zero, Center);
            VerticalLayoutGroup contentLayout = EnsureSingleComponent<VerticalLayoutGroup>(
                content.gameObject, report);
            if (contentLayout != null)
            {
                Undo.RecordObject(contentLayout, "Configure game-over content layout");
                contentLayout.padding = new RectOffset(24, 24, 24, 24);
                contentLayout.spacing = 20f;
                contentLayout.childAlignment = TextAnchor.MiddleCenter;
                contentLayout.childControlWidth = true;
                contentLayout.childControlHeight = true;
                contentLayout.childForceExpandWidth = true;
                contentLayout.childForceExpandHeight = false;
            }

            RectTransform illustrationTransform = RepairOrCreateGameOverChild(
                canvasObject.transform, content, view, "illustrationImage", "Illustration", report);
            Image illustration = EnsureSingleComponent<Image>(
                illustrationTransform.gameObject, report);
            if (illustration != null)
            {
                Undo.RecordObject(illustration, "Configure ending illustration");
                illustration.raycastTarget = false;
            }

            RectTransform titleTransform = RepairOrCreateGameOverChild(
                canvasObject.transform, content, view, "titleText", "Title", report);
            TextMeshProUGUI title = EnsureSingleComponent<TextMeshProUGUI>(
                titleTransform.gameObject, report);
            ConfigureReadableText(title, font, 56f, 48f, 60f, true, true, 3f);

            RectTransform bodyTransform = RepairOrCreateGameOverChild(
                canvasObject.transform, content, view, "bodyText", "Body", report, "BODY");
            TextMeshProUGUI body = EnsureSingleComponent<TextMeshProUGUI>(
                bodyTransform.gameObject, report);
            ConfigureReadableText(body, font, 38f, 34f, 42f, true, true, 5f);

            RectTransform restartTransform = RepairOrCreateGameOverChild(
                canvasObject.transform, content, view, "restartButton", "RestartButton", report);
            Image restartImage = EnsureSingleComponent<Image>(restartTransform.gameObject, report);
            Button restart = EnsureSingleComponent<Button>(restartTransform.gameObject, report);
            if (restartImage != null)
            {
                Undo.RecordObject(restartImage, "Configure restart button");
                restartImage.color = ButtonColour;
                restartImage.raycastTarget = true;
            }

            TextMeshProUGUI restartText = EnsureButtonText(
                restartTransform, "Restart", report);
            ConfigureReadableText(restartText, font, 40f, 34f, 42f, true, true, 2f);
            restartText.text = interfaceText != null ? interfaceText.Restart : "Yeniden Başlat";
            EnsureExpectedListener(restart, view, nameof(GameOverView.HandleRestartButton),
                view != null ? view.HandleRestartButton : null, report);

            if (view != null)
            {
                SetObjectProperty(view, "panelRoot", panel.gameObject, report);
                SetObjectProperty(view, "titleText", title, report);
                SetObjectProperty(view, "bodyText", body, report);
                SetObjectProperty(view, "illustrationImage", illustration, report);
                SetObjectProperty(view, "restartButton", restart, report);
                SetObjectProperty(view, "restartButtonText", restartText, report);
                SetObjectProperty(view, "interfaceText", interfaceText, report);
                SetObjectProperty(view, "panelImage", panelImage, report);
            }

            ConfigureLayoutElement(illustrationTransform.gameObject, 260f, report);
            ConfigureLayoutElement(titleTransform.gameObject, 96f, report);
            ConfigureLayoutElement(bodyTransform.gameObject, 220f, report);
            ConfigureLayoutElement(restartTransform.gameObject, 112f, report);

            SetSiblingIndex(illustrationTransform, 0);
            SetSiblingIndex(titleTransform, 1);
            SetSiblingIndex(bodyTransform, 2);
            SetSiblingIndex(restartTransform, 3);
            SetSiblingIndex(restartText.transform, 0);
            SetSiblingIndex(content, 0);

            DeactivateLegacyGameOverChild(panel, "Illustration", illustrationTransform);
            DeactivateLegacyGameOverChild(panel, "Title", titleTransform);
            DeactivateLegacyGameOverChild(panel, "Body", bodyTransform);
            DeactivateLegacyGameOverChild(panel, "BODY", bodyTransform);
            DeactivateLegacyGameOverChild(panel, "RestartButton", restartTransform);

            if (panel.gameObject.activeSelf)
            {
                Undo.RecordObject(panel.gameObject, "Deactivate game-over panel");
                panel.gameObject.SetActive(false);
            }

            return new GameOverParts(panel, view);
        }

        private static void DeactivateLegacyGameOverChild(
            RectTransform panel,
            string childName,
            RectTransform activeReplacement)
        {
            Transform legacy = panel != null ? panel.Find(childName) : null;
            if (legacy == null || legacy == activeReplacement || !legacy.gameObject.activeSelf)
            {
                return;
            }

            Undo.RecordObject(legacy.gameObject, "Deactivate legacy game-over UI");
            legacy.gameObject.SetActive(false);
        }

        private static AudioService ConfigureAudio(Scene scene, SceneSetupReport report)
        {
            GameObject audioObject = EnsureRoot(scene, "AudioService", report);
            AudioSource source = EnsureSingleComponent<AudioSource>(audioObject, report);
            AudioService service = EnsureSingleComponent<AudioService>(audioObject, report);
            if (source != null)
            {
                Undo.RecordObject(source, "Configure audio source");
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 0f;
            }

            if (service != null)
            {
                SetObjectProperty(service, "audioSource", source, report);
            }

            return service;
        }

        // Bootstrap and menu ---------------------------------------------------------

        private static void ApplyBootstrapScene(Scene scene, SceneSetupReport report)
        {
            if (!CheckRootDuplicates(scene, "BootstrapController", report))
            {
                return;
            }

            GameObject root = EnsureRoot(scene, "BootstrapController", report);
            BootstrapController controller = EnsureSingleComponent<BootstrapController>(root, report);
            SetStringProperty(controller, "mainMenuSceneName", "MainMenu", report);
        }

        private static void ApplyMainMenuScene(
            Scene scene,
            SessionIntent intent,
            InterfaceTextDefinition interfaceText,
            TMP_FontAsset font,
            SceneSetupReport report)
        {
            if (!CheckRootDuplicates(scene, CanvasName, report)
                || !CheckRootDuplicates(scene, "MainMenuController", report))
            {
                return;
            }

            EnsureCamera(scene, report);
            EnsureEventSystem(scene, report);
            GameObject canvasObject = EnsureRoot(scene, CanvasName, report, true);
            ConfigureCanvas(canvasObject, report);
            RectTransform safeArea = EnsureUiChild(canvasObject.transform, "SafeArea", report);
            Stretch(safeArea);
            EnsureSingleComponent<SafeAreaFitter>(safeArea.gameObject, report);

            RectTransform panel = EnsureUiChild(safeArea, "MainMenuPanel", report);
            Stretch(panel);
            TextMeshProUGUI title = EnsureText(panel, "Title", new Vector2(0f, 280f),
                new Vector2(850f, 160f), 64f, report);
            ConfigureReadableText(title, font, 64f, 52f, 68f, true, true, 2f);
            title.text = interfaceText != null ? interfaceText.MainMenuTitle : "Royal Decisions";

            Button newGame = EnsureMenuButton(panel, "NewGameButton", "Yeni Oyun", 40f, report);
            Button continueButton = EnsureMenuButton(
                panel, "ContinueButton", "Devam Et", -120f, report);
            TextMeshProUGUI saveError = EnsureText(panel, "SaveError", new Vector2(0f, -300f),
                new Vector2(850f, 150f), 30f, report);
            TextMeshProUGUI newGameText = newGame != null
                ? newGame.GetComponentInChildren<TextMeshProUGUI>(true)
                : null;
            TextMeshProUGUI continueText = continueButton != null
                ? continueButton.GetComponentInChildren<TextMeshProUGUI>(true)
                : null;
            ConfigureReadableText(newGameText, font, 40f, 34f, 42f, true, true, 2f);
            ConfigureReadableText(continueText, font, 40f, 34f, 42f, true, true, 2f);
            ConfigureReadableText(saveError, font, 30f, 28f, 32f, true, true, 2f);
            saveError.text = string.Empty;
            saveError.gameObject.SetActive(false);
            if (interfaceText != null)
            {
                newGameText.text = interfaceText.NewGame;
                continueText.text = interfaceText.ContinueGame;
            }
            MainMenuTextView textView = EnsureSingleComponent<MainMenuTextView>(
                panel.gameObject, report);
            SetObjectProperty(textView, "interfaceText", interfaceText, report);
            SetObjectProperty(textView, "titleText", title, report);
            SetObjectProperty(textView, "newGameText", newGameText, report);
            SetObjectProperty(textView, "continueText", continueText, report);
            SetObjectProperty(textView, "saveErrorText", saveError, report);

            GameObject controllerObject = EnsureRoot(scene, "MainMenuController", report);
            MainMenuController controller = EnsureSingleComponent<MainMenuController>(
                controllerObject, report);
            SetStringProperty(controller, "gameSceneName", "Game", report);
            SetObjectProperty(controller, "sessionIntent", intent, report);
            SetObjectProperty(controller, "continueButton", continueButton, report);
            SetObjectProperty(controller, "interfaceText", interfaceText, report);
            SetObjectProperty(controller, "mainMenuTextView", textView, report);

            EnsureExpectedListener(newGame, controller, nameof(MainMenuController.OnNewGamePressed),
                controller != null ? controller.OnNewGamePressed : null, report);
            EnsureExpectedListener(continueButton, controller,
                nameof(MainMenuController.OnContinuePressed),
                controller != null ? controller.OnContinuePressed : null, report);
        }

        // Validation -----------------------------------------------------------------

        private static void ValidateGameScene(
            Scene scene,
            ContentCatalogue catalogue,
            SessionIntent intent,
            SceneSetupReport report)
        {
            InterfaceTextDefinition interfaceText =
                AssetDatabase.LoadAssetAtPath<InterfaceTextDefinition>(InterfaceTextPath);
            TMP_FontAsset turkishFont =
                AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TurkishFontPath);
            ValidateTurkishTextAssets(scene, turkishFont, report);

            GameObject canvas = RequirePath(scene, "/UICanvas", report);
            RequirePath(scene, "/Main Camera", report);
            GameObject eventSystem = RequirePath(scene, "/EventSystem", report);
            GameObject safeArea = RequirePath(scene, "/UICanvas/SafeArea", report);
            GameObject backgroundObject = RequirePath(scene, "/UICanvas/Background", report);
            GameObject hudObject = RequirePath(scene, "/UICanvas/SafeArea/HUD", report);
            GameObject cardArea = RequirePath(scene, "/UICanvas/SafeArea/CardArea", report);
            GameObject cardObject = RequirePath(scene, "/UICanvas/SafeArea/CardArea/Card", report);
            GameObject panel = RequirePath(scene, "/UICanvas/SafeArea/GameOverPanel", report);
            GameObject audioObject = RequirePath(scene, "/AudioService", report);
            GameObject controllerObject = RequirePath(scene, "/GameSceneController", report);
            GameObject footerObject = RequirePath(scene, "/UICanvas/SafeArea/Footer", report);

            if (canvas != null)
            {
                CanvasScaler scaler = RequireSingleComponent<CanvasScaler>(canvas, scene.path, report);
                Canvas canvasComponent = RequireSingleComponent<Canvas>(canvas, scene.path, report);
                RequireSingleComponent<GraphicRaycaster>(canvas, scene.path, report);
                if (canvasComponent != null
                    && (canvasComponent.renderMode != RenderMode.ScreenSpaceOverlay
                        || canvasComponent.pixelPerfect))
                {
                    AddInvalid(report, scene.path, "/UICanvas", "Canvas settings are incorrect.");
                }
                if (scaler != null
                    && (scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize
                        || scaler.referenceResolution != new Vector2(1080f, 1920f)
                        || !Mathf.Approximately(scaler.matchWidthOrHeight, 1f)))
                {
                    AddInvalid(report, scene.path, "/UICanvas", "CanvasScaler settings are incorrect.");
                }
            }

            if (eventSystem != null)
            {
                RequireSingleComponent<EventSystem>(eventSystem, scene.path, report);
                RequireSingleComponent<InputSystemUIInputModule>(eventSystem, scene.path, report);
            }

            if (safeArea != null)
            {
                RequireSingleComponent<SafeAreaFitter>(safeArea, scene.path, report);
            }

            if (backgroundObject != null)
            {
                RequireSingleComponent<BackgroundView>(backgroundObject, scene.path, report);
                ValidateNonRaycastImage(scene, "/UICanvas/Background", report);
                ValidateNonRaycastImage(scene, "/UICanvas/Background/Artwork", report);
                ValidateNonRaycastImage(scene, "/UICanvas/Background/DarkOverlay", report);
                ValidateNonRaycastImage(scene, "/UICanvas/Background/Vignette", report);
            }

            StatItemView[] statItems = new StatItemView[4];
            string[] itemNames =
            {
                "StatItem_People", "StatItem_Security", "StatItem_Authority", "StatItem_Wealth"
            };
            string[] slotNames =
            {
                "StatSlot_People", "StatSlot_Security", "StatSlot_Authority", "StatSlot_Wealth"
            };
            StatType[] stats =
            {
                StatType.People, StatType.Security, StatType.Authority, StatType.Wealth
            };
            Sprite uiSprite = LoadBuiltInUiSprite(report);
            for (int i = 0; i < itemNames.Length; i++)
            {
                string slotPath = "/UICanvas/SafeArea/HUD/" + slotNames[i];
                GameObject slotObject = RequirePath(scene, slotPath, report);
                string itemPath = slotPath + "/" + itemNames[i];
                GameObject itemObject = RequirePath(scene, itemPath, report);
                GameObject fillObject = RequirePath(scene, itemPath + "/Fill", report);
                GameObject iconObject = RequirePath(scene, slotPath + "/Icon", report);
                GameObject fallbackObject = RequirePath(scene, slotPath + "/IconFallback", report);
                GameObject nameObject = RequirePath(scene, slotPath + "/Name", report);
                GameObject valueObject = RequirePath(scene, slotPath + "/Value", report);
                GameObject impactObject = RequirePath(scene, slotPath + "/Impact", report);
                GameObject criticalObject = RequirePath(scene, slotPath + "/Critical", report);
                if (slotObject == null || itemObject == null || fillObject == null)
                {
                    continue;
                }

                StatItemView item = RequireSingleComponent<StatItemView>(
                    itemObject, scene.path, report);
                Image background = RequireSingleComponent<Image>(
                    itemObject, scene.path, report);
                Image fill = RequireSingleComponent<Image>(fillObject, scene.path, report);
                statItems[i] = item;
                if (item != null && item.Stat != stats[i])
                {
                    AddInvalid(report, scene.path, itemPath,
                        "Stat type is incorrect.");
                }
                if (item != null && GetObjectProperty(item, "fillImage") != fill)
                {
                    AddInvalid(report, scene.path, itemPath,
                        "Fill reference must point to this item's own child Fill Image.");
                }
                if (item != null && (GetObjectProperty(item, "iconImage")
                        != (iconObject != null ? iconObject.GetComponent<Image>() : null)
                    || GetObjectProperty(item, "iconFallbackLabel")
                        != (fallbackObject != null ? fallbackObject.GetComponent<TMP_Text>() : null)
                    || GetObjectProperty(item, "label")
                        != (nameObject != null ? nameObject.GetComponent<TMP_Text>() : null)
                    || GetObjectProperty(item, "valueText")
                        != (valueObject != null ? valueObject.GetComponent<TMP_Text>() : null)
                    || GetObjectProperty(item, "impactLabel")
                        != (impactObject != null ? impactObject.GetComponent<TMP_Text>() : null)
                    || GetObjectProperty(item, "criticalLabel")
                        != (criticalObject != null ? criticalObject.GetComponent<TMP_Text>() : null)))
                {
                    AddInvalid(report, scene.path, itemPath,
                        "Semantic stat references are incomplete or point outside their slot.");
                }
                if (slotObject.transform.GetSiblingIndex() != i)
                {
                    AddInvalid(report, scene.path, slotPath,
                        "HUD semantic visual order must be People, Security, Authority, Wealth.");
                }
                if (background != null && (background.sprite != uiSprite
                    || background.type != Image.Type.Simple
                    || background.raycastTarget
                    || !ColoursMatch(background.color, StatBackgroundColour)))
                {
                    AddInvalid(report, scene.path, itemPath,
                        "Stat background must use the built-in UISprite, Simple type, no raycast, "
                        + "and the managed background colour.");
                }
                if (fill == null)
                {
                    continue;
                }
                if (fill.sprite == null)
                {
                    AddInvalid(report, scene.path, itemPath + "/Fill",
                        "Fill Image sprite must not be null.");
                }
                else if (uiSprite != null && fill.sprite != uiSprite)
                {
                    AddInvalid(report, scene.path, itemPath + "/Fill",
                        "Fill Image must use Unity's built-in UISprite.");
                }
                if (fill.type != Image.Type.Filled)
                {
                    AddInvalid(report, scene.path, itemPath + "/Fill",
                        "Fill Image Type must be Filled.");
                }
                if (fill.fillMethod != Image.FillMethod.Horizontal)
                {
                    AddInvalid(report, scene.path, itemPath + "/Fill",
                        "Fill Image method must be Horizontal.");
                }
                if (fill.fillOrigin != (int)Image.OriginHorizontal.Left)
                {
                    AddInvalid(report, scene.path, itemPath + "/Fill",
                        "Fill Image origin must be Left.");
                }
                if (fill.preserveAspect)
                {
                    AddInvalid(report, scene.path, itemPath + "/Fill",
                        "Fill Image Preserve Aspect must be disabled.");
                }
                if (fill.raycastTarget)
                {
                    AddInvalid(report, scene.path, itemPath + "/Fill",
                        "Fill Image Raycast Target must be disabled.");
                }
                if (!ColoursMatch(fill.color, StatFillColours[i]))
                {
                    AddInvalid(report, scene.path, itemPath + "/Fill",
                        "Fill Image colour does not match its managed stat colour.");
                }
                RectTransform fillTransform = fill.transform as RectTransform;
                if (fillTransform == null || fillTransform.anchorMin != Vector2.zero
                    || fillTransform.anchorMax != Vector2.one
                    || fillTransform.offsetMin != Vector2.zero
                    || fillTransform.offsetMax != Vector2.zero)
                {
                    AddInvalid(report, scene.path, itemPath + "/Fill",
                        "Fill RectTransform must be fully stretched with zero offsets.");
                }
            }

            HUDView hud = hudObject != null
                ? RequireSingleComponent<HUDView>(hudObject, scene.path, report)
                : null;
            ValidateReference(hud, "interfaceText", interfaceText, scene.path,
                "/UICanvas/SafeArea/HUD", report);
            if (hud != null && !hud.TryValidate(out string hudMessage))
            {
                AddInvalid(report, scene.path, "/UICanvas/SafeArea/HUD", hudMessage);
            }

            CardView card = cardObject != null
                ? RequireSingleComponent<CardView>(cardObject, scene.path, report)
                : null;
            CardSwipeController swipe = cardObject != null
                ? RequireSingleComponent<CardSwipeController>(cardObject, scene.path, report)
                : null;
            Image cardImage = cardObject != null ? cardObject.GetComponent<Image>() : null;
            if (cardObject != null && (cardImage == null || !cardImage.raycastTarget))
            {
                AddInvalid(report, scene.path, "/UICanvas/SafeArea/CardArea/Card",
                    "Card needs a raycast-enabled Image.");
            }
            if (cardArea != null)
            {
                RequireSingleComponent<ResponsiveCardSizer>(cardArea, scene.path, report);
                RequirePath(scene, "/UICanvas/SafeArea/CardArea/NextCard", report);
            }
            RequirePath(scene, "/UICanvas/SafeArea/CardArea/Card/PortraitRegion/PortraitMask/Portrait", report);
            RequirePath(scene, "/UICanvas/SafeArea/CardArea/Card/Frame", report);
            ValidateTextColour(scene, "/UICanvas/SafeArea/CardArea/Card/Speaker",
                SpeakerTextColour, report);
            ValidateTextColour(scene, "/UICanvas/SafeArea/CardArea/Card/Body",
                BodyTextColour, report);
            if (swipe != null && (GetObjectProperty(swipe, "cardView") != card
                || GetObjectProperty(swipe, "dragParent")
                    != (cardArea != null ? cardArea.transform : null)))
            {
                AddInvalid(report, scene.path, "/UICanvas/SafeArea/CardArea/Card",
                    "CardSwipeController references are incorrect.");
            }

            ValidatePreview(scene, ChoiceSide.Left, report);
            ValidatePreview(scene, ChoiceSide.Right, report);

            GameOverView gameOver = panel != null
                ? RequireSingleComponent<GameOverView>(panel, scene.path, report)
                : null;
            ValidateReference(gameOver, "interfaceText", interfaceText, scene.path,
                "/UICanvas/SafeArea/GameOverPanel", report);
            GameObject restartObject = RequirePath(
                scene, "/UICanvas/SafeArea/GameOverPanel/Content/RestartButton", report);
            Button restart = restartObject != null ? restartObject.GetComponent<Button>() : null;
            RequirePath(scene, "/UICanvas/SafeArea/GameOverPanel/Content/Illustration", report);
            RequirePath(scene, "/UICanvas/SafeArea/GameOverPanel/Content/Title", report);
            RequirePath(scene, "/UICanvas/SafeArea/GameOverPanel/Content/Body", report);
            RequirePath(scene, "/UICanvas/SafeArea/GameOverPanel/Content/RestartButton/Text (TMP)", report);
            if (panel != null && panel.activeSelf)
            {
                AddInvalid(report, scene.path, "/UICanvas/SafeArea/GameOverPanel",
                    "GameOverPanel must start inactive.");
            }
            ValidateExpectedListener(restart, gameOver,
                nameof(GameOverView.HandleRestartButton), scene.path,
                "/UICanvas/SafeArea/GameOverPanel/Content/RestartButton", report);

            FooterView footer = footerObject != null
                ? RequireSingleComponent<FooterView>(footerObject, scene.path, report)
                : null;
            RunStatusView runStatus = footerObject != null
                ? RequireSingleComponent<RunStatusView>(footerObject, scene.path, report)
                : null;
            ValidateReference(footer, "interfaceText", interfaceText, scene.path,
                "/UICanvas/SafeArea/Footer", report);
            ValidateReference(runStatus, "interfaceText", interfaceText, scene.path,
                "/UICanvas/SafeArea/Footer", report);
            RequirePath(scene, "/UICanvas/SafeArea/Footer/Reign", report);
            RequirePath(scene, "/UICanvas/SafeArea/Footer/Ruler", report);
            RequirePath(scene, "/UICanvas/SafeArea/Footer/Progress", report);
            RequirePath(scene, "/UICanvas/SafeArea/Footer/Seal", report);

            AudioService audio = audioObject != null
                ? RequireSingleComponent<AudioService>(audioObject, scene.path, report)
                : null;
            AudioSource source = audioObject != null
                ? RequireSingleComponent<AudioSource>(audioObject, scene.path, report)
                : null;
            if (source != null && (source.playOnAwake || source.loop
                || !Mathf.Approximately(source.spatialBlend, 0f)))
            {
                AddInvalid(report, scene.path, "/AudioService", "AudioSource settings are incorrect.");
            }
            if (audio != null && GetObjectProperty(audio, "audioSource") != source)
            {
                AddInvalid(report, scene.path, "/AudioService", "AudioSource reference is incorrect.");
            }

            GameSceneController controller = controllerObject != null
                ? RequireSingleComponent<GameSceneController>(controllerObject, scene.path, report)
                : null;
            ValidateReference(controller, "catalogue", catalogue, scene.path,
                "/GameSceneController", report);
            ValidateReference(controller, "cardView", card, scene.path,
                "/GameSceneController", report);
            ValidateReference(controller, "hudView", hud, scene.path,
                "/GameSceneController", report);
            ValidateReference(controller, "gameOverView", gameOver, scene.path,
                "/GameSceneController", report);
            ValidateReference(controller, "swipeController", swipe, scene.path,
                "/GameSceneController", report);
            ValidateReference(controller, "audioService", audio, scene.path,
                "/GameSceneController", report);
            ValidateReference(controller, "sessionIntent", intent, scene.path,
                "/GameSceneController", report);
            ValidateReference(controller, "runStatusView", runStatus, scene.path,
                "/GameSceneController", report);
            ValidateReference(controller, "footerView", footer, scene.path,
                "/GameSceneController", report);

            if (canvas != null)
            {
                GameUIThemeController themeController = RequireSingleComponent<GameUIThemeController>(
                    canvas, scene.path, report);
                GameUITheme assignedTheme = GetObjectProperty(themeController, "theme") as GameUITheme;
                if (assignedTheme == null)
                {
                    AddInvalid(report, scene.path, "/UICanvas",
                        "GameUIThemeController needs a serialized GameUITheme.");
                }
                else if (!UIContrastMath.MeetsNormalText(
                    assignedTheme.PrimaryText, assignedTheme.CardSurface))
                {
                    AddInvalid(report, scene.path, "/UICanvas",
                        "Assigned GameUITheme does not meet normal-text contrast on the card.");
                }
                ValidateReference(themeController, "hudView", hud, scene.path,
                    "/UICanvas", report);
                ValidateReference(themeController, "cardView", card, scene.path,
                    "/UICanvas", report);
                ValidateReference(themeController, "footerView", footer, scene.path,
                    "/UICanvas", report);
            }
        }

        private static void ValidatePreview(Scene scene, ChoiceSide side, SceneSetupReport report)
        {
            string name = side == ChoiceSide.Left ? "PreviewLeft" : "PreviewRight";
            string path = "/UICanvas/SafeArea/CardArea/Card/" + name;
            GameObject previewObject = RequirePath(scene, path, report);
            GameObject labelObject = RequirePath(scene, path + "/Label", report);
            GameObject edgeObject = RequirePath(scene, path + "/EdgeHighlight", report);
            GameObject markerObject = RequirePath(scene, path + "/CommitMarker", report);
            if (previewObject == null || labelObject == null
                || edgeObject == null || markerObject == null)
            {
                return;
            }

            ChoicePreviewView view = RequireSingleComponent<ChoicePreviewView>(
                previewObject, scene.path, report);
            CanvasGroup group = RequireSingleComponent<CanvasGroup>(
                previewObject, scene.path, report);
            TMP_Text label = RequireSingleComponent<TextMeshProUGUI>(
                labelObject, scene.path, report);
            Image edge = RequireSingleComponent<Image>(edgeObject, scene.path, report);
            CanvasGroup marker = RequireSingleComponent<CanvasGroup>(
                markerObject, scene.path, report);
            Image legacyOverlay = previewObject.GetComponent<Image>();
            if (view != null && (view.Side != side
                || GetObjectProperty(view, "label") != label
                || GetObjectProperty(view, "canvasGroup") != group
                || GetObjectProperty(view, "edgeHighlight") != edge
                || GetObjectProperty(view, "commitMarker") != marker))
            {
                AddInvalid(report, scene.path, path, "Choice preview references are incorrect.");
            }
            if (edge != null && edge.raycastTarget)
            {
                AddInvalid(report, scene.path, path + "/EdgeHighlight",
                    "Choice edge highlight must not block raycasts.");
            }
            if (legacyOverlay != null && legacyOverlay.enabled)
            {
                AddInvalid(report, scene.path, path,
                    "Legacy full-area choice overlay must be disabled.");
            }
        }

        private static void ValidateNonRaycastImage(
            Scene scene,
            string path,
            SceneSetupReport report)
        {
            GameObject imageObject = RequirePath(scene, path, report);
            if (imageObject == null)
            {
                return;
            }

            Image image = RequireSingleComponent<Image>(imageObject, scene.path, report);
            if (image != null && image.raycastTarget)
            {
                AddInvalid(report, scene.path, path, "Background Image must not block raycasts.");
            }
        }

        private static void ValidateTheme(GameUITheme theme, SceneSetupReport report)
        {
            if (!ColoursMatch(theme.OverallBackground, OverallBackgroundColour)
                || !ColoursMatch(theme.UISurface, SurfaceColour)
                || !ColoursMatch(theme.CardSurface, CardSurfaceColour)
                || !ColoursMatch(theme.BorderGold, BorderGoldColour)
                || !ColoursMatch(theme.EmptyBar, StatBackgroundColour)
                || !ColoursMatch(theme.GetStatColor(StatType.People), StatFillColours[0])
                || !ColoursMatch(theme.GetStatColor(StatType.Security), StatFillColours[1])
                || !ColoursMatch(theme.GetStatColor(StatType.Authority), StatFillColours[2])
                || !ColoursMatch(theme.GetStatColor(StatType.Wealth), StatFillColours[3]))
            {
                AddInvalid(report, DefaultThemePath, string.Empty,
                    "Default GameUITheme palette differs from the managed neutral baseline.");
            }

            if (!UIContrastMath.MeetsNormalText(theme.PrimaryText, theme.CardSurface)
                || !UIContrastMath.MeetsNormalText(theme.SecondaryText, theme.UISurface)
                || !UIContrastMath.MeetsNormalText(theme.HighlightGold, theme.CardSurface))
            {
                AddInvalid(report, DefaultThemePath, string.Empty,
                    "Default GameUITheme normal-text contrast must be at least 4.5:1.");
            }
        }

        private static void ValidateTurkishTextAssets(
            Scene scene,
            TMP_FontAsset expectedFont,
            SceneSetupReport report)
        {
            if (!TurkishGlyphValidator.TryValidate(expectedFont, out string fontMessage))
            {
                AddInvalid(report, scene.path, string.Empty,
                    "The project-owned Turkish TMP font is invalid: " + fontMessage);
                return;
            }

            TextMeshProUGUI[] textObjects = FindComponentsInScene<TextMeshProUGUI>(scene);
            for (int i = 0; i < textObjects.Length; i++)
            {
                TextMeshProUGUI text = textObjects[i];
                TMP_FontAsset resolvedFont = text.font != null
                    ? text.font
                    : TMP_Settings.defaultFontAsset;
                if (resolvedFont == null
                    || !resolvedFont.HasCharacters(
                        TurkishGlyphValidator.RequiredTurkishCharacters,
                        out _,
                        true,
                        false))
                {
                    AddInvalid(report, scene.path, HierarchyPath(text.transform),
                        "The resolved TMP font must cover all required Turkish glyphs.");
                }
            }
        }

        private static void ValidateTextColour(
            Scene scene,
            string path,
            Color expected,
            SceneSetupReport report)
        {
            GameObject textObject = RequirePath(scene, path, report);
            if (textObject == null)
            {
                return;
            }

            TextMeshProUGUI text = RequireSingleComponent<TextMeshProUGUI>(
                textObject, scene.path, report);
            if (text != null && !ColoursMatch(text.color, expected))
            {
                AddInvalid(report, scene.path, path, "TMP text colour is incorrect.");
            }
        }

        private static void ValidateBootstrapScene(Scene scene, SceneSetupReport report)
        {
            GameObject root = RequirePath(scene, "/BootstrapController", report);
            BootstrapController controller = root != null
                ? RequireSingleComponent<BootstrapController>(root, scene.path, report)
                : null;
            if (controller != null && GetStringProperty(controller, "mainMenuSceneName") != "MainMenu")
            {
                AddInvalid(report, scene.path, "/BootstrapController",
                    "Main menu scene name must be MainMenu.");
            }
        }

        private static void ValidateMainMenuScene(
            Scene scene,
            SessionIntent intent,
            SceneSetupReport report)
        {
            InterfaceTextDefinition interfaceText =
                AssetDatabase.LoadAssetAtPath<InterfaceTextDefinition>(InterfaceTextPath);
            TMP_FontAsset turkishFont =
                AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TurkishFontPath);
            RequirePath(scene, "/Main Camera", report);
            GameObject eventObject = RequirePath(scene, "/EventSystem", report);
            RequirePath(scene, "/UICanvas/SafeArea/MainMenuPanel/Title", report);
            GameObject newObject = RequirePath(
                scene, "/UICanvas/SafeArea/MainMenuPanel/NewGameButton", report);
            GameObject continueObject = RequirePath(
                scene, "/UICanvas/SafeArea/MainMenuPanel/ContinueButton", report);
            GameObject controllerObject = RequirePath(scene, "/MainMenuController", report);
            GameObject panelObject = RequirePath(
                scene, "/UICanvas/SafeArea/MainMenuPanel", report);

            // The Game UI foundation deliberately leaves the existing MainMenu scene untouched.
            // Validate the Phase-F localization wiring only when that separately managed migration
            // has begun; a legacy menu without either marker remains a supported baseline here.
            bool hasLocalizedMenu = panelObject != null
                && (panelObject.GetComponent<MainMenuTextView>() != null
                    || panelObject.transform.Find("SaveError") != null);
            if (hasLocalizedMenu)
            {
                ValidateTurkishTextAssets(scene, turkishFont, report);
                RequirePath(scene, "/UICanvas/SafeArea/MainMenuPanel/SaveError", report);
            }

            if (eventObject != null)
            {
                RequireSingleComponent<InputSystemUIInputModule>(eventObject, scene.path, report);
            }

            MainMenuController controller = controllerObject != null
                ? RequireSingleComponent<MainMenuController>(controllerObject, scene.path, report)
                : null;
            Button newButton = newObject != null ? newObject.GetComponent<Button>() : null;
            Button continueButton = continueObject != null ? continueObject.GetComponent<Button>() : null;
            ValidateReference(controller, "sessionIntent", intent, scene.path,
                "/MainMenuController", report);
            ValidateReference(controller, "continueButton", continueButton, scene.path,
                "/MainMenuController", report);
            if (hasLocalizedMenu)
            {
                ValidateReference(controller, "interfaceText", interfaceText, scene.path,
                    "/MainMenuController", report);
                MainMenuTextView textView = RequireSingleComponent<MainMenuTextView>(
                    panelObject, scene.path, report);
                ValidateReference(textView, "interfaceText", interfaceText, scene.path,
                    "/UICanvas/SafeArea/MainMenuPanel", report);
                ValidateReference(controller, "mainMenuTextView", textView, scene.path,
                    "/MainMenuController", report);
            }
            if (controller != null && GetStringProperty(controller, "gameSceneName") != "Game")
            {
                AddInvalid(report, scene.path, "/MainMenuController",
                    "Game scene name must be Game.");
            }
            ValidateExpectedListener(newButton, controller,
                nameof(MainMenuController.OnNewGamePressed), scene.path,
                "/UICanvas/SafeArea/MainMenuPanel/NewGameButton", report);
            ValidateExpectedListener(continueButton, controller,
                nameof(MainMenuController.OnContinuePressed), scene.path,
                "/UICanvas/SafeArea/MainMenuPanel/ContinueButton", report);
        }

        private static void ValidateBuildScenes(SceneSetupReport report)
        {
            string[] expected = { BootstrapScenePath, MainMenuScenePath, GameScenePath };
            EditorBuildSettingsScene[] actual = EditorBuildSettings.scenes;
            if (actual.Length != expected.Length)
            {
                report.Add(SceneSetupIssueSeverity.Error, "BUILD_SCENES", "Build",
                    "ProjectSettings/EditorBuildSettings.asset", string.Empty,
                    "Build scene list must contain exactly Bootstrap, MainMenu, and Game.");
                return;
            }

            for (int i = 0; i < expected.Length; i++)
            {
                if (!actual[i].enabled || actual[i].path != expected[i])
                {
                    report.Add(SceneSetupIssueSeverity.Error, "BUILD_SCENE_ORDER", "Build",
                        "ProjectSettings/EditorBuildSettings.asset", string.Empty,
                        "Build scene index " + i + " must be " + expected[i] + " and enabled.");
                }
            }
        }

        // Authoring primitives -------------------------------------------------------

        private static void EnsureCamera(Scene scene, SceneSetupReport report)
        {
            GameObject cameraObject = EnsureRoot(scene, "Main Camera", report);
            Camera camera = EnsureSingleComponent<Camera>(cameraObject, report);
            EnsureSingleComponent<AudioListener>(cameraObject, report);
            if (camera != null)
            {
                Undo.RecordObject(camera, "Configure camera");
                camera.orthographic = true;
                camera.clearFlags = CameraClearFlags.SolidColor;
            }
            if (cameraObject != null)
            {
                Undo.RecordObject(cameraObject, "Configure camera tag");
                cameraObject.tag = "MainCamera";
            }
        }

        private static void EnsureEventSystem(Scene scene, SceneSetupReport report)
        {
            GameObject eventObject = EnsureRoot(scene, "EventSystem", report);
            EnsureSingleComponent<EventSystem>(eventObject, report);
            InputSystemUIInputModule module = EnsureSingleComponent<InputSystemUIInputModule>(
                eventObject, report);
            StandaloneInputModule legacy = eventObject != null
                ? eventObject.GetComponent<StandaloneInputModule>()
                : null;
            if (legacy != null)
            {
                report.Add(SceneSetupIssueSeverity.Error, "LEGACY_INPUT_MODULE", "Components",
                    scene.path, "/EventSystem",
                    "StandaloneInputModule is ambiguous and will not be deleted automatically.");
            }
            if (module != null && module.actionsAsset == null)
            {
                Undo.RecordObject(module, "Assign default UI actions");
                module.AssignDefaultActions();
            }
        }

        private static void ConfigureCanvas(GameObject canvasObject, SceneSetupReport report)
        {
            if (canvasObject == null)
            {
                return;
            }
            Canvas canvas = EnsureSingleComponent<Canvas>(canvasObject, report);
            CanvasScaler scaler = EnsureSingleComponent<CanvasScaler>(canvasObject, report);
            EnsureSingleComponent<GraphicRaycaster>(canvasObject, report);
            if (canvas != null)
            {
                Undo.RecordObject(canvas, "Configure Canvas");
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.pixelPerfect = false;
            }
            if (scaler != null)
            {
                Undo.RecordObject(scaler, "Configure CanvasScaler");
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1080f, 1920f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 1f;
            }
        }

        private static Button EnsureMenuButton(
            RectTransform parent,
            string name,
            string label,
            float y,
            SceneSetupReport report)
        {
            RectTransform transform = EnsureUiChild(parent, name, report);
            SetRect(transform, Center, Center, new Vector2(0f, y),
                new Vector2(600f, 120f), Center);
            Image image = EnsureSingleComponent<Image>(transform.gameObject, report);
            Button button = EnsureSingleComponent<Button>(transform.gameObject, report);
            if (image != null)
            {
                Undo.RecordObject(image, "Configure menu button");
                image.color = ButtonColour;
                image.raycastTarget = true;
            }
            EnsureButtonText(transform, label, report);
            return button;
        }

        private static TextMeshProUGUI EnsureButtonText(
            RectTransform parent,
            string text,
            SceneSetupReport report)
        {
            RectTransform textTransform = EnsureUiChild(parent, "Text (TMP)", report);
            Stretch(textTransform);
            TextMeshProUGUI label = EnsureSingleComponent<TextMeshProUGUI>(
                textTransform.gameObject, report);
            ConfigureText(label, 40f);
            if (label != null && (string.IsNullOrEmpty(label.text)
                || label.text == "New Text" || label.text == "Button"))
            {
                Undo.RecordObject(label, "Set button text");
                label.text = text;
            }
            return label;
        }

        private static TextMeshProUGUI EnsureText(
            RectTransform parent,
            string name,
            Vector2 position,
            Vector2 size,
            float fontSize,
            SceneSetupReport report)
        {
            RectTransform transform = EnsureUiChild(parent, name, report);
            SetRect(transform, Center, Center, position, size, Center);
            TextMeshProUGUI text = EnsureSingleComponent<TextMeshProUGUI>(
                transform.gameObject, report);
            ConfigureText(text, fontSize);
            return text;
        }

        private static void ConfigureText(TextMeshProUGUI text, float fontSize)
        {
            if (text == null)
            {
                return;
            }
            Undo.RecordObject(text, "Configure TMP text");
            text.fontSize = fontSize;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            if (text.font == null && TMP_Settings.defaultFontAsset != null)
            {
                text.font = TMP_Settings.defaultFontAsset;
            }
        }

        private static void ConfigureReadableText(
            TextMeshProUGUI text,
            TMP_FontAsset font,
            float fontSize,
            float minimum,
            float maximum,
            bool autoSize,
            bool wrap,
            float lineSpacing)
        {
            if (text == null)
            {
                return;
            }

            ConfigureText(text, fontSize);
            Undo.RecordObject(text, "Configure readable TMP text");
            if (font != null)
            {
                text.font = font;
            }
            text.enableAutoSizing = autoSize;
            text.fontSizeMin = minimum;
            text.fontSizeMax = maximum;
            text.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.lineSpacing = lineSpacing;
        }

        private static void SetTextColour(TextMeshProUGUI text, Color colour)
        {
            if (text == null || ColoursMatch(text.color, colour))
            {
                return;
            }

            Undo.RecordObject(text, "Configure TMP text colour");
            text.color = colour;
        }

        private static bool ColoursMatch(Color left, Color right)
        {
            return Mathf.Approximately(left.r, right.r)
                && Mathf.Approximately(left.g, right.g)
                && Mathf.Approximately(left.b, right.b)
                && Mathf.Approximately(left.a, right.a);
        }

        private static RectTransform RepairOrCreateGameOverChild(
            Transform canvas,
            RectTransform panel,
            GameOverView view,
            string referenceProperty,
            string expectedName,
            SceneSetupReport report,
            string legacyName = null)
        {
            RectTransform existing = FindDirectChild(panel, expectedName, report);
            if (existing != null)
            {
                return existing;
            }

            GameObject referenced = GetReferencedGameObject(view, referenceProperty);
            if (referenced != null && referenced.transform.parent == canvas
                && (referenced.name == expectedName || referenced.name == legacyName))
            {
                Undo.SetTransformParent(referenced.transform, panel, "Repair game-over hierarchy");
                Undo.RecordObject(referenced, "Repair game-over name");
                referenced.name = expectedName;
                return referenced.transform as RectTransform;
            }

            RectTransform legacy = FindDirectChild(canvas, expectedName, report);
            if (legacy == null && !string.IsNullOrEmpty(legacyName))
            {
                legacy = FindDirectChild(canvas, legacyName, report);
            }
            if (legacy != null)
            {
                Undo.SetTransformParent(legacy, panel, "Repair game-over hierarchy");
                Undo.RecordObject(legacy.gameObject, "Repair game-over name");
                legacy.gameObject.name = expectedName;
                return legacy;
            }

            return EnsureUiChild(panel, expectedName, report);
        }

        private static GameObject GetReferencedGameObject(Object target, string propertyName)
        {
            Object referenced = GetObjectProperty(target, propertyName);
            if (referenced is Component component)
            {
                return component.gameObject;
            }
            return referenced as GameObject;
        }

        private static GameObject EnsureRoot(
            Scene scene,
            string name,
            SceneSetupReport report,
            bool rectTransform = false)
        {
            GameObject existing = FindUniqueRoot(scene, name, report);
            if (existing != null)
            {
                return existing;
            }

            GameObject created = rectTransform
                ? new GameObject(name, typeof(RectTransform))
                : new GameObject(name);
            Undo.RegisterCreatedObjectUndo(created, "Create " + name);
            SceneManager.MoveGameObjectToScene(created, scene);
            return created;
        }

        private static RectTransform EnsureUiChild(
            Transform parent,
            string name,
            SceneSetupReport report)
        {
            RectTransform existing = FindDirectChild(parent, name, report);
            if (existing != null)
            {
                return existing;
            }
            GameObject created = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(created, "Create " + name);
            created.transform.SetParent(parent, false);
            return (RectTransform)created.transform;
        }

        private static RectTransform FindDirectChild(
            Transform parent,
            string name,
            SceneSetupReport report)
        {
            if (parent == null)
            {
                return null;
            }
            RectTransform found = null;
            int count = 0;
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name != name)
                {
                    continue;
                }
                count++;
                found = child as RectTransform;
            }
            if (count > 1)
            {
                report.Add(SceneSetupIssueSeverity.Error, "DUPLICATE_PATH", "Hierarchy",
                    parent.gameObject.scene.path, HierarchyPath(parent) + "/" + name,
                    "Multiple direct children occupy this managed path.");
            }
            return count == 1 ? found : null;
        }

        private static T EnsureSingleComponent<T>(GameObject gameObject, SceneSetupReport report)
            where T : Component
        {
            if (gameObject == null)
            {
                return null;
            }
            T[] components = gameObject.GetComponents<T>();
            if (components.Length > 1)
            {
                report.Add(SceneSetupIssueSeverity.Error, "DUPLICATE_COMPONENT", "Components",
                    gameObject.scene.path, HierarchyPath(gameObject.transform),
                    "Multiple " + typeof(T).Name + " components exist; none were deleted.");
                return components[0];
            }
            return components.Length == 1 ? components[0] : Undo.AddComponent<T>(gameObject);
        }

        private static T RequireSingleComponent<T>(
            GameObject gameObject,
            string scenePath,
            SceneSetupReport report) where T : Component
        {
            T[] components = gameObject.GetComponents<T>();
            if (components.Length != 1)
            {
                report.Add(SceneSetupIssueSeverity.Error, "COMPONENT_COUNT", "Components",
                    scenePath, HierarchyPath(gameObject.transform),
                    "Expected exactly one " + typeof(T).Name + "; found " + components.Length + ".");
                return components.Length > 0 ? components[0] : null;
            }
            return components[0];
        }

        private static void SetRect(
            RectTransform transform,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 position,
            Vector2 size,
            Vector2 pivot)
        {
            if (transform == null)
            {
                return;
            }
            Undo.RecordObject(transform, "Configure RectTransform");
            transform.anchorMin = anchorMin;
            transform.anchorMax = anchorMax;
            transform.anchoredPosition = position;
            transform.sizeDelta = size;
            transform.pivot = pivot;
            transform.localScale = Vector3.one;
        }

        private static void Stretch(RectTransform transform)
        {
            SetRect(transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, Center);
        }

        private static void SetSiblingIndex(Transform transform, int index)
        {
            if (transform == null || transform.GetSiblingIndex() == index)
            {
                return;
            }
            Undo.RecordObject(transform, "Set managed sibling order");
            transform.SetSiblingIndex(index);
        }

        private static void SetObjectProperty(
            Object target,
            string propertyName,
            Object value,
            SceneSetupReport report)
        {
            SerializedProperty property = FindProperty(target, propertyName, report);
            if (property == null || property.objectReferenceValue == value)
            {
                return;
            }
            Undo.RecordObject(target, "Wire " + propertyName);
            property.objectReferenceValue = value;
            property.serializedObject.ApplyModifiedProperties();
        }

        private static void SetObjectArrayProperty<T>(
            Object target,
            string propertyName,
            T[] values,
            SceneSetupReport report) where T : Object
        {
            SerializedProperty property = FindProperty(target, propertyName, report);
            if (property == null || !property.isArray)
            {
                return;
            }
            Undo.RecordObject(target, "Wire " + propertyName);
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
            property.serializedObject.ApplyModifiedProperties();
        }

        private static void SetEnumProperty(
            Object target,
            string propertyName,
            int value,
            SceneSetupReport report)
        {
            SerializedProperty property = FindProperty(target, propertyName, report);
            if (property == null || property.intValue == value)
            {
                return;
            }
            Undo.RecordObject(target, "Set " + propertyName);
            property.intValue = value;
            property.serializedObject.ApplyModifiedProperties();
        }

        private static void SetStringProperty(
            Object target,
            string propertyName,
            string value,
            SceneSetupReport report)
        {
            SerializedProperty property = FindProperty(target, propertyName, report);
            if (property == null || property.stringValue == value)
            {
                return;
            }
            Undo.RecordObject(target, "Set " + propertyName);
            property.stringValue = value;
            property.serializedObject.ApplyModifiedProperties();
        }

        private static SerializedProperty FindProperty(
            Object target,
            string propertyName,
            SceneSetupReport report)
        {
            if (target == null)
            {
                report.Add(SceneSetupIssueSeverity.Error, "NULL_SERIALIZED_TARGET", "References",
                    string.Empty, string.Empty,
                    "Cannot assign " + propertyName + " because its component is missing.");
                return null;
            }
            SerializedObject serialized = new SerializedObject(target);
            serialized.Update();
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                report.Add(SceneSetupIssueSeverity.Error, "SERIALIZED_PROPERTY_MISSING", "References",
                    AssetDatabase.GetAssetPath(target),
                    target is Component component ? HierarchyPath(component.transform) : target.name,
                    target.GetType().Name + "." + propertyName + " was not found.");
            }
            return property;
        }

        private static Object GetObjectProperty(Object target, string propertyName)
        {
            if (target == null)
            {
                return null;
            }
            SerializedProperty property = new SerializedObject(target).FindProperty(propertyName);
            return property != null ? property.objectReferenceValue : null;
        }

        private static string GetStringProperty(Object target, string propertyName)
        {
            if (target == null)
            {
                return string.Empty;
            }
            SerializedProperty property = new SerializedObject(target).FindProperty(propertyName);
            return property != null ? property.stringValue : string.Empty;
        }

        private static void EnsureExpectedListener(
            Button button,
            Object target,
            string method,
            UnityEngine.Events.UnityAction action,
            SceneSetupReport report)
        {
            if (button == null || target == null || action == null)
            {
                report.Add(SceneSetupIssueSeverity.Error, "BUTTON_WIRING_TARGET", "Events",
                    button != null ? button.gameObject.scene.path : string.Empty,
                    button != null ? HierarchyPath(button.transform) : string.Empty,
                    "Button or expected listener target is missing.");
                return;
            }
            int expectedCount = 0;
            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
            {
                Object listenerTarget = button.onClick.GetPersistentTarget(i);
                string listenerMethod = button.onClick.GetPersistentMethodName(i);
                if (listenerTarget == target && listenerMethod == method)
                {
                    expectedCount++;
                    continue;
                }
                report.Add(SceneSetupIssueSeverity.Error, "UNEXPECTED_BUTTON_LISTENER", "Events",
                    button.gameObject.scene.path, HierarchyPath(button.transform),
                    "An unexpected persistent listener was preserved; reconcile it manually.");
            }
            if (report.ErrorCount > 0 && expectedCount == 0
                && button.onClick.GetPersistentEventCount() > 0)
            {
                return;
            }
            for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0 && expectedCount > 1; i--)
            {
                if (button.onClick.GetPersistentTarget(i) == target
                    && button.onClick.GetPersistentMethodName(i) == method)
                {
                    UnityEventTools.RemovePersistentListener(button.onClick, i);
                    expectedCount--;
                }
            }
            if (expectedCount == 0)
            {
                Undo.RecordObject(button, "Wire button listener");
                UnityEventTools.AddPersistentListener(button.onClick, action);
                EditorUtility.SetDirty(button);
            }
        }

        private static void ValidateExpectedListener(
            Button button,
            Object target,
            string method,
            string scenePath,
            string hierarchyPath,
            SceneSetupReport report)
        {
            if (button == null || target == null)
            {
                AddInvalid(report, scenePath, hierarchyPath, "Button or listener target is missing.");
                return;
            }
            int expected = 0;
            int unexpected = 0;
            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
            {
                if (button.onClick.GetPersistentTarget(i) == target
                    && button.onClick.GetPersistentMethodName(i) == method)
                {
                    expected++;
                }
                else
                {
                    unexpected++;
                }
            }
            if (expected != 1 || unexpected != 0)
            {
                AddInvalid(report, scenePath, hierarchyPath,
                    "Expected exactly one " + method + " listener and no unexpected listeners.");
            }
        }

        // Project paths, backup, and reporting ---------------------------------------

        private static GameUITheme EnsureDefaultTheme(SceneSetupReport report)
        {
            Object existing = AssetDatabase.LoadMainAssetAtPath(DefaultThemePath);
            if (existing != null && !(existing is GameUITheme))
            {
                report.Add(SceneSetupIssueSeverity.Error, "ASSET_TYPE_CONFLICT", "Assets",
                    DefaultThemePath, string.Empty,
                    "The default UI theme path is occupied by " + existing.GetType().Name + ".");
                return null;
            }

            if (existing is GameUITheme theme)
            {
                return theme;
            }

            EnsureAssetFolder("Assets/_Game/Content/UI");
            GameUITheme created = ScriptableObject.CreateInstance<GameUITheme>();
            AssetDatabase.CreateAsset(created, DefaultThemePath);
            return created;
        }

        private static void EnsureAssetFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }

        private static SessionIntent EnsureSessionIntent(SceneSetupReport report)
        {
            Object existing = AssetDatabase.LoadMainAssetAtPath(SessionIntentPath);
            if (existing != null && !(existing is SessionIntent))
            {
                report.Add(SceneSetupIssueSeverity.Error, "ASSET_TYPE_CONFLICT", "Assets",
                    SessionIntentPath, string.Empty,
                    "The SessionIntent path is occupied by " + existing.GetType().Name + ".");
                return null;
            }
            if (existing is SessionIntent intent)
            {
                return intent;
            }
            SessionIntent created = ScriptableObject.CreateInstance<SessionIntent>();
            AssetDatabase.CreateAsset(created, SessionIntentPath);
            return created;
        }

        private static Scene OpenRequiredScene(string path, SceneSetupReport report)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
            {
                report.Add(SceneSetupIssueSeverity.Error, "SCENE_MISSING", "Scenes",
                    path, string.Empty, "Required existing scene is missing.");
                return default;
            }
            return EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        }

        private static Scene OpenOrCreateEmptyScene(string path)
        {
            return AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null
                ? EditorSceneManager.OpenScene(path, OpenSceneMode.Single)
                : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        private static void ApplyBuildScenes()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(BootstrapScenePath, true),
                new EditorBuildSettingsScene(MainMenuScenePath, true),
                new EditorBuildSettingsScene(GameScenePath, true)
            };
        }

        private static BackupManifest CreateBackup(SceneSetupReport report)
        {
            string backupRoot = BackupAbsolutePath;
            if (Directory.Exists(backupRoot))
            {
                FileUtil.DeleteFileOrDirectory(backupRoot);
            }
            Directory.CreateDirectory(backupRoot);
            BackupManifest manifest = new BackupManifest
            {
                buildScenes = BuildSceneRecords(EditorBuildSettings.scenes)
            };
            string[] managedAssets =
            {
                GameScenePath, BootstrapScenePath, MainMenuScenePath, SessionIntentPath,
                DefaultThemePath
            };
            for (int i = 0; i < managedAssets.Length; i++)
            {
                string path = managedAssets[i];
                string absolute = AbsoluteProjectPath(path);
                if (File.Exists(absolute))
                {
                    string backup = Path.Combine(backupRoot, Path.GetFileName(path));
                    FileUtil.CopyFileOrDirectory(absolute, backup);
                    manifest.backups.Add(new BackupFileRecord { assetPath = path, backupPath = backup });
                }
                else
                {
                    manifest.createdAssetPaths.Add(path);
                }
            }
            File.WriteAllText(Path.Combine(backupRoot, BackupManifestName),
                JsonUtility.ToJson(manifest, true));
            report.Add(SceneSetupIssueSeverity.Info, "BACKUP_CREATED", "Rollback",
                backupRoot, string.Empty, "Pre-apply backup created.");
            return manifest;
        }

        private static void RestoreBackup(SceneSetupReport report)
        {
            string manifestPath = Path.Combine(BackupAbsolutePath, BackupManifestName);
            if (!File.Exists(manifestPath))
            {
                report.Add(SceneSetupIssueSeverity.Error, "BACKUP_MISSING", "Rollback",
                    manifestPath, string.Empty, "No scene-setup backup manifest exists.");
                return;
            }
            BackupManifest manifest = JsonUtility.FromJson<BackupManifest>(
                File.ReadAllText(manifestPath));
            RestoreBackup(report, manifest);
        }

        private static void RestoreBackup(SceneSetupReport report, BackupManifest manifest)
        {
            if (manifest == null)
            {
                report.Add(SceneSetupIssueSeverity.Error, "BACKUP_INVALID", "Rollback",
                    BackupAbsolutePath, string.Empty, "Backup manifest is invalid.");
                return;
            }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            for (int i = 0; i < manifest.createdAssetPaths.Count; i++)
            {
                if (AssetDatabase.LoadMainAssetAtPath(manifest.createdAssetPaths[i]) != null)
                {
                    AssetDatabase.DeleteAsset(manifest.createdAssetPaths[i]);
                }
            }
            for (int i = 0; i < manifest.backups.Count; i++)
            {
                BackupFileRecord backup = manifest.backups[i];
                if (!File.Exists(backup.backupPath))
                {
                    report.Add(SceneSetupIssueSeverity.Error, "BACKUP_FILE_MISSING", "Rollback",
                        backup.backupPath, string.Empty, "A backup file is missing.");
                    continue;
                }
                string destination = AbsoluteProjectPath(backup.assetPath);
                if (File.Exists(destination))
                {
                    FileUtil.DeleteFileOrDirectory(destination);
                }
                FileUtil.CopyFileOrDirectory(backup.backupPath, destination);
            }
            EditorBuildSettings.scenes = RestoreBuildScenes(manifest.buildScenes);
            AssetDatabase.Refresh();
            report.Add(SceneSetupIssueSeverity.Info, "BACKUP_RESTORED", "Rollback",
                BackupAbsolutePath, string.Empty, "The last scene-setup backup was restored.");
        }

        private static BuildSceneRecord[] BuildSceneRecords(EditorBuildSettingsScene[] scenes)
        {
            BuildSceneRecord[] records = new BuildSceneRecord[scenes.Length];
            for (int i = 0; i < scenes.Length; i++)
            {
                records[i] = new BuildSceneRecord { path = scenes[i].path, enabled = scenes[i].enabled };
            }
            return records;
        }

        private static EditorBuildSettingsScene[] RestoreBuildScenes(BuildSceneRecord[] records)
        {
            if (records == null)
            {
                return Array.Empty<EditorBuildSettingsScene>();
            }
            EditorBuildSettingsScene[] scenes = new EditorBuildSettingsScene[records.Length];
            for (int i = 0; i < records.Length; i++)
            {
                scenes[i] = new EditorBuildSettingsScene(records[i].path, records[i].enabled);
            }
            return scenes;
        }

        private static void WriteAndLog(SceneSetupReport report)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AbsoluteProjectPath(ReportPath)));
            File.WriteAllText(AbsoluteProjectPath(ReportPath), JsonUtility.ToJson(report, true));
            for (int i = 0; i < report.Issues.Count; i++)
            {
                SceneSetupIssue issue = report.Issues[i];
                string line = "[SceneSetup][" + issue.Code + "] " + issue.Message
                    + (string.IsNullOrEmpty(issue.HierarchyPath)
                        ? string.Empty
                        : " (" + issue.HierarchyPath + ")");
                if (issue.Severity == SceneSetupIssueSeverity.Error)
                {
                    Debug.LogError(line);
                }
                else if (issue.Severity == SceneSetupIssueSeverity.Warning)
                {
                    Debug.LogWarning(line);
                }
                else
                {
                    Debug.Log(line);
                }
            }
            Debug.Log("[SceneSetup] " + report.Operation + ": " + report.ErrorCount
                + " errors, " + report.WarningCount + " warnings, " + report.InfoCount + " info.");
        }

        // Lookup and validation helpers ---------------------------------------------

        private static bool CheckRootDuplicates(
            Scene scene,
            string name,
            SceneSetupReport report)
        {
            int count = 0;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].name == name)
                {
                    count++;
                }
            }
            if (count <= 1)
            {
                return true;
            }
            report.Add(SceneSetupIssueSeverity.Error, "DUPLICATE_ROOT", "Hierarchy",
                scene.path, "/" + name,
                "Multiple root objects occupy this managed path; none were deleted.");
            return false;
        }

        private static GameObject FindUniqueRoot(
            Scene scene,
            string name,
            SceneSetupReport report)
        {
            GameObject found = null;
            int count = 0;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].name != name)
                {
                    continue;
                }
                found = roots[i];
                count++;
            }
            if (count > 1 && report != null)
            {
                report.Add(SceneSetupIssueSeverity.Error, "DUPLICATE_ROOT", "Hierarchy",
                    scene.path, "/" + name, "Multiple root objects occupy this managed path.");
            }
            return count == 1 ? found : null;
        }

        private static T[] FindComponentsInScene<T>(Scene scene) where T : Component
        {
            List<T> found = new List<T>();
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                found.AddRange(roots[i].GetComponentsInChildren<T>(true));
            }
            return found.ToArray();
        }

        private static GameObject RequirePath(
            Scene scene,
            string path,
            SceneSetupReport report)
        {
            string[] parts = path.Trim('/').Split('/');
            if (parts.Length == 0)
            {
                return null;
            }
            List<GameObject> roots = new List<GameObject>();
            GameObject[] sceneRoots = scene.GetRootGameObjects();
            for (int i = 0; i < sceneRoots.Length; i++)
            {
                if (sceneRoots[i].name == parts[0])
                {
                    roots.Add(sceneRoots[i]);
                }
            }
            if (roots.Count != 1)
            {
                report.Add(SceneSetupIssueSeverity.Error, "PATH_COUNT", "Hierarchy",
                    scene.path, "/" + parts[0],
                    "Expected one object at this managed path; found " + roots.Count + ".");
                return null;
            }
            Transform current = roots[0].transform;
            for (int p = 1; p < parts.Length; p++)
            {
                Transform next = null;
                int count = 0;
                for (int i = 0; i < current.childCount; i++)
                {
                    Transform child = current.GetChild(i);
                    if (child.name == parts[p])
                    {
                        next = child;
                        count++;
                    }
                }
                if (count != 1)
                {
                    report.Add(SceneSetupIssueSeverity.Error, "PATH_COUNT", "Hierarchy",
                        scene.path, string.Join("/", parts, 0, p + 1).Insert(0, "/"),
                        "Expected one object at this managed path; found " + count + ".");
                    return null;
                }
                current = next;
            }
            return current.gameObject;
        }

        private static void ValidateReference(
            Object target,
            string propertyName,
            Object expected,
            string scenePath,
            string hierarchyPath,
            SceneSetupReport report)
        {
            if (target == null || GetObjectProperty(target, propertyName) != expected)
            {
                AddInvalid(report, scenePath, hierarchyPath,
                    (target != null ? target.GetType().Name : "Missing component")
                    + "." + propertyName + " is incorrect.");
            }
        }

        private static void AddInvalid(
            SceneSetupReport report,
            string scenePath,
            string hierarchyPath,
            string message)
        {
            report.Add(SceneSetupIssueSeverity.Error, "INVALID_SETUP", "Validation",
                scenePath, hierarchyPath, message);
        }

        private static string HierarchyPath(Transform transform)
        {
            if (transform == null)
            {
                return string.Empty;
            }
            string path = "/" + transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = "/" + transform.name + path;
            }
            return path;
        }

        private static string AbsoluteProjectPath(string relativePath)
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", relativePath));
        }

        private static string BackupAbsolutePath => AbsoluteProjectPath(BackupRelativePath);

        private static readonly Vector2 Center = new Vector2(0.5f, 0.5f);

        private readonly struct CardParts
        {
            public CardParts(RectTransform area, CardView view, CardSwipeController swipe)
            {
                Area = area;
                View = view;
                Swipe = swipe;
            }
            public RectTransform Area { get; }
            public CardView View { get; }
            public CardSwipeController Swipe { get; }
        }

        private readonly struct GameOverParts
        {
            public GameOverParts(RectTransform root, GameOverView view)
            {
                Root = root;
                View = view;
            }
            public RectTransform Root { get; }
            public GameOverView View { get; }
        }

        private readonly struct FooterParts
        {
            public FooterParts(RectTransform root, RunStatusView runStatus, FooterView footer)
            {
                Root = root;
                RunStatus = runStatus;
                Footer = footer;
            }

            public RectTransform Root { get; }
            public RunStatusView RunStatus { get; }
            public FooterView Footer { get; }
        }

        [Serializable]
        private sealed class BackupManifest
        {
            public List<BackupFileRecord> backups = new List<BackupFileRecord>();
            public List<string> createdAssetPaths = new List<string>();
            public BuildSceneRecord[] buildScenes = Array.Empty<BuildSceneRecord>();
        }

        [Serializable]
        private sealed class BackupFileRecord
        {
            public string assetPath;
            public string backupPath;
        }

        [Serializable]
        private sealed class BuildSceneRecord
        {
            public string path;
            public bool enabled;
        }
    }
}
