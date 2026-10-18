# YahtzeeSim – Reverse-Engineered Requirements

This document was reverse-engineered from the repository as it exists today. It records **what the code does**, groups that behaviour into
**software requirements (SW-xxx)**, and maps every software requirement to a higher-level **user requirement (UR-xx)**.
A status column shows where the code meets, misses or disagrees with the intent expressed in [TODO.md](../TODO.md).

## 1. Scope and system context

| Item | Description |
|---|---|
| Product | Solo Yahtzee scorecard/simulator with dice rolling, hold, scoring, odds guidance and a 6-card "Full Sheet" mode |
| Front end | [YahtzeeSim.Web](../YahtzeeSim.Web) – Blazor WebAssembly PWA, static-hosted on Cloudflare Pages, installable via "Add to Home Screen". The only front end (decided): MAUI and WinForms are to be removed |
| Shared engine | [YahtzeeSim.Engine](../YahtzeeSim.Engine) – the single game engine (decided), a class library with no UI dependencies, referenced by Web and the test project, namespace `YahtzeeSim.Engine`. Today it still lives in `YahtzeeSim.Maui/Engine` and is linked into Web |

Status legend: ✅ Implemented · 🟡 Partial · ❌ Not implemented. "Decided, not yet built" means the behaviour is agreed but the code does not do it yet.

Terminology: **Card** = one 13-category scoresheet (a "game" column G1–G6). **Turn** = up to three rolls ending in one score entry.
**Scratch** = a category sacrificed with a 0, shown as **X**. **Full Sheet** = six cards played together.

## 2. User requirements

| ID | User requirement |
|---|---|
| UR-01 | I can play a complete game of Yahtzee on a digital scorecard from start to finish. |
| UR-02 | I can roll five dice up to three times per turn and choose which dice to keep between rolls. |
| UR-03 | I can record each turn in a category of my choice, or sacrifice (scratch) a category when nothing fits. |
| UR-04 | Scores follow standard Yahtzee rules so the results can be trusted. |
| UR-05 | I can play each game individually (Single), or play six games at once on one sheet (Full Sheet) and see a combined total. |
| UR-06 | I get odds and pacing guidance to help me decide what to keep and where to score. |
| UR-07 | I can undo a mistaken score. |
| UR-08 | I can mark the scorecard with pen colours of my choice, at sensible moments only. |
| UR-09 | The app celebrates when I score a Yahtzee. |
| UR-10 | Subtotals, bonus and totals are calculated automatically when a game ends. |
| UR-11 | The app is comfortable on a phone: big touch targets, readable, layout adapts to screen size. |
| UR-12 | I can use it on Android, Windows, iPhone and any device with a browser, from one web-based solution (no App Store). |
| UR-13 | The app starts reliably, keeps working offline once installed, and I can recover from a bad cached/broken state. |
| UR-14 | As the owner I can build, deploy and maintain it with one source of truth for the rules. |

## 3. Software requirements

Source links point to the file that implements the behaviour (engine links point to `YahtzeeSim.Maui/Engine` until the engine moves to `YahtzeeSim.Engine`).
"Web" = [Home.razor](../YahtzeeSim.Web/Pages/Home.razor) / [Home.razor.css](../YahtzeeSim.Web/Pages/Home.razor.css).

### 3.1 Game rules and card model (RUL)

