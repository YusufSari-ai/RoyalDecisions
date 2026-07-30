# Manual Unity Steps

Steps that must be performed in the Unity Editor by hand, because they touch scenes, prefabs,
`ProjectSettings/`, packages or Build Profiles — all of which are owned by the team, not by
generated code (see `CLAUDE.md` §11).

Tick items off as they are completed. Later phases append to this file.

---

## Required before the MVP can ship

### U1 — Lock the app to portrait

`Edit > Project Settings > Player > Resolution and Presentation`

- [ ] `Default Orientation` = **Portrait**
- [ ] Uncheck `Allowed Orientations for Auto Rotation > Landscape Right`
- [ ] Uncheck `Allowed Orientations for Auto Rotation > Landscape Left`

Currently `defaultScreenOrientation: 4` (Auto Rotation) with all four orientations enabled, which
contradicts the portrait-only requirement in `CLAUDE.md` §2 and §14.

### U2 — Import TextMeshPro Essential Resources

`Window > TextMeshPro > Import TMP Essential Resources`

- [ ] Import **Essential Resources** (skip "Examples & Extras")

The TMP *code* already ships inside `com.unity.ugui` 2.0.0, so no package install is needed. The
*runtime assets* are still a one-time `.unitypackage` import, and without them every `TMP_Text`
renders with a missing material. Needed before Phase 5.

### U3 — Set application identity

`Edit > Project Settings > Player`

- [ ] `Company Name` — currently `DefaultCompany`
- [ ] `Product Name` — currently `RoyalDecisions`
- [ ] Android > `Override Default Package Name` ticked, `Package Name` set
      (e.g. `com.yusufsari.royaldecisions`)

Only a Standalone identifier exists today (`com.DefaultCompany.2D-URP`); Android has none, which
blocks a device build.

### U4 — Install Android build support

Unity Hub > Installs > `6000.3.20f1` > Add Modules

- [ ] Android Build Support
- [ ] OpenJDK
- [ ] Android SDK & NDK Tools
- [ ] `File > Build Profiles` — switch the active platform to Android

Needed for Phase 8.

---

## Recommended

### U5 — Commit the baseline before Phase 1 is reviewed

- [ ] Accept the deletion of `Assets/Editor/HubForceResolve.cs` — it is Unity Hub's bootstrap
      script, written to call `Client.Resolve()` once and then delete itself. The deletion is the
      script working as designed.
- [ ] Add `CLAUDE.md` and the new `ProjectSettings/Packages/` folder.

### U6 — Widen the supported aspect ratio

`Player > Resolution and Presentation > Supported Aspect Ratio`

- [ ] Raise `Up To` from `2.4` to `3.0`

At `2.4`, some 21:9 phones and folded foldables get letterboxed. `CLAUDE.md` §14 requires common
screen ratios to work.

### U7 — Ignore the generated solution file

- [ ] Add `*.slnx` to `.gitignore`

`RoyalDecisions.slnx` is auto-generated and currently tracked; the template's `*.sln` rule does not
match the newer `.slnx` extension.

---

## Notes for later phases

- **Safe Area is mandatory, not optional.** `androidRenderOutsideSafeArea: 1` is already set, so the
  app draws under notches and camera cutouts. The Canvas needs explicit Safe Area handling in
  Phase 5.
- **No packages need to be installed for the MVP.** uGUI, TextMeshPro, Input System, Test Framework
  and JSON serialisation are all present already.
- **Scene wiring** (Canvas, prefabs, Inspector references) arrives in Phase 5 and Phase 8; nothing
  in Phase 1 touches a scene.

---

## Phase 1 — nothing to wire

Phase 1 added only plain C# types, assembly definitions and EditMode tests. There is no scene,
prefab or Inspector work to do.

To confirm the phase locally:

- [ ] Reopen the project and check the Console shows no errors from `Assets/_Game/`
- [ ] `Window > General > Test Runner > EditMode > Run All` — all tests green

---

## Phase 2 — nothing to wire

Phase 2 added the rule services (`StatSystem`, `ConditionEvaluator`, `ChoiceResolver`,
`GameOverEvaluator`, `CardDeckService`, `SeededRandomSource`) and their EditMode tests. All of it is
plain C# inside the existing `RoyalDecisions.Domain` assembly — no new assembly definition, no
scene, prefab or Inspector work.

To confirm the phase locally:

- [ ] Console shows no errors or warnings from `Assets/_Game/`
- [ ] `Window > General > Test Runner > EditMode > Run All` — all tests green

Note for Phase 3: the placeholder content generator must emit **unique card IDs**. Weighted card
selection sorts eligible cards by ID ordinally to keep draws independent of asset order, and that
ordering is only well defined when IDs do not repeat — so duplicate IDs must be a hard validation
error in the generator.

---

## Phase 3 — run the content generator

Phase 3 added the `ContentCatalogue`, the content validator, and an Editor-only generator. There is
still no scene or prefab work, but this phase does need you to **run a menu command once**.

### P3.0 — Commit first

- [ ] Commit the existing work before generating anything.

The generator writes 29 assets into the project. It is written to abort rather than overwrite, and
never touches anything outside its own folder, but a commit is the only thing that makes a mistake
trivially recoverable.

### P3.1 — Generate

- [ ] `Tools > Royal Decisions > Generate Placeholder Content`
- [ ] Console reports `Created 29, Updated 0, Unchanged 0, Skipped 0, Warnings 0, Errors 0`

This writes, under `Assets/_Game/Content/Placeholder/`:

- `Cards/` — 20 `CardDefinition` assets
- `Endings/` — 8 `EndingDefinition` assets
- `PlaceholderContentCatalogue.asset` — the `ContentCatalogue`

### P3.2 — Confirm idempotency

- [ ] Run the same command a **second** time
- [ ] Console reports `Created 0, Updated 0, Unchanged 29`
- [ ] `git status` shows **no modified files**

If the second run reports updates, something is non-deterministic in generation and should be
reported rather than committed.

### P3.3 — Spot-check the content

- [ ] Every generated asset shows the `RoyalDecisions.Placeholder` label at the bottom of the
      Inspector
- [ ] Card speakers and ending titles begin with `[PLACEHOLDER]`
- [ ] `PlaceholderContentCatalogue` lists 20 cards, 8 endings, and
      `openingCardId = card_01_coronation`

### P3.4 — Optional: prove the overwrite guard

- [ ] Remove the `RoyalDecisions.Placeholder` label from any one generated asset
- [ ] Re-run the command — it must **abort**, report that asset as skipped, and write nothing
- [ ] Restore the label and re-run to return to a clean state

---

## Phase 5 — build the Game scene

Phase 5 added the passive presentation layer: `CardView`, `HUDView` + `StatItemView`,
`GameOverView`, `AudioService`, and `SafeAreaFitter`. All of it is driven from outside — nothing
renders until Phase 7 calls it — so this section builds the scene and wires the references.

**Build the Game scene now.** `Bootstrap` and `MainMenu` are described at the end as target
structure; they stay unbuilt until Phase 7, because nothing can move between scenes until
`GameFlowController` exists.

### P5.0 — Prerequisites

- [ ] **U2 — Import TMP Essential Resources.** Still outstanding. Every `TMP_Text` renders with a
      missing material without it, and the EditMode view tests cannot run.
- [ ] **Run the placeholder generator** (`Tools > Royal Decisions > Generate Placeholder Content`)
      if you have not — there is nothing to look at otherwise.
- [ ] **U1 — Lock to portrait.** Still outstanding, and Safe Area behaviour is only meaningful in
      the orientation you ship.

### P5.1 — Create the scene

- [ ] `File > New Scene` → Basic (URP), save as `Assets/_Game/Scenes/Game.unity`
- [ ] `File > Build Profiles > Scene List` — add it

### P5.2 — Camera and EventSystem

- [ ] Main Camera: `Projection = Orthographic`, `Background Type = Solid Color`
- [ ] `GameObject > UI > Event System`
- [ ] The EventSystem **must** use `InputSystemUIInputModule`. The project is set to the new Input
      System only (`activeInputHandler: 1`), so the legacy `StandaloneInputModule` will not work.
      Unity offers a **Replace with InputSystemUIInputModule** button — accept it.

### P5.3 — Canvas

`GameObject > UI > Canvas`, renamed `UICanvas`:

- [ ] `Render Mode` = **Screen Space – Overlay**
- [ ] `Pixel Perfect` off
- [ ] **CanvasScaler** → `UI Scale Mode` = **Scale With Screen Size**
- [ ] `Reference Resolution` = **1080 × 1920** (portrait)
- [ ] `Screen Match Mode` = **Match Width Or Height**, `Match` = **1** (height)