| ID | Requirement | Source | UR | Status |
|---|---|---|---|---|
| SW-RUL-01 | The game shall use five dice, each showing 1–6, drawn uniformly at random (`System.Random`). | [YahtzeeGameEngine.cs](../YahtzeeSim.Maui/Engine/YahtzeeGameEngine.cs) | UR-02 | ✅ |
| SW-RUL-02 | Each card shall contain 13 categories in this order: Aces, Twos, Threes, Fours, Fives, Sixes (upper); Three of a Kind, Four of a Kind, Full House, Small Straight, Large Straight, Yahtzee, Chance (lower). | [ScoreRules.cs](../YahtzeeSim.Maui/Engine/ScoreRules.cs) | UR-01 | ✅ |
| SW-RUL-03 | An upper category shall be valid when at least one die shows its face and shall score face × count of that face. | `ScoreRules.EvaluateCategory` | UR-04 | ✅ |
| SW-RUL-04 | Three of a Kind (≥3 same) and Four of a Kind (≥4 same) shall be valid accordingly and score the sum of all five dice. | `ScoreRules.EvaluateCategory` | UR-04 | ✅ |
| SW-RUL-05 | Full House shall be valid only for exactly three of one face plus exactly two of another, and score 25. Five-of-a-kind is **not** a Full House in Full Sheet mode (in Single mode a Joker Yahtzee scores Full House, see SW-RUL-11). | `ScoreRules.IsFullHouse` | UR-04 | ✅ (house rule for Full Sheet) |
| SW-RUL-06 | Small Straight (4 consecutive faces) shall score 30; Large Straight (5 consecutive faces) shall score 40. | `ScoreRules.IsSmallStraight/IsLargeStraight` | UR-04 | ✅ |
| SW-RUL-07 | Yahtzee (five of a kind) shall score 50. | `ScoreRules.EvaluateCategory` | UR-04 | ✅ |
| SW-RUL-08 | Chance shall always be valid and score the sum of all dice. | `ScoreRules.EvaluateCategory` | UR-04 | ✅ |
| SW-RUL-09 | A category cell on a card shall be fillable exactly once; a filled cell shall not be editable (except via Undo, SW-UND-02). | `YahtzeeGameEngine.TryApplyScore` | UR-01 | ✅ |
| SW-RUL-10 | A card shall be complete when all 13 cells are filled (value or scratch). | `GameCardState.IsComplete` | UR-01 | ✅ |
| SW-RUL-11 | **Single mode only.** If the Yahtzee box holds 50, each further Yahtzee on the card shall earn a +100 bonus, recorded as one check mark per extra Yahtzee in a "Yahtzee Bonus" row of the Lower Section, as on a real scoresheet. Each check mark is worth 100 and is included in the lower subtotal and game total. Undo shall restore the check count. The roll shall then be placed by the Joker rules: (1) the matching Upper box if open; (2) otherwise any open Lower box, where Full House, Small Straight and Large Straight score 25 / 30 / 40; (3) otherwise a 0 in any open Upper box. If the Yahtzee box holds 0, no bonus is awarded but Joker placement still applies. **Full Sheet mode** applies neither the bonus nor the Joker rules. | – | UR-04 | ❌ Decided, not yet built |
| SW-RUL-12 | Categories shall have human-readable display names ("Three of a Kind", etc.). | `ScoreRules.GetDisplayName` | UR-01 | ✅ |

### 3.2 Turn flow (TRN)

| ID | Requirement | Source | UR | Status |
|---|---|---|---|---|
| SW-TRN-01 | A new game shall start with all dice blank (value 0), none held, and roll count 0. | `YahtzeeGameEngine.Reset` | UR-01 | ✅ |
| SW-TRN-02 | Rolling shall be allowed only while the roll count is below 3 and the game is not complete. | `CanRoll` | UR-02 | ✅ |
| SW-TRN-03 | A roll shall re-randomise only non-held dice and increment the roll count. | `RollDice` | UR-02 | ✅ |
| SW-TRN-04 | Holding a die shall be possible only after the first roll, before the third roll, and only for a die that has a value; toggling shall flip its held flag. | `ToggleHold` | UR-02 | ✅ |
| SW-TRN-05 | After the third roll all dice shall be flagged held, hold toggling shall be disabled, and the player shall be required to score. | `RollDice`, `AwaitingScoreSelection` | UR-02 | ✅ |
| SW-TRN-06 | A category shall be selectable only after at least one roll, only if its cell is unfilled, and only on an enabled card. | `CanSelectCategory`, `TryApplyScore` | UR-03 | ✅ |
| SW-TRN-07 | A category that is valid for the current dice shall score its value and end the turn, at any roll. A valid category can never be scratched. Choosing a category that is not valid shall ask the player to confirm a scratch (SW-TRN-08); declining leaves the state unchanged. | `TryApplyScore` | UR-03 | 🟡 Decided; today an invalid choice before roll 3 is rejected outright |
| SW-TRN-08 | After at least one roll, a confirmed scratch of an unfilled, not-valid cell shall be recorded as value 0, shown "X", and end the turn. After the third roll the player must score or scratch. | `TryApplyScore` | UR-03 | 🟡 Decided; today a scratch is only possible at roll 3, without confirmation |
| SW-TRN-09 | After a score is applied the turn shall reset: roll count 0, dice cleared, holds cleared. | `StartNextTurn` | UR-01 | ✅ |
| SW-TRN-10 | Held dice shall persist across rolls within a turn and be visibly distinguished. | engine + both UIs | UR-02 | ✅ (TODO.md says "hold is broken"; no defect reproduced from code review – verify on device) |
| SW-TRN-11 | When every card in play is complete, Roll shall be disabled. | `IsGameComplete`, `CanRoll` | UR-01 | ✅ |

### 3.3 Scoring, totals and pace (SCR)

| ID | Requirement | Source | UR | Status |
|---|---|---|---|---|
| SW-SCR-01 | A scratched cell shall store value 0, be flagged `IsScratch`, and display "X"; other filled cells display their number; empty cells display blank. | [ScoreCellState.cs](../YahtzeeSim.Maui/Engine/ScoreCellState.cs) | UR-03 | ✅ |
| SW-SCR-02 | Each filled cell shall remember the pen colour hex in force when it was filled. | `TryApplyScore` | UR-08 | ✅ |
| SW-SCR-03 | Upper subtotal, lower subtotal, upper bonus and game total shall be computed only when the card is complete; before that they shall be unavailable and displayed as "--". | [GameCardState.cs](../YahtzeeSim.Maui/Engine/GameCardState.cs) | UR-10 | ✅ (TODO.md: "calculated automatically at game end") |
| SW-SCR-04 | The upper bonus shall be 35 when the upper subtotal (excluding bonus) is ≥ 63, otherwise 0. | `GameCardState` | UR-10 | ✅ |
| SW-SCR-05 | Game total shall equal upper subtotal + lower subtotal + bonus. | `GameCardState.GameTotal` | UR-10 | ✅ |
| SW-SCR-06 | A "Card Total" shall equal the sum of game totals and shall be shown only when all cards in play are complete (Single: card 1; Full Sheet: all six), in the G1 column of the Card Total row. | Web `GetCardTotalDisplay` | UR-05 | ✅ |
| SW-SCR-07 | An "Upper Pace" row shall show, per card, Σ((count − 3) × face) over filled upper cells (scratch counts as −3 × face), with "+" for positive values, and "--" for disabled cards. | `ScoreRules.GetUpperPaceDelta` | UR-06 | ✅ |
| SW-SCR-08 | The Upper Pace figure shall be tracked for every game in both Single and Full Sheet modes. A blank upper cell counts as exactly three of its face, so the figure equals the projected upper subtotal minus 63 (≥ 0 means the 35-point bonus is on track). No further display is required. | `ScoreRules.GetUpperPaceDelta` | UR-06 | ✅ Covered by SW-SCR-07 (decided: keep the +/- number and colour tone as is) |

### 3.4 Game modes (MOD)