Matching on height keeps the card the same width on a taller phone rather than shrinking it.

### P5.4 — Safe Area

- [ ] Child of `UICanvas` named `SafeArea`, `RectTransform` **stretched to all four edges**, all
      offsets `0`
- [ ] Add **`SafeAreaFitter`**; leave `Target` empty so it uses its own `RectTransform`
- [ ] **Every other UI element parents under `SafeArea`**

`androidRenderOutsideSafeArea` is already enabled, so the app draws under notches. Without this the
HUD sits under the camera cutout on most modern phones.

### P5.5 — HUD

- [ ] Child of `SafeArea` named `HUD`, anchored to the top, add **`HUDView`**
- [ ] Four children: `StatItem_Authority`, `StatItem_People`, `StatItem_Security`,
      `StatItem_Wealth`

Each stat item:

- [ ] Add **`StatItemView`**, set its `Stat` to the matching statistic
- [ ] Child `Fill` — `Image`, **`Image Type = Filled`**, `Fill Method = Horizontal`,
      `Fill Origin = Left`
- [ ] Optional children: `Icon` (`Image`) and `Label` (`TextMeshProUGUI`)
- [ ] Assign `Fill Image`, and `Icon Image` / `Label` if used
- [ ] `Animation Speed` — `2.5` is a sensible default; `0` snaps instantly

Then on `HUDView`:

- [ ] `Stat Items` — size **4**, one per statistic

`HUDView` warns in the Inspector if a statistic is missing, duplicated, or a slot is empty.

### P5.6 — Card

- [ ] Child of `SafeArea` named `CardArea`, then a child `Card` with **`CardView`**
- [ ] `Card Root` — the `Card` RectTransform (**Phase 6 drags this; Phase 5 never moves it**)
- [ ] `Visual Root` — leave empty to toggle the `Card` object itself
- [ ] Children: `Portrait` (`Image`), `Speaker` (`TextMeshProUGUI`), `Body` (`TextMeshProUGUI`)
- [ ] Two preview children, `PreviewLeft` and `PreviewRight`, each with a **`CanvasGroup`**, a
      `TextMeshProUGUI` label, and **`ChoicePreviewView`**
- [ ] On each `ChoicePreviewView`: set `Side` (Left / Right), assign `Label` and `Canvas Group`
- [ ] On `CardView`: assign `Speaker Text`, `Body Text`, `Portrait Image`, `Left Preview`,
      `Right Preview`

Portrait fallback (`Portrait Fallback` on `CardView`):

- [ ] `Fallback Sprite` — leave empty until there is art
- [ ] `Use Fallback Colour` — **on**, so a card with no portrait shows a flat block rather than a
      hole. Turn it off to hide the portrait slot entirely.

### P5.7 — Game Over panel

- [ ] Child of `SafeArea` named `GameOverPanel`, stretched full screen, add **`GameOverView`**
- [ ] Children: `Illustration` (`Image`), `Title` (`TextMeshProUGUI`), `Body` (`TextMeshProUGUI`),
      `RestartButton` (`Button`)
- [ ] Assign `Panel Root` = the `GameOverPanel` object, plus `Title Text`, `Body Text`,
      `Illustration Image`, `Restart Button`
- [ ] `Generic Title` / `Generic Body` — shown when a boundary is reached that no ending covers
- [ ] On `RestartButton` → `OnClick()` → add `GameOverPanel` → **`GameOverView.HandleRestartButton`**
- [ ] **Deactivate `GameOverPanel`** in the Inspector; `Show` activates it

`HandleRestartButton` only raises `RestartRequested`. Phase 7 subscribes and decides what a restart
means — the view restarts nothing.

### P5.8 — Audio

- [ ] Child of `SafeArea` (or the scene root) named `AudioService`
- [ ] Add **`AudioSource`**: `Play On Awake` **off**, `Loop` **off**, `Spatial Blend` = **2D (0)**
- [ ] Add **`AudioService`**, assign the `Audio Source`
- [ ] `Cue Library` — leave empty for now. Every cue then resolves to silence, which is a supported
      configuration, not an error.

When there is audio: `Assets > Create > Royal Decisions > Audio Cue Library`, add `id` → `clip`
pairs where `id` matches `ChoiceDefinition.audioEventId` **exactly** (comparison is ordinal, so
case matters), and assign it to `AudioService`.