| ID | Requirement | Source | UR | Status |
|---|---|---|---|---|
| SW-MOD-01 | The app shall open in a not-started state: dice, Roll, Undo and score cells are disabled and the status line prompts "Tap New Game". | Web `OnInitialized` | UR-01 | ✅ |
| SW-MOD-02 | "New Game" shall ask for a mode (Single or Full Sheet) with a Cancel option, reset the engine, and then prompt for a pen colour. | Web `SelectGameMode` | UR-01, UR-05 | ✅ |
| SW-MOD-03 | New Game shall be available at any time and shall always ask "Abandon current game?" before discarding the current game. | same | UR-01 | 🟡 Decided, not yet built (no confirmation today) |
| SW-MOD-04 | In Single mode only card G1 shall be enabled; G2–G6 columns remain visible but cannot be scored. | `IsCardEnabled` | UR-01, UR-05 | ✅ Disabled columns stay visible, greyed out (accepted by decision) |
| SW-MOD-05 | In Full Sheet mode all six cards shall be enabled; each turn's dice may be scored to any unfilled cell on any card, in any order. | `IsCardEnabled`, `TryApplyScore` | UR-05 | ✅ |
| SW-MOD-06 | When a Full Sheet completes, the app shall show a summary with per-card Upper, Lower, Bonus, Total and the series total. | Web summary panel | UR-05 | ✅ (no equivalent panel for Single) |

### 3.5 Probability guidance (PRB)

| ID | Requirement | Source | UR | Status |
|---|---|---|---|---|
| SW-PRB-01 | A "Prob" column shall show, for each of the 13 categories, the chance of completing it given current dice and rolls remaining, formatted as a percentage with one decimal; blank before the first roll of a turn. | [ProbabilityCalculator.cs](../YahtzeeSim.Maui/Engine/ProbabilityCalculator.cs) | UR-06 | ✅ |
| SW-PRB-02 | With no rolls remaining the probability shall be 100% if the category is already satisfied, else 0%; if already satisfied with rolls left, 100%. | `EstimateProbability` | UR-06 | ✅ |
| SW-PRB-03 | For upper categories "complete" shall mean ≥3 of the face, and the probability shall be computed exactly (Markov chain, holding all matching dice). | `UpperSectionExactProbability` | UR-06 | ✅ (TODO.md: upper = three-of-a-kind probability) |
| SW-PRB-04 | For lower categories the probability shall be estimated by Monte-Carlo (4 000 trials) using a greedy per-category hold strategy and a fixed random seed, so the same dice, holds and rolls remaining always give the same figures. | `MonteCarloEstimate`, `BuildHoldMask` | UR-06 | 🟡 Decided, not yet built (unseeded today, so figures jitter; greedy strategy is not optimal) |
| SW-PRB-05 | Probabilities shall be recalculated after a roll, an undo or a hold toggle, and cleared after a score. | Web `RefreshAll` | UR-06 | 🟡 Decided; today a hold toggle does not recalculate, so figures are stale until the next roll |
| SW-PRB-06 | Probabilities shall be displayed for every category regardless of whether the cell is already filled on the relevant card. | `Calculate` | UR-06 | ✅ Accepted by decision (filled categories keep showing odds) |

### 3.6 Undo (UND)

| ID | Requirement | Source | UR | Status |
|---|---|---|---|---|
| SW-UND-01 | Immediately before a score is applied the engine shall snapshot dice, holds, roll count, mode and all cards (deep copy). | `CreateSnapshot` | UR-07 | ✅ |
| SW-UND-02 | Undo shall restore the snapshot (reverting the last score and returning to the same roll state) and then clear it; only one level of undo is supported. | `Undo` | UR-07 | ✅ |
| SW-UND-03 | Undo shall be enabled only when a snapshot exists, and the snapshot shall be cleared on New Game. Rolls cannot be undone. | `CanUndo`, `Reset` | UR-07 | ✅ |

### 3.7 Pen colours and celebration (PEN, CEL)

| ID | Requirement | Source | UR | Status |
|---|---|---|---|---|
| SW-PEN-01 | The default pen colour shall be `#1D2939`. | Web `_currentPenColorHex` | UR-08 | ✅ |
| SW-PEN-02 | The pen palette shall have 24 swatches (6 hues × 4 shades). | MainPage.xaml, `Home.razor` `PenColors` | UR-08 | ✅ (list duplicated in both UIs) |
| SW-PEN-03 | A mandatory pen-colour picker shall be shown right after a new game starts. | `PromptForPenColorAsync` / `OpenColorPicker` | UR-08 | ✅ |
| SW-PEN-04 | After a valid Yahtzee is scored, the picker shall be shown again with a "Keep Current" option. | `OnScoreClickedAsync` / `ApplyScoreAsync` | UR-08 | ✅ Confirmed by decision: the picker fires on scoring; the celebration fires on rolling (SW-CEL-01) |
| SW-PEN-05 | The colour shall not be changeable at any other time (no other entry point). | UI | UR-08 | ✅ |
| SW-PEN-06 | A filled cell shall render with its stored pen colour as background and a black/white text colour chosen by luminance. | `GetContrastingTextColor` | UR-08 | ✅ |
| SW-CEL-01 | Rolling five of a kind shall show a full-screen splash/animation that dismisses itself after about 2 seconds or on tap. | Web notice text | UR-09 | 🟡 Decided; today a text banner on scoring |
| SW-CEL-02 | Recording a scratch shall inform the user that the category was recorded as X (stored as 0). | same | UR-03 | ✅ Web banner |

### 3.8 MAUI user interface (UIM) – to be retired with MAUI

| ID | Requirement | Source | UR | Status |
|---|---|---|---|---|
| SW-UIM-01 | The page shall stack: top bar (title, status line, New Game), dice row, Roll/Undo row, scrollable score grid, end-of-game summary. | MainPage.xaml | UR-01 | ✅ |
| SW-UIM-02 | The five dice shall be equal-width buttons showing Unicode glyphs ⚀–⚅, blank until rolled, held dice highlighted (orange, thick border); size scales with width (66–122 px). | `RefreshDice`, `ApplyDiceSizing` | UR-02 | ✅ |
| SW-UIM-03 | Roll and Undo shall sit directly under the dice at an 80 % / 20 % width split, 56 px tall. | MainPage.xaml (`8*,2*`) | UR-11 | ✅ |
| SW-UIM-04 | Pressing Roll shall blank non-held dice; releasing it shall perform the roll. | `RollButton_Pressed/Released` | UR-02 | Web rolls on click (Web behaviour is the one kept) |
| SW-UIM-05 | Roll shall be disabled unless a game is started and `CanRoll`; Undo disabled unless `CanUndo`. | `RefreshStatus` | UR-02 | ✅ |
| SW-UIM-06 | The status line shall describe the state: not started, roll to start, "Roll n of 3 …", or "Roll 3 reached: pick any score cell". | `RefreshStatus` | UR-01 | ✅ |
| SW-UIM-07 | The score grid shall have columns Category, Prob, G1–G6 with one button per cell; colours: valid-now = green, otherwise grey, filled = pen colour. | `BuildScoreGrid`, `AddScoreButton` | UR-03 | ✅ |
| SW-UIM-08 | A score button shall be enabled only if `CanSelectCategory` (valid for current dice, or any unfilled cell after roll 3). | `AddScoreButton` | UR-03 | Superseded by SW-UIW-03 |
| SW-UIM-09 | Mode selection shall use a native action sheet; the pen picker shall be a full-screen overlay. | `StartGameButton_Clicked`, `ColorPickerOverlay` | UR-01 | ✅ |
| SW-UIM-10 | The layout shall adapt: fonts, dice and row heights scale with width; on screens narrower than 680 or shorter than 760 the grid scrolls vertically. | `OnSizeAllocated` | UR-11 | ✅ |
| SW-UIM-11 | Score buttons shall be at least 38 px tall (row height 42–64 px). | `AddScoreButton`, `GetInteractiveRowHeight` | UR-11 | ✅ |
| SW-UIM-12 | The grid shall show Upper, Lower, Bonus, Game Total rows per card, and a Card Total row, with a separator between upper and lower sections. | `BuildScoreGrid` | UR-10 | ✅ (TODO.md "real Yahtzee card look" not done) |
| SW-UIM-13 | The Web scorecard shall use real-card layout and wording: "Upper Section" and "Lower Section" headings, a "Bonus 35 if 63+" label, and the official box order and names. | – | UR-01 | ❌ Decided, not yet built (Web only) |