### P5.9 — Verify in the Editor

- [ ] Console clean on entering Play Mode
- [ ] `Window > General > Device Simulator` — check **16:9**, **19.5:9** and **21:9**; the HUD and
      card must stay inside the safe area with a notch simulated
- [ ] Nothing renders until Phase 7 drives it — an empty card and empty bars are correct for now

### Required Inspector references

| Component | Required | Optional |
|---|---|---|
| `SafeAreaFitter` | — (defaults to own RectTransform) | `Target` |
| `HUDView` | `Stat Items` ×4, one per statistic | — |
| `StatItemView` | `Stat`, `Fill Image` | `Icon Image`, `Icon Sprite`, `Label`, fallback |
| `CardView` | `Speaker Text`, `Body Text`, `Portrait Image`, both previews | `Card Root`, `Visual Root`, fallback |
| `ChoicePreviewView` | `Side`, `Label`, `Canvas Group` | scale settings |
| `GameOverView` | `Title Text`, `Body Text`, `Panel Root` | `Illustration Image`, `Restart Button`, fallback, generic text |
| `AudioService` | — | `Audio Source`, `Cue Library` |

Every optional reference left empty degrades to a no-op rather than an exception. `HUDView` and the
fallback settings validate in `OnValidate`, so a mis-wired prefab reports the specific problem in
the Inspector.

---

## Phase 6 — wire the swipe

Phase 6 added `CardSwipeController`. It moves the card, drives the previews, and raises two events.
It applies **no consequences** — nothing happens after a swipe until Phase 7 subscribes.

### P6.1 — Add the component

On the `Card` object from P5.6:

- [ ] Add **`CardSwipeController`**
- [ ] `Card View` — the `CardView` on the same object
- [ ] `Drag Parent` — the `CardArea` RectTransform (leave empty to use the card's parent)

**The card must have a `Graphic` with `Raycast Target` enabled**, or no pointer event ever reaches
the component:

- [ ] `Card` has an `Image` with **`Raycast Target` ticked** — a fully transparent one is fine, but
      an alpha of exactly `0` is still hit-testable only if the Image component itself is enabled

This is the single most common reason a uGUI swipe silently does nothing.

### P6.2 — Tune the feel

Defaults are a reasonable starting point; all are serialized:

| Field | Default | Effect |
|---|---|---|
| `Threshold Ratio` | `0.25` | Fraction of parent width needed to confirm |
| `Minimum Threshold Distance` | `40` | Floor, so an unlaid-out parent cannot confirm instantly |
| `Movement Multiplier` | `1.0` | Card travel per unit of finger travel |
| `Max Rotation Degrees` | `12` | Tilt at full threshold |
| `Rotate Clockwise On Right Drag` | on | Tilt direction |
| `Snap Back Duration` | `0.18` | Return animation |
| `Exit Duration` | `0.25` | Off-screen animation |
| `Snap Back Ease` / `Exit Ease` | ease-in-out | Curves |
| `Exit Margin Multiplier` | `1.0` | Extra card widths travelled past the edge |

Out-of-range values are clamped in `OnValidate`, so the Inspector cannot produce an unusable
configuration.

### P6.3 — Verify in the Editor

Enter Play Mode and drag the card with the mouse:

- [ ] The card follows horizontally and tilts; it does **not** move vertically
- [ ] Dragging left fades in only the left preview; right, only the right
- [ ] Releasing before the threshold snaps the card back and the previews fade out
- [ ] Releasing past the threshold sends the card off screen and it stays gone
- [ ] After a confirmed swipe, further dragging does nothing (the card is locked)
- [ ] Console stays clean

Nothing else happens yet — no stats, no next card. That is Phase 7.

### P6.4 — Verify on device

- [ ] Touch drag behaves as it does with the mouse
- [ ] Putting a **second finger** down mid-drag changes nothing — the first finger keeps control
- [ ] Rapid repeated swipes produce **one** decision each, never two
- [ ] The gesture feels the same on a tall and a short screen (the threshold is a fraction of
      width, not a pixel count)
- [ ] Swiping with a notch present keeps the card inside the safe area

---

---

## Phase 7 — final wiring and smoke test

Phase 7 added the application session and the composition root. **All the deferred manual work from
P5, P6 and P7 is collected here in dependency order**, so it can be done in one pass.

Nothing below has been done by code: no scene, prefab, setting or generated asset was touched.

### F1 — Prerequisites (all still outstanding)

- [ ] **U1** — Player Settings: `Default Orientation` = Portrait; untick both Landscape orientations
- [ ] **U2** — `Window > TextMeshPro > Import TMP Essential Resources`
- [ ] **U3** — Company Name, Product Name, Android package name
- [ ] **Generate content** — `Tools > Royal Decisions > Generate Placeholder Content`
      (expect `Created 29`; run it twice and confirm the second run reports `Unchanged 29`)

### F2 — Game scene (P5 + P6)

Follow **P5.1–P5.8** for the scene, Canvas, Safe Area, HUD, card, previews, audio and game-over
panel, then **P6.1–P6.2** for the swipe controller.

The one thing most likely to go wrong: the `Card` object needs an `Image` with **`Raycast Target`
ticked**, or no pointer event ever reaches `CardSwipeController`.

### F3 — Game flow (P7)

- [ ] Add **`GameSceneController`** to a root object in the Game scene
- [ ] `Catalogue` — `Assets/_Game/Content/Placeholder/PlaceholderContentCatalogue.asset`
- [ ] `Card View`, `Hud View`, `Game Over View`, `Swipe Controller` — the components from F2
- [ ] `Audio Service` — optional
- [ ] `Session Intent` — optional for now; see F4
- [ ] `Fallback Start Mode` — `NewGame` while there is no menu

`GameSceneController` needs only `Card View` and `Swipe Controller` to run; anything else missing
degrades rather than throwing, and it reports the problem through `WiringError`.

### F4 — Bootstrap and MainMenu scenes

Only needed once you want a menu. The Game scene runs standalone without them.

- [ ] `Assets > Create > Royal Decisions > Session Intent` → save as
      `Assets/_Game/Content/SessionIntent.asset`
- [ ] **Bootstrap.unity** — an empty object with `BootstrapController`; set `Main Menu Scene Name`;
      optionally assign an `AudioService`
- [ ] **MainMenu.unity** — Canvas with New Game and Continue buttons plus a `MainMenuController`;
      set `Game Scene Name`, assign the `SessionIntent` asset
- [ ] Wire New Game → `MainMenuController.OnNewGamePressed`,
      Continue → `MainMenuController.OnContinuePressed`
- [ ] Disable the Continue button when `IsContinueAvailable` is false
- [ ] Assign the same `SessionIntent` asset to `GameSceneController`
- [ ] `File > Build Profiles > Scene List` — add **Bootstrap, MainMenu, Game** (names, not indices)

### F5 — Smoke test

In the Editor, in this order:

- [ ] **New Game** → the opening card (`card_01_coronation`) appears
- [ ] Swipe past the threshold → the card flies off, the HUD moves, a new card arrives
- [ ] Swipe below the threshold → the card snaps back and no stat changes
- [ ] Play several turns → the turn count rises and each decision produces exactly one save
- [ ] Stop Play Mode, start it again with **Continue** → the run resumes on the same turn
- [ ] Drive a stat to `0` or `100` → the card leaves, *then* the ending appears
- [ ] Press **Restart** → a new run begins on the opening card
- [ ] Console has no errors from `Assets/_Game/`

The save file lives at `%userprofile%/AppData/LocalLow/<Company>/RoyalDecisions/run.json` — inspect
it to confirm `isRunActive: false` is persisted after an ending.

### F6 — Device verification (Phase 8)

- [ ] Portrait only; Safe Area respected around a notch
- [ ] Touch swipe matches mouse behaviour; a second finger mid-drag changes nothing
- [ ] Backgrounding mid-run and returning resumes correctly
- [ ] Deleting the save file leaves Continue unavailable and New Game working

**The MVP is not complete until F1–F6 are done and verified on a device.**

---

### Target structure for Phase 7

Not built yet — nothing can move between scenes until `GameFlowController` exists.

```
Bootstrap.unity   services constructed, then loads MainMenu
MainMenu.unity    New Game / Continue (Continue needs SaveService.HasSave)
Game.unity        built above
```

---

### Replacing placeholder content later

All 29 assets are disposable. To replace them with final content, either edit them in place and
stop running the generator, or delete the `Placeholder` folder and author content elsewhere under
`Assets/_Game/Content/`. Nothing in the gameplay code refers to any placeholder ID — the only
content reference the game needs is a `ContentCatalogue`, which Phase 7 will take as an Inspector
reference.