### 3.9 Web user interface (UIW)

| ID | Requirement | Source | UR | Status |
|---|---|---|---|---|
| SW-UIW-01 | The single page `/` shall present the app using HTML/CSS (table-based score grid), reusing the engine unchanged. | Home.razor | UR-12 | ✅ |
| SW-UIW-02 | Roll shall show a "rolling" visual on pointer-down for non-held dice and roll on click; Roll/Undo use an 8fr/2fr grid. | `RollButtonPressed/ClickedAsync`, Home.razor.css | UR-11 | ✅ |
| SW-UIW-03 | Score buttons shall be enabled for any unfilled cell on an enabled card once rolled; picking a cell that is not valid shall ask to confirm a scratch (SW-TRN-07, SW-TRN-08). | `CanAttemptScore`, `ApplyScoreAsync` | UR-03 | 🟡 Decided; today an invalid pick before roll 3 shows a "not valid yet" banner |
| SW-UIW-04 | Feedback messages (scratch, Yahtzee, invalid) shall appear in a notice banner that is cleared on undo and on a new game. | `_noticeText` | UR-03 | ✅ |
| SW-UIW-05 | Mode selection and pen colour shall use in-page modal overlays. | Home.razor | UR-01 | ✅ |
| SW-UIW-06 | Dice buttons shall be disabled unless holding is allowed. | Home.razor | UR-02 | ✅ |
| SW-UIW-07 | The layout shall adapt with CSS (`@media (max-width: 820px)`, clamp-based sizing). | Home.razor.css | UR-11 | ✅ |

### 3.10 Platform, packaging and reliability (PLT)

| ID | Requirement | Source | UR | Status |
|---|---|---|---|---|
| SW-PLT-01 | The product shall be delivered as one web-based solution (the Web PWA) running on Android, Windows and iPhone. The MAUI and WinForms projects shall be removed (history stays in git). Revisit if a single web solution proves not possible. | [YahtzeeSim.Web.csproj](../YahtzeeSim.Web/YahtzeeSim.Web.csproj) | UR-12 | 🟡 Decided, not yet done (MAUI and WinForms are still in the repo); device testing on Android and iPhone still needed |
| SW-PLT-02 | The Web app shall be a Blazor WebAssembly (net9) static site with no server component. | [YahtzeeSim.Web.csproj](../YahtzeeSim.Web/YahtzeeSim.Web.csproj) | UR-12 | ✅ |
| SW-PLT-03 | There shall be exactly one game engine: a class library `YahtzeeSim.Engine` (namespace `YahtzeeSim.Engine`) referenced by Web and the test project. No other copy of game logic shall exist. | YahtzeeSim.Web.csproj | UR-14 | 🟡 Decided, not yet done (engine lives in the MAUI folder and is linked into Web; WinForms has its own copy) |
| SW-PLT-04 | The Web app shall publish a PWA manifest (standalone, portrait, 192/512 icons, theme colour) and Apple meta tags so it can be added to an iOS/Android home screen. | [manifest.webmanifest](../YahtzeeSim.Web/wwwroot/manifest.webmanifest), [index.html](../YahtzeeSim.Web/wwwroot/index.html) | UR-12 | ✅ |
| SW-PLT-05 | The Web app shall work offline once installed. A service worker shall cache the app shell and serve it network-first with a cached fallback, so users get the latest version when online. A stale or broken cache shall never block startup; "Reset app data" (SW-PLT-06) remains the recovery path. | index.html, `service-worker*.js` | UR-13 | 🟡 Decided; today `index.html` unregisters workers and deletes `offline-cache-*` caches ("reliability mode"), so offline is unsupported |
| SW-PLT-06 | The Web app shall show an error bar with "Reload" and "Reset app data" (unregister workers, clear caches, reload). | index.html (`__yahtzeeSimResetApp`) | UR-13 | ✅ |
| SW-PLT-07 | Game state shall be autosaved after every action. On launch the app shall offer to resume the saved game, and New Game shall clear it. | – | UR-01 | ❌ Decided, not yet built (state is in-memory only) |

### 3.11 Build, deployment and quality (OPS, NFR)

| ID | Requirement | Source | UR | Status |
|---|---|---|---|---|
| SW-OPS-01 | A GitHub Actions workflow shall deploy the Web app on push to `main` (paths `YahtzeeSim.Web/**` or the workflow file) and on manual dispatch. | [deploy-cloudflare-pages.yml](../.github/workflows/deploy-cloudflare-pages.yml) | UR-14 | ✅ |
| SW-OPS-02 | The workflow shall validate the three Cloudflare secrets, publish Release, verify the Pages project exists (listing available projects on failure), then deploy with Wrangler to production. | same | UR-14 | ✅ |
| SW-OPS-03 | A local PowerShell script shall publish and deploy using the same environment variables. | [deploy-cloudflare-pages.ps1](../scripts/deploy-cloudflare-pages.ps1) | UR-14 | ✅ |
| SW-OPS-04 | The host shall send `no-store` for `index.html`, service-worker files and the manifest, and `nosniff` for all files. | [_headers](../YahtzeeSim.Web/wwwroot/_headers) | UR-13 | ✅ |
| SW-OPS-05 | Engine unit tests shall cover every SW-RUL, SW-TRN, SW-SCR and SW-UND requirement and run in CI on every push; a failing test shall block the Web deploy. | – | UR-14 | ❌ Decided, not yet built (no test project; CI only deploys Web) |
| SW-NFR-01 | The engine shall have no dependency on any UI framework. | Engine folder | UR-14 | ✅ |
| SW-NFR-02 | Updating the odds after a roll, undo or hold toggle shall take under 100 ms on a mid-range phone; if it exceeds that, the calculation shall move off the UI thread. | `ProbabilityCalculator` | UR-11 | 🟡 Decided; unmeasured today (≈ 6 categories × 4 000 trials on the UI thread) |
| SW-NFR-03 | The number of cards per sheet shall be fixed at six (`GameCount`). | `YahtzeeGameEngine.GameCount` | UR-05 | ✅ |

## 4. Traceability matrix (UR → SW)

| UR | Software requirements |
|---|---|
| UR-01 Play a complete game | RUL-02, RUL-09, RUL-10, RUL-12, TRN-01, TRN-09, TRN-11, MOD-01, MOD-02, MOD-03, MOD-04, UIM-01, UIM-06, UIM-09, UIM-13, UIW-05, PLT-07 |
| UR-02 Roll and hold | RUL-01, TRN-02, TRN-03, TRN-04, TRN-05, TRN-10, UIM-02, UIM-04, UIM-05, UIW-06 |
| UR-03 Score / scratch | TRN-06, TRN-07, TRN-08, SCR-01, CEL-02, UIM-07, UIM-08, UIW-03, UIW-04 |
| UR-04 Standard rules | RUL-03, RUL-04, RUL-05, RUL-06, RUL-07, RUL-08, RUL-11 |
| UR-05 Individual or six games at once | MOD-02, MOD-04, MOD-05, MOD-06, SCR-06, NFR-03 |
| UR-06 Odds and pace | PRB-01 … PRB-06, SCR-07, SCR-08 |
| UR-07 Undo | UND-01, UND-02, UND-03 |
| UR-08 Pen colours | SCR-02, PEN-01 … PEN-06 |
| UR-09 Yahtzee celebration | CEL-01 |
| UR-10 Automatic totals | SCR-03, SCR-04, SCR-05, UIM-12 |
| UR-11 Touch / responsive | UIM-03, UIM-10, UIM-11, UIW-02, UIW-07, NFR-02 |
| UR-12 Cross-platform | UIW-01, PLT-01, PLT-02, PLT-04 |
| UR-13 Reliable start / recovery | PLT-05, PLT-06, OPS-04 |
| UR-14 Maintainable / deployable | PLT-03, OPS-01, OPS-02, OPS-03, OPS-05, NFR-01 |

## 5. Gaps, inconsistencies and clean-up recommendations

Priority order (highest value for "getting the project back under control"):

1. **Reintroduce offline support safely** (SW-PLT-05): restore the service worker with a network-first strategy and a cached fallback, keep "Reset app data" as the recovery path, and remove the "reliability mode" script that unregisters workers.
2. **Remove MAUI and WinForms now and create `YahtzeeSim.Engine`** (SW-PLT-01, SW-PLT-03): move the engine into its own class library, rename the namespace, and update the solution, Web project, deploy workflow path filter and [README.md](../README.md).
3. **Move UI-independent logic out of the pages.** The page code duplicates pen palette, status text, pace calculation, card-total logic and contrast colour. Extract an engine class library (referenced by both apps instead of linked source) and put shared presentation logic there.
4. **Add an engine test project** covering every SW-RUL/TRN/SCR/UND row (these are pure and easily testable); then a CI build step.
5. **Build the decided items** (rows marked "Decided, not yet built"): Single-mode Yahtzee bonus and Joker rules, scratch-with-confirmation, rolled-Yahtzee splash, seeded odds that update on hold, New Game confirmation, autosave/resume, real-card wording, performance check.
6. **Finish remaining TODO.md items** still open: see the rows above marked ❌ or 🟡.
7. **Remove leftovers:** Blazor template files (`Counter.razor`, `Weather.razor`, `NavMenu.razor`, `sample-data/weather.json`), unused `ActiveCardIndex` in the engine.
8. **Optional:** hide probabilities for filled categories (declined for now, SW-PRB-06).

## 6. Decisions and remaining open questions

Decided with the owner:

| Topic | Decision |
|---|---|
| Yahtzee bonus and Joker (SW-RUL-11) | Official rules in Single mode only; Full Sheet unchanged (no bonus, no Joker, five-of-a-kind is not a Full House). |
| Scratching (SW-TRN-07/08) | Allowed after any roll, with a confirmation; a valid category always scores its value and cannot be scratched. |
| Yahtzee celebration (SW-CEL-01, SW-PEN-04) | Splash when rolled (about 2 s or tap); pen picker when scored. |
| Offline (SW-PLT-05) | Wanted: service worker, network-first with cached fallback. |
| Platforms and engine (SW-PLT-01, SW-PLT-03) | One web-based solution. Delete MAUI and WinForms now. One engine in a new `YahtzeeSim.Engine` class library, referenced by Web and the tests. Revisit if a single web solution is not possible. |
| Bonus pace (SW-SCR-07/08) | Existing per-game Upper Pace row (+/- number and colour tone) is sufficient, in both modes. |
| Probabilities (SW-PRB-04/05/06) | Seeded Monte-Carlo, recalculated on hold toggle; still shown for filled categories. |
| New Game (SW-MOD-03) | Always confirms. |
| Single mode layout (SW-MOD-04) | Unused columns stay visible, greyed out. |
| Persistence (SW-PLT-07) | Autosave with a resume prompt. |
| Card look (SW-UIM-13) | Real-card headings, bonus label, official box order and names. |
| Performance (SW-NFR-02) | Under 100 ms on a mid-range phone. |
| Tests (SW-OPS-05) | Engine unit tests in CI, gating the deploy. |

No open questions remain.